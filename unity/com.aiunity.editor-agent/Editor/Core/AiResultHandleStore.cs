#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace AiUnity.EditorAgent
{
    internal static class AiResultHandleStore
    {
        private const int MaxHandles = 96;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ResultHandleEntry> Map = new Dictionary<string, ResultHandleEntry>();
        private static readonly List<string> Order = new List<string>();

        private sealed class ResultHandleEntry
        {
            public string id;
            public string kind;
            public string sourceToolId;
            public string fieldName;
            public string createdAt;
            public string summaryJson;
            public string[] jsonItems;
            public string text;
        }

        public static string CreateJsonItemsHandle(string sourceToolId, string fieldName, List<string> jsonItems, string summaryJson)
        {
            ResultHandleEntry entry = new ResultHandleEntry
            {
                id = Guid.NewGuid().ToString("N"),
                kind = "items",
                sourceToolId = sourceToolId ?? string.Empty,
                fieldName = string.IsNullOrEmpty(fieldName) ? "items" : fieldName,
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                summaryJson = string.IsNullOrWhiteSpace(summaryJson) ? "{}" : summaryJson,
                jsonItems = jsonItems == null ? new string[0] : jsonItems.ToArray()
            };
            Add(entry);
            return entry.id;
        }

        public static string CreateTextHandle(string sourceToolId, string text, string summaryJson)
        {
            ResultHandleEntry entry = new ResultHandleEntry
            {
                id = Guid.NewGuid().ToString("N"),
                kind = "text",
                sourceToolId = sourceToolId ?? string.Empty,
                fieldName = "content",
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                summaryJson = string.IsNullOrWhiteSpace(summaryJson) ? "{}" : summaryJson,
                text = text ?? string.Empty
            };
            Add(entry);
            return entry.id;
        }

        public static bool TryBuildPageJson(string handleId, int offset, int limit, out int statusCode, out string json)
        {
            lock (Sync)
            {
                ResultHandleEntry entry;
                if (!Map.TryGetValue(handleId ?? string.Empty, out entry))
                {
                    statusCode = 404;
                    json = AiJson.Error("Unknown result handle: " + (handleId ?? string.Empty));
                    return false;
                }

                statusCode = 200;
                json = entry.kind == "text"
                    ? BuildTextPageJson(entry, offset, limit)
                    : BuildItemsPageJson(entry, offset, limit);
                return true;
            }
        }

        private static void Add(ResultHandleEntry entry)
        {
            lock (Sync)
            {
                Map[entry.id] = entry;
                Order.Add(entry.id);
                Trim();
            }
        }

        private static void Trim()
        {
            while (Order.Count > MaxHandles)
            {
                string oldest = Order[0];
                Order.RemoveAt(0);
                Map.Remove(oldest);
            }
        }

        private static string BuildItemsPageJson(ResultHandleEntry entry, int offset, int limit)
        {
            string[] items = entry.jsonItems ?? new string[0];
            int safeOffset = Math.Max(0, Math.Min(offset, items.Length));
            int safeLimit = limit <= 0 ? 20 : Math.Min(limit, 200);
            int count = Math.Min(safeLimit, items.Length - safeOffset);
            bool hasMore = safeOffset + count < items.Length;

            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"ok\":true,");
            sb.Append("\"handleId\":").Append(AiJson.Quote(entry.id)).Append(',');
            sb.Append("\"kind\":\"items\",");
            sb.Append("\"sourceToolId\":").Append(AiJson.Quote(entry.sourceToolId)).Append(',');
            sb.Append("\"fieldName\":").Append(AiJson.Quote(entry.fieldName)).Append(',');
            sb.Append("\"createdAt\":").Append(AiJson.Quote(entry.createdAt)).Append(',');
            sb.Append("\"offset\":").Append(AiJson.Number(safeOffset)).Append(',');
            sb.Append("\"limit\":").Append(AiJson.Number(safeLimit)).Append(',');
            sb.Append("\"count\":").Append(AiJson.Number(count)).Append(',');
            sb.Append("\"total\":").Append(AiJson.Number(items.Length)).Append(',');
            sb.Append("\"hasMore\":").Append(AiJson.Bool(hasMore)).Append(',');
            sb.Append("\"summary\":").Append(AiJson.AsJsonValue(entry.summaryJson)).Append(',');
            sb.Append(AiJson.Quote(entry.fieldName)).Append(':').Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(items[safeOffset + i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string BuildTextPageJson(ResultHandleEntry entry, int offset, int limit)
        {
            string text = entry.text ?? string.Empty;
            int safeOffset = Math.Max(0, Math.Min(offset, text.Length));
            int safeLimit = limit <= 0 ? 4096 : Math.Min(limit, 32768);
            int count = Math.Min(safeLimit, text.Length - safeOffset);
            bool hasMore = safeOffset + count < text.Length;
            string chunk = count <= 0 ? string.Empty : text.Substring(safeOffset, count);

            return "{"
                + "\"ok\":true,"
                + "\"handleId\":" + AiJson.Quote(entry.id) + ","
                + "\"kind\":\"text\","
                + "\"sourceToolId\":" + AiJson.Quote(entry.sourceToolId) + ","
                + "\"createdAt\":" + AiJson.Quote(entry.createdAt) + ","
                + "\"offset\":" + AiJson.Number(safeOffset) + ","
                + "\"limit\":" + AiJson.Number(safeLimit) + ","
                + "\"count\":" + AiJson.Number(count) + ","
                + "\"totalChars\":" + AiJson.Number(text.Length) + ","
                + "\"hasMore\":" + AiJson.Bool(hasMore) + ","
                + "\"summary\":" + AiJson.AsJsonValue(entry.summaryJson) + ","
                + "\"content\":" + AiJson.Quote(chunk)
                + "}";
        }
    }
}
#endif
