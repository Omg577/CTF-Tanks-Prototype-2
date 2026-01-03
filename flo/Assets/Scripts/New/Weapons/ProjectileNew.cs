using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ProjectileNew : NetworkBehaviour
{
    [Header("Impact Visual Sync")]
    [Tooltip("Small delay so clients can render the final impact pose before despawn.")]
    [SerializeField] private float impactDespawnDelay = 0.06f;

    [Tooltip("If we spawn inside a target at point-blank range, we apply a hit immediately.")]
    [SerializeField] private float pointBlankOverlapRadius = 0.18f;

    private Rigidbody _rb;
    private Collider _col;

    private ulong _instigatorClientId;
    private ulong _instigatorVehicleNetObjId;
    private TeamIdNew _instigatorTeam;

    private ProjectileConfigNew _config;
    private float _dieAt;

    private bool _hasImpacted;

    // NEW: for falloff
    private Vector3 _spawnPos;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        // Server-authoritative projectile physics
        _rb.isKinematic = !IsServer;

        if (IsServer)
        {
            _hasImpacted = false;

            // Reduce tunneling / pass-through at high speed
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

        _dieAt = Time.time + Mathf.Max(0.05f, config.lifetime);
        _hasImpacted = false;

        _rb.linearVelocity = velocity;
        _rb.angularVelocity = Vector3.zero;

        // IMPORTANT: arm immediately. Shooter collision safety is handled by IgnoreCollision in the weapon controller.
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

        if (_config == null)
        {
            SafeDespawn();
            return;
        }

        // Ignore hitting the shooter vehicle (belt + suspenders)
        var hitNO = collision.collider.GetComponentInParent<NetworkObject>();
        if (hitNO != null && hitNO.NetworkObjectId == _instigatorVehicleNetObjId)
            return;

        // If we hit a vehicle with health, ALWAYS treat it as a hit (even if collider layer isn't in hitMask).
        var health = hitNO != null ? hitNO.GetComponentInChildren<VehicleHealthNew>() : null;
        if (health != null)
        {
            // Friendly fire check
            if (!_config.friendlyFire)
            {
                var tc = hitNO.GetComponent<TeamComponentNew>();
                if (tc != null && tc.Team == _instigatorTeam)
                {
                    HandleImpact(collision);
                    return;
                }
            }

            // NEW: weakspot multiplier
            float mult = 1f;
            var weak = collision.collider.GetComponentInParent<WeakSpotNew>();
            if (weak != null)
                mult *= weak.damageMultiplier;

            // NEW: falloff multiplier
            mult *= DamageFalloffMultiplier(Vector3.Distance(_spawnPos, collision.GetContact(0).point));

            int finalDamage = Mathf.RoundToInt(_config.damage * mult);

            // Apply damage on server
            health.ServerApplyDamage(finalDamage, _instigatorClientId);

            HandleImpact(collision);
            return;
        }

        // Otherwise, only count as an impact if collider is in hitMask
        int otherLayerBit = 1 << collision.collider.gameObject.layer;
        if ((_config.hitMask.value & otherLayerBit) == 0)
        {
            // Ignore this collision and allow continuing
            return;
        }

        // World impact
        HandleImpact(collision);
    }

    private float DamageFalloffMultiplier(float distance)
    {
        if (_config == null) return 1f;
        if (!_config.useFalloff) return 1f;

        float maxD = Mathf.Max(0.001f, _config.falloffMaxDistance);
        float norm = Mathf.Clamp01(distance / maxD);
        return Mathf.Max(0f, _config.falloffCurve.Evaluate(norm));
    }

    private void HandleImpact(Collision collision)
    {
        if (_hasImpacted) return;
        _hasImpacted = true;

        // Snap to contact point so clients see it reach the target before despawn.
        Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 hitNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;

        // Stop physics + disable collider server-side
        if (_col != null) _col.enabled = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        transform.position = hitPoint;

        // Reliable: ensures clients receive the final hit pose even if transform updates are missed.
        ImpactClientRpc(hitPoint, hitNormal);

        // Delay despawn slightly so the hit pose renders on clients
        StartCoroutine(DespawnAfterDelay(impactDespawnDelay));
    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    private void ImpactClientRpc(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (!IsServer)
            transform.position = hitPoint;
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

            // Ignore shooter
            if (hitNO.NetworkObjectId == _instigatorVehicleNetObjId) continue;

            var health = hitNO.GetComponentInChildren<VehicleHealthNew>();
            if (health == null) continue;

            if (!_config.friendlyFire)
            {
                var tc = hitNO.GetComponent<TeamComponentNew>();
                if (tc != null && tc.Team == _instigatorTeam)
                    break;
            }

            // NEW: weakspot multiplier (closest collider might be weakspot)
            float mult = 1f;
            var weak = c.GetComponentInParent<WeakSpotNew>();
            if (weak != null) mult *= weak.damageMultiplier;

            int finalDamage = Mathf.RoundToInt(_config.damage * mult);
            health.ServerApplyDamage(finalDamage, _instigatorClientId);

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
