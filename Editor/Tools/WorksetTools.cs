#if UNITY_EDITOR
using System;

namespace AiUnity.EditorAgent
{
    internal static class WorksetTools
    {
        [Serializable]
        private sealed class CreateArgs
        {
            public string sourceToolId;
            public string fieldName;
            public string summaryJson;
            public string[] itemJsons;
        }

        [Serializable]
        private sealed class SummaryArgs
        {
            public string worksetId;
            public int offset;
            public int limit;
        }

        [Serializable]
        private sealed class DeleteArgs
        {
            public string worksetId;
        }

        [Serializable]
        private sealed class DeriveArgs
        {
            public string worksetId;
            public string operation;
            public string fieldName;
            public string contains;
            public string equals;
            public int skip;
            public int limit;
        }

        [AiTool(
            "workset.create",
            "Creates a persistent Unity-side workset from item JSON values so later steps can reuse the result without retransmitting the full list.",
            @"{""type"":""object"",""properties"":{""sourceToolId"":{""type"":""string""},""fieldName"":{""type"":""string""},""summaryJson"":{""type"":""string""},""itemJsons"":{""type"":""array""}}}",
            @"{""type"":""object""}"
        )]
        public static string Create(string argsJson)
        {
            CreateArgs args = AiJson.FromJsonOrThrow<CreateArgs>(argsJson);
            string id = AiWorksetStore.CreateJsonItemsWorkset("items", args == null ? string.Empty : args.sourceToolId, args == null ? string.Empty : args.fieldName, args == null ? "{}" : args.summaryJson, args == null || args.itemJsons == null ? null : new System.Collections.Generic.List<string>(args.itemJsons));
            return "{\"worksetId\":" + AiJson.Quote(id) + ",\"count\":" + AiJson.Number(args == null || args.itemJsons == null ? 0 : args.itemJsons.Length) + "}";
        }

        [AiTool(
            "workset.get_summary",
            "Returns a workset summary or a page of items. Args: worksetId, offset, limit.",
            @"{""type"":""object"",""properties"":{""worksetId"":{""type"":""string""},""offset"":{""type"":""integer""},""limit"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string GetSummary(string argsJson)
        {
            SummaryArgs args = AiJson.FromJsonOrThrow<SummaryArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.worksetId)) throw new Exception("worksetId is required.");
            if (args.offset > 0 || args.limit > 0)
            {
                string pageJson;
                if (!AiWorksetStore.TryBuildPageJson(args.worksetId, args.offset, args.limit, out pageJson))
                {
                    throw new Exception("Unknown workset: " + args.worksetId);
                }
                return pageJson;
            }
            return AiWorksetStore.BuildSummaryJson(args.worksetId);
        }

        [AiTool(
            "workset.derive",
            "Creates a derived workset using simple filter operations such as contains, exclude_contains, or equals.",
            @"{""type"":""object"",""properties"":{""worksetId"":{""type"":""string""},""operation"":{""type"":""string""},""fieldName"":{""type"":""string""},""contains"":{""type"":""string""},""equals"":{""type"":""string""},""skip"":{""type"":""integer""},""limit"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string Derive(string argsJson)
        {
            DeriveArgs args = AiJson.FromJsonOrThrow<DeriveArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.worksetId)) throw new Exception("worksetId is required.");
            string derivedId = AiWorksetStore.CreateDerivedWorkset(args.worksetId, args.operation, args.fieldName, args.contains, args.equals, args.skip, args.limit);
            return "{\"worksetId\":" + AiJson.Quote(derivedId) + ",\"sourceWorksetId\":" + AiJson.Quote(args.worksetId) + "}";
        }

        [AiTool(
            "workset.delete",
            "Deletes a persistent workset by id.",
            @"{""type"":""object"",""properties"":{""worksetId"":{""type"":""string""}}}",
            @"{""type"":""object""}",
            Danger = AiToolDanger.Medium,
            RequiresConfirmation = true
        )]
        public static string Delete(string argsJson)
        {
            DeleteArgs args = AiJson.FromJsonOrThrow<DeleteArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.worksetId)) throw new Exception("worksetId is required.");
            bool deleted = AiWorksetStore.Delete(args.worksetId);
            return "{\"worksetId\":" + AiJson.Quote(args.worksetId) + ",\"deleted\":" + AiJson.Bool(deleted) + "}";
        }
    }
}
#endif
