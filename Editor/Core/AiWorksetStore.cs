#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiWorksetRecord
    {
        public string id;
        public string kind;
        public string sourceToolId;
        public string fieldName;
        public string summaryJson;
        public string createdAt;
        public string updatedAt;
        public int total;
        public string[] itemJsons;
    }

    internal static class AiWorksetStore
    {
        public static string CreateJsonItemsWorkset(string kind, string sourceToolId, string fieldName, string summaryJson, List<string> itemJsons)
        {
            AiEditorAgentPaths.EnsureStateFolders();
            PruneExpired();
            AiWorksetRecord record = new AiWorksetRecord
            {
                id = Guid.NewGuid().ToString("N"),
                kind = string.IsNullOrEmpty(kind) ? "items" : kind,
                sourceToolId = sourceToolId ?? string.Empty,
                fieldName = string.IsNullOrEmpty(fieldName) ? "items" : fieldName,
                summaryJson = string.IsNullOrWhiteSpace(summaryJson) ? "{}" : summaryJson,
                createdAt = AiEditorAgentSettings.FormatTimestamp(DateTime.Now),
                updatedAt = AiEditorAgentSettings.FormatTimestamp(DateTime.Now),
                total = itemJsons == null ? 0 : itemJsons.Count,
                itemJsons = itemJsons == null ? new string[0] : itemJsons.ToArray()
            };

            Save(record);
            return record.id;
        }

        public static bool TryGet(string worksetId, out AiWorksetRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(worksetId)) return false;
            PruneExpired();
            string path = PathFor(worksetId);
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            record = JsonUtility.FromJson<AiWorksetRecord>(json);
            if (record == null) return false;
            if (AiEditorAgentSettings.IsTimestampExpired(record.updatedAt, AiEditorAgentSettings.WorksetTtlSeconds))
            {
                Delete(worksetId);
                record = null;
                return false;
            }
            return true;
        }

        public static bool Delete(string worksetId)
        {
            string path = PathFor(worksetId);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public static int Count()
        {
            AiEditorAgentPaths.EnsureStateFolders();
            PruneExpired();
            return Directory.GetFiles(AiEditorAgentPaths.WorksetsFolderAbsolute, "*.json", SearchOption.TopDirectoryOnly).Length;
        }

        public static string BuildSummaryJson(string worksetId)
        {
            AiWorksetRecord record;
            if (!TryGet(worksetId, out record))
            {
                return AiJson.Error("Unknown workset: " + (worksetId ?? string.Empty));
            }

            return "{"
                + "\"worksetId\":" + AiJson.Quote(record.id) + ","
                + "\"kind\":" + AiJson.Quote(record.kind) + ","
                + "\"sourceToolId\":" + AiJson.Quote(record.sourceToolId) + ","
                + "\"fieldName\":" + AiJson.Quote(record.fieldName) + ","
                + "\"createdAt\":" + AiJson.Quote(record.createdAt) + ","
                + "\"updatedAt\":" + AiJson.Quote(record.updatedAt) + ","
                + "\"total\":" + AiJson.Number(record.total) + ","
                + "\"summary\":" + AiJson.AsJsonValue(record.summaryJson)
                + "}";
        }

        public static bool TryBuildPageJson(string worksetId, int offset, int limit, out string json)
        {
            json = string.Empty;
            AiWorksetRecord record;
            if (!TryGet(worksetId, out record)) return false;

            string[] items = record.itemJsons ?? new string[0];
            int safeOffset = Math.Max(0, Math.Min(offset, items.Length));
            int safeLimit = limit <= 0 ? 20 : Math.Min(limit, 200);
            int count = Math.Min(safeLimit, items.Length - safeOffset);
            bool hasMore = safeOffset + count < items.Length;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append('{');
            sb.Append("\"ok\":true,");
            sb.Append("\"worksetId\":").Append(AiJson.Quote(record.id)).Append(',');
            sb.Append("\"kind\":").Append(AiJson.Quote(record.kind)).Append(',');
            sb.Append("\"sourceToolId\":").Append(AiJson.Quote(record.sourceToolId)).Append(',');
            sb.Append("\"fieldName\":").Append(AiJson.Quote(record.fieldName)).Append(',');
            sb.Append("\"offset\":").Append(AiJson.Number(safeOffset)).Append(',');
            sb.Append("\"limit\":").Append(AiJson.Number(safeLimit)).Append(',');
            sb.Append("\"count\":").Append(AiJson.Number(count)).Append(',');
            sb.Append("\"total\":").Append(AiJson.Number(items.Length)).Append(',');
            sb.Append("\"hasMore\":").Append(AiJson.Bool(hasMore)).Append(',');
            sb.Append("\"summary\":").Append(AiJson.AsJsonValue(record.summaryJson)).Append(',');
            sb.Append(AiJson.Quote(record.fieldName)).Append(':').Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(items[safeOffset + i]);
            }
            sb.Append("]}");
            json = sb.ToString();
            return true;
        }

        public static string CreateDerivedWorkset(string sourceWorksetId, string operation, string fieldName, string contains, string equals, int skip, int limit)
        {
            AiWorksetRecord record;
            if (!TryGet(sourceWorksetId, out record)) throw new Exception("Unknown workset: " + sourceWorksetId);

            List<string> filtered = new List<string>();
            string[] sourceItems = record.itemJsons ?? new string[0];
            string op = string.IsNullOrEmpty(operation) ? "contains" : operation.Trim().ToLowerInvariant();
            string normalizedField = string.IsNullOrEmpty(fieldName) ? string.Empty : fieldName.Trim();
            string normalizedContains = contains == null ? string.Empty : contains.Trim();
            string normalizedEquals = equals == null ? string.Empty : equals.Trim();

            for (int i = 0; i < sourceItems.Length; i++)
            {
                string itemJson = sourceItems[i];
                string value = itemJson;
                if (!string.IsNullOrEmpty(normalizedField))
                {
                    string extracted;
                    if (AiJsonQueryUtility.TryGetString(itemJson, normalizedField, out extracted))
                    {
                        value = extracted;
                    }
                }

                bool include = true;
                if (op == "contains")
                {
                    include = string.IsNullOrEmpty(normalizedContains) || value.IndexOf(normalizedContains, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else if (op == "exclude_contains")
                {
                    include = string.IsNullOrEmpty(normalizedContains) || value.IndexOf(normalizedContains, StringComparison.OrdinalIgnoreCase) < 0;
                }
                else if (op == "equals")
                {
                    include = string.Equals(value, normalizedEquals, StringComparison.OrdinalIgnoreCase);
                }

                if (include) filtered.Add(itemJson);
            }

            if (skip > 0 && skip < filtered.Count)
            {
                filtered.RemoveRange(0, skip);
            }
            else if (skip >= filtered.Count)
            {
                filtered.Clear();
            }

            if (limit > 0 && limit < filtered.Count)
            {
                filtered.RemoveRange(limit, filtered.Count - limit);
            }

            string summary = "{"
                + "\"sourceWorksetId\":" + AiJson.Quote(sourceWorksetId) + ","
                + "\"operation\":" + AiJson.Quote(op) + ","
                + "\"fieldName\":" + AiJson.Quote(normalizedField) + ","
                + "\"contains\":" + AiJson.Quote(normalizedContains) + ","
                + "\"equals\":" + AiJson.Quote(normalizedEquals) + ","
                + "\"skip\":" + AiJson.Number(skip) + ","
                + "\"limit\":" + AiJson.Number(limit)
                + "}";
            return CreateJsonItemsWorkset(record.kind, record.sourceToolId, record.fieldName, summary, filtered);
        }

        private static void Save(AiWorksetRecord record)
        {
            AiEditorAgentPaths.EnsureStateFolders();
            record.updatedAt = AiEditorAgentSettings.FormatTimestamp(DateTime.Now);
            record.total = record.itemJsons == null ? 0 : record.itemJsons.Length;
            File.WriteAllText(PathFor(record.id), JsonUtility.ToJson(record, true));
        }

        private static void PruneExpired()
        {
            AiEditorAgentPaths.EnsureStateFolders();
            string[] files = Directory.GetFiles(AiEditorAgentPaths.WorksetsFolderAbsolute, "*.json", SearchOption.TopDirectoryOnly);
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

                    AiWorksetRecord record = JsonUtility.FromJson<AiWorksetRecord>(json);
                    if (record == null || AiEditorAgentSettings.IsTimestampExpired(record.updatedAt, AiEditorAgentSettings.WorksetTtlSeconds))
                    {
                        File.Delete(files[i]);
                    }
                }
                catch
                {
                }
            }
        }

        private static string PathFor(string worksetId)
        {
            return Path.Combine(AiEditorAgentPaths.WorksetsFolderAbsolute, (worksetId ?? string.Empty) + ".json").Replace('\\', '/');
        }
    }
}
#endif
