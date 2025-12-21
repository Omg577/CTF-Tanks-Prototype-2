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

        // Hook client connect now that we're server
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        // Spawn for everyone already connected (host counts)
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

        // IMPORTANT: Spawn first so NetworkVariables exist on all peers
        vehicle.SpawnWithOwnership(clientId, destroyWithScene: true);

        // Assign team on the spawned instance (server authoritative)
        var teamComp = vehicle.GetComponent<TeamComponentNew>();
        if (teamComp != null)
        {
            teamComp.ServerSetTeam(team);
        }
        else
        {
            Debug.LogWarning("VehicleSpawnerNew: Spawned vehicle has no TeamComponentNew.");
        }

        _spawned[clientId] = vehicle;

        Debug.Log($"[VehicleSpawnerNew] Spawned vehicle for clientId={clientId}, team={team}, spawn=({sp.position.x:0.0},{sp.position.y:0.0},{sp.position.z:0.0})");
    }

    private TeamIdNew ResolveTeam(ulong clientId)
    {
        if (!assignTeamsByClientIdParity)
            return TeamIdNew.TeamA; // default MVP fallback

        // MVP rule: even = A, odd = B
        return (clientId % 2UL == 0UL) ? TeamIdNew.TeamA : TeamIdNew.TeamB;
    }

    private Transform PickSpawn(ulong clientId, TeamIdNew team)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return this.transform;

        // If you provide 2+ spawns, we split them by team:
        // first half -> TeamA, second half -> TeamB
        int n = spawnPoints.Length;

        // If only 1 spawn point, just use it
        if (n == 1)
            return spawnPoints[0] != null ? spawnPoints[0] : this.transform;

        int half = n / 2;

        // If n is odd, TeamA gets the extra point by default
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

        // Choose within that team's spawn range by clientId
        int idxInTeam = (int)(clientId % (ulong)count);
        int idx = start + idxInTeam;

        Transform chosen = (idx >= 0 && idx < n) ? spawnPoints[idx] : null;
        return chosen != null ? chosen : this.transform;
    }
}
