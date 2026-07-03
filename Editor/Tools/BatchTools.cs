#if UNITY_EDITOR
using System;

namespace AiUnity.EditorAgent
{
    internal static class BatchTools
    {
        [Serializable]
        private sealed class BatchArgs
        {
            public bool dryRun;
            public bool stopOnError;
            public AiBatchStep[] steps;
        }

        [AiTool(
            "batch.run",
            "Runs a planned sequence of Unity tool calls in one request. Use placeholders like {{step1.result.result.path}} or {{step2.result.result.resultHandle}} to read prior wrapped step results.",
            @"{""type"":""object"",""properties"":{""dryRun"":{""type"":""boolean""},""stopOnError"":{""type"":""boolean""},""steps"":{""type"":""array""}}}",
            @"{""type"":""object""}",
            Danger = AiToolDanger.Medium
        )]
        public static string Run(string argsJson)
        {
            BatchArgs args = AiJson.FromJsonOrThrow<BatchArgs>(argsJson);
            if (args == null || args.steps == null || args.steps.Length == 0) throw new Exception("steps are required.");
            return AiBatchExecutor.Run(args.steps, args.dryRun, args.stopOnError || !args.dryRun);
        }
    }
}
#endif
