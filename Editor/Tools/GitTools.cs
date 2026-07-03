#if UNITY_EDITOR
using System;

namespace AiUnity.EditorAgent
{
    internal static class GitTools
    {
        [Serializable]
        private sealed class TimeoutArgs
        {
            public int timeoutMs;
        }

        [AiTool(
            "git.status_summary",
            "Returns a compact git status summary for the current Unity project to help AI understand change scope before and after execution.",
            @"{""type"":""object"",""properties"":{""timeoutMs"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string StatusSummary(string argsJson)
        {
            TimeoutArgs args = AiJson.FromJsonOrThrow<TimeoutArgs>(argsJson);
            int timeoutMs = args == null || args.timeoutMs <= 0 ? 10000 : Math.Min(args.timeoutMs, 30000);
            return AiGitUtility.BuildStatusSummaryJson(timeoutMs);
        }

        [AiTool(
            "git.diff_summary",
            "Returns changed file names and diff stats for the current Unity project so AI can correlate validation results with actual edits.",
            @"{""type"":""object"",""properties"":{""timeoutMs"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string DiffSummary(string argsJson)
        {
            TimeoutArgs args = AiJson.FromJsonOrThrow<TimeoutArgs>(argsJson);
            int timeoutMs = args == null || args.timeoutMs <= 0 ? 10000 : Math.Min(args.timeoutMs, 30000);
            return AiGitUtility.BuildDiffSummaryJson(timeoutMs);
        }
    }
}
#endif
