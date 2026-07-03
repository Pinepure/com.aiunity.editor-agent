#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiPrefabSnapshotEntry
    {
        public string path;
        public int missingScriptCount;
        public int missingReferenceCount;
        public string inspectionError;
    }

    [Serializable]
    internal sealed class AiSceneSnapshotEntry
    {
        public string path;
        public int missingScriptCount;
        public string inspectionError;
    }

    [Serializable]
    internal sealed class AiContextSnapshotRecord
    {
        public string id;
        public string label;
        public string createdAt;
        public string compileHash;
        public string[] compileFingerprints;
        public string selectionHash;
        public string[] selectionPaths;
        public string gitStatusSummary;
        public string gitDiffSummary;
        public string[] changedFiles;
        public string[] unstagedFiles;
        public string[] stagedFiles;
        public string[] untrackedFiles;
        public AiPrefabSnapshotEntry[] prefabs;
        public AiSceneSnapshotEntry[] scenes;
    }

    internal static class AiSnapshotStore
    {
        public static string Save(AiContextSnapshotRecord record)
        {
            if (record == null) throw new Exception("snapshot record is required");
            AiEditorAgentPaths.EnsureStateFolders();
            PruneExpired();
            if (string.IsNullOrEmpty(record.id)) record.id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(record.createdAt)) record.createdAt = AiEditorAgentSettings.FormatTimestamp(DateTime.Now);
            File.WriteAllText(PathFor(record.id), JsonUtility.ToJson(record, true));
            return record.id;
        }

        public static bool TryGet(string snapshotId, out AiContextSnapshotRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(snapshotId)) return false;
            PruneExpired();
            string path = PathFor(snapshotId);
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            record = JsonUtility.FromJson<AiContextSnapshotRecord>(json);
            if (record == null) return false;
            if (AiEditorAgentSettings.IsTimestampExpired(record.createdAt, AiEditorAgentSettings.SnapshotTtlSeconds))
            {
                Delete(snapshotId);
                record = null;
                return false;
            }
            return true;
        }

        public static bool Delete(string snapshotId)
        {
            string path = PathFor(snapshotId);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public static int Count()
        {
            AiEditorAgentPaths.EnsureStateFolders();
            PruneExpired();
            return Directory.GetFiles(AiEditorAgentPaths.SnapshotsFolderAbsolute, "*.json", SearchOption.TopDirectoryOnly).Length;
        }

        private static void PruneExpired()
        {
            AiEditorAgentPaths.EnsureStateFolders();
            string[] files = Directory.GetFiles(AiEditorAgentPaths.SnapshotsFolderAbsolute, "*.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        File.Delete(files[i]);
                        continue;
                    }

                    AiContextSnapshotRecord record = JsonUtility.FromJson<AiContextSnapshotRecord>(json);
                    if (record == null || AiEditorAgentSettings.IsTimestampExpired(record.createdAt, AiEditorAgentSettings.SnapshotTtlSeconds))
                    {
                        File.Delete(files[i]);
                    }
                }
                catch
                {
                }
            }
        }

        private static string PathFor(string snapshotId)
        {
            return Path.Combine(AiEditorAgentPaths.SnapshotsFolderAbsolute, (snapshotId ?? string.Empty) + ".json").Replace('\\', '/');
        }
    }
}
#endif
