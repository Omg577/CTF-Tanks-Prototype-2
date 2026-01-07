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

    private void OnClientConnected(ulong clientId) => SpawnFor(clientId);

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

    // --------- NEW: Draft integration ----------
    public void ServerReplaceVehicleForClient(ulong clientId, NetworkObject newVehiclePrefab)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (newVehiclePrefab == null) return;

        // If we don't have a vehicle, just spawn
        if (!_spawned.TryGetValue(clientId, out var oldVehicle) || oldVehicle == null)
        {
            // fallback: spawn using new prefab
            TeamIdNew t = ResolveTeam(clientId);
            Transform sp0 = PickSpawn(clientId, t);

            NetworkObject v0 = Instantiate(newVehiclePrefab, sp0.position, sp0.rotation);
            v0.SpawnWithOwnership(clientId, destroyWithScene: true);

            var tc0 = v0.GetComponent<TeamComponentNew>();
            if (tc0 != null) tc0.ServerSetTeam(t);

            _spawned[clientId] = v0;
            return;
        }

        // Preserve team if set
        TeamIdNew team = ResolveTeam(clientId);
        var oldTeamComp = oldVehicle.GetComponent<TeamComponentNew>();
        if (oldTeamComp != null && oldTeamComp.Team != TeamIdNew.None)
            team = oldTeamComp.Team;

        // We'll spawn at current pose (countdown will teleport everyone anyway)
        Vector3 pos = oldVehicle.transform.position;
        Quaternion rot = oldVehicle.transform.rotation;

        // Despawn old
        if (oldVehicle.IsSpawned)
            oldVehicle.Despawn(true);
        Destroy(oldVehicle.gameObject);

        // Spawn new
        NetworkObject newVehicle = Instantiate(newVehiclePrefab, pos, rot);
        newVehicle.SpawnWithOwnership(clientId, destroyWithScene: true);

        var teamComp = newVehicle.GetComponent<TeamComponentNew>();
        if (teamComp != null) teamComp.ServerSetTeam(team);

        _spawned[clientId] = newVehicle;

        Debug.Log($"[VehicleSpawnerNew] Replaced vehicle for clientId={clientId} with {newVehiclePrefab.name}");
    }

    // --------- Round reset helpers ----------
    public void ServerTeleportAllToSpawns()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        foreach (var kvp in _spawned)
        {
            ulong clientId = kvp.Key;
            NetworkObject vehicle = kvp.Value;
            if (vehicle == null) continue;

            TeamIdNew team = ResolveTeam(clientId);
            var teamComp = vehicle.GetComponent<TeamComponentNew>();
            if (teamComp != null && teamComp.Team != TeamIdNew.None)
                team = teamComp.Team;

            Transform sp = PickSpawn(clientId, team);
            TeleportVehicleServer(vehicle, sp.position, sp.rotation);
        }
    }

    public void ServerTeleportSingleToSpawn(NetworkObject vehicle, ulong clientId, TeamIdNew team)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (vehicle == null) return;

        Transform sp = PickSpawn(clientId, team);
        TeleportVehicleServer(vehicle, sp.position, sp.rotation);
    }

    private static void TeleportVehicleServer(NetworkObject vehicle, Vector3 pos, Quaternion rot)
    {
        vehicle.transform.SetPositionAndRotation(pos, rot);

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
            count = Mathf.Max(1, half + (n % 2));
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
