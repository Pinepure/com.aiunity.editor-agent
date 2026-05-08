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
            public int pageSize;
        }

        [Serializable]
        private sealed class ManifestSearchArgs
        {
            public string query;
            public int limit;
            public string namespaceId;
            public string bundleId;
        }

        [Serializable]
        private sealed class ManifestBundleArgs
        {
            public string bundleId;
        }

        [Serializable]
        private sealed class DescribeManyArgs
        {
            public string[] ids;
        }

        [AiTool(
            "system.health",
            "Returns service health metadata, manifestHash, namespace counts, and discovery protocol hints.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string Health(string argsJson)
        {
            return AiProtocolUtility.BuildHealthJson(false, AiEditorApiServer.IsRunning, AiEditorAgentSettings.RequireToken);
        }

        [AiTool(
            "manifest.get",
            "Returns the full manifest with complete schemas. Prefer manifest.get_summary or manifest.search unless a full fallback is necessary.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string GetManifest(string argsJson)
        {
            return AiToolRegistry.BuildManifestFullJson(false, false);
        }

        [AiTool(
            "manifest.get_summary",
            "Returns the lightweight manifest summary without full schemas. Prefer this for capability discovery when a full manifest is unnecessary.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string GetManifestSummary(string argsJson)
        {
            return AiToolRegistry.BuildManifestSummaryJson(false, false);
        }

        [AiTool(
            "manifest.search",
            "Searches the manifest for candidate tools. Args: query, limit, namespaceId, bundleId.",
            @"{""type"":""object"",""properties"":{""query"":{""type"":""string""},""limit"":{""type"":""integer""},""namespaceId"":{""type"":""string""},""bundleId"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string SearchManifest(string argsJson)
        {
            ManifestSearchArgs args = AiJson.FromJsonOrThrow<ManifestSearchArgs>(argsJson);
            if (args != null && !string.IsNullOrEmpty(args.bundleId) && !AiToolRegistry.HasBundle(args.bundleId))
            {
                throw new Exception("Unknown manifest bundle: " + args.bundleId);
            }
            return AiToolRegistry.BuildManifestSearchJson(
                args == null ? string.Empty : args.query,
                args == null ? 0 : args.limit,
                args == null ? string.Empty : args.namespaceId,
                args == null ? string.Empty : args.bundleId,
                false);
        }

        [AiTool(
            "manifest.list_bundles",
            "Lists focused manifest bundles for common workflows such as asset analysis, scene editing, prefab authoring, and diagnostics.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string ListBundles(string argsJson)
        {
            return AiToolRegistry.BuildManifestBundleIndexJson(false, false);
        }

        [AiTool(
            "manifest.get_bundle",
            "Returns the lightweight manifest for a focused capability bundle. Args: bundleId.",
            @"{""type"":""object"",""properties"":{""bundleId"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string GetBundle(string argsJson)
        {
            ManifestBundleArgs args = AiJson.FromJsonOrThrow<ManifestBundleArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.bundleId)) throw new Exception("bundleId is required.");
            string json;
            if (!AiToolRegistry.TryBuildManifestBundleJson(args.bundleId, false, false, out json))
            {
                throw new Exception("Unknown manifest bundle: " + args.bundleId);
            }
            return json;
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
            "agent.get_brief",
            "Returns the concise protocol brief for efficient capability discovery, schema lookup, and paged result handling.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string GetAgentBrief(string argsJson)
        {
            return AiProtocolUtility.BuildAgentBriefJson(false);
        }

        [AiTool(
            "tool.describe_many",
            "Returns exact schemas and metadata for specific tool ids. Args: ids.",
            @"{""type"":""object"",""properties"":{""ids"":{""type"":""array""}}}",
            @"{""type"":""object""}"
        )]
        public static string DescribeMany(string argsJson)
        {
            DescribeManyArgs args = AiJson.FromJsonOrThrow<DescribeManyArgs>(argsJson);
            return AiToolRegistry.BuildToolDescribeManyJson(args == null ? null : args.ids, false, false);
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
            "Returns recent internal service logs with a paged resultHandle when the first page does not cover the full result. Args: maxEntries, pageSize.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""},""pageSize"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string RecentLogs(string argsJson)
        {
            RecentArgs args = AiJson.FromJsonOrThrow<RecentArgs>(argsJson);
            int maxEntries = args == null || args.maxEntries <= 0 ? 100 : Math.Min(args.maxEntries, 300);
            int pageSize = args == null || args.pageSize <= 0 ? 20 : Math.Min(args.pageSize, 100);
            List<AiServiceLogEntry> entries = AiEditorAgentState.GetServiceLogs(maxEntries);
            List<string> items = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                AiServiceLogEntry e = entries[i];
                items.Add("{\"time\":" + AiJson.Quote(e.time) + ",\"level\":" + AiJson.Quote(e.level) + ",\"message\":" + AiJson.Quote(e.message) + "}");
            }

            string summary = "{"
                + "\"source\":\"service.log_recent\","
                + "\"maxEntries\":" + AiJson.Number(maxEntries)
                + "}";
            return AiResultResponseBuilder.BuildJsonItemsResult("service.log_recent", "entries", items, summary, pageSize);
        }

        [AiTool(
            "service.call_recent",
            "Returns recent AI tool calls with a paged resultHandle when the first page does not cover the full result. Args: maxEntries, pageSize.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""},""pageSize"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string RecentCalls(string argsJson)
        {
            RecentArgs args = AiJson.FromJsonOrThrow<RecentArgs>(argsJson);
            int maxEntries = args == null || args.maxEntries <= 0 ? 100 : Math.Min(args.maxEntries, 300);
            int pageSize = args == null || args.pageSize <= 0 ? 20 : Math.Min(args.pageSize, 100);
            List<AiToolCallEntry> entries = AiEditorAgentState.GetToolCalls(maxEntries);
            List<string> items = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                AiToolCallEntry e = entries[i];
                items.Add("{\"time\":" + AiJson.Quote(e.time)
                    + ",\"toolId\":" + AiJson.Quote(e.toolId)
                    + ",\"ok\":" + AiJson.Bool(e.ok)
                    + ",\"durationMs\":" + AiJson.Number(e.durationMs)
                    + ",\"message\":" + AiJson.Quote(e.message)
                    + "}");
            }

            string summary = "{"
                + "\"source\":\"service.call_recent\","
                + "\"maxEntries\":" + AiJson.Number(maxEntries)
                + "}";
            return AiResultResponseBuilder.BuildJsonItemsResult("service.call_recent", "entries", items, summary, pageSize);
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
