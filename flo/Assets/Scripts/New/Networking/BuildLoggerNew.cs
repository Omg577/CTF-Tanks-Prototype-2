using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class BuildLoggerNew : MonoBehaviour
{
    [Header("On-screen console")]
    [SerializeField] private bool showOnScreen = true;
    [SerializeField] private int maxLines = 18;

    [Header("File logging")]
    [SerializeField] private bool writeToFile = true;
    [SerializeField] private string fileName = "runtime-log.txt";

    private readonly Queue<string> _lines = new();
    private string _filePath;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        _filePath = Path.Combine(Application.persistentDataPath, fileName);
        Enqueue($"[BuildLoggerNew] persistentDataPath={Application.persistentDataPath}");
        Enqueue($"[BuildLoggerNew] logFilePath={_filePath}");

        if (writeToFile)
        {
            try { File.WriteAllText(_filePath, $"Log start {DateTime.Now}\n"); }
            catch { /* ignore */ }
        }

        Application.logMessageReceived += HandleLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame)
            showOnScreen = !showOnScreen;
    }

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        string msg = $"[{type}] {condition}";
        Enqueue(msg);

        if (type == LogType.Exception || type == LogType.Error)
            Enqueue(stackTrace);

        if (writeToFile)
        {
            try
            {
                File.AppendAllText(_filePath, msg + "\n");
                if (type == LogType.Exception || type == LogType.Error)
                    File.AppendAllText(_filePath, stackTrace + "\n");
            }
            catch { /* ignore */ }
        }
    }

    private void Enqueue(string s)
    {
        _lines.Enqueue(s);
        while (_lines.Count > maxLines) _lines.Dequeue();
    }

    private void OnGUI()
    {
        if (!showOnScreen) return;

        GUI.Box(new Rect(10, 10, 900, 22 + 16 * maxLines), "Runtime Log (F1 toggles)");
        int y = 35;

        foreach (string line in _lines)
        {
            GUI.Label(new Rect(20, y, 880, 20), line);
            y += 16;
        }
    }
}
