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

    private readonly Dictionary<int, VehicleInputNew> _pendingServerInputs = new();
    private VehicleInputNew _lastServerInput;

    private VehicleInputNew[] _inputBuffer;
    private VehicleSimStateNew[] _stateBuffer;
    private VehicleSimStateNew _predictedVisualState;

    private Vector3 _visualTargetPos;
    private Quaternion _visualTargetRot;

    private bool _tickHooked;

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

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        if (visualRoot == null)
        {
            Transform found = transform.Find("Visual");
            visualRoot = found != null ? found : transform;
        }

        _visualTargetPos = visualRoot.position;
        _visualTargetRot = visualRoot.rotation;
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

        _rb.isKinematic = !IsServer;

        _predictedVisualState = new VehicleSimStateNew
        {
            Position = visualRoot.position,
            Rotation = visualRoot.rotation,
            Velocity = Vector3.zero,
            YawDegPerSec = 0f
        };

        _visualTargetPos = visualRoot.position;
        _visualTargetRot = visualRoot.rotation;

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
        if (visualRoot == null) return;
        if (!IsSpawned) return;

        if (!IsOwner)
        {
            float t = 1f - Mathf.Exp(-remoteVisualLerpSpeed * Time.deltaTime);
            visualRoot.position = Vector3.Lerp(visualRoot.position, _visualTargetPos, t);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, _visualTargetRot, t);
        }
        else if (IsServer)
        {
            visualRoot.position = transform.position;
            visualRoot.rotation = transform.rotation;
        }
    }

    private void OnNetworkTick()
    {
        if (!IsSpawned) return;
        if (_rb == null) return;

        var nm = NetworkManager;
        if (nm == null) return;

        int serverTick = nm.NetworkTickSystem.ServerTime.Tick;

        // Host: read input locally, feed server sim
        if (IsServer && IsOwner)
        {
            VehicleInputNew hostCmd = GatherInput(serverTick);
            Debug_LastLocalInput = hostCmd;
            _pendingServerInputs[serverTick] = hostCmd;

            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
            return;
        }

        if (IsOwner)
            OwnerPredictVisualTick(serverTick);

        if (IsServer)
        {
            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
        }
    }

    private void OwnerPredictVisualTick(int serverTick)
    {
        if (visualRoot == null) return;

        VehicleInputNew cmd = GatherInput(serverTick);
        Debug_LastLocalInput = cmd;

        int idx = Mod(serverTick, bufferSize);
        _inputBuffer[idx] = cmd;

        _predictedVisualState = _model.Step(_predictedVisualState, cmd, DtPerTick);
        _stateBuffer[idx] = _predictedVisualState;

        visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);

        SubmitInputServerRpc(cmd);
    }

    private VehicleInputNew GatherInput(int tick)
    {
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

        VehicleSimStateNew s = new VehicleSimStateNew
        {
            Position = _rb.position,
            Rotation = _rb.rotation,
            Velocity = _rb.linearVelocity,
            YawDegPerSec = _rb.angularVelocity.y * Mathf.Rad2Deg
        };

        VehicleSimStateNew stepped = _model.Step(s, _lastServerInput, DtPerTick);

        _rb.linearVelocity = stepped.Velocity;
        float yawRadPerSec = stepped.YawDegPerSec * Mathf.Deg2Rad;
        _rb.angularVelocity = new Vector3(0f, yawRadPerSec, 0f);
    }

    private void BroadcastSnapshot(int serverTick)
    {
        VehicleSimStateNew s = new VehicleSimStateNew
        {
            Position = _rb.position,
            Rotation = _rb.rotation,
            Velocity = _rb.linearVelocity,
            YawDegPerSec = _rb.angularVelocity.y * Mathf.Rad2Deg
        };

        VehicleSnapshotNew snap = VehicleSnapshotNew.FromSimState(serverTick, s);
        ReceiveSnapshotClientRpc(snap);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveSnapshotClientRpc(VehicleSnapshotNew snap)
    {
        if (!IsSpawned) return;
        if (visualRoot == null) return;

        // Root snaps to server truth on clients only
        if (!IsServer)
            transform.SetPositionAndRotation(snap.Position, snap.Rotation);

        if (!IsOwner)
        {
            _visualTargetPos = snap.Position;
            _visualTargetRot = snap.Rotation;
            return;
        }

        int idx = Mod(snap.Tick, bufferSize);
        VehicleSimStateNew predictedAtTick = _stateBuffer[idx];

        float posErr = Vector3.Distance(predictedAtTick.Position, snap.Position);
        float rotErr = Quaternion.Angle(predictedAtTick.Rotation, snap.Rotation);

        Debug_LastPosError = posErr;
        Debug_LastRotErrorDeg = rotErr;

        if (posErr < reconcilePosThreshold && rotErr < reconcileRotThresholdDeg)
        {
            _predictedVisualState.Position = snap.Position;
            _predictedVisualState.Rotation = snap.Rotation;
            return;
        }

        // ---- CRITICAL FIX: cap replay work to prevent stalls/hangs ----
        int currentServerTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
        int delta = currentServerTick - snap.Tick;

        // If delta is weird (negative or huge), don't replay; just reset to server state.
        if (delta <= 0 || delta > bufferSize - 1)
        {
            _predictedVisualState = snap.ToSimState();
            visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);
            return;
        }

        int replayCount = Mathf.Min(delta, maxReplayTicks);

        _predictedVisualState = snap.ToSimState();

        // Replay only the last 'replayCount' ticks
        int startTick = currentServerTick - replayCount + 1;
        for (int t = startTick; t <= currentServerTick; t++)
        {
            int bi = Mod(t, bufferSize);
            VehicleInputNew cmd = _inputBuffer[bi];
            _predictedVisualState = _model.Step(_predictedVisualState, cmd, DtPerTick);
            _stateBuffer[bi] = _predictedVisualState;
        }

        visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);
    }

    private static int Mod(int x, int m)
    {
        int r = x % m;
        return r < 0 ? r + m : r;
    }
}
