#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;

namespace AiUnity.EditorAgent
{
    internal sealed class AiConsoleCounts
    {
        public int errorCount;
        public int warningCount;
        public int logCount;
        public bool available;
        public string message;
    }

    internal static class AiConsoleUtility
    {
        public static AiConsoleCounts GetCounts()
        {
            AiConsoleCounts counts = new AiConsoleCounts();
            try
            {
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType == null)
                {
                    counts.message = "UnityEditor.LogEntries type is not available.";
                    return counts;
                }

                MethodInfo method = logEntriesType.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                {
                    counts.message = "LogEntries.GetCountsByType is not available.";
                    return counts;
                }

                object[] args = new object[] { 0, 0, 0 };
                method.Invoke(null, args);
                counts.errorCount = Convert.ToInt32(args[0]);
                counts.warningCount = Convert.ToInt32(args[1]);
                counts.logCount = Convert.ToInt32(args[2]);
                counts.available = true;
                counts.message = "ok";
                return counts;
            }
            catch (Exception e)
            {
                counts.message = e.Message;
                return counts;
            }
        }

        public static bool Clear(out string message)
        {
            try
            {
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType == null)
                {
                    message = "UnityEditor.LogEntries type is not available.";
                    return false;
                }

                MethodInfo method = logEntriesType.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                {
                    message = "LogEntries.Clear is not available.";
                    return false;
                }

                method.Invoke(null, null);
                message = "Console cleared.";
                return true;
            }
            catch (Exception e)
            {
                message = e.Message;
                return false;
            }
        }
    }
}
#endif
