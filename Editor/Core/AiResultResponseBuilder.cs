#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace AiUnity.EditorAgent
{
    internal static class AiResultResponseBuilder
    {
        public static string BuildJsonItemsResult(string sourceToolId, string fieldName, List<string> itemJsons, string summaryJson, int pageSize)
        {
            List<string> items = itemJsons ?? new List<string>();
            int safePageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 200);
            int count = Math.Min(safePageSize, items.Count);
            bool hasMore = count < items.Count;
            string handleId = hasMore ? AiResultHandleStore.CreateJsonItemsHandle(sourceToolId, fieldName, items, summaryJson) : string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"summary\":").Append(AiJson.AsJsonValue(summaryJson)).Append(',');
            sb.Append("\"returned\":").Append(AiJson.Number(count)).Append(',');
            sb.Append("\"pageSize\":").Append(AiJson.Number(safePageSize)).Append(',');
            sb.Append("\"total\":").Append(AiJson.Number(items.Count)).Append(',');
            sb.Append("\"hasMore\":").Append(AiJson.Bool(hasMore));
            if (!string.IsNullOrEmpty(handleId))
            {
                sb.Append(',').Append("\"resultHandle\":").Append(AiJson.Quote(handleId));
            }
            sb.Append(',').Append(AiJson.Quote(string.IsNullOrEmpty(fieldName) ? "items" : fieldName)).Append(':').Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(items[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string BuildTextChunkResult(string sourceToolId, string text, string summaryJson, int offset, int length)
        {
            string value = text ?? string.Empty;
            int safeOffset = Math.Max(0, Math.Min(offset, value.Length));
            int safeLength = length <= 0 ? 4096 : Math.Min(length, 32768);
            int count = Math.Min(safeLength, value.Length - safeOffset);
            bool hasMore = safeOffset + count < value.Length;
            string chunk = count <= 0 ? string.Empty : value.Substring(safeOffset, count);
            string handleId = hasMore ? AiResultHandleStore.CreateTextHandle(sourceToolId, value, summaryJson) : string.Empty;

            return "{"
                + "\"summary\":" + AiJson.AsJsonValue(summaryJson) + ","
                + "\"offset\":" + AiJson.Number(safeOffset) + ","
                + "\"limit\":" + AiJson.Number(safeLength) + ","
                + "\"count\":" + AiJson.Number(count) + ","
                + "\"totalChars\":" + AiJson.Number(value.Length) + ","
                + "\"hasMore\":" + AiJson.Bool(hasMore)
                + (string.IsNullOrEmpty(handleId) ? string.Empty : ",\"resultHandle\":" + AiJson.Quote(handleId))
                + ",\"content\":" + AiJson.Quote(chunk)
                + "}";
        }
    }
}
#endif
