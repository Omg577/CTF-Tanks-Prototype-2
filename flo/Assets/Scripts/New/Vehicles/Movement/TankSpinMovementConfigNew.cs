using UnityEngine;

[CreateAssetMenu(menuName = "DualPayload/Movement/TankSpinMovementConfigNew")]
public class TankSpinMovementConfigNew : MovementModelConfigNew
{
    [Header("Linear")]
    [Min(0f)] public float maxSpeed = 14f;
    [Min(0f)] public float accel = 45f;
    [Min(0f)] public float decel = 70f;

    [Header("Angular")]
    [Min(0f)] public float maxTurnSpeedDeg = 540f;
    [Min(0f)] public float turnAccelDeg = 5000f;

    [Header("Vertical / Grounding")]
    [Tooltip("Gravity acceleration (negative). Used only when not grounded.")]
    public float gravity = -35f;

    [Tooltip("Desired height above ground contact point.")]
    [Min(0f)] public float rideHeight = 0.6f;

    [Tooltip("Extra distance beyond rideHeight to search for ground.")]
    [Min(0f)] public float probeDistance = 0.9f;

    [Tooltip("SphereCast radius. Bigger reduces jitter on edges/triangles.")]
    [Min(0f)] public float probeRadius = 0.45f;

    [Tooltip("Max slope angle considered ground.")]
    [Range(0f, 89f)] public float maxSlopeAngle = 55f;

    [Tooltip("Layers that count as ground. IMPORTANT: ONLY terrain/ramps. No vehicles, payloads, triggers.")]
    public LayerMask groundMask = ~0;

    [Header("Anti-bob / Anti-jitter")]
    [Tooltip("How quickly the target ride height is smoothed (higher = less jitter).")]
    [Min(0.01f)] public float groundHeightSharpness = 25f;

    [Tooltip("Stickiness band above the targetY where we still treat as grounded.")]
    [Min(0f)] public float groundedEpsilon = 0.25f;

    [Tooltip("Limit how fast Y is allowed to change while grounded (prevents spikes).")]
    [Min(0.01f)] public float maxGroundAdjustPerSecond = 20f;

    public override IMovementModelNew CreateRuntimeModel()
        => new TankSpinMovementModelNew(this);
}
