#if UNITY_EDITOR
using System;

namespace AiUnity.EditorAgent
{
    internal static class ContextTools
    {
        [Serializable]
        private sealed class SnapshotArgs
        {
            public string label;
        }

        [Serializable]
        private sealed class DiffArgs
        {
            public string beforeSnapshotId;
            public string afterSnapshotId;
        }

        [AiTool(
            "context.snapshot",
            "Captures a Unity-side execution context snapshot including compile fingerprints, selection, git summaries, and changed prefab/scene state.",
            @"{""type"":""object"",""properties"":{""label"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string Snapshot(string argsJson)
        {
            SnapshotArgs args = AiJson.FromJsonOrThrow<SnapshotArgs>(argsJson);
            AiContextSnapshotRecord record = AiValidationCoordinator.CaptureBaseline(args == null ? string.Empty : args.label);
            return "{\"snapshotId\":" + AiJson.Quote(record.id) + ",\"label\":" + AiJson.Quote(record.label) + ",\"createdAt\":" + AiJson.Quote(record.createdAt) + "}";
        }

        [AiTool(
            "context.diff",
            "Compares two stored Unity context snapshots and returns the main differences in compile, selection, git, prefab, and scene state.",
            @"{""type"":""object"",""properties"":{""beforeSnapshotId"":{""type"":""string""},""afterSnapshotId"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string Diff(string argsJson)
        {
            DiffArgs args = AiJson.FromJsonOrThrow<DiffArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.beforeSnapshotId) || string.IsNullOrEmpty(args.afterSnapshotId))
            {
                throw new Exception("beforeSnapshotId and afterSnapshotId are required.");
            }

            AiContextSnapshotRecord before;
            AiContextSnapshotRecord after;
            if (!AiSnapshotStore.TryGet(args.beforeSnapshotId, out before)) throw new Exception("Unknown snapshot: " + args.beforeSnapshotId);
            if (!AiSnapshotStore.TryGet(args.afterSnapshotId, out after)) throw new Exception("Unknown snapshot: " + args.afterSnapshotId);

            return "{"
                + "\"beforeSnapshotId\":" + AiJson.Quote(before.id) + ","
                + "\"afterSnapshotId\":" + AiJson.Quote(after.id) + ","
                + "\"compileChanged\":" + AiJson.Bool(before.compileHash != after.compileHash) + ","
                + "\"selectionChanged\":" + AiJson.Bool(before.selectionHash != after.selectionHash) + ","
                + "\"gitStatusChanged\":" + AiJson.Bool((before.gitStatusSummary ?? string.Empty) != (after.gitStatusSummary ?? string.Empty)) + ","
                + "\"changedFileCount\":" + AiJson.Number(after.changedFiles == null ? 0 : after.changedFiles.Length) + ","
                + "\"changedFiles\":" + AiJson.StringArray(after.changedFiles) + ","
                + "\"gitDiffSummary\":" + AiJson.AsJsonValue(after.gitDiffSummary)
                + "}";
        }
    }
}
#endif
