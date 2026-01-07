using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class TankSelectUINew : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TankDraftManagerNew draft;
    [SerializeField] private GameStateManagerNew gsm;

    [Header("Root")]
    [SerializeField] private GameObject rootPanel;

    [Header("Options List")]
    [SerializeField] private Transform optionsParent;
    [SerializeField] private TankSelectOptionButtonNew optionButtonPrefab;

    [Header("Tank Options (Assign same 6+ TankDefinitionNew assets here)")]
    [SerializeField] private TankDefinitionNew[] uiTankOptions;

    [Header("Preview UI")]
    [SerializeField] private Image previewIcon;
    [SerializeField] private Text previewName;
    [SerializeField] private Text previewDesc;

    [Header("Preview Model (Optional)")]
    [SerializeField] private Transform previewModelAnchor;
    [SerializeField] private float previewSpinDegPerSec = 30f;

    [Header("Controls")]
    [SerializeField] private Button lockButton;
    [SerializeField] private Button unlockButton;

    [Header("Status")]
    [SerializeField] private Text timerText;
    [SerializeField] private Text statusText;

    // Optional debug line
    [Header("Debug (Optional)")]
    [SerializeField] private Text debugText;

    private readonly List<TankSelectOptionButtonNew> _spawnedButtons = new();
    private int _selectedTankId = -1;
    private GameObject _previewModelInstance;

    private void Awake()
    {
        // Don’t force-hide if rootPanel is null (avoid confusion)
        if (rootPanel != null)
            rootPanel.SetActive(false);

        if (statusText != null)
            statusText.text = "";

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
            string d = draft != null ? "draft=ok" : "draft=null";
            debugText.text = $"State={s} | {d}";
        }

        if (!inTankSelect) return;

        UpdateTimer();
        UpdateButtonsInteractable();
        UpdateLockUnlockButtons();
        SpinPreviewModel();
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
        if (_spawnedButtons.Count > 0) return;

        if (uiTankOptions == null || uiTankOptions.Length == 0)
        {
            if (statusText != null) statusText.text = "Assign uiTankOptions (6+ TankDefinitionNew).";
            return;
        }

        foreach (var def in uiTankOptions)
        {
            if (def == null) continue;

            var btn = Instantiate(optionButtonPrefab, optionsParent);
            btn.Bind(def.tankId, def.icon, def.displayName, OnSelectTank);
            _spawnedButtons.Add(btn);
        }
    }

    private void OnSelectTank(int tankId)
    {
        _selectedTankId = tankId;

        var def = FindUiDef(tankId);
        if (def == null) return;

        if (previewIcon != null) previewIcon.sprite = def.icon;
        if (previewName != null) previewName.text = def.displayName;
        if (previewDesc != null) previewDesc.text = def.description;

        SpawnPreviewModel(def);
    }

    private TankDefinitionNew FindUiDef(int tankId)
    {
        if (uiTankOptions == null) return null;
        foreach (var d in uiTankOptions)
            if (d != null && d.tankId == tankId)
                return d;
        return null;
    }

    private void SpawnPreviewModel(TankDefinitionNew def)
    {
        if (previewModelAnchor == null) return;

        if (_previewModelInstance != null)
            Destroy(_previewModelInstance);

        if (def.previewModelPrefab == null) return;

        _previewModelInstance = Instantiate(def.previewModelPrefab, previewModelAnchor);
        _previewModelInstance.transform.localPosition = Vector3.zero;
        _previewModelInstance.transform.localRotation = Quaternion.identity;
        _previewModelInstance.transform.localScale = Vector3.one;
    }

    private void SpinPreviewModel()
    {
        if (_previewModelInstance == null) return;
        _previewModelInstance.transform.Rotate(0f, previewSpinDegPerSec * Time.deltaTime, 0f, Space.Self);
    }

    private void UpdateTimer()
    {
        if (timerText == null || gsm == null) return;
        float rem = gsm.GetTankSelectRemaining();
        timerText.text = $"Tank Select: {rem:0.0}s";
    }

    private void UpdateButtonsInteractable()
    {
        if (draft == null || uiTankOptions == null) return;
        if (NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;

        if (!draft.TryGetPick(localId, out var localPick))
            return;

        TeamIdNew myTeam = localPick.Team;

        HashSet<int> lockedByMyTeam = new();
        for (int i = 0; i < draft.Picks.Count; i++)
        {
            var p = draft.Picks[i];
            if (p.Team == myTeam && p.Locked && p.TankId >= 0)
                lockedByMyTeam.Add(p.TankId);
        }

        // Buttons align to uiTankOptions order
        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            if (i >= uiTankOptions.Length) continue;
            var def = uiTankOptions[i];
            if (def == null) continue;

            bool taken = lockedByMyTeam.Contains(def.tankId);

            // If taken by team, allow only if it’s my own locked pick
            bool interactable = !taken || (localPick.Locked && localPick.TankId == def.tankId);
            _spawnedButtons[i].SetInteractable(interactable);
        }
    }

    private void UpdateLockUnlockButtons()
    {
        if (draft == null || NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        bool hasPick = draft.TryGetPick(localId, out var pick);

        bool canLock = (_selectedTankId >= 0) && hasPick && !pick.Locked;
        bool canUnlock = hasPick && pick.Locked;

        if (lockButton != null) lockButton.interactable = canLock;
        if (unlockButton != null) unlockButton.interactable = canUnlock;
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
            case TankDraftFailReasonNew.TankTakenByTeam: statusText.text = "Tank locked by your team already."; break;
            case TankDraftFailReasonNew.AlreadyLocked: statusText.text = "You already locked."; break;
            default: statusText.text = "Lock failed."; break;
        }

        CancelInvoke(nameof(ClearStatus));
        Invoke(nameof(ClearStatus), 2.0f);
    }

    private void ClearStatus()
    {
        if (statusText != null) statusText.text = "";
    }
}
