using UnityEngine;
using TMPro;

public class ScoreHUDNew : MonoBehaviour
{
    [Header("TMP References")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI payloadText;
    [SerializeField] private TextMeshProUGUI matchText;

    private PayloadNew _payloadA;
    private PayloadNew _payloadB;

    private void Start()
    {
        // Find payloads once (MVP). If you spawn them dynamically later, we’ll make this smarter.
        FindPayloads();
    }

    private void Update()
    {
        if (_payloadA == null || _payloadB == null)
            FindPayloads();

        UpdateScore();
        UpdatePayload();
        UpdateMatch();
    }

    private void FindPayloads()
    {
#if UNITY_6000_0_OR_NEWER
        var payloads = Object.FindObjectsByType<PayloadNew>(FindObjectsSortMode.None);
#else
        var payloads = Object.FindObjectsOfType<PayloadNew>();
#endif
        _payloadA = null;
        _payloadB = null;

        foreach (var p in payloads)
        {
            if (p.OwnerTeam == TeamIdNew.TeamA) _payloadA = p;
            else if (p.OwnerTeam == TeamIdNew.TeamB) _payloadB = p;
        }
    }

    private void UpdateScore()
    {
        if (scoreText == null) return;

        int a = 0, b = 0;
        if (ScoreManagerNew.Instance != null)
        {
            a = ScoreManagerNew.Instance.TeamAScore;
            b = ScoreManagerNew.Instance.TeamBScore;
        }

        scoreText.text = $"Score  A: {a}   B: {b}";
    }

    private void UpdatePayload()
    {
        if (payloadText == null) return;

        string a = PayloadStatus(_payloadA);
        string b = PayloadStatus(_payloadB);

        payloadText.text =
            $"Payload A: {a}\n" +
            $"Payload B: {b}";
    }

    private string PayloadStatus(PayloadNew p)
    {
        if (p == null) return "Missing";

        if (p.IsCarried) return $"Carried (carrierId={p.CarrierNetObjectId})";
        if (p.IsDropped) return $"Dropped (returns in {p.GetReturnRemainingSeconds():0.0}s)";
        return "Home";
    }

    private void UpdateMatch()
    {
        if (matchText == null) return;

        var gsm = GameStateManagerNew.Instance;
        if (gsm == null)
        {
            matchText.text = "Match: (no manager)";
            return;
        }

        switch (gsm.State)
        {
            case MatchStateNew.Lobby:
                matchText.text = "Match: Lobby (waiting for players)";
                break;
            case MatchStateNew.Countdown:
                matchText.text = $"Match: Starting in {gsm.GetCountdownRemaining():0.0}s";
                break;
            case MatchStateNew.InGame:
                matchText.text = $"Match: In Game ({gsm.GetMatchRemaining():0}s left)";
                break;
            case MatchStateNew.GameOver:
                matchText.text = "Match: Game Over";
                break;
        }
    }
}
