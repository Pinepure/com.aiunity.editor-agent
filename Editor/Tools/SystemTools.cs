#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    internal static class SystemTools
    {
        [Serializable]
        private sealed class RecentArgs
        {
            public int maxEntries;
        }

        [AiTool(
            "system.health",
            "Returns main-thread service, Unity, project, and registry health information.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string Health(string argsJson)
        {
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            return "{"
                + "\"service\":\"AI Unity Editor Agent\","
                + "\"version\":" + AiJson.Quote(AiEditorAgentPaths.ServiceVersion) + ","
                + "\"protocolVersion\":" + AiJson.Quote(AiEditorAgentPaths.ProtocolVersion) + ","
                + "\"serverRunning\":" + AiJson.Bool(AiEditorApiServer.IsRunning) + ","
                + "\"serverUrl\":" + AiJson.Quote(AiEditorAgentSettings.ServerUrl) + ","
                + "\"unityVersion\":" + AiJson.Quote(Application.unityVersion) + ","
                + "\"projectPath\":" + AiJson.Quote(AiEditorAgentPaths.ProjectRoot) + ","
                + "\"toolCount\":" + AiJson.Number(AiToolRegistry.Count) + ","
                + "\"isCompiling\":" + AiJson.Bool(EditorApplication.isCompiling) + ","
                + "\"isUpdating\":" + AiJson.Bool(EditorApplication.isUpdating) + ","
                + "\"consoleErrors\":" + AiJson.Number(counts.errorCount)
                + "}";
        }

        [AiTool(
            "manifest.get",
            "Returns the latest auto-generated AI tool manifest. Equivalent to GET /manifest.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string GetManifest(string argsJson)
        {
            return AiToolRegistry.BuildManifestJson(true);
        }

        [AiTool(
            "agent.get_manual",
            "Returns the built-in AGENT.md AI operating manual.",
            "{}",
            @"{""type"":""object"",""properties"":{""content"":{""type"":""string""}}}"
        )]
        public static string GetAgentManual(string argsJson)
        {
            string path = AiEditorAgentPaths.AgentMdPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new Exception("AGENT.md was not found.");
            }
            string content = File.ReadAllText(path, Encoding.UTF8);
            return "{\"path\":" + AiJson.Quote(path) + ",\"content\":" + AiJson.Quote(content) + "}";
        }

        [AiTool(
            "service.config_get",
            "Returns current service configuration without exposing the full token.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string GetConfig(string argsJson)
        {
            string token = AiEditorApiServer.Token;
            string masked = string.IsNullOrEmpty(token) || token.Length < 8 ? "" : token.Substring(0, 4) + "..." + token.Substring(token.Length - 4);
            return "{"
                + "\"autoStart\":" + AiJson.Bool(AiEditorAgentSettings.AutoStart) + ","
                + "\"requireToken\":" + AiJson.Bool(AiEditorAgentSettings.RequireToken) + ","
                + "\"confirmHighRiskTools\":" + AiJson.Bool(AiEditorAgentSettings.ConfirmHighRiskTools) + ","
                + "\"fullAccessEnabled\":" + AiJson.Bool(AiEditorAgentSettings.FullAccessEnabled) + ","
                + "\"port\":" + AiJson.Number(AiEditorAgentSettings.Port) + ","
                + "\"serverUrl\":" + AiJson.Quote(AiEditorAgentSettings.ServerUrl) + ","
                + "\"tokenPath\":" + AiJson.Quote(AiEditorAgentPaths.TokenPath) + ","
                + "\"tokenPreview\":" + AiJson.Quote(masked) + ","
                + "\"generatedToolsFolder\":" + AiJson.Quote(AiEditorAgentPaths.GeneratedToolsFolder)
                + "}";
        }

        [AiTool(
            "service.log_recent",
            "Returns recent internal AI Editor Agent service logs.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""}}}",
            @"{""type"":""object"",""properties"":{""entries"":{""type"":""array""}}}"
        )]
        public static string RecentLogs(string argsJson)
        {
            RecentArgs args = AiJson.FromJsonOrThrow<RecentArgs>(argsJson);
            List<AiServiceLogEntry> entries = AiEditorAgentState.GetServiceLogs(args == null ? 100 : args.maxEntries);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"entries\":[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AiServiceLogEntry e = entries[i];
                sb.Append('{');
                sb.Append("\"time\":").Append(AiJson.Quote(e.time)).Append(',');
                sb.Append("\"level\":").Append(AiJson.Quote(e.level)).Append(',');
                sb.Append("\"message\":").Append(AiJson.Quote(e.message));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "service.call_recent",
            "Returns recent AI tool calls and outcomes.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""}}}",
            @"{""type"":""object"",""properties"":{""entries"":{""type"":""array""}}}"
        )]
        public static string RecentCalls(string argsJson)
        {
            RecentArgs args = AiJson.FromJsonOrThrow<RecentArgs>(argsJson);
            List<AiToolCallEntry> entries = AiEditorAgentState.GetToolCalls(args == null ? 100 : args.maxEntries);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"entries\":[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AiToolCallEntry e = entries[i];
                sb.Append('{');
                sb.Append("\"time\":").Append(AiJson.Quote(e.time)).Append(',');
                sb.Append("\"toolId\":").Append(AiJson.Quote(e.toolId)).Append(',');
                sb.Append("\"ok\":").Append(AiJson.Bool(e.ok)).Append(',');
                sb.Append("\"durationMs\":").Append(AiJson.Number(e.durationMs)).Append(',');
                sb.Append("\"message\":").Append(AiJson.Quote(e.message));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "service.regenerate_token",
            "Regenerates the local API token. Existing AI clients must reload the token file.",
            "{}",
            @"{""type"":""object"",""properties"":{""tokenPath"":{""type"":""string""}}}",
            Danger = AiToolDanger.High,
            RequiresConfirmation = true
        )]
        public static string RegenerateToken(string argsJson)
        {
            AiEditorApiServer.RegenerateToken();
            return "{\"tokenPath\":" + AiJson.Quote(AiEditorAgentPaths.TokenPath) + "}";
        }
    }
}
#endif
