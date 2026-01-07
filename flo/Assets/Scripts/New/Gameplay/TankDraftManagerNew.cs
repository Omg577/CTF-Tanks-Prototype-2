using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TankDraftManagerNew : NetworkBehaviour
{
    [Header("Tank Options (6+)")]
    [SerializeField] private TankDefinitionNew[] tankOptions;

    [Header("Rules")]
    [SerializeField] private bool uniquePerTeam = true;

    [Header("Auto Lock")]
    [SerializeField] private bool autoLockOnTimeout = true;

    public NetworkList<TankPickEntryNew> Picks { get; private set; }

    // server only lookup
    private readonly Dictionary<int, TankDefinitionNew> _tankById = new();

    public static event System.Action<TankDraftFailReasonNew> OnClientDraftFail;

    private void Awake()
    {
        Picks = new NetworkList<TankPickEntryNew>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            RebuildLookup();
    }

    private void RebuildLookup()
    {
        if (!IsServer) return;

        _tankById.Clear();
        if (tankOptions == null) return;

        foreach (var def in tankOptions)
        {
            if (def == null) continue;
            _tankById[def.tankId] = def;
        }
    }

    // Called by GameStateManagerNew when entering TankSelect
    public void ServerBeginTankSelect(float durationSeconds)
    {
        if (!IsServer) return;

        RebuildLookup();
        Picks.Clear();

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            TeamIdNew team = ResolveTeamForClient(clientId);

            Picks.Add(new TankPickEntryNew
            {
                ClientId = clientId,
                Team = team,
                TankId = -1,
                Locked = false
            });
        }
    }

    // Called by GameStateManagerNew right as TankSelect ends
    public void ServerApplyFinalPicks(VehicleSpawnerNew spawner)
    {
        if (!IsServer) return;
        if (spawner == null) return;

        if (autoLockOnTimeout)
            AutoAssignAndLockEveryone();

        // Apply: replace each client's vehicle prefab
        for (int i = 0; i < Picks.Count; i++)
        {
            var p = Picks[i];
            if (p.TankId < 0) continue;

            if (!_tankById.TryGetValue(p.TankId, out var def) || def == null || def.tankPrefab == null)
                continue;

            spawner.ServerReplaceVehicleForClient(p.ClientId, def.tankPrefab);
        }
    }

    // -------------- Client API (UI calls these) --------------

    public void ClientRequestLock(int tankId)
    {
        if (!IsSpawned) return;
        RequestLockServerRpc(tankId);
    }

    public void ClientRequestUnlock()
    {
        if (!IsSpawned) return;
        RequestUnlockServerRpc();
    }

    // -------------- ServerRPCs --------------
    // IMPORTANT: RequireOwnership=false because TankDraftManager is server-owned.

    [ServerRpc(Delivery = RpcDelivery.Reliable, RequireOwnership = false)]
    private void RequestLockServerRpc(int tankId, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        var gsm = GameStateManagerNew.Instance;
        if (gsm == null || gsm.State != MatchStateNew.TankSelect)
        {
            FailTo(sender, TankDraftFailReasonNew.NotInTankSelect);
            return;
        }

        if (!_tankById.ContainsKey(tankId))
        {
            FailTo(sender, TankDraftFailReasonNew.InvalidTankId);
            return;
        }

        int idx = FindPickIndex(sender);
        if (idx < 0)
        {
            // late join safety
            Picks.Add(new TankPickEntryNew
            {
                ClientId = sender,
                Team = ResolveTeamForClient(sender),
                TankId = -1,
                Locked = false
            });
            idx = FindPickIndex(sender);
        }

        var entry = Picks[idx];

        if (entry.Locked)
        {
            FailTo(sender, TankDraftFailReasonNew.AlreadyLocked);
            return;
        }

        if (uniquePerTeam && IsTankLockedByTeam(entry.Team, tankId))
        {
            FailTo(sender, TankDraftFailReasonNew.TankTakenByTeam);
            return;
        }

        entry.TankId = tankId;
        entry.Locked = true;
        Picks[idx] = entry;
    }

    [ServerRpc(Delivery = RpcDelivery.Reliable, RequireOwnership = false)]
    private void RequestUnlockServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        var gsm = GameStateManagerNew.Instance;
        if (gsm == null || gsm.State != MatchStateNew.TankSelect)
        {
            FailTo(sender, TankDraftFailReasonNew.NotInTankSelect);
            return;
        }

        int idx = FindPickIndex(sender);
        if (idx < 0) return;

        var entry = Picks[idx];
        entry.Locked = false;
        Picks[idx] = entry;
    }

    // -------------- Helpers --------------

    public bool TryGetPick(ulong clientId, out TankPickEntryNew pick)
    {
        for (int i = 0; i < Picks.Count; i++)
        {
            if (Picks[i].ClientId == clientId)
            {
                pick = Picks[i];
                return true;
            }
        }
        pick = default;
        return false;
    }

    public TankDefinitionNew GetTankDef(int tankId)
        => _tankById.TryGetValue(tankId, out var def) ? def : null;

    private int FindPickIndex(ulong clientId)
    {
        for (int i = 0; i < Picks.Count; i++)
            if (Picks[i].ClientId == clientId)
                return i;
        return -1;
    }

    private bool IsTankLockedByTeam(TeamIdNew team, int tankId)
    {
        for (int i = 0; i < Picks.Count; i++)
        {
            var p = Picks[i];
            if (p.Team == team && p.Locked && p.TankId == tankId)
                return true;
        }
        return false;
    }

    private void AutoAssignAndLockEveryone()
    {
        if (!IsServer) return;

        HashSet<int> lockedA = new();
        HashSet<int> lockedB = new();

        for (int i = 0; i < Picks.Count; i++)
        {
            var p = Picks[i];
            if (p.Locked && p.TankId >= 0)
            {
                if (p.Team == TeamIdNew.TeamA) lockedA.Add(p.TankId);
                else if (p.Team == TeamIdNew.TeamB) lockedB.Add(p.TankId);
            }
        }

        for (int i = 0; i < Picks.Count; i++)
        {
            var p = Picks[i];
            if (p.Locked) continue;

            int assigned = PickFirstAvailable(p.Team, lockedA, lockedB);
            if (assigned < 0) continue;

            p.TankId = assigned;
            p.Locked = true;
            Picks[i] = p;

            if (p.Team == TeamIdNew.TeamA) lockedA.Add(assigned);
            else if (p.Team == TeamIdNew.TeamB) lockedB.Add(assigned);
        }
    }

    private int PickFirstAvailable(TeamIdNew team, HashSet<int> lockedA, HashSet<int> lockedB)
    {
        if (tankOptions == null || tankOptions.Length == 0) return -1;

        foreach (var def in tankOptions)
        {
            if (def == null) continue;
            int id = def.tankId;

            if (!uniquePerTeam)
                return id;

            if (team == TeamIdNew.TeamA && !lockedA.Contains(id)) return id;
            if (team == TeamIdNew.TeamB && !lockedB.Contains(id)) return id;
        }

        foreach (var def in tankOptions)
            if (def != null) return def.tankId;

        return -1;
    }

    private TeamIdNew ResolveTeamForClient(ulong clientId)
    {
        if (NetworkManager == null) return (clientId % 2UL == 0UL) ? TeamIdNew.TeamA : TeamIdNew.TeamB;

        foreach (var no in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (no == null) continue;
            if (no.OwnerClientId != clientId) continue;

            var tc = no.GetComponent<TeamComponentNew>();
            if (tc != null && tc.Team != TeamIdNew.None)
                return tc.Team;
        }

        return (clientId % 2UL == 0UL) ? TeamIdNew.TeamA : TeamIdNew.TeamB;
    }

    private void FailTo(ulong clientId, TankDraftFailReasonNew reason)
    {
        var send = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        DraftFailClientRpc(reason, send);
    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    private void DraftFailClientRpc(TankDraftFailReasonNew reason, ClientRpcParams clientRpcParams = default)
    {
        OnClientDraftFail?.Invoke(reason);
    }
}
