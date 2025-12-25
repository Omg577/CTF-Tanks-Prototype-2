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

    [Header("Remote Visual Smoothing (Buffered Interpolation)")]
    [Tooltip("Render this many ticks behind the server tick so we can interpolate between known snapshots.")]
    [SerializeField] private int interpolationDelayTicks = 3;

    [Tooltip("How many remote snapshots to keep buffered (per vehicle).")]
    [SerializeField] private int remoteSnapshotBufferLimit = 32;

    [Tooltip("Allow a small extrapolation window if we run out of snapshots (helps during packet loss).")]
    [SerializeField] private int maxExtrapolationTicks = 2;

    [Tooltip("Optional extra exponential smoothing on top of buffered interpolation.")]
    [SerializeField] private bool extraExpSmoothing = false;

    [SerializeField] private float remoteVisualLerpSpeed = 18f; // used only if extraExpSmoothing is enabled

    private Rigidbody _rb;
    private IMovementModelNew _model;

    // Server input queue keyed by server tick
    private readonly Dictionary<int, VehicleInputNew> _pendingServerInputs = new();
    private VehicleInputNew _lastServerInput;

    // Owner prediction buffers (server tick domain)
    private VehicleInputNew[] _inputBuffer;
    private VehicleSimStateNew[] _stateBuffer;
    private VehicleSimStateNew _predictedVisualState;

    // Remote snapshots buffer (non-owner)
    private readonly List<VehicleSnapshotNew> _remoteSnapshots = new();

    // Tick hook
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
            return (float)nm.NetworkConfig.TickRate > 0
                ? 1f / nm.NetworkConfig.TickRate
                : Time.fixedDeltaTime;
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

        if (visualRoot == transform)
        {
            Debug.LogWarning($"{name}: visualRoot is the root transform. " +
                             "Create a child named 'Visual' and move meshes under it, " +
                             "so client-side visual prediction doesn't move the physics body.");
        }
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

        _remoteSnapshots.Clear();

        // Physics authority: only server simulates RB
        _rb.isKinematic = !IsServer;

        // Clients should not locally participate in collision/gravity resolution.
        // This prevents "popping", getting stuck, or fighting server corrections.
        if (!IsServer)
        {
            _rb.detectCollisions = false;
            _rb.useGravity = false;
        }
        else
        {
            _rb.detectCollisions = true;
            _rb.useGravity = false;
        }

        // Initialize predicted visual state (yaw-only)
        _predictedVisualState = new VehicleSimStateNew
        {
            Position = visualRoot.position,
            Rotation = YawOnly(visualRoot.rotation),
            Velocity = Vector3.zero,
            YawDegPerSec = 0f
        };

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

        // Non-owner: buffered snapshot interpolation for smooth visuals
        if (!IsOwner)
        {
            if (TryGetRemoteInterpolatedPose(out Vector3 p, out Quaternion r))
            {
                if (extraExpSmoothing)
                {
                    float t = 1f - Mathf.Exp(-remoteVisualLerpSpeed * Time.deltaTime);
                    visualRoot.position = Vector3.Lerp(visualRoot.position, p, t);
                    visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, r, t);
                }
                else
                {
                    visualRoot.SetPositionAndRotation(p, r);
                }
            }
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
            int roundId = gsm.RoundId;
            if (_lastRoundIdSeen != roundId)
            {
                _lastRoundIdSeen = roundId;

                // Clear any buffered inputs/states to prevent replay after teleport/spawn reset
                if (!IsServer)
                {
                    ResetClientPredictionToCurrent();
                }
                else
                {
                    _pendingServerInputs.Clear();
                }
            }
        }

        int serverTick = nm.NetworkTickSystem.ServerTime.Tick;

        // Server: consume queued input and simulate authoritative root
        if (IsServer)
        {
            // Host owner: gather input locally and simulate immediately
            if (IsOwner)
            {
                VehicleInputNew hostCmd = GatherInput(serverTick);
                Debug_LastLocalInput = hostCmd;

                _pendingServerInputs[serverTick] = hostCmd;

                ServerSimTick(serverTick);
                BroadcastSnapshot(serverTick);
                return;
            }

            // Dedicated server: use last received input for each tick (or zero)
            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
            return;
        }

        // Owner client: predict VISUAL and send input
        if (IsOwner)
            OwnerPredictVisualTick(serverTick);
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
            Turn = Mathf.Clamp(turn, -1f, 1f)
        };
    }

    // -------------------------
    // Owner client prediction
    // -------------------------
    private void OwnerPredictVisualTick(int serverTick)
    {
        VehicleInputNew cmd = GatherInput(serverTick);
        Debug_LastLocalInput = cmd;

        int idx = Mod(serverTick, bufferSize);
        _inputBuffer[idx] = cmd;

        // Predict VISUAL state forward
        _predictedVisualState = _model.Step(_predictedVisualState, cmd, DtPerTick);
        _predictedVisualState.Rotation = YawOnly(_predictedVisualState.Rotation);

        _stateBuffer[idx] = _predictedVisualState;

        // Apply predicted pose to visual only
        visualRoot.SetPositionAndRotation(_predictedVisualState.Position, _predictedVisualState.Rotation);

        // Send input to server
        SubmitInputServerRpc(cmd);
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SubmitInputServerRpc(VehicleInputNew cmd)
    {
        // Store the input for this tick so server sim can consume it
        _pendingServerInputs[cmd.Tick] = cmd;
        _lastServerInput = cmd;
        Debug_LastServerAppliedInput = cmd;
    }

    // -------------------------
    // Server simulation
    // -------------------------
    private void ServerSimTick(int serverTick)
    {
        VehicleInputNew cmd;

        if (IsOwner)
        {
            // host owner sim already queued for this tick
            if (!_pendingServerInputs.TryGetValue(serverTick, out cmd))
                cmd = _lastServerInput;
        }
        else
        {
            // non-host players: use input from that tick if it arrived, else last
            if (!_pendingServerInputs.TryGetValue(serverTick, out cmd))
                cmd = _lastServerInput;
        }

        // Ensure tick set (when falling back)
        cmd.Tick = serverTick;

        VehicleSimStateNew s = new VehicleSimStateNew
        {
            Position = _rb.position,
            Rotation = YawOnly(_rb.rotation),
            Velocity = _rb.linearVelocity,
            YawDegPerSec = _rb.angularVelocity.y * Mathf.Rad2Deg
        };

        s = _model.Step(s, cmd, DtPerTick);
        s.Rotation = YawOnly(s.Rotation);

        _rb.MovePosition(s.Position);
        _rb.MoveRotation(s.Rotation);

        _rb.linearVelocity = s.Velocity;
        _rb.angularVelocity = new Vector3(0f, s.YawDegPerSec * Mathf.Deg2Rad, 0f);
    }

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
        if (!IsServer && IsOwner)
        {
            transform.SetPositionAndRotation(snap.Position, snapYaw);
        }


        // Non-owner: buffer snapshots for interpolation
        if (!IsOwner)
        {
            snap.Rotation = snapYaw; // ensure yaw-only stored
            EnqueueRemoteSnapshot(snap);
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
    // Remote snapshot interpolation (non-owner)
    // -------------------------

    private void EnqueueRemoteSnapshot(VehicleSnapshotNew snap)
    {
        int n = _remoteSnapshots.Count;

        if (n > 0)
        {
            int lastTick = _remoteSnapshots[n - 1].Tick;

            if (snap.Tick < lastTick)
            {
                // Late/out-of-order: insert in correct place (or replace duplicate)
                for (int i = 0; i < n; i++)
                {
                    if (_remoteSnapshots[i].Tick == snap.Tick)
                    {
                        _remoteSnapshots[i] = snap;
                        return;
                    }

                    if (_remoteSnapshots[i].Tick > snap.Tick)
                    {
                        _remoteSnapshots.Insert(i, snap);
                        TrimRemoteBuffer();
                        return;
                    }
                }

                // Older than everything we have: ignore
                return;
            }

            if (snap.Tick == lastTick)
            {
                _remoteSnapshots[n - 1] = snap;
                return;
            }
        }

        _remoteSnapshots.Add(snap);
        TrimRemoteBuffer();
    }

    private void TrimRemoteBuffer()
    {
        int over = _remoteSnapshots.Count - remoteSnapshotBufferLimit;
        if (over > 0)
            _remoteSnapshots.RemoveRange(0, over);
    }

    private bool TryGetRemoteInterpolatedPose(out Vector3 pos, out Quaternion rot)
    {
        pos = visualRoot.position;
        rot = YawOnly(visualRoot.rotation);

        if (_remoteSnapshots.Count == 0) return false;
        if (NetworkManager == null) return false;

        int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
        int renderTick = serverTick - interpolationDelayTicks;

        // Clamp before oldest
        VehicleSnapshotNew oldest = _remoteSnapshots[0];
        if (renderTick <= oldest.Tick)
        {
            pos = oldest.Position;
            rot = YawOnly(oldest.Rotation);
            return true;
        }

        VehicleSnapshotNew newest = _remoteSnapshots[_remoteSnapshots.Count - 1];

        // If we're beyond newest, optionally extrapolate a tiny bit
        if (renderTick >= newest.Tick)
        {
            int dtTicks = renderTick - newest.Tick;

            if (dtTicks <= maxExtrapolationTicks)
            {
                float dt = dtTicks * DtPerTick;
                pos = newest.Position + newest.Velocity * dt;

                float yawDelta = newest.YawDegPerSec * dt;
                rot = YawOnly(newest.Rotation) * Quaternion.Euler(0f, yawDelta, 0f);
                return true;
            }

            pos = newest.Position;
            rot = YawOnly(newest.Rotation);
            return true;
        }

        // Find surrounding snapshots (linear scan is fine for small buffers)
        VehicleSnapshotNew a = oldest;
        VehicleSnapshotNew b = newest;

        for (int i = 0; i < _remoteSnapshots.Count - 1; i++)
        {
            VehicleSnapshotNew s0 = _remoteSnapshots[i];
            VehicleSnapshotNew s1 = _remoteSnapshots[i + 1];

            if (s0.Tick <= renderTick && renderTick <= s1.Tick)
            {
                a = s0;
                b = s1;
                break;
            }
        }

        int span = Mathf.Max(1, b.Tick - a.Tick);
        float alpha = Mathf.Clamp01((renderTick - a.Tick) / (float)span);

        pos = Vector3.Lerp(a.Position, b.Position, alpha);
        rot = Quaternion.Slerp(YawOnly(a.Rotation), YawOnly(b.Rotation), alpha);
        return true;
    }

    // -------------------------
    // Teleport/round reset helpers
    // -------------------------
    private void ResetClientPredictionToCurrent()
    {
        if (visualRoot == null) return;

        _remoteSnapshots.Clear();

        _predictedVisualState = new VehicleSimStateNew
        {
            Position = visualRoot.position,
            Rotation = YawOnly(visualRoot.rotation),
            Velocity = Vector3.zero,
            YawDegPerSec = 0f
        };

        if (_inputBuffer != null) System.Array.Clear(_inputBuffer, 0, _inputBuffer.Length);
        if (_stateBuffer != null) System.Array.Clear(_stateBuffer, 0, _stateBuffer.Length);
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
