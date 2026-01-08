using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TankSelectOptionButtonNew : MonoBehaviour
{
    [Header("Core (auto-wired if left null)")]
    [SerializeField] private Button button;
    [SerializeField] private Image icon;

    // Support BOTH label types
    [SerializeField] private TMP_Text labelTMP;
    [SerializeField] private Text labelLegacy;

    [Header("Visual States (Optional)")]
    [SerializeField] private GameObject selectedVisual;
    [SerializeField] private GameObject takenVisual;
    [SerializeField] private GameObject lockedVisual;

    // Optional overlays (either TMP or legacy)
    [SerializeField] private TMP_Text takenLabelTMP;
    [SerializeField] private Text takenLabelLegacy;

    [SerializeField] private TMP_Text lockedLabelTMP;
    [SerializeField] private Text lockedLabelLegacy;

    private int _tankId;
    private System.Action<int> _onClick;

    public int TankId => _tankId;

    private void Awake()
    {
        AutoWire();
        SetState(interactable: true, selected: false, takenByTeam: false, lockedByYou: false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoWire();
    }
#endif

    private void AutoWire()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (icon == null)
        {
            // Choose first child Image that's NOT the button background (common prefab pattern)
            var imgs = GetComponentsInChildren<Image>(true);
            foreach (var img in imgs)
            {
                if (img == null) continue;
                if (button != null && img.gameObject == button.gameObject) continue;
                icon = img;
                break;
            }
        }

        // If neither assigned, try to find them
        if (labelTMP == null)
            labelTMP = GetComponentInChildren<TMP_Text>(true);

        // Only use legacy if we didn't find TMP (avoid double-writing)
        if (labelLegacy == null && labelTMP == null)
            labelLegacy = GetComponentInChildren<Text>(true);

        // Optional overlays: only wire if missing
        if (takenLabelTMP == null)
            takenLabelTMP = FindChildTMPByHint("taken");

        if (lockedLabelTMP == null)
            lockedLabelTMP = FindChildTMPByHint("lock");

        if (takenLabelLegacy == null && takenLabelTMP == null)
            takenLabelLegacy = FindChildLegacyByHint("taken");

        if (lockedLabelLegacy == null && lockedLabelTMP == null)
            lockedLabelLegacy = FindChildLegacyByHint("lock");
    }

    private TMP_Text FindChildTMPByHint(string hint)
    {
        var tmps = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps)
        {
            if (t == null) continue;
            var n = t.name.ToLowerInvariant();
            if (n.Contains(hint)) return t;
        }
        return null;
    }

    private Text FindChildLegacyByHint(string hint)
    {
        var texts = GetComponentsInChildren<Text>(true);
        foreach (var t in texts)
        {
            if (t == null) continue;
            var n = t.name.ToLowerInvariant();
            if (n.Contains(hint)) return t;
        }
        return null;
    }

    public void Bind(int tankId, Sprite sprite, string displayName, System.Action<int> onClick)
    {
        _tankId = tankId;
        _onClick = onClick;

        if (icon != null)
            icon.sprite = sprite;

        string shownName = string.IsNullOrWhiteSpace(displayName) ? $"Tank {tankId}" : displayName;
        SetLabelText(shownName);

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_tankId));
        }

        SetState(interactable: true, selected: false, takenByTeam: false, lockedByYou: false);
    }

    private void SetLabelText(string text)
    {
        if (labelTMP != null)
            labelTMP.text = text;
        else if (labelLegacy != null)
            labelLegacy.text = text;
        else
            Debug.LogWarning($"[{nameof(TankSelectOptionButtonNew)}] No label (TMP_Text or Text) found on '{name}'.");
    }

    private void SetTakenText(string text)
    {
        if (takenLabelTMP != null)
            takenLabelTMP.text = text;
        else if (takenLabelLegacy != null)
            takenLabelLegacy.text = text;
    }

    private void SetLockedText(string text)
    {
        if (lockedLabelTMP != null)
            lockedLabelTMP.text = text;
        else if (lockedLabelLegacy != null)
            lockedLabelLegacy.text = text;
    }

    public void SetInteractable(bool value)
    {
        if (button != null) button.interactable = value;
    }

    public void SetState(bool interactable, bool selected, bool takenByTeam, bool lockedByYou)
    {
        SetInteractable(interactable);

        if (selectedVisual != null) selectedVisual.SetActive(selected);
        if (takenVisual != null) takenVisual.SetActive(takenByTeam);
        if (lockedVisual != null) lockedVisual.SetActive(lockedByYou);

        SetTakenText(takenByTeam ? "TAKEN" : "");
        SetLockedText(lockedByYou ? "LOCKED" : "");
    }
}
