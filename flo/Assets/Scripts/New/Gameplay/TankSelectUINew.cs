using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class TankSelectUINew : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TankDraftManagerNew draft;
    [SerializeField] private GameStateManagerNew gsm;

    [Header("Root Panel")]
    [SerializeField] private GameObject rootPanel;

    [Header("Options")]
    [SerializeField] private Transform optionsParent;
    [SerializeField] private TankSelectOptionButtonNew optionButtonPrefab;
    [SerializeField] private TankDefinitionNew[] uiTankOptions;

    [Header("Buttons")]
    [SerializeField] private Button lockButton;
    [SerializeField] private Button unlockButton;

    [Header("Text")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text debugText; // optional

    private readonly Dictionary<int, TankSelectOptionButtonNew> _btnByTankId = new();
    private int _selectedTankId = -1;

    private void Awake()
    {
        if (lockButton != null) lockButton.onClick.AddListener(OnClickLock);
        if (unlockButton != null) unlockButton.onClick.AddListener(OnClickUnlock);

        TankDraftManagerNew.OnClientDraftFail += OnDraftFail;
    }

    private void OnDestroy()
    {
        TankDraftManagerNew.OnClientDraftFail -= OnDraftFail;
    }

    private void Start()
    {
        ReacquireRefs();
        BuildOptions();
        if (rootPanel != null) rootPanel.SetActive(false);
    }

    private void Update()
    {
        ReacquireRefs();

        bool inTankSelect = (gsm != null && gsm.State == MatchStateNew.TankSelect);

        if (rootPanel != null && rootPanel.activeSelf != inTankSelect)
            rootPanel.SetActive(inTankSelect);

        if (debugText != null)
        {
            string s = gsm != null ? gsm.State.ToString() : "gsm=null";
            string d = draft != null ? $"draftPicks={draft.Picks.Count}" : "draft=null";
            debugText.text = $"State={s} | {d}";
        }

        if (!inTankSelect) return;

        UpdateTimer();
        UpdateButtonStates();
        UpdateLockUnlockButtons();
        UpdateStatusLine();
    }

    private void ReacquireRefs()
    {
        if (gsm == null) gsm = GameStateManagerNew.Instance;
        if (gsm == null) gsm = FindFirstObjectByType<GameStateManagerNew>();

        if (draft == null) draft = FindFirstObjectByType<TankDraftManagerNew>();
    }

    private void BuildOptions()
    {
        if (optionsParent == null || optionButtonPrefab == null) return;
        if (_btnByTankId.Count > 0) return;

        if (uiTankOptions == null || uiTankOptions.Length == 0) return;

        foreach (var def in uiTankOptions)
        {
            if (def == null) continue;

            var btn = Instantiate(optionButtonPrefab, optionsParent);
            btn.Bind(def.tankId, def.icon, def.displayName, OnSelectTank);
            _btnByTankId[def.tankId] = btn;
        }
    }

    private void OnSelectTank(int tankId)
    {
        _selectedTankId = tankId;
    }

    private void UpdateTimer()
    {
        if (timerText == null || gsm == null) return;
        timerText.text = $"Tank Select: {gsm.GetTankSelectRemaining():0.0}s";
    }

    private void UpdateButtonStates()
    {
        if (draft == null || NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (!draft.TryGetPick(localId, out var me)) return;

        // Collect tanks locked by MY team (excluding me)
        HashSet<int> takenByMyTeam = new();
        for (int i = 0; i < draft.Picks.Count; i++)
        {
            var p = draft.Picks[i];
            if (!p.Locked) continue;
            if (p.Team != me.Team) continue;
            if (p.ClientId == localId) continue;
            if (p.TankId >= 0) takenByMyTeam.Add(p.TankId);
        }

        foreach (var kvp in _btnByTankId)
        {
            int tankId = kvp.Key;
            var btn = kvp.Value;
            if (btn == null) continue;

            bool selected = (_selectedTankId == tankId);

            bool lockedByYou = (me.Locked && me.TankId == tankId);
            bool taken = takenByMyTeam.Contains(tankId);

            // Disable if taken by teammate, unless it’s your locked pick
            bool interactable = !taken || lockedByYou;

            btn.SetState(interactable, selected, takenByTeam: taken, lockedByYou: lockedByYou);
        }
    }

    private void UpdateLockUnlockButtons()
    {
        if (draft == null || NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        bool hasPick = draft.TryGetPick(localId, out var me);

        bool canLock = (_selectedTankId >= 0) && hasPick && !me.Locked;
        bool canUnlock = hasPick && me.Locked;

        if (lockButton != null) lockButton.interactable = canLock;
        if (unlockButton != null) unlockButton.interactable = canUnlock;
    }

    private void UpdateStatusLine()
    {
        if (statusText == null || draft == null || NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (!draft.TryGetPick(localId, out var me)) return;

        if (!me.Locked)
        {
            statusText.text = "Pick a tank and press Lock.";
            return;
        }

        string name = FindTankName(me.TankId);
        statusText.text = $"Locked in: {name}";
    }

    private string FindTankName(int tankId)
    {
        if (uiTankOptions == null) return tankId.ToString();
        foreach (var d in uiTankOptions)
            if (d != null && d.tankId == tankId)
                return d.displayName;
        return tankId.ToString();
    }

    private void OnClickLock()
    {
        if (draft == null) return;
        if (_selectedTankId < 0) return;
        draft.ClientRequestLock(_selectedTankId);
    }

    private void OnClickUnlock()
    {
        if (draft == null) return;
        draft.ClientRequestUnlock();
    }

    private void OnDraftFail(TankDraftFailReasonNew reason)
    {
        if (statusText == null) return;

        switch (reason)
        {
            case TankDraftFailReasonNew.NotInTankSelect: statusText.text = "Not in tank select."; break;
            case TankDraftFailReasonNew.InvalidTankId: statusText.text = "Invalid tank."; break;
            case TankDraftFailReasonNew.TankTakenByTeam: statusText.text = "Tank is taken by your team."; break;
            case TankDraftFailReasonNew.AlreadyLocked: statusText.text = "You already locked."; break;
            default: statusText.text = "Lock failed."; break;
        }

        CancelInvoke(nameof(ClearStatus));
        Invoke(nameof(ClearStatus), 2.0f);
    }

    private void ClearStatus()
    {
        // don’t clear if we are locked
        if (draft == null || NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (draft.TryGetPick(localId, out var me) && me.Locked) return;

        if (statusText != null) statusText.text = "";
    }
}
