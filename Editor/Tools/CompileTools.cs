#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    internal static class CompileTools
    {
        [Serializable]
        private sealed class CompileErrorsArgs
        {
            public int maxEntries;
            public bool errorsOnly;
            public bool includeStackTrace;
            public int pageSize;
        }

        [Serializable]
        private sealed class CompileSummaryArgs
        {
            public int maxEntries;
            public bool errorsOnly;
            public int maxGroups;
        }

        [Serializable]
        private sealed class ErrorGroup
        {
            public string fingerprint;
            public string type;
            public string sampleCondition;
            public string latestTime;
            public int count;
        }

        [AiTool(
            "compile.status",
            "Returns current Unity compilation, asset updating, play mode, and console error counts.",
            "{}",
            @"{""type"":""object""}"
        )]
        public static string Status(string argsJson)
        {
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            return BuildStatusJson(counts);
        }

        [AiTool(
            "compile.snapshot",
            "Returns compile status plus grouped recent error summaries for quick diagnosis with minimal token usage. Args: maxEntries, errorsOnly, maxGroups.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""},""errorsOnly"":{""type"":""boolean""},""maxGroups"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string Snapshot(string argsJson)
        {
            CompileSummaryArgs args = AiJson.FromJsonOrThrow<CompileSummaryArgs>(argsJson);
            int maxEntries = args == null || args.maxEntries <= 0 ? 60 : Math.Min(args.maxEntries, 200);
            bool errorsOnly = args == null || args.errorsOnly;
            int maxGroups = args == null || args.maxGroups <= 0 ? 5 : Math.Min(args.maxGroups, 20);

            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            List<AiCapturedConsoleEntry> entries = AiEditorAgentState.GetConsoleEntries(maxEntries, errorsOnly);
            List<ErrorGroup> groups = BuildGroups(entries);

            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            AppendStatusFields(sb, counts);
            sb.Append(',');
            sb.Append("\"maxEntries\":").Append(AiJson.Number(maxEntries)).Append(',');
            sb.Append("\"errorsOnly\":").Append(AiJson.Bool(errorsOnly)).Append(',');
            sb.Append("\"groupCount\":").Append(AiJson.Number(groups.Count)).Append(',');
            sb.Append("\"truncatedGroups\":").Append(AiJson.Bool(groups.Count > maxGroups)).Append(',');
            sb.Append("\"groups\":[");
            int count = Math.Min(maxGroups, groups.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(BuildGroupJson(groups[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "compile.errors",
            "Returns recent captured Unity errors and warnings as a paged result. Args: maxEntries, errorsOnly, includeStackTrace, pageSize.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""},""errorsOnly"":{""type"":""boolean""},""includeStackTrace"":{""type"":""boolean""},""pageSize"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string Errors(string argsJson)
        {
            CompileErrorsArgs args = AiJson.FromJsonOrThrow<CompileErrorsArgs>(argsJson);
            int maxEntries = args == null || args.maxEntries <= 0 ? 100 : Math.Min(args.maxEntries, 300);
            bool errorsOnly = args == null || args.errorsOnly;
            bool includeStackTrace = args != null && args.includeStackTrace;
            int pageSize = args == null || args.pageSize <= 0 ? 20 : Math.Min(args.pageSize, 100);

            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            List<AiCapturedConsoleEntry> entries = AiEditorAgentState.GetConsoleEntries(maxEntries, errorsOnly);
            List<string> itemJsons = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                itemJsons.Add(BuildEntryJson(entries[i], includeStackTrace));
            }

            string summary = "{"
                + "\"source\":\"compile.errors\","
                + "\"errorsOnly\":" + AiJson.Bool(errorsOnly) + ","
                + "\"includeStackTrace\":" + AiJson.Bool(includeStackTrace) + ","
                + "\"consoleCountsAvailable\":" + AiJson.Bool(counts.available) + ","
                + "\"consoleErrorCount\":" + AiJson.Number(counts.errorCount) + ","
                + "\"consoleWarningCount\":" + AiJson.Number(counts.warningCount) + ","
                + "\"consoleLogCount\":" + AiJson.Number(counts.logCount)
                + "}";
            return AiResultResponseBuilder.BuildJsonItemsResult("compile.errors", "entries", itemJsons, summary, pageSize);
        }

        [AiTool(
            "compile.errors_summary",
            "Returns grouped error fingerprints and counts for fast diagnosis with minimal token usage. Args: maxEntries, errorsOnly, maxGroups.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""},""errorsOnly"":{""type"":""boolean""},""maxGroups"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string ErrorsSummary(string argsJson)
        {
            CompileSummaryArgs args = AiJson.FromJsonOrThrow<CompileSummaryArgs>(argsJson);
            int maxEntries = args == null || args.maxEntries <= 0 ? 100 : Math.Min(args.maxEntries, 300);
            bool errorsOnly = args == null || args.errorsOnly;
            int maxGroups = args == null || args.maxGroups <= 0 ? 20 : Math.Min(args.maxGroups, 100);

            List<AiCapturedConsoleEntry> entries = AiEditorAgentState.GetConsoleEntries(maxEntries, errorsOnly);
            List<ErrorGroup> groups = BuildGroups(entries);

            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"maxEntries\":").Append(AiJson.Number(maxEntries)).Append(',');
            sb.Append("\"errorsOnly\":").Append(AiJson.Bool(errorsOnly)).Append(',');
            sb.Append("\"groupCount\":").Append(AiJson.Number(groups.Count)).Append(',');
            sb.Append("\"truncated\":").Append(AiJson.Bool(groups.Count > maxGroups)).Append(',');
            sb.Append("\"groups\":[");
            int count = Math.Min(maxGroups, groups.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(BuildGroupJson(groups[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "console.clear",
            "Clears the Unity Console using Unity internal reflection when available.",
            "{}",
            @"{""type"":""object"",""properties"":{""cleared"":{""type"":""boolean""}}}",
            Danger = AiToolDanger.Medium,
            RequiresConfirmation = true
        )]
        public static string ClearConsole(string argsJson)
        {
            string message;
            bool cleared = AiConsoleUtility.Clear(out message);
            AiEditorAgentState.ClearCapturedConsole();
            return "{\"cleared\":" + AiJson.Bool(cleared) + ",\"message\":" + AiJson.Quote(message) + "}";
        }

        private static string BuildStatusJson(AiConsoleCounts counts)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            AppendStatusFields(sb, counts);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStatusFields(StringBuilder sb, AiConsoleCounts counts)
        {
            sb.Append("\"isCompiling\":").Append(AiJson.Bool(EditorApplication.isCompiling)).Append(',');
            sb.Append("\"isUpdating\":").Append(AiJson.Bool(EditorApplication.isUpdating)).Append(',');
            sb.Append("\"isPlaying\":").Append(AiJson.Bool(EditorApplication.isPlaying)).Append(',');
            sb.Append("\"isPlayingOrWillChangePlaymode\":").Append(AiJson.Bool(EditorApplication.isPlayingOrWillChangePlaymode)).Append(',');
            sb.Append("\"hasCompileErrors\":").Append(AiJson.Bool(counts.available && counts.errorCount > 0)).Append(',');
            sb.Append("\"consoleCountsAvailable\":").Append(AiJson.Bool(counts.available)).Append(',');
            sb.Append("\"consoleErrorCount\":").Append(AiJson.Number(counts.errorCount)).Append(',');
            sb.Append("\"consoleWarningCount\":").Append(AiJson.Number(counts.warningCount)).Append(',');
            sb.Append("\"consoleLogCount\":").Append(AiJson.Number(counts.logCount)).Append(',');
            sb.Append("\"consoleMessage\":").Append(AiJson.Quote(counts.message));
        }

        private static string BuildEntryJson(AiCapturedConsoleEntry entry, bool includeStackTrace)
        {
            string fingerprint = BuildFingerprint(entry);
            return "{"
                + "\"time\":" + AiJson.Quote(entry.time) + ","
                + "\"type\":" + AiJson.Quote(entry.type) + ","
                + "\"fingerprint\":" + AiJson.Quote(fingerprint) + ","
                + "\"condition\":" + AiJson.Quote(entry.condition)
                + (includeStackTrace ? ",\"stackTrace\":" + AiJson.Quote(entry.stackTrace) : string.Empty)
                + "}";
        }

        private static List<ErrorGroup> BuildGroups(List<AiCapturedConsoleEntry> entries)
        {
            Dictionary<string, ErrorGroup> map = new Dictionary<string, ErrorGroup>();
            for (int i = 0; i < entries.Count; i++)
            {
                AiCapturedConsoleEntry entry = entries[i];
                string fingerprint = BuildFingerprint(entry);
                ErrorGroup group;
                if (!map.TryGetValue(fingerprint, out group))
                {
                    group = new ErrorGroup
                    {
                        fingerprint = fingerprint,
                        type = entry.type,
                        sampleCondition = FirstLine(entry.condition),
                        latestTime = entry.time,
                        count = 0
                    };
                    map[fingerprint] = group;
                }

                group.count++;
                group.latestTime = entry.time;
            }

            List<ErrorGroup> groups = new List<ErrorGroup>(map.Values);
            groups.Sort(delegate (ErrorGroup a, ErrorGroup b)
            {
                int countCompare = b.count.CompareTo(a.count);
                if (countCompare != 0) return countCompare;
                return string.Compare(a.fingerprint, b.fingerprint, StringComparison.OrdinalIgnoreCase);
            });
            return groups;
        }

        private static string BuildGroupJson(ErrorGroup group)
        {
            return "{"
                + "\"fingerprint\":" + AiJson.Quote(group.fingerprint) + ","
                + "\"type\":" + AiJson.Quote(group.type) + ","
                + "\"sampleCondition\":" + AiJson.Quote(group.sampleCondition) + ","
                + "\"latestTime\":" + AiJson.Quote(group.latestTime) + ","
                + "\"count\":" + AiJson.Number(group.count)
                + "}";
        }

        private static string BuildFingerprint(AiCapturedConsoleEntry entry)
        {
            string head = FirstLine(entry == null ? string.Empty : entry.condition);
            return (entry == null ? "Error" : entry.type) + ":" + head;
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string normalized = value.Replace("\r", string.Empty);
            int newline = normalized.IndexOf('\n');
            string first = newline >= 0 ? normalized.Substring(0, newline) : normalized;
            return first.Length <= 220 ? first : first.Substring(0, 220);
        }
    }
}
#endif
