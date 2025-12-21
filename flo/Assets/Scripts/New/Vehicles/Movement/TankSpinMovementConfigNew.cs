using UnityEngine;

[CreateAssetMenu(menuName = "DualPayload/Movement/TankSpinMovementConfigNew")]
public class TankSpinMovementConfigNew : MovementModelConfigNew
{
    [Header("Linear")]
    public float maxSpeed = 9f;
    public float accel = 30f;
    public float decel = 35f;

    [Header("Angular")]
    public float maxTurnSpeedDeg = 180f;
    public float turnAccelDeg = 720f;

    public override IMovementModelNew CreateRuntimeModel()
        => new TankSpinMovementModelNew(this);
}
