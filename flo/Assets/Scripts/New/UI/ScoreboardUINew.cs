using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ScoreboardUINew : MonoBehaviour
{
    [SerializeField] private GameObject rootPanel;
    [SerializeField] private TextMeshProUGUI text;

    private void Awake()
    {
        if (rootPanel != null)
            rootPanel.SetActive(false);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // Hold-to-show (classic Tab)
        bool show = kb.tabKey.isPressed;

        if (rootPanel != null && rootPanel.activeSelf != show)
            rootPanel.SetActive(show);

        if (!show) return;

        RefreshText();
    }

    private void RefreshText()
    {
        if (text == null) return;

        var mgr = PlayerStatsManagerNew.Instance;
        if (mgr == null)
        {
            text.text = "No PlayerStatsManagerNew";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("SCOREBOARD");
        sb.AppendLine("Name\tKills\tDeaths");

        foreach (var s in mgr.Stats)
            sb.AppendLine($"{s.DisplayName}\t{s.Kills}\t{s.Deaths}");

        text.text = sb.ToString();
    }
}
