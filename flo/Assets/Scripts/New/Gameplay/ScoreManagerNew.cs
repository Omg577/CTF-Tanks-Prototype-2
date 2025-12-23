using Unity.Netcode;
using UnityEngine;

public class ScoreManagerNew : NetworkBehaviour
{
    public static ScoreManagerNew Instance { get; private set; }

    private readonly NetworkVariable<int> teamAScore = new(0);
    private readonly NetworkVariable<int> teamBScore = new(0);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public int TeamAScore => teamAScore.Value;
    public int TeamBScore => teamBScore.Value;

    public void ServerAddPoint(TeamIdNew scoringTeam)
    {
        if (!IsServer) return;

        if (scoringTeam == TeamIdNew.TeamA) teamAScore.Value++;
        else if (scoringTeam == TeamIdNew.TeamB) teamBScore.Value++;
    }

    public void ServerResetScores()
    {
        if (!IsServer) return;
        teamAScore.Value = 0;
        teamBScore.Value = 0;
    }
}
