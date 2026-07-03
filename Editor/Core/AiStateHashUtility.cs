#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace AiUnity.EditorAgent
{
    internal static class AiStateHashUtility
    {
        public static string BuildStateHashesJson()
        {
            string compileHash = BuildCompileHash();
            string consoleHash = BuildConsoleHash();
            string selectionHash = BuildSelectionHash();
            string worksetsHash = HashParts("worksets", AiWorksetStore.Count().ToString());
            string snapshotsHash = HashParts("snapshots", AiSnapshotStore.Count().ToString());
            string recipesHash = HashParts("recipes", AiRecipeStore.Count().ToString());

            return "{"
                + "\"compile\":" + AiJson.Quote(compileHash) + ","
                + "\"console\":" + AiJson.Quote(consoleHash) + ","
                + "\"selection\":" + AiJson.Quote(selectionHash) + ","
                + "\"worksets\":" + AiJson.Quote(worksetsHash) + ","
                + "\"snapshots\":" + AiJson.Quote(snapshotsHash) + ","
                + "\"recipes\":" + AiJson.Quote(recipesHash)
                + "}";
        }

        public static string BuildProjectStateHash()
        {
            return HashParts(
                BuildCompileHash(),
                BuildConsoleHash(),
                BuildSelectionHash(),
                AiWorksetStore.Count().ToString(),
                AiSnapshotStore.Count().ToString(),
                AiRecipeStore.Count().ToString());
        }

        private static string BuildCompileHash()
        {
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            return HashParts(
                EditorApplication.isCompiling ? "1" : "0",
                EditorApplication.isUpdating ? "1" : "0",
                EditorApplication.isPlaying ? "1" : "0",
                counts.errorCount.ToString(),
                counts.warningCount.ToString(),
                counts.logCount.ToString());
        }

        private static string BuildConsoleHash()
        {
            List<AiCapturedConsoleEntry> entries = AiEditorAgentState.GetConsoleEntries(40, true);
            List<string> parts = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                AiCapturedConsoleEntry entry = entries[i];
                string line = entry == null ? string.Empty : entry.condition;
                int newLine = line.IndexOf('\n');
                if (newLine >= 0) line = line.Substring(0, newLine);
                parts.Add((entry == null ? string.Empty : entry.type) + ":" + line);
            }
            return HashParts(parts.ToArray());
        }

        private static string BuildSelectionHash()
        {
            UnityEngine.Object[] objects = Selection.objects;
            List<string> parts = new List<string>();
            for (int i = 0; i < objects.Length; i++)
            {
                UnityEngine.Object obj = objects[i];
                if (obj == null) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                parts.Add(obj.GetInstanceID().ToString() + "|" + (path ?? string.Empty) + "|" + obj.name);
            }
            return HashParts(parts.ToArray());
        }

        private static string HashParts(params string[] parts)
        {
            ulong hash = 1469598103934665603UL;
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i] ?? string.Empty;
                    for (int c = 0; c < part.Length; c++)
                    {
                        hash ^= part[c];
                        hash *= 1099511628211UL;
                    }
                    hash ^= '|';
                    hash *= 1099511628211UL;
                }
            }
            return hash.ToString("x16");
        }
    }
}
#endif
