using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerStatsManagerNew : NetworkBehaviour
{
    public static PlayerStatsManagerNew Instance { get; private set; }

    [Serializable]
    public struct PlayerStatLine : INetworkSerializable, IEquatable<PlayerStatLine>
    {
        public ulong ClientId;
        public FixedString128Bytes DisplayName;
        public int Kills;
        public int Deaths;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
        }

        // IMPORTANT: include all fields so list can detect change
        public bool Equals(PlayerStatLine other)
        {
            return ClientId == other.ClientId
                   && DisplayName.Equals(other.DisplayName)
                   && Kills == other.Kills
                   && Deaths == other.Deaths;
        }

        public override bool Equals(object obj) => obj is PlayerStatLine other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ClientId.GetHashCode();
                hash = (hash * 397) ^ DisplayName.GetHashCode();
                hash = (hash * 397) ^ Kills;
                hash = (hash * 397) ^ Deaths;
                return hash;
            }
        }
    }

    // Network list replicated by NGO
    private NetworkList<PlayerStatLine> stats = new NetworkList<PlayerStatLine>();

    // Version-proof enumerable (no interface assumptions)
    public System.Collections.Generic.IEnumerable<PlayerStatLine> Stats
    {
        get
        {
            for (int i = 0; i < stats.Count; i++)
                yield return stats[i];
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        // Hook callbacks on server to maintain list membership
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                EnsureEntry(id);
        }

        // Optional: uncomment to verify replication on clients
        // stats.OnListChanged += e => Debug.Log($"[StatsList] ChangeType={e.Type} Index={e.Index} IsServer={IsServer} IsClient={IsClient}");
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId) => EnsureEntry(clientId);

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        for (int i = 0; i < stats.Count; i++)
        {
            if (stats[i].ClientId == clientId)
            {
                stats.RemoveAt(i);
                return;
            }
        }
    }

    private void EnsureEntry(ulong clientId)
    {
        if (!IsServer) return;

        for (int i = 0; i < stats.Count; i++)
            if (stats[i].ClientId == clientId) return;

        stats.Add(new PlayerStatLine
        {
            ClientId = clientId,
            DisplayName = new FixedString128Bytes($"Player {clientId}"),
            Kills = 0,
            Deaths = 0
        });
    }

    // --------------------------
    // Robust replace helper:
    // Remove+Insert guarantees dirtying + replication across NGO versions
    // --------------------------
    private void ReplaceAt(int index, PlayerStatLine value)
    {
        stats.RemoveAt(index);
        stats.Insert(index, value);
    }

    public void ServerResetAll()
    {
        if (!IsServer) return;

        for (int i = 0; i < stats.Count; i++)
        {
            var s = stats[i];
            s.Kills = 0;
            s.Deaths = 0;
            ReplaceAt(i, s);
        }
    }

    public void ServerAddKill(ulong clientId)
    {
        if (!IsServer) return;

        for (int i = 0; i < stats.Count; i++)
        {
            if (stats[i].ClientId == clientId)
            {
                var s = stats[i];
                s.Kills += 1;
                ReplaceAt(i, s);
                return;
            }
        }

        EnsureEntry(clientId);
        ServerAddKill(clientId);
    }

    public void ServerAddDeath(ulong clientId)
    {
        if (!IsServer) return;

        for (int i = 0; i < stats.Count; i++)
        {
            if (stats[i].ClientId == clientId)
            {
                var s = stats[i];
                s.Deaths += 1;
                ReplaceAt(i, s);
                return;
            }
        }

        EnsureEntry(clientId);
        ServerAddDeath(clientId);
    }

    public void ServerSetDisplayName(ulong clientId, string name)
    {
        if (!IsServer) return;

        EnsureEntry(clientId);

        for (int i = 0; i < stats.Count; i++)
        {
            if (stats[i].ClientId == clientId)
            {
                var s = stats[i];
                s.DisplayName = new FixedString128Bytes(string.IsNullOrWhiteSpace(name) ? $"Player {clientId}" : name);
                ReplaceAt(i, s);
                return;
            }
        }
    }
}
