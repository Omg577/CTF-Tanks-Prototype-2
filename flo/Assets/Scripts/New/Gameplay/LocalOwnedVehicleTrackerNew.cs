using System;
using Unity.Netcode;
using UnityEngine;

public class LocalOwnedVehicleTrackerNew : MonoBehaviour
{
    public static event Action<NetworkObject> OnLocalVehicleChanged;

    [SerializeField] private float pollInterval = 0.25f;

    private float _nextPollTime;
    private ulong _lastVehicleNetId;

    public static NetworkObject CurrentLocalVehicle { get; private set; }

    private void Update()
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.IsClient || !NetworkManager.Singleton.IsConnectedClient) return;

        if (Time.time < _nextPollTime) return;
        _nextPollTime = Time.time + pollInterval;

        var v = FindLocalVehicle();
        ulong id = v != null ? v.NetworkObjectId : 0UL;

        if (id == _lastVehicleNetId) return;

        _lastVehicleNetId = id;
        CurrentLocalVehicle = v;
        OnLocalVehicleChanged?.Invoke(v);
    }

    private NetworkObject FindLocalVehicle()
    {
        ulong localId = NetworkManager.Singleton.LocalClientId;

        foreach (var no in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;
            if (no.OwnerClientId != localId) continue;

            // Filter: "is this a vehicle?"
            if (no.GetComponent<VehicleMovementNetcodeNew>() == null) continue;

            return no;
        }

        return null;
    }
}
