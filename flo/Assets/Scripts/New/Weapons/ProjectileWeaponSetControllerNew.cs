using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
public class ProjectileWeaponSetControllerNew : NetworkBehaviour
{
    [Header("Configs (0=1 key, 1=2 key, 2=3 key...)")]
    [SerializeField] private ProjectileConfigNew[] projectileConfigs;

    [Header("Prefab")]
    [SerializeField] private NetworkObject projectilePrefab;

    [Header("Spawn Point")]
    [SerializeField] private Transform muzzle;

    [Header("Spawn Safety")]
    [SerializeField] private float spawnForwardOffset = 0.6f;
    [SerializeField] private float ignoreShooterCollisionSeconds = 0.12f;

    [Header("Input")]
    [SerializeField] private bool allowWeaponSwitch = true;

    private int _activeIndex = 0;
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

        var gsm = GameStateManagerNew.Instance;
        if (gsm != null && !gsm.PlayersCanMove) return;

        var health = GetComponent<VehicleHealthNew>();
        if (health != null && health.IsDead) return;

        if (projectileConfigs == null || projectileConfigs.Length == 0) return;
        if (projectilePrefab == null) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (mouse == null || kb == null) return;

        if (allowWeaponSwitch)
        {
            if (kb.digit1Key.wasPressedThisFrame) _activeIndex = 0;
            if (kb.digit2Key.wasPressedThisFrame) _activeIndex = Mathf.Min(1, projectileConfigs.Length - 1);
            if (kb.digit3Key.wasPressedThisFrame) _activeIndex = Mathf.Min(2, projectileConfigs.Length - 1);
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            Vector3 muzzlePos = muzzle != null ? muzzle.position : transform.position;

            RequestFireServerRpc(_activeIndex, muzzlePos, forward);
        }
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestFireServerRpc(int configIndex, Vector3 spawnPos, Vector3 dir)
    {
        if (projectileConfigs == null || projectileConfigs.Length == 0) return;
        if (configIndex < 0 || configIndex >= projectileConfigs.Length) return;

        var config = projectileConfigs[configIndex];
        if (config == null || projectilePrefab == null) return;
        if (NetworkManager == null) return;

        double now = NetworkManager.ServerTime.Time;
        if (now < _nextAllowedFireServerTime) return;
        _nextAllowedFireServerTime = now + config.fireCooldownSeconds;

        TeamIdNew team = TeamIdNew.None;
        var tc = GetComponent<TeamComponentNew>();
        if (tc != null) team = tc.Team;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 spawnPosSafe = spawnPos + dir * Mathf.Max(0f, spawnForwardOffset);

        NetworkObject projNO = Object.Instantiate(projectilePrefab, spawnPosSafe, Quaternion.LookRotation(dir, Vector3.up));
        projNO.Spawn(true);

        if (ignoreShooterCollisionSeconds > 0f)
            IgnoreCollisionsWithShooterTemporarily(projNO.gameObject, ignoreShooterCollisionSeconds);

        var proj = projNO.GetComponent<ProjectileNew>();
        if (proj != null)
        {
            Vector3 vel = dir * config.speed;
            proj.ServerInit(config, OwnerClientId, NetworkObjectId, team, vel);
        }
    }

    private void IgnoreCollisionsWithShooterTemporarily(GameObject projectileGO, float seconds)
    {
        var shooterCols = GetComponentsInChildren<Collider>(includeInactive: false);
        var projCols = projectileGO.GetComponentsInChildren<Collider>(includeInactive: false);

        foreach (var sc in shooterCols)
            foreach (var pc in projCols)
                if (sc != null && pc != null)
                    Physics.IgnoreCollision(sc, pc, true);

        StartCoroutine(ReenableCollisionAfter(projectileGO, shooterCols, projCols, seconds));
    }

    private System.Collections.IEnumerator ReenableCollisionAfter(GameObject projectileGO, Collider[] shooterCols, Collider[] projCols, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (projectileGO == null) yield break;

        foreach (var sc in shooterCols)
            foreach (var pc in projCols)
                if (sc != null && pc != null)
                    Physics.IgnoreCollision(sc, pc, false);
    }
}
