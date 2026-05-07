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
            return "{"
                + "\"isCompiling\":" + AiJson.Bool(EditorApplication.isCompiling) + ","
                + "\"isUpdating\":" + AiJson.Bool(EditorApplication.isUpdating) + ","
                + "\"isPlaying\":" + AiJson.Bool(EditorApplication.isPlaying) + ","
                + "\"isPlayingOrWillChangePlaymode\":" + AiJson.Bool(EditorApplication.isPlayingOrWillChangePlaymode) + ","
                + "\"hasCompileErrors\":" + AiJson.Bool(counts.available && counts.errorCount > 0) + ","
                + "\"consoleCountsAvailable\":" + AiJson.Bool(counts.available) + ","
                + "\"consoleErrorCount\":" + AiJson.Number(counts.errorCount) + ","
                + "\"consoleWarningCount\":" + AiJson.Number(counts.warningCount) + ","
                + "\"consoleLogCount\":" + AiJson.Number(counts.logCount) + ","
                + "\"consoleMessage\":" + AiJson.Quote(counts.message)
                + "}";
        }

        [AiTool(
            "compile.errors",
            "Returns recent captured Unity errors and warnings. Console counts use Unity internal reflection when available.",
            @"{""type"":""object"",""properties"":{""maxEntries"":{""type"":""integer""},""errorsOnly"":{""type"":""boolean""}}}",
            @"{""type"":""object""}"
        )]
        public static string Errors(string argsJson)
        {
            CompileErrorsArgs args = AiJson.FromJsonOrThrow<CompileErrorsArgs>(argsJson);
            int max = args == null || args.maxEntries <= 0 ? 100 : args.maxEntries;
            bool errorsOnly = args == null || args.errorsOnly;
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            List<AiCapturedConsoleEntry> entries = AiEditorAgentState.GetConsoleEntries(max, errorsOnly);

            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"consoleCountsAvailable\":").Append(AiJson.Bool(counts.available)).Append(',');
            sb.Append("\"consoleErrorCount\":").Append(AiJson.Number(counts.errorCount)).Append(',');
            sb.Append("\"consoleWarningCount\":").Append(AiJson.Number(counts.warningCount)).Append(',');
            sb.Append("\"consoleLogCount\":").Append(AiJson.Number(counts.logCount)).Append(',');
            sb.Append("\"entries\":[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AiCapturedConsoleEntry e = entries[i];
                sb.Append('{');
                sb.Append("\"time\":").Append(AiJson.Quote(e.time)).Append(',');
                sb.Append("\"type\":").Append(AiJson.Quote(e.type)).Append(',');
                sb.Append("\"condition\":").Append(AiJson.Quote(e.condition)).Append(',');
                sb.Append("\"stackTrace\":").Append(AiJson.Quote(e.stackTrace));
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append('}');
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
    }
}
#endif
