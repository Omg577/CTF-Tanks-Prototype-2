using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "New/Weapons/ProjectileConfigNew", fileName = "ProjectileConfigNew")]
public class ProjectileConfigNew : ScriptableObject
{
    [Header("Prefab (per weapon)")]
    [Tooltip("NetworkObject prefab spawned for this weapon. Must include ProjectileNew + NetworkObject.")]
    public NetworkObject projectilePrefab;

    [Header("Flight")]
    [Min(0.1f)] public float speed = 25f;
    [Min(0.05f)] public float lifetime = 3.0f;

    [Header("Damage")]
    [Min(0)] public int damage = 25;
    public bool friendlyFire = false;

    [Header("Firing")]
    [Min(0.01f)] public float fireCooldownSeconds = 0.25f;

    [Tooltip("Spawn the projectile this far forward from the muzzle to avoid starting inside a collider.")]
    [Min(0f)] public float spawnForwardOffset = 0.6f;

    [Tooltip("Ignore collisions with the shooter for a short time after spawn.")]
    [Min(0f)] public float ignoreShooterCollisionSeconds = 0.12f;

    [Header("Damage Falloff")]
    public bool useFalloff = false;

    [Tooltip("x = normalized distance (0..1), y = damage multiplier.")]
    public AnimationCurve falloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Min(0.1f)] public float falloffMaxDistance = 40f;

    [Header("Archetype C: Explosive Splash")]
    public bool isExplosive = false;

    [Min(0f)] public float splashRadius = 4f;

    [Range(0f, 1f)]
    public float splashEdgeMultiplier = 0.35f;

    [Tooltip("If LOS is blocked by world geometry, multiply splash damage by this value. (0 = no damage through cover)")]
    [Range(0f, 1f)]
    public float blockedSplashMultiplier = 0.15f;

    [Header("Archetype D: Ricochet")]
    public bool isRicochet = false;

    [Min(0)] public int maxBounces = 3;

    [Range(0.1f, 1f)]
    public float bounceSpeedMultiplier = 0.85f;

    [Header("Collision")]
    public LayerMask hitMask = ~0;     // world + vehicles
    public LayerMask vehicleMask = ~0; // vehicles only (for point-blank overlap and splash)

    // -----------------------------
    // NEW: Toon VFX (Networked)
    // -----------------------------
    [Header("VFX - Muzzle Flash (Networked Cosmetic)")]
    [Tooltip("Prefab spawned locally on each client via RPC. Should NOT be a NetworkObject.")]
    public GameObject muzzleFlashPrefab;

    [Tooltip("Offset applied in muzzle 'shot rotation' space (yaw-only).")]
    public Vector3 muzzleFlashLocalOffset = Vector3.zero;

    [Tooltip("Fallback destroy time if the prefab doesn't self-destroy.")]
    [Min(0f)] public float muzzleFlashDestroySeconds = 0.75f;
}
