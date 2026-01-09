using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Camera))]
public class CameraFollowNew : MonoBehaviour
{
    [Header("Target & Offset")]
    public Transform target;

    [Tooltip("Local-space style offset in world units (x=right, y=up, z=back).")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -14f);

    [Tooltip("Pitch angle looking down (degrees).")]
    [SerializeField, Range(5f, 85f)] private float cameraAngle = 55f;

    [Tooltip("Yaw angle around world up (degrees). 0 means looking along world +Z.")]
    [SerializeField] private float yawDegrees = 0f;

    [Header("Dead Zone (world units)")]
    [Tooltip("How far the target can move from camera center before the camera follows.")]
    [SerializeField] private Vector2 deadZone = new Vector2(1.2f, 1.2f);

    [Header("Smoothing / Follow Feel")]
    [Tooltip("Smoothing time while moving (smaller = snappier).")]
    [SerializeField] private float smoothTimeMoving = 0.08f;

    [Tooltip("Smoothing time while idle (larger = steadier).")]
    [SerializeField] private float smoothTimeIdle = 0.16f;

    [Tooltip("Speed threshold to consider 'idle' (reduces micro wobble).")]
    [SerializeField] private float idleSpeedThreshold = 0.25f;

    [Header("Dynamic Follow Speed (optional)")]
    [Tooltip("If enabled, camera responsiveness increases with target speed.")]
    [SerializeField] private bool dynamicResponsiveness = true;

    [Tooltip("Scales responsiveness based on target speed.")]
    [SerializeField] private float speedMultiplier = 0.06f;

    [Tooltip("Minimum responsiveness boost when nearly stopped.")]
    [SerializeField] private float minResponsivenessBoost = 0.0f;

    [Header("Level Bounds (XZ)")]
    [Tooltip("Minimum X,Z the camera can go.")]
    public Vector2 minBounds;

    [Tooltip("Maximum X,Z the camera can go.")]
    public Vector2 maxBounds;

    [Tooltip("If false, no bounds clamping is applied.")]
    [SerializeField] private bool useBounds = false;

    [Header("Freeze Zones")]
    [Tooltip("Assign BoxColliders (IsTrigger) here to freeze camera when player enters.")]
    public List<Collider> freezeZones = new List<Collider>();
    private bool followEnabled = true;

    [Header("Force Snap")]
    [SerializeField] private float forceSnapDuration = 0.35f;
    [SerializeField] private float forceSnapLerpSpeed = 40f;
    private float forceSnapTimer = 0f;

    // Internals
    private Vector3 _posVel;
    private Vector3 _lastTargetPos;
    private bool _hasLastPos;

    private void Start()
    {
        ApplyBaseRotation();

        if (target != null)
        {
            _lastTargetPos = target.position;
            _hasLastPos = true;
        }
    }

    private void LateUpdate()
    {
        if (!followEnabled || target == null) return;

        // --- Stable planar velocity from position delta (works for netcode/visual targets)
        float dt = Mathf.Max(1e-5f, Time.deltaTime);
        Vector3 planarVel = Vector3.zero;

        if (!_hasLastPos)
        {
            _lastTargetPos = target.position;
            _hasLastPos = true;
        }
        else
        {
            Vector3 v = (target.position - _lastTargetPos) / dt;
            _lastTargetPos = target.position;

            v.y = 0f;
            planarVel = v;
        }

        float speed = planarVel.magnitude;

        // --- Compute desired camera world rotation & offset direction
        Quaternion yawRot = Quaternion.Euler(0f, yawDegrees, 0f);
        Quaternion pitchRot = Quaternion.Euler(cameraAngle, 0f, 0f);
        Quaternion camRot = yawRot * pitchRot;

        // Offset is applied in the camera's yaw space (so "back" stays consistent with yaw)
        Vector3 worldOffset = yawRot * offset;

        // Raw desired camera position
        Vector3 desiredPos = target.position + worldOffset;
        Vector3 cur = transform.position;

        // --- Dead-zone logic (only XZ)
        float x = cur.x;
        if (desiredPos.x > cur.x + deadZone.x) x = desiredPos.x - deadZone.x;
        else if (desiredPos.x < cur.x - deadZone.x) x = desiredPos.x + deadZone.x;

        float z = cur.z;
        if (desiredPos.z > cur.z + deadZone.y) z = desiredPos.z - deadZone.y;
        else if (desiredPos.z < cur.z - deadZone.y) z = desiredPos.z + deadZone.y;

        Vector3 nextPos = new Vector3(x, desiredPos.y, z);

        // --- Bounds clamp (XZ only)
        if (useBounds)
        {
            nextPos.x = Mathf.Clamp(nextPos.x, minBounds.x, maxBounds.x);
            nextPos.z = Mathf.Clamp(nextPos.z, minBounds.y, maxBounds.y);
        }

        // --- Follow smoothing
        float smoothTime = (speed >= idleSpeedThreshold) ? smoothTimeMoving : smoothTimeIdle;

        // Optional speed-based responsiveness: lower smoothTime as speed increases
        if (dynamicResponsiveness)
        {
            float boost = Mathf.Max(speed * speedMultiplier, minResponsivenessBoost);
            smoothTime = Mathf.Max(0.02f, smoothTime - boost);
        }

        // Force snap overrides smoothing for a short burst
        if (forceSnapTimer > 0f)
        {
            float t = 1f - Mathf.Exp(-forceSnapLerpSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, nextPos, t);
            forceSnapTimer -= Time.deltaTime;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(transform.position, nextPos, ref _posVel, smoothTime);
        }

        // Rotation: stable angle (no wobble)
        transform.rotation = camRot;

        // If you prefer "always look at target" aiming instead, comment the line above and uncomment:
        // transform.rotation = Quaternion.LookRotation((target.position - transform.position).normalized, Vector3.up);
    }

    private void ApplyBaseRotation()
    {
        Quaternion yawRot = Quaternion.Euler(0f, yawDegrees, 0f);
        Quaternion pitchRot = Quaternion.Euler(cameraAngle, 0f, 0f);
        transform.rotation = yawRot * pitchRot;
    }

    // call these from zone triggers
    public void FreezeFollow() => followEnabled = false;
    public void UnfreezeFollow() => followEnabled = true;

    // call this from RespawnManager / Health / etc.
    public void ForceSnap()
    {
        forceSnapTimer = forceSnapDuration;
        // Debug.Log("[CameraFollowNew] ForceSnap triggered.");
    }

    /// <summary>
    /// Instantly snaps camera to the "desired" position (based on offset/yaw),
    /// and resets smoothing/velocity history to prevent wobble after rebinding/respawn.
    /// </summary>
    public void SnapNow()
    {
        if (target == null) return;

        _posVel = Vector3.zero;
        _lastTargetPos = target.position;
        _hasLastPos = true;

        Quaternion yawRot = Quaternion.Euler(0f, yawDegrees, 0f);
        Vector3 worldOffset = yawRot * offset;

        transform.position = target.position + worldOffset;
        ApplyBaseRotation();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (freezeZones == null || freezeZones.Count == 0) return;
        if (freezeZones.Contains(other)) FreezeFollow();
    }

    private void OnTriggerExit(Collider other)
    {
        if (freezeZones == null || freezeZones.Count == 0) return;
        if (freezeZones.Contains(other)) UnfreezeFollow();
    }
}
