using Unity.Netcode;
using UnityEngine;

public class GameStateManagerNew : NetworkBehaviour
{
    public static GameStateManagerNew Instance { get; private set; }

    [Header("Match Settings")]
    [SerializeField] private int winningCaptures = 3;
    [SerializeField] private float matchLengthSeconds = 8 * 60f; // 8 min
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private float gameOverSeconds = 6f;

    [Header("Start Condition (MVP)")]
    [Tooltip("If true, match starts when both teams have at least one player connected.")]
    [SerializeField] private bool requireBothTeamsPresent = true;

    [Header("References")]
    [SerializeField] private PayloadNew payloadTeamA;
    [SerializeField] private PayloadNew payloadTeamB;

    // Networked state
    private readonly NetworkVariable<byte> state = new(
        (byte)MatchStateNew.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> stateEndsAtServerTime = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> matchEndsAtServerTime = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public MatchStateNew State => (MatchStateNew)state.Value;

    public bool PlayersCanMove =>
        State == MatchStateNew.InGame;

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
            SetState(MatchStateNew.Lobby, 0f);
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        // Win condition by score
        if (ScoreManagerNew.Instance != null)
        {
            if (ScoreManagerNew.Instance.TeamAScore >= winningCaptures)
            {
                EnterGameOver();
                return;
            }
            if (ScoreManagerNew.Instance.TeamBScore >= winningCaptures)
            {
                EnterGameOver();
                return;
            }
        }

        // State machine timings
        float now = (float)NetworkManager.ServerTime.Time;

        switch (State)
        {
            case MatchStateNew.Lobby:
                if (CanStartMatch())
                    EnterCountdown();
                break;

            case MatchStateNew.Countdown:
                if (now >= stateEndsAtServerTime.Value)
                    EnterInGame();
                break;

            case MatchStateNew.InGame:
                if (now >= matchEndsAtServerTime.Value)
                    EnterGameOver();
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

        // MVP: require at least 2 players connected
        if (NetworkManager.Singleton.ConnectedClientsIds.Count < 2)
            return false;

        if (!requireBothTeamsPresent)
            return true;

        // Require at least 1 TeamA and 1 TeamB vehicle spawned
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

    private void EnterCountdown()
    {
        float now = (float)NetworkManager.ServerTime.Time;
        SetState(MatchStateNew.Countdown, now + countdownSeconds);
    }

    private void EnterInGame()
    {
        float now = (float)NetworkManager.ServerTime.Time;
        SetState(MatchStateNew.InGame, 0f);
        matchEndsAtServerTime.Value = now + matchLengthSeconds;
    }

    private void EnterGameOver()
    {
        float now = (float)NetworkManager.ServerTime.Time;
        SetState(MatchStateNew.GameOver, now + gameOverSeconds);
    }

    private void ResetToLobby()
    {
        // Reset score
        if (ScoreManagerNew.Instance != null)
        {
            // MVP reset: easiest is to add a ServerReset() in ScoreManagerNew if you want.
            // For now, we just restart scene/reset manually later.
        }

        // Return payloads home
        if (payloadTeamA != null) payloadTeamA.ServerReturnHome();
        if (payloadTeamB != null) payloadTeamB.ServerReturnHome();

        SetState(MatchStateNew.Lobby, 0f);
    }

    private void SetState(MatchStateNew newState, float endsAtServerTime)
    {
        state.Value = (byte)newState;
        stateEndsAtServerTime.Value = endsAtServerTime;
    }

    // Client/UI helpers
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
