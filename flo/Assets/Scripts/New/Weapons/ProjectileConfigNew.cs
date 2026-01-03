using UnityEngine;

[CreateAssetMenu(menuName = "New/Weapons/ProjectileConfigNew", fileName = "ProjectileConfigNew")]
public class ProjectileConfigNew : ScriptableObject
{
    [Header("Flight")]
    public float speed = 25f;
    public float lifetime = 3.0f;

    [Header("Damage")]
    public int damage = 25;
    public bool friendlyFire = false;

    [Header("Firing")]
    public float fireCooldownSeconds = 0.25f;

    [Header("Damage Falloff")]
    public bool useFalloff = false;

    [Tooltip("x = normalized distance (0..1), y = damage multiplier. Example: (0,1) -> (1,0.5)")]
    public AnimationCurve falloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Tooltip("Distance (meters) where falloff reaches the end of the curve. Usually equals your gameplay range.")]
    public float falloffMaxDistance = 40f;

    [Header("Collision")]
    public LayerMask hitMask = ~0;          // things the projectile can collide with
    public LayerMask vehicleMask = ~0;      // which layers count as "damageable vehicles"
}
