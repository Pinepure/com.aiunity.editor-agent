#if UNITY_EDITOR
using System;

namespace AiUnity.EditorAgent
{
    internal static class ValidationTools
    {
        [Serializable]
        private sealed class BaselineArgs
        {
            public string label;
        }

        [Serializable]
        private sealed class RunArgs
        {
            public string baselineSnapshotId;
            public string[] validatorIds;
        }

        [Serializable]
        private sealed class SnapshotArgs
        {
            public string snapshotId;
        }

        [AiTool(
            "validator.capture_baseline",
            "Captures the current compile, git, prefab, and scene baseline so later validation can compare what changed.",
            @"{""type"":""object"",""properties"":{""label"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string CaptureBaseline(string argsJson)
        {
            BaselineArgs args = AiJson.FromJsonOrThrow<BaselineArgs>(argsJson);
            AiContextSnapshotRecord record = AiValidationCoordinator.CaptureBaseline(args == null ? string.Empty : args.label);
            return "{\"snapshotId\":" + AiJson.Quote(record.id) + ",\"label\":" + AiJson.Quote(record.label) + ",\"createdAt\":" + AiJson.Quote(record.createdAt) + "}";
        }

        [AiTool(
            "validator.run",
            "Runs post-change validation by combining Unity checks and Git summaries against a captured baseline.",
            @"{""type"":""object"",""properties"":{""baselineSnapshotId"":{""type"":""string""},""validatorIds"":{""type"":""array""}}}",
            @"{""type"":""object""}"
        )]
        public static string Run(string argsJson)
        {
            RunArgs args = AiJson.FromJsonOrThrow<RunArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.baselineSnapshotId)) throw new Exception("baselineSnapshotId is required.");
            return AiValidationCoordinator.Run(args.baselineSnapshotId, args.validatorIds);
        }

        [AiTool(
            "validator.get_report",
            "Returns a stored validation baseline snapshot for inspection.",
            @"{""type"":""object"",""properties"":{""snapshotId"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string GetReport(string argsJson)
        {
            SnapshotArgs args = AiJson.FromJsonOrThrow<SnapshotArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.snapshotId)) throw new Exception("snapshotId is required.");
            AiContextSnapshotRecord record;
            if (!AiSnapshotStore.TryGet(args.snapshotId, out record)) throw new Exception("Unknown snapshot: " + args.snapshotId);
            return UnityEngine.JsonUtility.ToJson(record, true);
        }
    }
}
#endif
