using UnityEngine;

public class ProjectileVfxNew : MonoBehaviour
{
    [Header("VFX Prefabs (optional)")]
    public GameObject impactVfxPrefab;
    public GameObject bounceVfxPrefab;

    [Header("Tuning")]
    [Min(0f)] public float impactVfxLifetime = 1.5f;
    [Min(0f)] public float bounceVfxLifetime = 1.0f;
}
