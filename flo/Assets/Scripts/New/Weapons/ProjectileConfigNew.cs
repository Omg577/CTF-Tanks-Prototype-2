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

    [Header("Collision")]
    public LayerMask hitMask = ~0;          // things the projectile can collide with
    public LayerMask vehicleMask = ~0;      // which layers count as "damageable vehicles"
}

