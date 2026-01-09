using UnityEngine;

/// <summary>
/// Planar-only tank spin model:
/// - Controls yaw rate and XZ velocity
/// - Does NOT touch vertical (Y). Gravity/ramps/bumps are handled by Rigidbody physics on the server.
/// </summary>
public class TankSpinMovementModelNew : IMovementModelNew
{
    private readonly TankSpinMovementConfigNew _cfg;

    public TankSpinMovementModelNew(TankSpinMovementConfigNew cfg)
    {
        _cfg = cfg;
    }

    public VehicleSimStateNew Step(VehicleSimStateNew s, VehicleInputNew input, float dt)
    {
        if (dt <= 0f) return s;

        // --- Yaw control ---
        float desiredYawDegPerSec = Mathf.Clamp(input.Turn, -1f, 1f) * _cfg.maxTurnSpeedDeg;
        s.YawDegPerSec = Mathf.MoveTowards(s.YawDegPerSec, desiredYawDegPerSec, _cfg.turnAccelDeg * dt);

        // integrate yaw (yaw-only)
        s.Rotation = Quaternion.AngleAxis(s.YawDegPerSec * dt, Vector3.up) * s.Rotation;

        // --- Forward speed control (XZ only) ---
        Vector3 fwd = s.Rotation * Vector3.forward;

        Vector3 planarVel = new Vector3(s.Velocity.x, 0f, s.Velocity.z);
        float currentFwdSpeed = Vector3.Dot(planarVel, fwd);

        float desiredSpeed = Mathf.Clamp(input.Throttle, -1f, 1f) * _cfg.maxSpeed;
        float accel = Mathf.Abs(desiredSpeed) > Mathf.Abs(currentFwdSpeed) ? _cfg.accel : _cfg.decel;
        float newFwdSpeed = Mathf.MoveTowards(currentFwdSpeed, desiredSpeed, accel * dt);

        Vector3 newPlanarVel = fwd * newFwdSpeed;

        // Preserve Y velocity (physics owns vertical)
        s.Velocity = new Vector3(newPlanarVel.x, s.Velocity.y, newPlanarVel.z);

        // Preserve Position here (server RB will move itself)
        // We still return the state for prediction buffers etc.
        s.Position = s.Position;

        return s;
    }
}
