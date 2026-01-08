using Unity.Netcode;
using UnityEngine;

public class GameStateManagerNew : NetworkBehaviour
{
    public static GameStateManagerNew Instance { get; private set; }

    [Header("Match Settings")]
    [SerializeField] private int winningCaptures = 3;
    [SerializeField] private float matchLengthSeconds = 8 * 60f;

    [Header("Phases")]
    [SerializeField] private float tankSelectSeconds = 15f;
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private float gameOverSeconds = 6f;

    [Header("Start Condition (MVP)")]
    [SerializeField] private bool requireBothTeamsPresent = true;

    [Header("References")]
    [SerializeField] private PayloadNew payloadTeamA;
    [SerializeField] private PayloadNew payloadTeamB;
    [SerializeField] private VehicleSpawnerNew vehicleSpawner;
    [SerializeField] private TankDraftManagerNew tankDraftManager;

    // Networked state
    private readonly NetworkVariable<byte> state = new(
        (byte)MatchStateNew.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> stateEndsAtServerTime = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> matchEndsAtServerTime = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> roundId = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<byte> winnerTeam = new(
        (byte)TeamIdNew.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public MatchStateNew State => (MatchStateNew)state.Value;
    public int RoundId => roundId.Value;
    public TeamIdNew Winner => (TeamIdNew)winnerTeam.Value;

    public bool PlayersCanMove => State == MatchStateNew.InGame;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            SetState(MatchStateNew.Lobby, 0f);
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        // Lazy find refs if not assigned
        if (vehicleSpawner == null) vehicleSpawner = FindFirstObjectByType<VehicleSpawnerNew>();
        if (tankDraftManager == null) tankDraftManager = FindFirstObjectByType<TankDraftManagerNew>();

        float now = (float)NetworkManager.ServerTime.Time;

        switch (State)
        {
            case MatchStateNew.Lobby:
                winnerTeam.Value = (byte)TeamIdNew.None;

                if (CanStartMatch())
                    EnterTankSelect();
                break;

            case MatchStateNew.TankSelect:
                // NEW: end early if everyone locked
                if (tankDraftManager != null && tankDraftManager.ServerAllPlayersLocked())
                {
                    FinishTankSelectAndEnterCountdown();
                    return;
                }

                if (now >= stateEndsAtServerTime.Value)
                {
                    FinishTankSelectAndEnterCountdown();
                    return;
                }
                break;

            case MatchStateNew.Countdown:
                if (now >= stateEndsAtServerTime.Value)
                    EnterInGame();
                break;

            case MatchStateNew.InGame:
                // Win condition ONLY while InGame
                if (ScoreManagerNew.Instance != null)
                {
                    if (ScoreManagerNew.Instance.TeamAScore >= winningCaptures)
                    {
                        SetWinnerAndGameOver(TeamIdNew.TeamA);
                        return;
                    }
                    if (ScoreManagerNew.Instance.TeamBScore >= winningCaptures)
                    {
                        SetWinnerAndGameOver(TeamIdNew.TeamB);
                        return;
                    }
                }

                // Time limit
                if (now >= matchEndsAtServerTime.Value)
                {
                    SetWinnerAndGameOver(TeamIdNew.None);
                    return;
                }
                break;

            case MatchStateNew.GameOver:
                if (now >= stateEndsAtServerTime.Value)
                    ResetToLobby();
                break;
        }
    }

    private bool CanStartMatch()
    {
        if (NetworkManager.Singleton == null) return false;
        if (NetworkManager.Singleton.ConnectedClientsIds.Count < 2) return false;
        if (!requireBothTeamsPresent) return true;

        int a = 0, b = 0;
        foreach (var netObj in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null) continue;
            var teamComp = netObj.GetComponent<TeamComponentNew>();
            if (teamComp == null) continue;

            if (teamComp.Team == TeamIdNew.TeamA) a++;
            else if (teamComp.Team == TeamIdNew.TeamB) b++;
        }
        return a > 0 && b > 0;
    }

    private void EnterTankSelect()
    {
        float now = (float)NetworkManager.ServerTime.Time;

        // Start a new round cycle here (clients can reset prediction / UI)
        roundId.Value++;

        // Prep draft list on server
        if (tankDraftManager != null)
            tankDraftManager.ServerBeginTankSelect(tankSelectSeconds);

        SetState(MatchStateNew.TankSelect, now + tankSelectSeconds);
    }

    private void FinishTankSelectAndEnterCountdown()
    {
        float now = (float)NetworkManager.ServerTime.Time;

        // Apply final tank picks (server swaps prefabs)
        if (tankDraftManager != null && vehicleSpawner != null)
            tankDraftManager.ServerApplyFinalPicks(vehicleSpawner);

        // Reset scores + payloads + teleport + heal for the actual match start
        if (ScoreManagerNew.Instance != null)
            ScoreManagerNew.Instance.ServerResetScores();

        if (PlayerStatsManagerNew.Instance != null)
            PlayerStatsManagerNew.Instance.ServerResetAll();

        if (payloadTeamA != null) payloadTeamA.ServerReturnHome();
        if (payloadTeamB != null) payloadTeamB.ServerReturnHome();

        if (vehicleSpawner != null)
            vehicleSpawner.ServerTeleportAllToSpawns();

        if (RespawnManagerNew.Instance != null)
            RespawnManagerNew.Instance.ServerResetAllVehiclesForNewRound(invulnerabilitySeconds: 1.0f);

        SetState(MatchStateNew.Countdown, now + countdownSeconds);
    }

    private void EnterInGame()
    {
        float now = (float)NetworkManager.ServerTime.Time;
        SetState(MatchStateNew.InGame, 0f);
        matchEndsAtServerTime.Value = now + matchLengthSeconds;
    }

    private void SetWinnerAndGameOver(TeamIdNew winner)
    {
        winnerTeam.Value = (byte)winner;

        float now = (float)NetworkManager.ServerTime.Time;
        SetState(MatchStateNew.GameOver, now + gameOverSeconds);
    }

    private void ResetToLobby()
    {
        SetState(MatchStateNew.Lobby, 0f);
    }

    private void SetState(MatchStateNew newState, float endsAtServerTime)
    {
        state.Value = (byte)newState;
        stateEndsAtServerTime.Value = endsAtServerTime;
    }

    // Client/UI helpers
    public float GetTankSelectRemaining()
    {
        if (!IsSpawned) return 0f;
        if (State != MatchStateNew.TankSelect) return 0f;
        float now = (float)NetworkManager.Singleton.ServerTime.Time;
        return Mathf.Max(0f, stateEndsAtServerTime.Value - now);
    }

    public float GetCountdownRemaining()
    {
        if (!IsSpawned) return 0f;
        if (State != MatchStateNew.Countdown) return 0f;
        float now = (float)NetworkManager.Singleton.ServerTime.Time;
        return Mathf.Max(0f, stateEndsAtServerTime.Value - now);
    }

    public float GetMatchRemaining()
    {
        if (!IsSpawned) return 0f;
        if (State != MatchStateNew.InGame) return 0f;
        float now = (float)NetworkManager.Singleton.ServerTime.Time;
        return Mathf.Max(0f, matchEndsAtServerTime.Value - now);
    }
}
