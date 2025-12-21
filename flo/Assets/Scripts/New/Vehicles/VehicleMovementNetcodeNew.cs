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

    [Header("Visual Root (Phase 6)")]
    [Tooltip("This transform is what we predict/smooth. Root stays authoritative.")]
    [SerializeField] private Transform visualRoot;

    [Header("Prediction/Reconcile")]
    [SerializeField] private float reconcilePosThreshold = 0.75f;    // loosened for Phase 6
    [SerializeField] private float reconcileRotThresholdDeg = 12f;    // loosened for Phase 6
    [SerializeField] private int bufferSize = 1024;

    [Header("Remote Visual Smoothing")]
    [SerializeField] private float remoteVisualLerpSpeed = 18f;

    private Rigidbody _rb;
    private IMovementModelNew _model;

    // Server: inputs keyed by *server tick*
    private readonly Dictionary<int, VehicleInputNew> _pendingServerInputs = new();
    private VehicleInputNew _lastServerInput;

    // Owner prediction buffers (server tick domain)
    private VehicleInputNew[] _inputBuffer;
    private VehicleSimStateNew[] _stateBuffer;
    private VehicleSimStateNew _predictedVisualState; // predicted state for VISUAL ONLY

    // Visual smoothing targets (non-owner)
    private Vector3 _visualTargetPos;
    private Quaternion _visualTargetRot;

    // Debug info (Phase 7 HUD reads these)
    public VehicleInputNew Debug_LastLocalInput { get; private set; }
    public VehicleInputNew Debug_LastServerAppliedInput { get; private set; }
    public float Debug_LastPosError { get; private set; }
    public float Debug_LastRotErrorDeg { get; private set; }

    private float DtPerTick => 1f / NetworkManager.NetworkTickSystem.TickRate;

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();

        if (vehicleConfig == null || vehicleConfig.movementModel == null)
        {
            Debug.LogError($"{name}: Missing VehicleConfigNew or movementModel.");
            enabled = false;
            return;
        }

        // Auto-find Visual child if not assigned
        if (visualRoot == null)
        {
            Transform found = transform.Find("Visual");
            visualRoot = found != null ? found : transform;
        }

        _model = vehicleConfig.movementModel.CreateRuntimeModel();

        _inputBuffer = new VehicleInputNew[bufferSize];
        _stateBuffer = new VehicleSimStateNew[bufferSize];

        // Physics authority: only server simulates RB
        _rb.isKinematic = !IsServer;

        // Initialize predicted VISUAL state from current pose
        _predictedVisualState = new VehicleSimStateNew
        {
            Position = visualRoot.position,
            Rotation = visualRoot.rotation,
            Velocity = Vector3.zero,
            YawDegPerSec = 0f
        };

        _visualTargetPos = visualRoot.position;
        _visualTargetRot = visualRoot.rotation;

        NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
    }

    private void Update()
    {
        // Non-owner: smooth VISUAL towards visual targets
        if (!IsOwner)
        {
            float t = 1f - Mathf.Exp(-remoteVisualLerpSpeed * Time.deltaTime);
            visualRoot.position = Vector3.Lerp(visualRoot.position, _visualTargetPos, t);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, _visualTargetRot, t);
        }
        else
        {
            // Host owner: keep visual snapped to root (no prediction needed)
            if (IsServer)
            {
                // If Visual is a child, lock it to root pose
                // (If visualRoot == transform, this does nothing)
                visualRoot.position = transform.position;
                visualRoot.rotation = transform.rotation;
            }
        }
    }

    private void OnNetworkTick()
    {
        int serverTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;

        // HOST: read input locally, feed server sim; visual just follows root (handled in Update)
        if (IsServer && IsOwner)
        {
            VehicleInputNew hostCmd = GatherInput(serverTick);
            Debug_LastLocalInput = hostCmd;
            _pendingServerInputs[serverTick] = hostCmd;

            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
            return;
        }

        // Owner client: predict VISUAL and send input keyed by serverTick
        if (IsOwner)
        {
            OwnerPredictVisualTick(serverTick);
        }

        // Server: authoritative sim + snapshot
        if (IsServer)
        {
            ServerSimTick(serverTick);
            BroadcastSnapshot(serverTick);
        }
    }

    // -------------------------
    // Owner (client): predict VISUAL + send input
    // -------------------------
    private void OwnerPredictVisualTick(int serverTick)
    {
        VehicleInputNew cmd = GatherInput(serverTick);
        Debug_LastLocalInput = cmd;

        int idx = Mod(serverTick, bufferSize);
        _inputBuffer[idx] = cmd;

        _predictedVisualState = _model.Step(_predictedVisualState, cmd, DtPerTick);
        _stateBuffer[idx] = _predictedVisualState;

        // PHASE 6: only move VISUAL for prediction (root stays authoritative)
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

    // NGO new-style RPC attribute (replaces RequireOwnership)
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitInputServerRpc(VehicleInputNew cmd)
    {
        _pendingServerInputs[cmd.Tick] = cmd;
    }

    // -------------------------
    // Server: authoritative sim
    // -------------------------
    private void ServerSimTick(int serverTick)
    {
        // PHASE 6: tolerate late packets
        // Prefer exact tick, else use latest input <= serverTick
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

    // -------------------------
    // Snapshots
    // -------------------------
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
        // PHASE 6: Always set ROOT immediately to server truth on clients.
        // This prevents "physics vs smoothing fighting" and keeps authoritative position consistent.
        if (!IsServer) // don't override server's RB-driven transform
        {
            transform.SetPositionAndRotation(snap.Position, snap.Rotation);
        }

        // Non-owner: update VISUAL smoothing targets
        if (!IsOwner)
        {
            _visualTargetPos = snap.Position;
            _visualTargetRot = snap.Rotation;
            return;
        }

        // Owner: reconcile predicted VISUAL history with server snapshot
        int idx = Mod(snap.Tick, bufferSize);
        VehicleSimStateNew predictedAtTick = _stateBuffer[idx];

        float posErr = Vector3.Distance(predictedAtTick.Position, snap.Position);
        float rotErr = Quaternion.Angle(predictedAtTick.Rotation, snap.Rotation);

        Debug_LastPosError = posErr;
        Debug_LastRotErrorDeg = rotErr;

        if (posErr < reconcilePosThreshold && rotErr < reconcileRotThresholdDeg)
        {
            // Even if within threshold, keep our prediction seed near server to avoid drift
            _predictedVisualState.Position = snap.Position;
            _predictedVisualState.Rotation = snap.Rotation;
            return;
        }

        // Hard reset to server truth
        _predictedVisualState = snap.ToSimState();

        // Replay inputs from snap tick to current server tick
        int currentServerTick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
        for (int t = snap.Tick + 1; t <= currentServerTick; t++)
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
