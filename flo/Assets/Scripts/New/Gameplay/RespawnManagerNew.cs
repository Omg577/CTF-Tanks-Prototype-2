using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class RespawnManagerNew : NetworkBehaviour
{
    public static RespawnManagerNew Instance { get; private set; }

    [Header("Respawn Settings")]
    [SerializeField] private float respawnDelaySeconds = 5f;
    [SerializeField] private float invulnerabilitySecondsAfterRespawn = 1.5f;

    [Header("Refs")]
    [SerializeField] private VehicleSpawnerNew vehicleSpawner; // optional; we can auto-find

    // Track pending respawn routines so we can stop them on round reset
    private readonly Dictionary<ulong, Coroutine> _respawnRoutinesByVehicleId = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (vehicleSpawner == null)
                vehicleSpawner = FindFirstObjectByType<VehicleSpawnerNew>();
        }
    }

    /// <summary>
    /// Server-only: called by VehicleHealthNew when it reaches 0 HP.
    /// </summary>
    public void ServerHandleDeath(ulong victimVehicleNetworkObjectId, ulong instigatorClientId)
    {
        if (!IsServer) return;

        // Find the victim vehicle
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victimVehicleNetworkObjectId, out var victimNO) || victimNO == null)
            return;

        // ----------------------------
        // 11.2 Stats: deaths + kills
        // ----------------------------
        if (PlayerStatsManagerNew.Instance != null)
        {
            ulong victimClientId = victimNO.OwnerClientId;

            PlayerStatsManagerNew.Instance.ServerAddDeath(victimClientId);

            // Award a kill only if instigator is valid and not self
            if (instigatorClientId != ulong.MaxValue && instigatorClientId != victimClientId)
                PlayerStatsManagerNew.Instance.ServerAddKill(instigatorClientId);
        }

        // Drop payload if victim is carrying it
        DropAnyPayloadCarriedBy(victimVehicleNetworkObjectId);

        // Disable victim physics/movement server-side
        ServerDisableVehicle(victimNO);

        // Start respawn timer (cancel/replace if already scheduled)
        if (_respawnRoutinesByVehicleId.TryGetValue(victimVehicleNetworkObjectId, out var existing) && existing != null)
        {
            StopCoroutine(existing);
            _respawnRoutinesByVehicleId.Remove(victimVehicleNetworkObjectId);
        }

        Coroutine c = StartCoroutine(ServerRespawnRoutine(victimVehicleNetworkObjectId));
        _respawnRoutinesByVehicleId[victimVehicleNetworkObjectId] = c;
    }


    /// <summary>
    /// NEW: Called on the server when a new round begins.
    /// Heals/revives everyone, re-enables colliders/rigidbodies, and cancels any pending respawns.
    /// (Teleport is handled by GameStateManager/VehicleSpawner already.)
    /// </summary>
    public void ServerResetAllVehiclesForNewRound(float invulnerabilitySeconds = 0f)
    {
        if (!IsServer) return;

        // Cancel any pending respawn routines
        foreach (var kvp in _respawnRoutinesByVehicleId)
        {
            if (kvp.Value != null)
                StopCoroutine(kvp.Value);
        }
        _respawnRoutinesByVehicleId.Clear();

        // Heal + revive + enable all spawned vehicles
        foreach (var no in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;

            var health = no.GetComponent<VehicleHealthNew>();
            if (health == null) continue; // only vehicles should have this

            // Ensure it's enabled regardless of previous dead state
            ServerEnableVehicle(no);

            // Reset HP/death state
            health.ServerResetFullHealth();
            health.ServerMarkDead(false);

            if (invulnerabilitySeconds > 0f)
                health.ServerSetInvulnerableSeconds(invulnerabilitySeconds);
        }

        // Optional: tell clients to reset prediction for all vehicles (safe after teleports)
        NotifyAllVehiclesRespawnedClientRpc();
    }

    private IEnumerator ServerRespawnRoutine(ulong victimVehicleNetworkObjectId)
    {
        double start = NetworkManager.ServerTime.Time;
        double end = start + respawnDelaySeconds;

        while (NetworkManager != null && NetworkManager.ServerTime.Time < end)
            yield return null;

        _respawnRoutinesByVehicleId.Remove(victimVehicleNetworkObjectId);

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victimVehicleNetworkObjectId, out var victimNO) || victimNO == null)
            yield break;

        if (vehicleSpawner == null)
            vehicleSpawner = FindFirstObjectByType<VehicleSpawnerNew>();

        ulong ownerClientId = victimNO.OwnerClientId;

        TeamIdNew team = TeamIdNew.TeamA;
        var teamComp = victimNO.GetComponent<TeamComponentNew>();
        if (teamComp != null && teamComp.Team != TeamIdNew.None)
            team = teamComp.Team;

        // Teleport back to spawn + zero velocities
        if (vehicleSpawner != null)
            vehicleSpawner.ServerTeleportSingleToSpawn(victimNO, ownerClientId, team);

        // Reset health + invulnerability
        var health = victimNO.GetComponent<VehicleHealthNew>();
        if (health != null)
        {
            health.ServerResetFullHealth();
            health.ServerSetInvulnerableSeconds(invulnerabilitySecondsAfterRespawn);
            health.ServerMarkDead(false);
        }

        // Re-enable physics/colliders
        ServerEnableVehicle(victimNO);

        // Tell clients to reset prediction buffers for this vehicle
        NotifyVehicleRespawnedClientRpc(victimVehicleNetworkObjectId);
    }

    private void DropAnyPayloadCarriedBy(ulong victimVehicleNetworkObjectId)
    {
#if UNITY_6000_0_OR_NEWER
        var payloads = Object.FindObjectsByType<PayloadNew>(FindObjectsSortMode.None);
#else
        var payloads = Object.FindObjectsOfType<PayloadNew>();
#endif
        foreach (var p in payloads)
        {
            if (p == null) continue;
            if (p.IsCarried && p.CarrierNetObjectId == victimVehicleNetworkObjectId)
                p.ServerDrop();
        }
    }

    private static void ServerDisableVehicle(NetworkObject victimNO)
    {
        var rb = victimNO.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        var cols = victimNO.GetComponentsInChildren<Collider>();
        foreach (var c in cols)
            c.enabled = false;
    }

    private static void ServerEnableVehicle(NetworkObject victimNO)
    {
        var cols = victimNO.GetComponentsInChildren<Collider>();
        foreach (var c in cols)
            c.enabled = true;

        var rb = victimNO.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    [ClientRpc]
    private void NotifyVehicleRespawnedClientRpc(ulong vehicleNetworkObjectId)
    {
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(vehicleNetworkObjectId, out var no) && no != null)
        {
            var move = no.GetComponent<VehicleMovementNetcodeNew>();
            if (move != null)
                move.ClientForceResetPredictionNow();
        }
    }

    [ClientRpc]
    private void NotifyAllVehiclesRespawnedClientRpc()
    {
        if (NetworkManager.Singleton == null) return;

        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;
            var move = no.GetComponent<VehicleMovementNetcodeNew>();
            if (move != null)
                move.ClientForceResetPredictionNow();
        }
    }
}
