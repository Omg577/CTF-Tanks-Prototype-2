using UnityEngine;

public class TopDownFollowCameraNew : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Framing (Brawl Stars-ish)")]
    public float height = 16f;
    public float distance = 12f;
    [Range(25f, 85f)] public float tiltDegrees = 58f;
    public float yawDegrees = 45f;

    [Header("Follow Smoothing")]
    [Tooltip("How quickly the camera catches up while moving (smaller = snappier).")]
    public float smoothTimeMoving = 0.06f;

    [Tooltip("How quickly the camera catches up while stopping/idle (larger = more stable).")]
    public float smoothTimeIdle = 0.14f;

    [Tooltip("If target speed is below this, treat as idle (reduces bob).")]
    public float idleSpeedThreshold = 0.25f;

    [Header("Rotation Smoothing")]
    public float rotationLerpSpeed = 10f;

    [Header("Look-ahead (reduce motion sickness)")]
    public bool enableLookAhead = true;

    [Tooltip("How far to offset camera toward movement direction (keep small, like 0.75-2.0).")]
    public float lookAheadDistance = 1.25f;

    [Tooltip("Ignore tiny movement changes (prevents micro-wobble).")]
    public float lookAheadDeadzoneSpeed = 1.25f;

    [Tooltip("Max change speed of look-ahead per second (clamps the 'swing').")]
    public float lookAheadMaxDeltaPerSec = 6f;

    [Tooltip("How quickly look-ahead reacts (lower = steadier).")]
    public float lookAheadLerpSpeed = 5f;

    [Tooltip("Use Rigidbody velocity for look-ahead.")]
    public bool useVelocityLookAhead = true;

    private Vector3 _posVel;
    private Vector3 _lookAheadCurrent;
    private Rigidbody _targetRb;

    private void LateUpdate()
    {
        if (target == null) return;

        if (_targetRb == null)
            _targetRb = target.GetComponent<Rigidbody>();

        // Build desired camera rotation (fixed style)
        Quaternion yawRot = Quaternion.Euler(0f, yawDegrees, 0f);
        Quaternion tiltRot = Quaternion.Euler(tiltDegrees, 0f, 0f);
        Quaternion camRot = yawRot * tiltRot;

        // --- Compute planar velocity ---
        Vector3 planarVel = Vector3.zero;
        if (useVelocityLookAhead && _targetRb != null)
        {
            planarVel = _targetRb.linearVelocity;
            planarVel.y = 0f;
        }

        float speed = planarVel.magnitude;

        // --- Look-ahead (deadzone + clamp) ---
        Vector3 desiredLookAhead = Vector3.zero;

        if (enableLookAhead && speed >= lookAheadDeadzoneSpeed)
        {
            desiredLookAhead = planarVel.normalized * lookAheadDistance;
        }

        // Smooth look-ahead but cap per-frame delta to avoid "drunk swing"
        float tLA = 1f - Mathf.Exp(-lookAheadLerpSpeed * Time.deltaTime);
        Vector3 nextLookAhead = Vector3.Lerp(_lookAheadCurrent, desiredLookAhead, tLA);

        float maxDelta = lookAheadMaxDeltaPerSec * Time.deltaTime;
        Vector3 delta = nextLookAhead - _lookAheadCurrent;
        if (delta.magnitude > maxDelta)
            nextLookAhead = _lookAheadCurrent + delta.normalized * maxDelta;

        _lookAheadCurrent = nextLookAhead;

        // --- Desired camera position ---
        Vector3 focusPoint = target.position + _lookAheadCurrent;

        Vector3 desiredPos =
            focusPoint
            - (camRot * Vector3.forward) * distance
            + Vector3.up * height;

        // --- Adaptive follow smoothing ---
        float smoothTime = (speed >= idleSpeedThreshold) ? smoothTimeMoving : smoothTimeIdle;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVel, smoothTime);

        // --- Rotation (look at focus point) ---
        Quaternion desiredRot = Quaternion.LookRotation((focusPoint - transform.position).normalized, Vector3.up);
        float tRot = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, tRot);
    }
}
