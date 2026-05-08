#if UNITY_EDITOR
using System.Text;

namespace AiUnity.EditorAgent
{
    internal static class AiProtocolUtility
    {
        public static string BuildHealthJson(bool includeOkField, bool serverRunning, bool requiresToken)
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append('{');
            if (includeOkField) sb.Append("\"ok\":true,");
            sb.Append("\"service\":\"AI Unity Editor Agent\",");
            sb.Append("\"version\":").Append(AiJson.Quote(AiEditorAgentPaths.ServiceVersion)).Append(',');
            sb.Append("\"protocolVersion\":").Append(AiJson.Quote(AiEditorAgentPaths.ProtocolVersion)).Append(',');
            sb.Append("\"serverRunning\":").Append(AiJson.Bool(serverRunning)).Append(',');
            sb.Append("\"requiresToken\":").Append(AiJson.Bool(requiresToken)).Append(',');
            sb.Append("\"serverUrl\":").Append(AiJson.Quote(AiEditorAgentSettings.ServerUrl)).Append(',');
            sb.Append("\"manifestHash\":").Append(AiJson.Quote(AiToolRegistry.ManifestHash)).Append(',');
            sb.Append("\"toolCount\":").Append(AiJson.Number(AiToolRegistry.Count)).Append(',');
            sb.Append("\"namespaces\":").Append(BuildNamespaceArrayJson()).Append(',');
            sb.Append("\"supportsManifestSearch\":true,");
            sb.Append("\"supportsDescribeMany\":true,");
            sb.Append("\"supportsResultHandles\":true,");
            sb.Append("\"supportsBundles\":true,");
            sb.Append("\"supportsTextChunking\":true,");
            sb.Append("\"supportsCompileSnapshot\":true,");
            sb.Append("\"recommendedFlow\":[");
            sb.Append(AiJson.Quote("GET /health and compare manifestHash before refreshing capabilities.")).Append(',');
            sb.Append(AiJson.Quote("Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.")).Append(',');
            sb.Append(AiJson.Quote("Use POST /tool/describe_many for exact argument and return schemas before calling tools.")).Append(',');
            sb.Append(AiJson.Quote("Use GET /result/{handleId} for additional pages or text chunks when a tool returns a resultHandle.")).Append(',');
            sb.Append(AiJson.Quote("Use GET /manifest/full only as a fallback when search is insufficient."));
            sb.Append("],");
            sb.Append("\"paths\":").Append(BuildPathMapJson());
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildAgentBriefJson(bool includeOkField)
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append('{');
            if (includeOkField) sb.Append("\"ok\":true,");
            sb.Append("\"summary\":").Append(AiJson.Quote("Prefer cached discovery through health, manifest search, on-demand tool descriptions, and paged result handles instead of repeatedly loading the full manifest.")).Append(',');
            sb.Append("\"steps\":[");
            sb.Append(AiJson.Quote("Call GET /health and reuse cached capabilities while manifestHash stays unchanged.")).Append(',');
            sb.Append(AiJson.Quote("Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.")).Append(',');
            sb.Append(AiJson.Quote("Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.")).Append(',');
            sb.Append(AiJson.Quote("When a tool returns resultHandle, page additional data through GET /result/{handleId} instead of re-running the tool with larger limits.")).Append(',');
            sb.Append(AiJson.Quote("Fallback to GET /manifest/full or GET /agent only when the optimized discovery flow is insufficient."));
            sb.Append("],");
            sb.Append("\"paths\":").Append(BuildPathMapJson());
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildPathMapJson()
        {
            return "{"
                + "\"health\":\"/health\","
                + "\"manifestSummary\":\"/manifest\","
                + "\"manifestFull\":\"/manifest/full\","
                + "\"manifestSearch\":\"/manifest/search\","
                + "\"manifestBundles\":\"/manifest/bundles\","
                + "\"toolDescribeMany\":\"/tool/describe_many\","
                + "\"agentBrief\":\"/agent/brief\","
                + "\"resultPage\":\"/result/{handleId}\""
                + "}";
        }

        private static string BuildNamespaceArrayJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            System.Collections.Generic.List<AiToolNamespaceInfo> items = AiToolRegistry.GetNamespaceInfosCopy();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"id\":").Append(AiJson.Quote(items[i].id)).Append(',');
                sb.Append("\"count\":").Append(AiJson.Number(items[i].count));
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
#endif
