using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ProjectileNew : NetworkBehaviour
{
    private Rigidbody _rb;
    private Collider _col;

    private ulong _instigatorClientId;
    private ulong _instigatorVehicleNetObjId;
    private TeamIdNew _instigatorTeam;

    private ProjectileConfigNew _config;
    private float _dieAt;

    private bool _armed;
    private bool _hasImpacted;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        _rb.isKinematic = !IsServer;

        if (IsServer)
        {
            _armed = false;
            _hasImpacted = false;
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

        _dieAt = Time.time + Mathf.Max(0.05f, config.lifetime);

        _armed = false;
        _hasImpacted = false;

        _rb.linearVelocity = velocity;
        _rb.angularVelocity = Vector3.zero;

        Invoke(nameof(Arm), 0.02f);
    }

    private void Arm() => _armed = true;

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
        if (!_armed) return;

        if (_hasImpacted) return;
        _hasImpacted = true;

        if (_config == null)
        {
            SafeDespawn();
            return;
        }

        // Must be in hitMask to count as an impact
        int otherLayerBit = 1 << collision.collider.gameObject.layer;
        if ((_config.hitMask.value & otherLayerBit) == 0)
        {
            _hasImpacted = false; // ignore + allow future collisions
            return;
        }

        // Find the victim NetworkObject (vehicle root, etc.)
        var hitNO = collision.collider.GetComponentInParent<NetworkObject>();

        // Ignore hitting the shooter vehicle
        if (hitNO != null && hitNO.NetworkObjectId == _instigatorVehicleNetObjId)
        {
            _hasImpacted = false;
            return;
        }

        // Damage is COMPONENT-BASED (not layer-based)
        if (hitNO != null)
        {
            var health = hitNO.GetComponentInChildren<VehicleHealthNew>();
            if (health != null)
            {
                // Friendly fire check (optional)
                if (!_config.friendlyFire)
                {
                    var tc = hitNO.GetComponent<TeamComponentNew>();
                    if (tc != null && tc.Team == _instigatorTeam)
                    {
                        SafeDespawn();
                        return;
                    }
                }

                // Apply damage on server
                health.ServerApplyDamage(_config.damage, _instigatorClientId);
            }
        }

        SafeDespawn();
    }

    private void SafeDespawn()
    {
        if (!IsServer) return;
        if (NetworkObject == null) return;
        if (!NetworkObject.IsSpawned) return;

        if (_col != null) _col.enabled = false;
        NetworkObject.Despawn(true);
    }
}
