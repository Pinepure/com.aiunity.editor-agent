#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiServiceLogEntry
    {
        public string time;
        public string level;
        public string message;
    }

    [Serializable]
    internal sealed class AiToolCallEntry
    {
        public string time;
        public string toolId;
        public bool ok;
        public long durationMs;
        public string message;
    }

    [Serializable]
    internal sealed class AiCapturedConsoleEntry
    {
        public string time;
        public string type;
        public string condition;
        public string stackTrace;
    }

    internal static class AiEditorAgentState
    {
        private const int MaxServiceLogs = 300;
        private const int MaxToolCalls = 300;
        private const int MaxConsoleEntries = 300;

        private static readonly object Sync = new object();
        private static readonly List<AiServiceLogEntry> ServiceLogs = new List<AiServiceLogEntry>();
        private static readonly List<AiToolCallEntry> ToolCalls = new List<AiToolCallEntry>();
        private static readonly List<AiCapturedConsoleEntry> ConsoleEntries = new List<AiCapturedConsoleEntry>();
        private static bool subscribed;

        public static void EnsureSubscribed()
        {
            if (subscribed) return;
            subscribed = true;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        public static void Dispose()
        {
            if (!subscribed) return;
            subscribed = false;
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        public static void Log(string level, string message)
        {
            lock (Sync)
            {
                ServiceLogs.Add(new AiServiceLogEntry
                {
                    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    level = string.IsNullOrEmpty(level) ? "info" : level,
                    message = message ?? string.Empty
                });
                Trim(ServiceLogs, MaxServiceLogs);
            }
        }

        public static void Info(string message)
        {
            Log("info", message);
            Debug.Log("[AI Editor Agent] " + message);
        }

        public static void Warn(string message)
        {
            Log("warning", message);
            Debug.LogWarning("[AI Editor Agent] " + message);
        }

        public static void Error(string message)
        {
            Log("error", message);
            Debug.LogError("[AI Editor Agent] " + message);
        }

        public static void RecordCall(string toolId, bool ok, long durationMs, string message)
        {
            lock (Sync)
            {
                ToolCalls.Add(new AiToolCallEntry
                {
                    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    toolId = toolId ?? string.Empty,
                    ok = ok,
                    durationMs = durationMs,
                    message = message ?? string.Empty
                });
                Trim(ToolCalls, MaxToolCalls);
            }
        }

        public static List<AiServiceLogEntry> GetServiceLogs(int max)
        {
            lock (Sync)
            {
                return Tail(ServiceLogs, max <= 0 ? 100 : max);
            }
        }

        public static List<AiToolCallEntry> GetToolCalls(int max)
        {
            lock (Sync)
            {
                return Tail(ToolCalls, max <= 0 ? 100 : max);
            }
        }

        public static List<AiCapturedConsoleEntry> GetConsoleEntries(int max, bool errorsOnly)
        {
            lock (Sync)
            {
                List<AiCapturedConsoleEntry> filtered = new List<AiCapturedConsoleEntry>();
                for (int i = 0; i < ConsoleEntries.Count; i++)
                {
                    AiCapturedConsoleEntry e = ConsoleEntries[i];
                    if (!errorsOnly || e.type == "Error" || e.type == "Exception" || e.type == "Assert")
                    {
                        filtered.Add(e);
                    }
                }
                return Tail(filtered, max <= 0 ? 100 : max);
            }
        }

        public static void ClearServiceLogs()
        {
            lock (Sync)
            {
                ServiceLogs.Clear();
            }
        }

        public static void ClearToolCalls()
        {
            lock (Sync)
            {
                ToolCalls.Clear();
            }
        }

        public static void ClearCapturedConsole()
        {
            lock (Sync)
            {
                ConsoleEntries.Clear();
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning)
            {
                return;
            }

            lock (Sync)
            {
                ConsoleEntries.Add(new AiCapturedConsoleEntry
                {
                    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    type = type.ToString(),
                    condition = condition ?? string.Empty,
                    stackTrace = stackTrace ?? string.Empty
                });
                Trim(ConsoleEntries, MaxConsoleEntries);
            }
        }

        private static List<T> Tail<T>(List<T> list, int max)
        {
            int count = Math.Min(max, list.Count);
            int start = Math.Max(0, list.Count - count);
            List<T> copy = new List<T>(count);
            for (int i = start; i < list.Count; i++) copy.Add(list[i]);
            return copy;
        }

        private static void Trim<T>(List<T> list, int max)
        {
            if (list.Count <= max) return;
            int remove = list.Count - max;
            list.RemoveRange(0, remove);
        }
    }
}
#endif
