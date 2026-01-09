using UnityEngine;

/// <summary>
/// Visual-only slope alignment using 4 ground samples.
/// - Preserves yaw from root
/// - Applies pitch/roll to match averaged ground normal
/// - Adjusts visualRoot local Y so the mesh doesn't float on slopes
/// 
/// IMPORTANT:
/// - visualRoot should be a child under the networked/physics root
/// - groundMask must include ONLY terrain/ramps (no vehicles, triggers)
/// </summary>
public class VehicleSlopeAlignNew : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform visualRoot;

    [Header("Sampling")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float sampleStartHeight = 1.5f;
    [SerializeField] private float sampleDistance = 4.0f;

    [Tooltip("Local offsets (meters) around the vehicle used for ground sampling.")]
    [SerializeField] private float sampleForward = 0.9f;
    [SerializeField] private float sampleRight = 0.7f;

    [Header("Limits")]
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 70f;

    [Header("Smoothing")]
    [SerializeField, Min(0.01f)] private float tiltSharpness = 20f;
    [SerializeField, Min(0.01f)] private float heightSharpness = 25f;

    [Header("Height (visual)")]
    [Tooltip("How far above the sampled ground plane the visual should sit (meters). Small: 0.00 to 0.08.")]
    [SerializeField] private float visualClearance = 0.02f;

    [Tooltip("Clamp the visual height adjustment speed to avoid spikes (meters/sec).")]
    [SerializeField] private float maxLocalYAdjustPerSecond = 2.0f;

    private Vector3 _baseLocalPos;
    private float _currentLocalY;

    private void Awake()
    {
        if (!visualRoot) visualRoot = transform;
        _baseLocalPos = visualRoot.localPosition;
        _currentLocalY = _baseLocalPos.y;
    }

    private void LateUpdate()
    {
        if (!visualRoot) return;

        // Sample points around the vehicle (world)
        Vector3 pF = transform.TransformPoint(new Vector3(0f, 0f, sampleForward));
        Vector3 pB = transform.TransformPoint(new Vector3(0f, 0f, -sampleForward));
        Vector3 pR = transform.TransformPoint(new Vector3(sampleRight, 0f, 0f));
        Vector3 pL = transform.TransformPoint(new Vector3(-sampleRight, 0f, 0f));

        bool okF = RayDown(pF, out RaycastHit hF);
        bool okB = RayDown(pB, out RaycastHit hB);
        bool okR = RayDown(pR, out RaycastHit hR);
        bool okL = RayDown(pL, out RaycastHit hL);

        int count = (okF ? 1 : 0) + (okB ? 1 : 0) + (okR ? 1 : 0) + (okL ? 1 : 0);
        if (count < 3) return;

        // Average normal + average ground height
        Vector3 n = Vector3.zero;
        float avgGroundY = 0f;

        if (okF) { n += hF.normal; avgGroundY += hF.point.y; }
        if (okB) { n += hB.normal; avgGroundY += hB.point.y; }
        if (okR) { n += hR.normal; avgGroundY += hR.point.y; }
        if (okL) { n += hL.normal; avgGroundY += hL.point.y; }

        n.Normalize();
        avgGroundY /= count;

        float slopeAngle = Vector3.Angle(n, Vector3.up);
        if (slopeAngle > maxSlopeAngle) return;

        // --- Rotation (preserve yaw, apply pitch/roll)
        float yaw = transform.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        Vector3 fwd = yawRot * Vector3.forward;
        Vector3 fwdOnPlane = Vector3.ProjectOnPlane(fwd, n);
        if (fwdOnPlane.sqrMagnitude < 1e-6f) return;
        fwdOnPlane.Normalize();

        Quaternion targetRot = Quaternion.LookRotation(fwdOnPlane, n);

        float tRot = 1f - Mathf.Exp(-tiltSharpness * Time.deltaTime);
        visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRot, tRot);

        // --- Height (convert desired world Y into a local Y adjustment)
        // We want the visualRoot's WORLD Y to be near (avgGroundY + visualClearance + baseVisualOffsetWorld)
        // Since visualRoot is a child, we adjust only its LOCAL y.

        // Current world Y of the parent/root
        float parentWorldY = transform.position.y;

        // Desired world Y for the visual root
        float targetWorldY = avgGroundY + visualClearance;

        // Convert to desired local Y (approx; assumes parent up is world up, which it is because root is yaw-only)
        float desiredLocalY = _baseLocalPos.y + (targetWorldY - parentWorldY);

        // Smooth & clamp adjustment speed
        float tH = 1f - Mathf.Exp(-heightSharpness * Time.deltaTime);

        float unclamped = Mathf.Lerp(_currentLocalY, desiredLocalY, tH);

        float maxStep = maxLocalYAdjustPerSecond * Time.deltaTime;
        _currentLocalY = Mathf.MoveTowards(_currentLocalY, unclamped, maxStep);

        Vector3 lp = visualRoot.localPosition;
        lp.y = _currentLocalY;
        visualRoot.localPosition = lp;
    }

    private bool RayDown(Vector3 at, out RaycastHit hit)
    {
        Vector3 origin = at + Vector3.up * sampleStartHeight;
        return Physics.Raycast(origin, Vector3.down, out hit, sampleDistance, groundMask, QueryTriggerInteraction.Ignore);
    }
}
