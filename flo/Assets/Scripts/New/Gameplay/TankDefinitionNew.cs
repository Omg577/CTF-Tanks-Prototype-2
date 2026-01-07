using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "New/Tanks/TankDefinitionNew", fileName = "TankDefinitionNew")]
public class TankDefinitionNew : ScriptableObject
{
    [Header("Identity")]
    public int tankId = 0;
    public string displayName = "Tank";
    [TextArea] public string description = "";

    [Header("Prefab")]
    [Tooltip("NetworkObject vehicle prefab to spawn for this tank.")]
    public NetworkObject tankPrefab;

    [Header("UI")]
    public Sprite icon;

    [Tooltip("Optional non-networked preview model prefab (mesh-only).")]
    public GameObject previewModelPrefab;
}
