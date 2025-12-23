using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class VehicleMovementNetcodeNew : NetworkBehaviour
{
    [Header("Config")]
    [SerializeField] private VehicleConfigNew vehicleConfig;

    [Header("Visual Root")]
    [SerializeField] private Transform visualRoot;

    [Header("Prediction/Reconcile")]
    [SerializeField] private float reconcilePosThreshold = 0.75f;
    [SerializeField] private float reconcileRotThresholdDeg = 12f;
    [SerializeField] private int bufferSize = 1024;

    [Tooltip("Hard cap: max ticks we will replay in a single reconcile to avoid stalls/hangs in builds.")]
    [SerializeField] private int maxReplayTicks = 64;

    [Header("Remote Visual Smoothing")]
    [SerializeField] private float remoteVisualLerpSpeed = 18f;

    private Rigidbody _rb;
    private IMovementModelNew _model;

    // Server input queue keyed by server tick
    private readonly Dictionary<int, VehicleInputNew> _pendingServerInputs = new();
    private VehicleInputNew _lastServerInput;

    // Owner prediction buffers (server tick domain)
    private VehicleInputNew[] _inputBuffer;
    private VehicleSimStateNew[] _stateBuffer;
    private VehicleSimStateNew _predictedVisualState;

    // Visual smoothing targets (non-owner)
    private Vector3 _visualTargetPos;
    private Quaternion _visualTargetRot;

    private bool _tickHooked;

    // Round reset guard (prevents prediction freakout after server teleport)
    private int _lastRoundIdSeen = -1;

    // Debug (optional)
    public VehicleInputNew Debug_LastLocalInput { get; private set; }
    public VehicleInputNew Debug_LastServerAppliedInput { get; private set; }
    public float Debug_LastPosError { get; private set; }
    public float Debug_LastRotErrorDeg { get; private set; }

    private float DtPerTick
    {
        get
        {
            var nm = NetworkManager;
            if (nm == null) return Time.fixedDeltaTime;
            return 1f / nm.NetworkTickSystem.TickRate;
        }
    }

    // -------------------------
    // Yaw-only clamp helper
    // -------------------------
    private static Quaternion YawOnly(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return Quaternion.Euler(0f, e.y, 0f);
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        if (visualRoot == null)
        {
            Transform found = transform.Find("Visual");
            visualRoot = found != null ? found : transform;
        }

        _visualTargetPos = visualRoot.position;
        _visualTargetRot = YawOnly(visualRoot.rotation);
    }

    public override void OnNetworkSpawn()
    {
        if (vehicleConfig == null || vehicleConfig.movementModel == null)
        {
            Debug.LogError($"{name}: Missing VehicleConfigNew or movementModel.");
            enabled = false;
            return;
        }

        _model = vehicleConfig.movementModel.CreateRuntimeModel();

        _inputBuffer = new VehicleInputNew[bufferSize];
        _stateBuffer = new VehicleSimStateNew[bufferSize];

        // Physics authority: only server simulates RB
        _rb.isKinematic = !IsServer;

        // Initialize predicted visual state (yaw-only)
        _predictedVisualState = new VehicleSimStateNew
        {
            Position = visualRoot.position,
            Rotation = YawOnly(visualRoot.rotation),
            Velocity = Vector3.zero,
            YawDegPerSec = 0f
        };

        _visualTargetPos = visualRoot.position;
        _visualTargetRot = YawOnly(visualRoot.rotation);

        if (!_tickHooked && NetworkManager != null)
        {
            NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
            _tickHooked = true;
        }
    }

    public override void OnNetworkDespawn() => UnhookTick();
    private void OnDestroy() => UnhookTick();

    private void UnhookTick()
    {
        if (_tickHooked && NetworkManager != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
        _tickHooked = false;
    }

    private void Update()
    {
        if (!IsSpawned || visualRoot == null) return;

        // Non-owner: smooth visual towards snapshot targets
        if (!IsOwner)
        {
            float t = 1f - Mathf.Exp(-remoteVisualLerpSpeed * Time.deltaTime);
            visualRoot.position = Vector3.Lerp(visualRoot.position, _visualTargetPos, t);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, _visualTargetRot, t);
        }
        else if (IsServer)
        {
            // Host owner: visual follows root (yaw-only)
            visualRoot.position = transform.position;
            visualRoot.rotation = YawOnly(transform.rotation);
        }
    }

    private void OnNetworkTick()
    {
        if (!IsSpawned || _rb == null) return;

        var nm = NetworkManager;
        if (nm == null) return;

        // ---- Round reset handling (teleport-safe) ----
        var gsm = GameStateManagerNew.Instance;
        if (gsm != null)
        {
            int rid = gsm.RoundId;
            if (rid != _lastRoundIdSeen)
            {
                _lastRoundIdSeen = rid;

                // Only clients need this (server is authoritative)
                if (!IsServer)
                    ResetClientPredictionToCurrent();
            }
        }

        int serverTick = nm.NetworkTickSystem.ServerTime.Tick;

        // HOST: read input locally, feed server sim
        if (IsServer && IsOwner)
        {
            VehicleInputNew hostCmd = GatherInput(serverTick);
            Debug_LastLocalInput = hostCmd;

            _pendingServerInputs[serverTick] = hostCmd;

            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
            return;
        }

        // Owner client: predict VISUAL and send input
        if (IsOwner)
            OwnerPredictVisualTick(serverTick);

        // Dedicated server: sim + broadcast
        if (IsServer)
        {
            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
        }
    }

    // -------------------------
    // Owner prediction (visual only)
    // -------------------------
    private void OwnerPredictVisualTick(int serverTick)
    {
        if (visualRoot == null) return;

        VehicleInputNew cmd = GatherInput(serverTick);
        Debug_LastLocalInput = cmd;

        int idx = Mod(serverTick, bufferSize);
        _inputBuffer[idx] = cmd;

        _predictedVisualState = _model.Step(_predictedVisualState, cmd, DtPerTick);
        _predictedVisualState.Rotation = YawOnly(_predictedVisualState.Rotation);

        _stateBuffer[idx] = _predictedVisualState;

        visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);

        SubmitInputServerRpc(cmd);
    }

    private VehicleInputNew GatherInput(int tick)
    {
        // Match gating: only allow movement in InGame
        var gsm = GameStateManagerNew.Instance;
        if (gsm != null && !gsm.PlayersCanMove)
        {
            return new VehicleInputNew { Tick = tick, Throttle = 0f, Turn = 0f };
        }

        // Death gating: if dead, no input (respawn manager re-enables on respawn)
        var health = GetComponent<VehicleHealthNew>();
        if (health != null && health.IsDead)
        {
            return new VehicleInputNew { Tick = tick, Throttle = 0f, Turn = 0f };
        }

        var kb = Keyboard.current;

        float throttle = 0f;
        float turn = 0f;

        if (kb != null)
        {
            if (kb.wKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed) throttle -= 1f;

            if (kb.dKey.isPressed) turn += 1f;
            if (kb.aKey.isPressed) turn -= 1f;
        }

        return new VehicleInputNew
        {
            Tick = tick,
            Throttle = Mathf.Clamp(throttle, -1f, 1f),
            Turn = Mathf.Clamp(turn, -1f, 1f),
        };
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitInputServerRpc(VehicleInputNew cmd)
    {
        _pendingServerInputs[cmd.Tick] = cmd;
    }

    // -------------------------
    // Server authoritative simulation
    // -------------------------
    private void ServerSimTick(int serverTick)
    {
        // Tolerate late packets: exact tick else latest <= tick
        if (_pendingServerInputs.TryGetValue(serverTick, out var exact))
        {
            _lastServerInput = exact;
            _pendingServerInputs.Remove(serverTick);
        }
        else
        {
            int bestTick = int.MinValue;
            VehicleInputNew bestCmd = default;

            foreach (var kvp in _pendingServerInputs)
            {
                if (kvp.Key <= serverTick && kvp.Key > bestTick)
                {
                    bestTick = kvp.Key;
                    bestCmd = kvp.Value;
                }
            }

            if (bestTick != int.MinValue)
            {
                _lastServerInput = bestCmd;
                _pendingServerInputs.Remove(bestTick);
            }
        }

        Debug_LastServerAppliedInput = _lastServerInput;

        // Read current RB state (yaw-only seed)
        VehicleSimStateNew s = new VehicleSimStateNew
        {
            Position = _rb.position,
            Rotation = YawOnly(_rb.rotation),
            Velocity = _rb.linearVelocity,
            YawDegPerSec = _rb.angularVelocity.y * Mathf.Rad2Deg
        };

        // Step + enforce yaw-only
        VehicleSimStateNew stepped = _model.Step(s, _lastServerInput, DtPerTick);
        stepped.Rotation = YawOnly(stepped.Rotation);

        // Apply to RB
        _rb.linearVelocity = stepped.Velocity;

        float yawRadPerSec = stepped.YawDegPerSec * Mathf.Deg2Rad;
        _rb.angularVelocity = new Vector3(0f, yawRadPerSec, 0f);
    }

    // -------------------------
    // Snapshot broadcast + reconcile
    // -------------------------
    private void BroadcastSnapshot(int serverTick)
    {
        VehicleSimStateNew s = new VehicleSimStateNew
        {
            Position = _rb.position,
            Rotation = YawOnly(_rb.rotation),
            Velocity = _rb.linearVelocity,
            YawDegPerSec = _rb.angularVelocity.y * Mathf.Rad2Deg
        };

        VehicleSnapshotNew snap = VehicleSnapshotNew.FromSimState(serverTick, s);
        ReceiveSnapshotClientRpc(snap);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveSnapshotClientRpc(VehicleSnapshotNew snap)
    {
        if (!IsSpawned || visualRoot == null) return;

        Quaternion snapYaw = YawOnly(snap.Rotation);

        // Root follows server truth on clients
        if (!IsServer)
        {
            transform.SetPositionAndRotation(snap.Position, snapYaw);
        }

        // Non-owner: smooth visual
        if (!IsOwner)
        {
            _visualTargetPos = snap.Position;
            _visualTargetRot = snapYaw;
            return;
        }

        // Owner reconcile
        int idx = Mod(snap.Tick, bufferSize);
        VehicleSimStateNew predictedAtTick = _stateBuffer[idx];

        float posErr = Vector3.Distance(predictedAtTick.Position, snap.Position);
        float rotErr = Quaternion.Angle(YawOnly(predictedAtTick.Rotation), snapYaw);

        Debug_LastPosError = posErr;
        Debug_LastRotErrorDeg = rotErr;

        if (posErr < reconcilePosThreshold && rotErr < reconcileRotThresholdDeg)
        {
            // Keep seed close to server
            _predictedVisualState.Position = snap.Position;
            _predictedVisualState.Rotation = snapYaw;
            return;
        }

        // Hard reset to server truth
        _predictedVisualState = snap.ToSimState();
        _predictedVisualState.Rotation = snapYaw;

        int currentServerTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
        int delta = currentServerTick - snap.Tick;

        // If delta is weird, don't replay
        if (delta <= 0 || delta > bufferSize - 1)
        {
            visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);
            return;
        }

        int replayCount = Mathf.Min(delta, maxReplayTicks);
        int startTick = currentServerTick - replayCount + 1;

        for (int t = startTick; t <= currentServerTick; t++)
        {
            int bi = Mod(t, bufferSize);
            VehicleInputNew cmd = _inputBuffer[bi];

            _predictedVisualState = _model.Step(_predictedVisualState, cmd, DtPerTick);
            _predictedVisualState.Rotation = YawOnly(_predictedVisualState.Rotation);

            _stateBuffer[bi] = _predictedVisualState;
        }

        visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);
    }

    // -------------------------
    // Teleport/round reset helpers
    // -------------------------
    private void ResetClientPredictionToCurrent()
    {
        if (visualRoot == null) return;

        _predictedVisualState = new VehicleSimStateNew
        {
            Position = visualRoot.position,
            Rotation = YawOnly(visualRoot.rotation),
            Velocity = Vector3.zero,
            YawDegPerSec = 0f
        };

        if (_inputBuffer != null) System.Array.Clear(_inputBuffer, 0, _inputBuffer.Length);
        if (_stateBuffer != null) System.Array.Clear(_stateBuffer, 0, _stateBuffer.Length);

        _visualTargetPos = visualRoot.position;
        _visualTargetRot = YawOnly(visualRoot.rotation);
    }

    // NEW: called by RespawnManagerNew.NotifyVehicleRespawnedClientRpc(...)
    public void ClientForceResetPredictionNow()
    {
        if (!IsSpawned) return;
        if (IsServer) return; // server doesn't predict visuals

        ResetClientPredictionToCurrent();
    }

    private static int Mod(int x, int m)
    {
        int r = x % m;
        return r < 0 ? r + m : r;
    }
}
