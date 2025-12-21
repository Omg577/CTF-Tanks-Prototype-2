using UnityEngine;

public class TankSpinMovementModelNew : IMovementModelNew
{
    private readonly TankSpinMovementConfigNew _cfg;

    public TankSpinMovementModelNew(TankSpinMovementConfigNew cfg) => _cfg = cfg;

    public VehicleSimStateNew Step(VehicleSimStateNew s, VehicleInputNew input, float dt)
    {
        // Desired forward speed
        float desiredSpeed = Mathf.Clamp(input.Throttle, -1f, 1f) * _cfg.maxSpeed;

        // Current forward speed along forward axis
        Vector3 fwd = s.Rotation * Vector3.forward;
        float currentFwdSpeed = Vector3.Dot(s.Velocity, fwd);

        float accel = Mathf.Abs(desiredSpeed) > Mathf.Abs(currentFwdSpeed) ? _cfg.accel : _cfg.decel;
        float newFwdSpeed = Mathf.MoveTowards(currentFwdSpeed, desiredSpeed, accel * dt);

        // PHASE 6: MVP ground clamp (no vertical sim)
        s.Velocity = fwd * newFwdSpeed;

        // Yaw control
        float desiredYawDegPerSec = Mathf.Clamp(input.Turn, -1f, 1f) * _cfg.maxTurnSpeedDeg;
        s.YawDegPerSec = Mathf.MoveTowards(s.YawDegPerSec, desiredYawDegPerSec, _cfg.turnAccelDeg * dt);

        // Integrate
        s.Rotation = Quaternion.AngleAxis(s.YawDegPerSec * dt, Vector3.up) * s.Rotation;
        s.Position += s.Velocity * dt;

        return s;
    }
}
