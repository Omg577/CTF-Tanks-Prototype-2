using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class VehicleSpawnerNew : MonoBehaviour
{
    [SerializeField] private NetworkObject vehiclePrefab;
    [SerializeField] private Transform[] spawnPoints;

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
        // Safely unhook if we ever hooked.
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private IEnumerator BindWhenNetworkManagerReady()
    {
        // Wait until a NetworkManager exists (prevents NullReferenceException)
        while (NetworkManager.Singleton == null)
            yield return null;

        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;

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

        Transform sp = PickSpawn(clientId);

        NetworkObject vehicle = Instantiate(vehiclePrefab, sp.position, sp.rotation);

        // Use SpawnWithOwnership for vehicles (more straightforward than player objects for this MVP)
        vehicle.SpawnWithOwnership(clientId, destroyWithScene: true);

        _spawned[clientId] = vehicle;
    }

    private Transform PickSpawn(ulong clientId)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return this.transform;

        int idx = (int)(clientId % (ulong)spawnPoints.Length);
        return spawnPoints[idx] != null ? spawnPoints[idx] : this.transform;
    }
}
