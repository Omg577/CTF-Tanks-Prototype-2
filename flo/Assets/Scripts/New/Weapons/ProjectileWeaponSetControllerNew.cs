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

    [Header("Server Guardrails")]
    [Tooltip("If true, server only allows firing when GameStateManagerNew.PlayersCanMove is true (InGame).")]
    [SerializeField] private bool requireInGameToFire = true;

    [Tooltip("If true, server blocks firing while VehicleHealthNew.IsDead is true.")]
    [SerializeField] private bool blockWhileDead = true;

    [Tooltip("If true, server blocks firing while the vehicle is invulnerable/spawn-protected (if exposed).")]
    [SerializeField] private bool blockWhileInvulnerable = false;

    [Header("Abuse Guard (Server)")]
    [Tooltip("How many invalid fire requests are tolerated per second before we start ignoring them temporarily.")]
    [SerializeField] private int invalidRequestsPerSecondThreshold = 12;

    [Tooltip("How long to ignore fire requests after invalid spam is detected.")]
    [SerializeField] private float invalidSpamIgnoreSeconds = 1.0f;

    // Server-authoritative cooldown per weapon index
    private double[] _nextAllowedFireServerTime;

    // Server abuse tracking
    private int _invalidCountThisWindow;
    private double _invalidWindowStartServerTime;
    private double _ignoreUntilServerTime;

    private int _activeIndex;

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
            _invalidWindowStartServerTime = NetworkManager.ServerTime.Time;
            _invalidCountThisWindow = 0;
            _ignoreUntilServerTime = 0;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        // Local gating (just for UX; server enforces too)
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
        if (NetworkManager == null) return;

        double now = NetworkManager.ServerTime.Time;

        // Ignore window after spam
        if (now < _ignoreUntilServerTime)
            return;

        // Validate config list
        if (projectileConfigs == null || projectileConfigs.Length == 0)
        {
            RegisterInvalid(now);
            return;
        }

        // Validate index
        if (configIndex < 0 || configIndex >= projectileConfigs.Length)
        {
            RegisterInvalid(now);
            return;
        }

        ProjectileConfigNew config = projectileConfigs[configIndex];
        if (config == null)
        {
            RegisterInvalid(now);
            return;
        }

        // Validate prefab per config
        NetworkObject prefab = config.projectilePrefab;
        if (prefab == null)
        {
            RegisterInvalid(now);
            return;
        }

        // -------- Server Enforced Guardrails --------

        if (requireInGameToFire)
        {
            var gsm = GameStateManagerNew.Instance;
            if (gsm != null && !gsm.PlayersCanMove)
                return;
        }

        var health = GetComponent<VehicleHealthNew>();
        if (blockWhileDead && health != null && health.IsDead)
            return;

        // Optional: block while invulnerable (left as future hook)
        if (blockWhileInvulnerable && health != null)
        {
            // wire this if you expose a property on VehicleHealthNew
        }

        // Server-side cooldown per weapon
        if (_nextAllowedFireServerTime == null || _nextAllowedFireServerTime.Length < projectileConfigs.Length)
            _nextAllowedFireServerTime = new double[projectileConfigs.Length];

        if (now < _nextAllowedFireServerTime[configIndex])
            return;

        _nextAllowedFireServerTime[configIndex] = now + config.fireCooldownSeconds;

        // -------- Server-authoritative muzzle + direction --------

        Vector3 origin = (muzzle != null) ? muzzle.position : transform.position;

        // Yaw-only direction for top-down
        Vector3 dir = (muzzle != null) ? muzzle.forward : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Quaternion shotRot = Quaternion.LookRotation(dir, Vector3.up);

        // --- NEW: networked muzzle flash (cosmetic) ---
        if (config.muzzleFlashPrefab != null)
            PlayMuzzleFlashClientRpc(configIndex, origin, shotRot);

        Vector3 spawnPos = origin + dir * Mathf.Max(0f, config.spawnForwardOffset);

        TeamIdNew team = TeamIdNew.None;
        var tc = GetComponent<TeamComponentNew>();
        if (tc != null) team = tc.Team;

        NetworkObject projNO = Instantiate(prefab, spawnPos, shotRot);
        projNO.Spawn(true);

        // Ignore shooter collisions briefly (server)
        if (config.ignoreShooterCollisionSeconds > 0f)
            IgnoreCollisionsWithShooterTemporarily(projNO.gameObject, config.ignoreShooterCollisionSeconds);

        var proj = projNO.GetComponent<ProjectileNew>();
        if (proj != null)
        {
            Vector3 vel = dir * config.speed;
            proj.ServerInit(config, rpcParams.Receive.SenderClientId, NetworkObjectId, team, vel);
        }
    }

    // Runs on all clients so everyone sees muzzle flash
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void PlayMuzzleFlashClientRpc(int configIndex, Vector3 muzzlePos, Quaternion shotRot)
    {
        if (!IsSpawned) return;
        if (projectileConfigs == null) return;
        if (configIndex < 0 || configIndex >= projectileConfigs.Length) return;

        var config = projectileConfigs[configIndex];
        if (config == null) return;

        var prefab = config.muzzleFlashPrefab;
        if (prefab == null) return;

        Vector3 offsetWorld = shotRot * config.muzzleFlashLocalOffset;
        Vector3 pos = muzzlePos + offsetWorld;

        var vfx = Instantiate(prefab, pos, shotRot);

        // fallback cleanup (if prefab doesn't self-destroy)
        if (config.muzzleFlashDestroySeconds > 0f)
            Destroy(vfx, config.muzzleFlashDestroySeconds);
    }

    private void RegisterInvalid(double now)
    {
        // Reset window every 1 second
        if (now - _invalidWindowStartServerTime >= 1.0)
        {
            _invalidWindowStartServerTime = now;
            _invalidCountThisWindow = 0;
        }

        _invalidCountThisWindow++;

        if (_invalidCountThisWindow >= Mathf.Max(1, invalidRequestsPerSecondThreshold))
        {
            _ignoreUntilServerTime = now + Mathf.Max(0.1f, invalidSpamIgnoreSeconds);
            _invalidCountThisWindow = 0;
            _invalidWindowStartServerTime = now;
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
