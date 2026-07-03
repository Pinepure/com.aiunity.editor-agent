#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiRecipeRecord
    {
        public string key;
        public string name;
        public string goal;
        public string createdAt;
        public string updatedAt;
        public int successCount;
        public string[] stepToolIds;
        public string[] validatorIds;
        public string[] changedFiles;
        public string notes;
    }

    internal static class AiRecipeStore
    {
        public static AiRecipeRecord RecordSuccess(string key, string name, string goal, string[] stepToolIds, string[] validatorIds, string[] changedFiles, string notes)
        {
            AiEditorAgentPaths.EnsureStateFolders();
            string effectiveKey = string.IsNullOrEmpty(key) ? BuildKey(name, goal) : key.Trim().ToLowerInvariant();
            AiRecipeRecord record;
            if (!TryGet(effectiveKey, out record))
            {
                record = new AiRecipeRecord
                {
                    key = effectiveKey,
                    name = string.IsNullOrEmpty(name) ? effectiveKey : name,
                    goal = goal ?? string.Empty,
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    successCount = 0
                };
            }

            record.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            record.successCount++;
            record.stepToolIds = stepToolIds == null ? new string[0] : stepToolIds;
            record.validatorIds = validatorIds == null ? new string[0] : validatorIds;
            record.changedFiles = changedFiles == null ? new string[0] : changedFiles;
            record.notes = notes ?? string.Empty;
            Save(record);
            return record;
        }

        public static bool TryGet(string key, out AiRecipeRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(key)) return false;
            string path = PathFor(key);
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            record = JsonUtility.FromJson<AiRecipeRecord>(json);
            return record != null;
        }

        public static List<AiRecipeRecord> List(int max)
        {
            AiEditorAgentPaths.EnsureStateFolders();
            string[] files = Directory.GetFiles(AiEditorAgentPaths.RecipesFolderAbsolute, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            List<AiRecipeRecord> items = new List<AiRecipeRecord>();
            for (int i = Math.Max(0, files.Length - max); i < files.Length; i++)
            {
                string json = File.ReadAllText(files[i]);
                AiRecipeRecord record = JsonUtility.FromJson<AiRecipeRecord>(json);
                if (record != null) items.Add(record);
            }
            return items;
        }

        public static List<AiRecipeRecord> Match(string query, int limit)
        {
            string normalized = string.IsNullOrEmpty(query) ? string.Empty : query.Trim().ToLowerInvariant();
            List<AiRecipeRecord> all = List(200);
            List<AiRecipeRecord> matches = new List<AiRecipeRecord>();
            List<int> scores = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                AiRecipeRecord record = all[i];
                int score = Score(record, normalized);
                if (!string.IsNullOrEmpty(normalized) && score <= 0) continue;
                matches.Add(record);
                scores.Add(score);
            }

            for (int i = 0; i < matches.Count - 1; i++)
            {
                for (int j = i + 1; j < matches.Count; j++)
                {
                    if (scores[j] > scores[i])
                    {
                        int tmpScore = scores[i];
                        scores[i] = scores[j];
                        scores[j] = tmpScore;

                        AiRecipeRecord tmp = matches[i];
                        matches[i] = matches[j];
                        matches[j] = tmp;
                    }
                }
            }

            if (limit > 0 && matches.Count > limit)
            {
                matches.RemoveRange(limit, matches.Count - limit);
            }
            return matches;
        }

        public static int Count()
        {
            AiEditorAgentPaths.EnsureStateFolders();
            return Directory.GetFiles(AiEditorAgentPaths.RecipesFolderAbsolute, "*.json", SearchOption.TopDirectoryOnly).Length;
        }

        private static void Save(AiRecipeRecord record)
        {
            File.WriteAllText(PathFor(record.key), JsonUtility.ToJson(record, true));
        }

        private static string PathFor(string key)
        {
            return Path.Combine(AiEditorAgentPaths.RecipesFolderAbsolute, (key ?? string.Empty) + ".json").Replace('\\', '/');
        }

        private static string BuildKey(string name, string goal)
        {
            string basis = (name ?? string.Empty) + "|" + (goal ?? string.Empty);
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < basis.Length; i++)
            {
                hash ^= basis[i];
                hash *= 1099511628211UL;
            }
            return "recipe-" + hash.ToString("x16");
        }

        private static int Score(AiRecipeRecord record, string query)
        {
            if (record == null) return 0;
            if (string.IsNullOrEmpty(query)) return Math.Max(1, record.successCount);

            int score = 0;
            string name = (record.name ?? string.Empty).ToLowerInvariant();
            string goal = (record.goal ?? string.Empty).ToLowerInvariant();
            string notes = (record.notes ?? string.Empty).ToLowerInvariant();
            if (name.IndexOf(query, StringComparison.Ordinal) >= 0) score += 100;
            if (goal.IndexOf(query, StringComparison.Ordinal) >= 0) score += 80;
            if (notes.IndexOf(query, StringComparison.Ordinal) >= 0) score += 30;

            string[] tools = record.stepToolIds ?? new string[0];
            for (int i = 0; i < tools.Length; i++)
            {
                if ((tools[i] ?? string.Empty).ToLowerInvariant().IndexOf(query, StringComparison.Ordinal) >= 0)
                {
                    score += 20;
                }
            }

            score += Math.Min(20, record.successCount * 2);
            return score;
        }
    }
}
#endif
