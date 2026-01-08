using UnityEngine;

public class AutoDestroyVfxNew : MonoBehaviour
{
    [SerializeField] private float destroyAfterSeconds = 0.75f;

    private void OnEnable()
    {
        if (destroyAfterSeconds <= 0f) destroyAfterSeconds = 0.5f;
        CancelInvoke(nameof(Kill));
        Invoke(nameof(Kill), destroyAfterSeconds);
    }

    private void Kill()
    {
        Destroy(gameObject);
    }
}
