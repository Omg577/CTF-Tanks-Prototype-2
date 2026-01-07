using UnityEngine;
using UnityEngine.UI;

public class TankSelectOptionButtonNew : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private Text label;

    private int _tankId;
    private System.Action<int> _onClick;

    public void Bind(int tankId, Sprite sprite, string displayName, System.Action<int> onClick)
    {
        _tankId = tankId;
        _onClick = onClick;

        if (icon != null) icon.sprite = sprite;
        if (label != null) label.text = displayName;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_tankId));
        }
    }

    public void SetInteractable(bool value)
    {
        if (button != null) button.interactable = value;
    }
}
