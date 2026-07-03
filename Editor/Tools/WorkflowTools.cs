#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace AiUnity.EditorAgent
{
    internal static class WorkflowTools
    {
        [Serializable]
        private sealed class RecipeMatchArgs
        {
            public string query;
            public int limit;
        }

        [Serializable]
        private sealed class RecipeSuccessArgs
        {
            public string key;
            public string name;
            public string goal;
            public string[] stepToolIds;
            public string[] validatorIds;
            public string[] changedFiles;
            public string notes;
        }

        [AiTool(
            "workflow.match_recipes",
            "Finds previously successful Unity workflows that are similar to the current goal so AI can reuse good paths before planning from scratch.",
            @"{""type"":""object"",""properties"":{""query"":{""type"":""string""},""limit"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string MatchRecipes(string argsJson)
        {
            RecipeMatchArgs args = AiJson.FromJsonOrThrow<RecipeMatchArgs>(argsJson);
            int limit = args == null || args.limit <= 0 ? 6 : Math.Min(args.limit, 20);
            List<AiRecipeRecord> matches = AiRecipeStore.Match(args == null ? string.Empty : args.query, limit);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"count\":").Append(AiJson.Number(matches.Count)).Append(",\"items\":[");
            for (int i = 0; i < matches.Count; i++)
            {
                AiRecipeRecord item = matches[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"key\":").Append(AiJson.Quote(item.key)).Append(',');
                sb.Append("\"name\":").Append(AiJson.Quote(item.name)).Append(',');
                sb.Append("\"goal\":").Append(AiJson.Quote(item.goal)).Append(',');
                sb.Append("\"successCount\":").Append(AiJson.Number(item.successCount)).Append(',');
                sb.Append("\"stepToolIds\":").Append(AiJson.StringArray(item.stepToolIds)).Append(',');
                sb.Append("\"validatorIds\":").Append(AiJson.StringArray(item.validatorIds));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "workflow.record_success",
            "Records a successful Unity workflow after validation so future runs can reuse the same path.",
            @"{""type"":""object"",""properties"":{""key"":{""type"":""string""},""name"":{""type"":""string""},""goal"":{""type"":""string""},""stepToolIds"":{""type"":""array""},""validatorIds"":{""type"":""array""},""changedFiles"":{""type"":""array""},""notes"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string RecordSuccess(string argsJson)
        {
            RecipeSuccessArgs args = AiJson.FromJsonOrThrow<RecipeSuccessArgs>(argsJson);
            if (args == null) throw new Exception("Arguments are required.");
            AiRecipeRecord record = AiRecipeStore.RecordSuccess(args.key, args.name, args.goal, args.stepToolIds, args.validatorIds, args.changedFiles, args.notes);
            return "{\"key\":" + AiJson.Quote(record.key) + ",\"successCount\":" + AiJson.Number(record.successCount) + "}";
        }
    }
}
#endif
