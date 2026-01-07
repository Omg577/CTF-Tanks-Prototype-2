using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ProjectileNew : NetworkBehaviour
{
    [Header("Impact Visual Sync")]
    [SerializeField] private float impactDespawnDelay = 0.06f;

    [Header("Point-Blank Fix")]
    [SerializeField] private float pointBlankOverlapRadius = 0.18f;

    [Header("Ricochet Safety")]
    [SerializeField] private float bounceSeparation = 0.02f;
    [SerializeField] private float bounceIgnoreSeconds = 0.05f;

    [Header("Splash LOS")]
    [Tooltip("Small offset from explosion center to avoid raycast starting inside surfaces.")]
    [SerializeField] private float splashLosStartOffset = 0.05f;

    [Tooltip("If true, splash only applies if the target is visible from the explosion point (no walls in between).")]
    [SerializeField] private bool requireLineOfSightForSplash = true;

    private Rigidbody _rb;
    private Collider _col;
    private ProjectileVfxNew _vfx;

    private ulong _instigatorClientId;
    private ulong _instigatorVehicleNetObjId;
    private TeamIdNew _instigatorTeam;

    private ProjectileConfigNew _config;
    private float _dieAt;

    private bool _hasImpacted;

    // For falloff
    private Vector3 _spawnPos;

    // For ricochet
    private int _bouncesLeft;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _vfx = GetComponent<ProjectileVfxNew>(); // optional
    }

    public override void OnNetworkSpawn()
    {
        _rb.isKinematic = !IsServer;

        if (IsServer)
        {
            _hasImpacted = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    public void ServerInit(
        ProjectileConfigNew config,
        ulong instigatorClientId,
        ulong instigatorVehicleNetObjId,
        TeamIdNew instigatorTeam,
        Vector3 velocity)
    {
        if (!IsServer) return;

        _config = config;
        _instigatorClientId = instigatorClientId;
        _instigatorVehicleNetObjId = instigatorVehicleNetObjId;
        _instigatorTeam = instigatorTeam;

        _spawnPos = transform.position;
        _bouncesLeft = (_config != null && _config.isRicochet) ? _config.maxBounces : 0;

        _dieAt = Time.time + Mathf.Max(0.05f, config != null ? config.lifetime : 0.5f);
        _hasImpacted = false;

        _rb.linearVelocity = velocity;
        _rb.angularVelocity = Vector3.zero;

        TryPointBlankOverlapHit();
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (_config == null)
        {
            SafeDespawn();
            return;
        }

        if (Time.time >= _dieAt)
            SafeDespawn();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;
        if (_hasImpacted) return;
        if (_config == null) { SafeDespawn(); return; }

        // Ignore shooter vehicle
        var hitNO = collision.collider.GetComponentInParent<NetworkObject>();
        if (hitNO != null && hitNO.NetworkObjectId == _instigatorVehicleNetObjId)
            return;

        int otherLayerBit = 1 << collision.collider.gameObject.layer;

        // Vehicle hit?
        var health = hitNO != null ? hitNO.GetComponentInChildren<VehicleHealthNew>() : null;
        if (health != null)
        {
            if (!_config.friendlyFire)
            {
                var tc = hitNO.GetComponent<TeamComponentNew>();
                if (tc != null && tc.Team == _instigatorTeam)
                {
                    HandleImpact(collision);
                    return;
                }
            }

            float mult = 1f;

            var weak = collision.collider.GetComponentInParent<WeakSpotNew>();
            if (weak != null) mult *= weak.damageMultiplier;

            Vector3 hitPointTmp = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            mult *= DamageFalloffMultiplier(Vector3.Distance(_spawnPos, hitPointTmp));

            int finalDamage = Mathf.RoundToInt(_config.damage * mult);
            health.ServerApplyDamage(finalDamage, _instigatorClientId);

            // Rocket splash on vehicle hit (avoid double-damage direct hit)
            if (_config.isExplosive && _config.splashRadius > 0f)
                ApplySplash(hitPointTmp, directHitNetObjId: hitNO.NetworkObjectId);

            HandleImpact(collision);
            return;
        }

        // Not a vehicle. If it's not in hitMask, ignore entirely.
        if ((_config.hitMask.value & otherLayerBit) == 0)
            return;

        // Explosive: splash on world hit too
        if (_config.isExplosive)
        {
            Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            ApplySplash(hitPoint, directHitNetObjId: 0);
            HandleImpact(collision);
            return;
        }

        // Ricochet: bounce on world hit
        if (_config.isRicochet && _bouncesLeft > 0)
        {
            Bounce(collision);
            return;
        }

        // Otherwise: impact/despawn
        HandleImpact(collision);
    }

    private void Bounce(Collision collision)
    {
        ContactPoint cp = collision.contactCount > 0 ? collision.GetContact(0) : default;
        Vector3 n = (collision.contactCount > 0) ? cp.normal : -transform.forward;

        Vector3 v = _rb.linearVelocity;
        if (v.sqrMagnitude < 0.001f)
            v = transform.forward * (_config != null ? _config.speed : 10f);

        Vector3 reflected = Vector3.Reflect(v, n);

        float newSpeed = v.magnitude * Mathf.Clamp(_config.bounceSpeedMultiplier, 0.1f, 1f);
        reflected = reflected.normalized * newSpeed;

        _bouncesLeft--;

        Vector3 bouncePoint = transform.position;
        if (collision.contactCount > 0)
        {
            bouncePoint = cp.point;
            transform.position = cp.point + n * bounceSeparation;
        }

        _rb.linearVelocity = reflected;
        _rb.angularVelocity = Vector3.zero;

        // Bounce sparks for everyone
        BounceClientRpc(bouncePoint, n);

        // Prevent sticky immediate re-collide
        StartCoroutine(TemporaryIgnore(collision.collider, bounceIgnoreSeconds));
    }

    private IEnumerator TemporaryIgnore(Collider other, float seconds)
    {
        if (_col == null || other == null) yield break;

        Physics.IgnoreCollision(_col, other, true);
        yield return new WaitForSeconds(seconds);
        if (_col != null && other != null)
            Physics.IgnoreCollision(_col, other, false);
    }

    private float DamageFalloffMultiplier(float distance)
    {
        if (_config == null || !_config.useFalloff) return 1f;

        float maxD = Mathf.Max(0.001f, _config.falloffMaxDistance);
        float norm = Mathf.Clamp01(distance / maxD);
        return Mathf.Max(0f, _config.falloffCurve.Evaluate(norm));
    }

    private void ApplySplash(Vector3 center, ulong directHitNetObjId)
    {
        float r = Mathf.Max(0f, _config.splashRadius);
        if (r <= 0f) return;

        var cols = Physics.OverlapSphere(center, r, _config.vehicleMask, QueryTriggerInteraction.Ignore);
        foreach (var c in cols)
        {
            if (c == null) continue;

            var no = c.GetComponentInParent<NetworkObject>();
            if (no == null) continue;

            // Avoid double-damaging direct-hit vehicle
            if (directHitNetObjId != 0 && no.NetworkObjectId == directHitNetObjId)
                continue;

            // Ignore shooter
            if (no.NetworkObjectId == _instigatorVehicleNetObjId)
                continue;

            var health = no.GetComponentInChildren<VehicleHealthNew>();
            if (health == null) continue;

            if (!_config.friendlyFire)
            {
                var tc = no.GetComponent<TeamComponentNew>();
                if (tc != null && tc.Team == _instigatorTeam)
                    continue;
            }

            Vector3 targetPoint = c.ClosestPoint(center);

            // Base splash falloff by radius
            float d = Vector3.Distance(center, targetPoint);
            float t = Mathf.Clamp01(d / Mathf.Max(0.001f, r));
            float splashMult = Mathf.Lerp(1f, Mathf.Clamp01(_config.splashEdgeMultiplier), t);

            // LOS modifier: if blocked, reduce splash
            if (requireLineOfSightForSplash)
            {
                bool hasLos = HasLineOfSight(center, targetPoint, no);
                if (!hasLos)
                {
                    float blocked = Mathf.Clamp01(_config.blockedSplashMultiplier);

                    // If you set blocked=0, it's identical to "no splash through walls"
                    if (blocked <= 0f)
                        continue;

                    splashMult *= blocked;
                }
            }

            int splashDamage = Mathf.RoundToInt(_config.damage * splashMult);
            if (splashDamage > 0)
                health.ServerApplyDamage(splashDamage, _instigatorClientId);
        }
    }

    /// <summary>
    /// Returns true if the first thing hit from center->targetPoint belongs to targetNetObj (or nothing blocks the ray).
    /// Uses config.hitMask so "walls" are whatever your projectile normally collides with.
    /// </summary>
    private bool HasLineOfSight(Vector3 center, Vector3 targetPoint, NetworkObject targetNetObj)
    {
        Vector3 toTarget = targetPoint - center;
        float dist = toTarget.magnitude;
        if (dist <= 0.01f) return true;

        Vector3 dir = toTarget / dist;

        Vector3 start = center + dir * Mathf.Max(0f, splashLosStartOffset);
        float rayDist = Mathf.Max(0f, dist - splashLosStartOffset);

        RaycastHit[] hits = Physics.RaycastAll(
            start,
            dir,
            rayDist,
            _config != null ? _config.hitMask : ~0,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0)
            return true;

        int best = -1;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;

            // Ignore the projectile itself
            var hitNO = h.collider.GetComponentInParent<NetworkObject>();
            if (hitNO != null && hitNO.NetworkObjectId == NetworkObjectId)
                continue;

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                best = i;
            }
        }

        if (best < 0) return true;

        var nearestNO = hits[best].collider.GetComponentInParent<NetworkObject>();
        return nearestNO != null && targetNetObj != null && nearestNO.NetworkObjectId == targetNetObj.NetworkObjectId;
    }


    private void HandleImpact(Collision collision)
    {
        if (_hasImpacted) return;
        _hasImpacted = true;

        Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 hitNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;

        if (_col != null) _col.enabled = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        transform.position = hitPoint;

        ImpactClientRpc(hitPoint, hitNormal);

        StartCoroutine(DespawnAfterDelay(impactDespawnDelay));
    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    private void ImpactClientRpc(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (!IsClient) return;

        if (!IsServer)
            transform.position = hitPoint;

        if (_vfx != null && _vfx.impactVfxPrefab != null)
        {
            var go = Instantiate(_vfx.impactVfxPrefab, hitPoint, Quaternion.LookRotation(hitNormal));
            if (_vfx.impactVfxLifetime > 0f) Destroy(go, _vfx.impactVfxLifetime);
        }
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void BounceClientRpc(Vector3 point, Vector3 normal)
    {
        if (!IsClient) return;

        if (_vfx != null && _vfx.bounceVfxPrefab != null)
        {
            var go = Instantiate(_vfx.bounceVfxPrefab, point, Quaternion.LookRotation(normal));
            if (_vfx.bounceVfxLifetime > 0f) Destroy(go, _vfx.bounceVfxLifetime);
        }
    }

    private IEnumerator DespawnAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, seconds));
        SafeDespawn();
    }

    private void TryPointBlankOverlapHit()
    {
        if (_config == null) return;

        float r = Mathf.Max(0.05f, pointBlankOverlapRadius);
        var hits = Physics.OverlapSphere(transform.position, r, _config.vehicleMask, QueryTriggerInteraction.Ignore);

        foreach (var c in hits)
        {
            if (c == null) continue;

            var hitNO = c.GetComponentInParent<NetworkObject>();
            if (hitNO == null) continue;

            if (hitNO.NetworkObjectId == _instigatorVehicleNetObjId) continue;

            var health = hitNO.GetComponentInChildren<VehicleHealthNew>();
            if (health == null) continue;

            if (!_config.friendlyFire)
            {
                var tc = hitNO.GetComponent<TeamComponentNew>();
                if (tc != null && tc.Team == _instigatorTeam)
                    break;
            }

            float mult = 1f;
            var weak = c.GetComponentInParent<WeakSpotNew>();
            if (weak != null) mult *= weak.damageMultiplier;

            int finalDamage = Mathf.RoundToInt(_config.damage * mult);
            health.ServerApplyDamage(finalDamage, _instigatorClientId);

            if (_config.isExplosive && _config.splashRadius > 0f)
                ApplySplash(transform.position, directHitNetObjId: hitNO.NetworkObjectId);

            _hasImpacted = true;
            if (_col != null) _col.enabled = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            ImpactClientRpc(transform.position, Vector3.up);
            StartCoroutine(DespawnAfterDelay(impactDespawnDelay));
            break;
        }
    }

    private void SafeDespawn()
    {
        if (!IsServer) return;
        if (NetworkObject == null) return;
        if (!NetworkObject.IsSpawned) return;

        NetworkObject.Despawn(true);
    }
}
