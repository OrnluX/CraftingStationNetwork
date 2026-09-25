using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace CraftingStationNetwork.Diagnostics
{
    internal static class DevelopmentConsole
    {
        private const int MaxLines = 250;
        private const int WindowId = 734201;

        private static readonly object Sync = new object();
        private static readonly Queue<string> Lines = new Queue<string>(MaxLines);
        private static readonly HashSet<string> OnceKeys = new HashSet<string>(StringComparer.Ordinal);

        private static StreamWriter _writer;
        private static bool _enabled;
        private static bool _visible = true;
        private static Rect _windowRect = new Rect(20f, 20f, 920f, 480f);
        private static Vector2 _scroll;
        private static string _logPath;

        internal static bool Enabled => _enabled;

        internal static void Initialize(bool enabled)
        {
            _enabled = enabled;
            if (!_enabled)
            {
                return;
            }

            try
            {
                string directory = Path.Combine(Paths.PluginPath, "CraftingStationNetwork");
                Directory.CreateDirectory(directory);
                _logPath = Path.Combine(directory, "CraftingStationNetwork.dev.log");
                _writer = new StreamWriter(_logPath, false) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                _logPath = null;
                Plugin.Log?.LogWarning($"Could not create dedicated development log: {ex.GetType().Name}: {ex.Message}");
            }

            Trace($"Development console initialized. File={_logPath ?? "<unavailable>"}");
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                try
                {
                    _writer?.Dispose();
                }
                catch
                {
                }

                _writer = null;
            }
        }

        internal static void Trace(string message)
        {
            if (!_enabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            lock (Sync)
            {
                while (Lines.Count >= MaxLines)
                {
                    Lines.Dequeue();
                }

                Lines.Enqueue(line);

                try
                {
                    _writer?.WriteLine(line);
                }
                catch
                {
                }
            }
        }

        internal static void TraceOnce(string key, string message)
        {
            if (!_enabled || string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (Sync)
            {
                if (!OnceKeys.Add(key))
                {
                    return;
                }
            }

            Trace(message);
        }

        internal static void Draw()
        {
            if (!_enabled)
            {
                return;
            }

            if (!_visible)
            {
                if (GUI.Button(new Rect(10f, 10f, 190f, 28f), "CSN Dev Console"))
                {
                    _visible = true;
                }

                return;
            }

            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "CraftingStationNetwork - Development Console");
        }

        private static void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dedicated runtime diagnostics (separate from the BepInEx console)");

            if (GUILayout.Button("Clear", GUILayout.Width(70f)))
            {
                lock (Sync)
                {
                    Lines.Clear();
                    OnceKeys.Clear();
                }
            }

            if (GUILayout.Button("Hide", GUILayout.Width(70f)))
            {
                _visible = false;
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_logPath))
            {
                GUILayout.Label("Log file: " + _logPath);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            string[] snapshot;
            lock (Sync)
            {
                snapshot = Lines.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                GUILayout.Label(snapshot[i]);
            }

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }
    }
}
