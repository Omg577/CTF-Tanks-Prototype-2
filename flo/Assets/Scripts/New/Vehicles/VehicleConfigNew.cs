using UnityEngine;

[CreateAssetMenu(menuName = "DualPayload/VehicleConfigNew")]
public class VehicleConfigNew : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "Vehicle";

    [Header("Movement")]
    public MovementModelConfigNew movementModel;

    [Header("Gameplay (later)")]
    public float maxHealth = 100f;
}
