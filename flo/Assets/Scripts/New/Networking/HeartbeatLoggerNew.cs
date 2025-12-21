using System.Diagnostics;
using System.IO;
using UnityEngine;

public class HeartbeatLoggerNew : MonoBehaviour
{
    [SerializeField] private string fileName = "heartbeat.txt";
    [SerializeField] private float intervalSeconds = 1f;

    private string _path;
    private float _next;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        _path = Path.Combine(Application.persistentDataPath, fileName);

        // OVERWRITE each run so we don't mix sessions
        File.WriteAllText(_path,
            $"Heartbeat start\n" +
            $"pid={Process.GetCurrentProcess().Id}\n" +
            $"persistentDataPath={Application.persistentDataPath}\n");

        _next = Time.realtimeSinceStartup + intervalSeconds;
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < _next) return;
        _next += intervalSeconds;

        File.AppendAllText(_path, $"t={Time.realtimeSinceStartup:0.00} frame={Time.frameCount}\n");
    }
}
