using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
public class ProjectileWeaponControllerNew : NetworkBehaviour
{
    [Header("Config")]
    [SerializeField] private ProjectileConfigNew projectileConfig;

    [Header("Prefab")]
    [SerializeField] private NetworkObject projectilePrefab;

    [Header("Spawn Point")]
    [SerializeField] private Transform muzzle;

    [Header("Spawn Safety")]
    [Tooltip("Spawns projectile this far forward from muzzle to avoid intersecting shooter colliders.")]
    [SerializeField] private float spawnForwardOffset = 0.6f;

    [Tooltip("Seconds to ignore collisions between projectile and the shooter (server-side).")]
    [SerializeField] private float ignoreShooterCollisionSeconds = 0.12f;

    private double _nextAllowedFireServerTime;

    public override void OnNetworkSpawn()
    {
        if (muzzle == null)
        {
            var found = transform.Find("Muzzle");
            muzzle = found != null ? found : transform;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // Match + death gating
        var gsm = GameStateManagerNew.Instance;
        if (gsm != null && !gsm.PlayersCanMove) return;

        var health = GetComponent<VehicleHealthNew>();
        if (health != null && health.IsDead) return;

        if (projectileConfig == null || projectilePrefab == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            // Movement-based aim: shoot where vehicle is facing (yaw only)
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            Vector3 muzzlePos = muzzle != null ? muzzle.position : transform.position;

            RequestFireServerRpc(muzzlePos, forward);
        }
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestFireServerRpc(Vector3 spawnPos, Vector3 dir)
    {
        if (projectileConfig == null || projectilePrefab == null) return;
        if (NetworkManager == null) return;

        // Server cooldown
        double now = NetworkManager.ServerTime.Time;
        if (now < _nextAllowedFireServerTime) return;
        _nextAllowedFireServerTime = now + projectileConfig.fireCooldownSeconds;

        // Determine team (optional)
        TeamIdNew team = TeamIdNew.None;
        var tc = GetComponent<TeamComponentNew>();
        if (tc != null) team = tc.Team;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        // Spawn slightly forward to avoid starting inside the shooter collider
        Vector3 spawnPosSafe = spawnPos + dir * Mathf.Max(0f, spawnForwardOffset);

        NetworkObject projNO = Object.Instantiate(projectilePrefab, spawnPosSafe, Quaternion.LookRotation(dir, Vector3.up));
        projNO.Spawn(true); // server-owned

        // Ignore collisions with shooter for a short time (server only)
        if (ignoreShooterCollisionSeconds > 0f)
        {
            IgnoreCollisionsWithShooterTemporarily(projNO.gameObject, ignoreShooterCollisionSeconds);
        }

        // Initialize projectile behavior
        var proj = projNO.GetComponent<ProjectileNew>();
        if (proj != null)
        {
            Vector3 vel = dir * projectileConfig.speed;
            proj.ServerInit(projectileConfig, OwnerClientId, NetworkObjectId, team, vel);
        }
    }

    private void IgnoreCollisionsWithShooterTemporarily(GameObject projectileGO, float seconds)
    {
        // shooter colliders (include children)
        var shooterCols = GetComponentsInChildren<Collider>(includeInactive: false);
        var projCols = projectileGO.GetComponentsInChildren<Collider>(includeInactive: false);

        foreach (var sc in shooterCols)
        {
            if (sc == null) continue;
            foreach (var pc in projCols)
            {
                if (pc == null) continue;
                Physics.IgnoreCollision(sc, pc, true);
            }
        }

        // Re-enable after delay (server)
        StartCoroutine(ReenableCollisionAfter(projectileGO, shooterCols, projCols, seconds));
    }

    private System.Collections.IEnumerator ReenableCollisionAfter(GameObject projectileGO, Collider[] shooterCols, Collider[] projCols, float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (projectileGO == null) yield break;

        foreach (var sc in shooterCols)
        {
            if (sc == null) continue;
            foreach (var pc in projCols)
            {
                if (pc == null) continue;
                Physics.IgnoreCollision(sc, pc, false);
            }
        }
    }
}
