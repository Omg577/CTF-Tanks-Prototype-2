using System.Globalization;
using Unity.Netcode;
using UnityEngine;

public class VehicleDebugHUDNew : NetworkBehaviour
{
    [SerializeField] private bool show = true;

    private VehicleMovementNetcodeNew _move;
    private Rigidbody _rb;

    private void Awake()
    {
        _move = GetComponent<VehicleMovementNetcodeNew>();
        _rb = GetComponent<Rigidbody>();
    }

    private void OnGUI()
    {
        if (!show) return;
        if (!IsSpawned) return;
        if (!IsOwner) return; // only show for local owned vehicle

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        string mode =
            nm.IsHost ? "HOST" :
            nm.IsServer ? "SERVER" :
            nm.IsClient ? "CLIENT" : "OFF";

        int localTick = nm.NetworkTickSystem.LocalTime.Tick;
        int serverTick = nm.NetworkTickSystem.ServerTime.Tick;

        var li = _move != null ? _move.Debug_LastLocalInput : default;
        var si = _move != null ? _move.Debug_LastServerAppliedInput : default;

        float posErr = _move != null ? _move.Debug_LastPosError : 0f;
        float rotErr = _move != null ? _move.Debug_LastRotErrorDeg : 0f;

        int x = 10, y = 60, w = 520, h = 18;

        GUI.Label(new Rect(x, y, w, h), $"Mode={mode}   LocalClientId={nm.LocalClientId}   IsOwner={IsOwner}   IsServer={IsServer}");
        y += h;

        GUI.Label(new Rect(x, y, w, h), $"Ticks: Local={localTick}   Server={serverTick}");
        y += h;

        GUI.Label(new Rect(x, y, w, h), $"Input(Local): tick={li.Tick} thr={li.Throttle:0.00} turn={li.Turn:0.00}");
        y += h;

        GUI.Label(new Rect(x, y, w, h), $"Input(ServerApplied): tick={si.Tick} thr={si.Throttle:0.00} turn={si.Turn:0.00}");
        y += h;

        GUI.Label(new Rect(x, y, w, h), $"Reconcile error: pos={posErr:0.000}m rot={rotErr:0.00}deg");
        y += h;

        if (_rb != null)
        {
            GUI.Label(new Rect(x, y, w, h), $"RB: kinematic={_rb.isKinematic} linearVel={_rb.linearVelocity.magnitude:0.00} angVelY(deg/s)={_rb.angularVelocity.y * Mathf.Rad2Deg:0.0}");
            y += h;
        }

        GUI.Label(new Rect(x, y, w, h), $"Tip: Attach camera to Visual (not root) for smooth owner prediction.");
    }
}
