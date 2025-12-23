using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class VehicleSpawnerNew : MonoBehaviour
{
    [Header("Prefab + Spawns")]
    [SerializeField] private NetworkObject vehiclePrefab;

    [Tooltip("Recommended: first half = TeamA spawns, second half = TeamB spawns.\n" +
             "Example: size 4 => [0,1]=TeamA, [2,3]=TeamB.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Team Assignment (MVP)")]
    [Tooltip("If true: even clientId = TeamA, odd clientId = TeamB.")]
    [SerializeField] private bool assignTeamsByClientIdParity = true;

    private readonly Dictionary<ulong, NetworkObject> _spawned = new();

    private void Awake()
    {
        if (vehiclePrefab == null)
            Debug.LogError("VehicleSpawnerNew: vehiclePrefab not assigned.");
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenNetworkManagerReady());
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private IEnumerator BindWhenNetworkManagerReady()
    {
        while (NetworkManager.Singleton == null)
            yield return null;

        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            SpawnFor(clientId);
    }

    private void OnClientConnected(ulong clientId)
    {
        SpawnFor(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (_spawned.TryGetValue(clientId, out var obj) && obj != null)
        {
            obj.Despawn();
            Destroy(obj.gameObject);
        }

        _spawned.Remove(clientId);
    }

    private void SpawnFor(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (_spawned.ContainsKey(clientId)) return;

        TeamIdNew team = ResolveTeam(clientId);
        Transform sp = PickSpawn(clientId, team);

        NetworkObject vehicle = Instantiate(vehiclePrefab, sp.position, sp.rotation);
        vehicle.SpawnWithOwnership(clientId, destroyWithScene: true);

        var teamComp = vehicle.GetComponent<TeamComponentNew>();
        if (teamComp != null) teamComp.ServerSetTeam(team);

        _spawned[clientId] = vehicle;

        Debug.Log($"[VehicleSpawnerNew] Spawned clientId={clientId}, team={team}");
    }

    // -----------------------------
    // NEW: Round reset teleport
    // -----------------------------
    public void ServerTeleportAllToSpawns()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        foreach (var kvp in _spawned)
        {
            ulong clientId = kvp.Key;
            NetworkObject vehicle = kvp.Value;
            if (vehicle == null) continue;

            // Team from TeamComponent if present; otherwise fallback to parity rule
            TeamIdNew team = ResolveTeam(clientId);
            var teamComp = vehicle.GetComponent<TeamComponentNew>();
            if (teamComp != null && teamComp.Team != TeamIdNew.None)
                team = teamComp.Team;

            Transform sp = PickSpawn(clientId, team);
            TeleportVehicleServer(vehicle, sp.position, sp.rotation);
        }
    }

    private static void TeleportVehicleServer(NetworkObject vehicle, Vector3 pos, Quaternion rot)
    {
        // Set transform
        vehicle.transform.SetPositionAndRotation(pos, rot);

        // Also hard-reset Rigidbody if present (important)
        var rb = vehicle.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.Sleep();
            rb.WakeUp();
        }
    }

    private TeamIdNew ResolveTeam(ulong clientId)
    {
        if (!assignTeamsByClientIdParity)
            return TeamIdNew.TeamA;

        return (clientId % 2UL == 0UL) ? TeamIdNew.TeamA : TeamIdNew.TeamB;
    }

    private Transform PickSpawn(ulong clientId, TeamIdNew team)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return this.transform;

        int n = spawnPoints.Length;

        if (n == 1)
            return spawnPoints[0] != null ? spawnPoints[0] : this.transform;

        int half = n / 2;

        int start, count;
        if (team == TeamIdNew.TeamA)
        {
            start = 0;
            count = Mathf.Max(1, half + (n % 2)); // TeamA gets extra if odd
        }
        else
        {
            start = Mathf.Max(1, half + (n % 2));
            count = Mathf.Max(1, n - start);
        }

        int idxInTeam = (int)(clientId % (ulong)count);
        int idx = start + idxInTeam;

        Transform chosen = (idx >= 0 && idx < n) ? spawnPoints[idx] : null;
        return chosen != null ? chosen : this.transform;
    }
}
