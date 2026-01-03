using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(NetworkObject))]
public class ProjectileWeaponSetControllerNew : NetworkBehaviour
{
    [Header("Weapons (1/2/3 keys switch index)")]
    [SerializeField] private ProjectileConfigNew[] projectileConfigs;

    [Header("Muzzle")]
    [SerializeField] private Transform muzzle;

    [Header("Input")]
    [SerializeField] private bool allowWeaponSwitch = true;

    // Server-authoritative cooldown per weapon
    private double[] _nextAllowedFireServerTime;

    public override void OnNetworkSpawn()
    {
        if (muzzle == null)
        {
            var found = transform.Find("Muzzle");
            if (found == null) found = transform.Find("muzzle");
            muzzle = found != null ? found : transform;
        }

        if (IsServer)
        {
            int n = projectileConfigs != null ? projectileConfigs.Length : 0;
            _nextAllowedFireServerTime = new double[Mathf.Max(1, n)];
        }
    }

    private int _activeIndex;

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        var gsm = GameStateManagerNew.Instance;
        if (gsm != null && !gsm.PlayersCanMove) return;

        var health = GetComponent<VehicleHealthNew>();
        if (health != null && health.IsDead) return;

        if (projectileConfigs == null || projectileConfigs.Length == 0) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (allowWeaponSwitch)
        {
            if (kb.digit1Key.wasPressedThisFrame) _activeIndex = 0;
            if (kb.digit2Key.wasPressedThisFrame) _activeIndex = Mathf.Min(1, projectileConfigs.Length - 1);
            if (kb.digit3Key.wasPressedThisFrame) _activeIndex = Mathf.Min(2, projectileConfigs.Length - 1);
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            RequestFireServerRpc(_activeIndex);
        }
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestFireServerRpc(int configIndex, RpcParams rpcParams = default)
    {
        if (projectileConfigs == null || projectileConfigs.Length == 0) return;
        if (configIndex < 0 || configIndex >= projectileConfigs.Length) return;

        var config = projectileConfigs[configIndex];
        if (config == null) return;

        // NEW: prefab comes from config
        var prefab = config.projectilePrefab;
        if (prefab == null) return;

        double now = NetworkManager.ServerTime.Time;

        if (_nextAllowedFireServerTime == null || _nextAllowedFireServerTime.Length < projectileConfigs.Length)
            _nextAllowedFireServerTime = new double[projectileConfigs.Length];

        if (now < _nextAllowedFireServerTime[configIndex]) return;
        _nextAllowedFireServerTime[configIndex] = now + config.fireCooldownSeconds;

        // Server-authoritative muzzle + direction
        Vector3 origin = (muzzle != null) ? muzzle.position : transform.position;

        Vector3 dir = (muzzle != null) ? muzzle.forward : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 spawnPos = origin + dir * Mathf.Max(0f, config.spawnForwardOffset);

        TeamIdNew team = TeamIdNew.None;
        var tc = GetComponent<TeamComponentNew>();
        if (tc != null) team = tc.Team;

        NetworkObject projNO = Instantiate(prefab, spawnPos, Quaternion.LookRotation(dir, Vector3.up));
        projNO.Spawn(true);

        if (config.ignoreShooterCollisionSeconds > 0f)
            IgnoreCollisionsWithShooterTemporarily(projNO.gameObject, config.ignoreShooterCollisionSeconds);

        var proj = projNO.GetComponent<ProjectileNew>();
        if (proj != null)
        {
            Vector3 vel = dir * config.speed;
            proj.ServerInit(config, rpcParams.Receive.SenderClientId, NetworkObjectId, team, vel);
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
