using UnityEngine;

public class PerformanceBootstrapNew : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // Disable vSync so FPS isn't capped to monitor refresh in a weird way
        QualitySettings.vSyncCount = 0;

        // Pick a target framerate (120 is a good dev default)
        Application.targetFrameRate = 120;

        // Optional: reduce physics jitter by letting fixed timestep run normally
        // (fixed timestep is set in Project Settings > Time)
    }
}
