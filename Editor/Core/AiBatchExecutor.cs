#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiBatchStep
    {
        public string id;
        public string toolId;
        public string argsJson;
        public bool continueOnError;
        public bool requiresValidation;
    }

    [Serializable]
    internal sealed class AiBatchExecutionResult
    {
        public bool ok;
        public int completedSteps;
        public string[] executedToolIds;
        public string summaryJson;
        public string[] stepResultJsons;
    }

    internal static class AiBatchExecutor
    {
        public static string Run(AiBatchStep[] steps, bool dryRun, bool stopOnError)
        {
            if (steps == null || steps.Length == 0)
            {
                throw new Exception("At least one batch step is required.");
            }
            if (steps.Length > AiEditorAgentSettings.MaxBatchSteps)
            {
                throw new Exception("Batch step limit exceeded. Max steps: " + AiEditorAgentSettings.MaxBatchSteps);
            }

            Dictionary<string, string> stepJsonById = new Dictionary<string, string>();
            List<string> stepResultJsons = new List<string>();
            List<string> executedToolIds = new List<string>();
            bool ok = true;
            int completedSteps = 0;

            for (int i = 0; i < steps.Length; i++)
            {
                AiBatchStep step = steps[i];
                if (step == null) continue;
                if (string.IsNullOrEmpty(step.id)) step.id = "step_" + (i + 1);
                if (string.IsNullOrEmpty(step.toolId)) throw new Exception("Batch step toolId is required at index " + i);

                string argsJson = AiJsonQueryUtility.ReplacePlaceholders(string.IsNullOrWhiteSpace(step.argsJson) ? "{}" : step.argsJson, stepJsonById);
                string resultJson;
                bool stepOk = true;
                string errorMessage = string.Empty;

                if (dryRun)
                {
                    resultJson = "{"
                        + "\"ok\":true,"
                        + "\"toolId\":" + AiJson.Quote(step.toolId) + ","
                        + "\"dryRun\":true,"
                        + "\"args\":" + AiJson.AsJsonValue(argsJson)
                        + "}";
                }
                else
                {
                    try
                    {
                        resultJson = ExecuteTool(step.toolId, argsJson);
                    }
                    catch (Exception e)
                    {
                        stepOk = false;
                        errorMessage = e.Message;
                        resultJson = "{"
                            + "\"ok\":false,"
                            + "\"toolId\":" + AiJson.Quote(step.toolId) + ","
                            + "\"error\":" + AiJson.Quote(errorMessage)
                            + "}";
                    }
                }

                string wrapped = "{"
                    + "\"id\":" + AiJson.Quote(step.id) + ","
                    + "\"toolId\":" + AiJson.Quote(step.toolId) + ","
                    + "\"ok\":" + AiJson.Bool(stepOk) + ","
                    + "\"result\":" + AiJson.AsJsonValue(resultJson)
                    + "}";

                stepResultJsons.Add(wrapped);
                stepJsonById[step.id] = wrapped;
                executedToolIds.Add(step.toolId);
                if (stepOk) completedSteps++;

                if (!stepOk)
                {
                    ok = false;
                    if (stopOnError && !step.continueOnError)
                    {
                        break;
                    }
                }
            }

            StringBuilder summary = new StringBuilder();
            summary.Append('{');
            summary.Append("\"dryRun\":").Append(AiJson.Bool(dryRun)).Append(',');
            summary.Append("\"stopOnError\":").Append(AiJson.Bool(stopOnError)).Append(',');
            summary.Append("\"completedSteps\":").Append(AiJson.Number(completedSteps)).Append(',');
            summary.Append("\"requestedSteps\":").Append(AiJson.Number(steps.Length));
            summary.Append('}');

            StringBuilder response = new StringBuilder();
            response.Append('{');
            response.Append("\"ok\":").Append(AiJson.Bool(ok)).Append(',');
            response.Append("\"completedSteps\":").Append(AiJson.Number(completedSteps)).Append(',');
            response.Append("\"executedToolIds\":").Append(AiJson.StringArray(executedToolIds)).Append(',');
            response.Append("\"summary\":").Append(summary).Append(',');
            response.Append("\"steps\":[");
            for (int i = 0; i < stepResultJsons.Count; i++)
            {
                if (i > 0) response.Append(',');
                response.Append(stepResultJsons[i]);
            }
            response.Append("]}");
            return response.ToString();
        }

        private static string ExecuteTool(string toolId, string argsJson)
        {
            AiToolEntry entry;
            if (!AiToolRegistry.TryGet(toolId, out entry))
            {
                AiToolRegistry.Rebuild();
                if (!AiToolRegistry.TryGet(toolId, out entry))
                {
                    throw new Exception("Unknown tool: " + toolId);
                }
            }

            bool shouldConfirm = AiEditorAgentSettings.ShouldConfirmTool(entry.info);
            if (shouldConfirm)
            {
                bool allowed = EditorUtility.DisplayDialog(
                    "AI Editor Agent Batch Confirmation",
                    "AI wants to execute a tool in a batch.\n\nTool: " + toolId + "\nDanger: " + entry.info.danger + "\n\nArguments:\n" + argsJson,
                    "Allow",
                    "Deny");
                if (!allowed)
                {
                    throw new Exception("User denied execution.");
                }
            }

            try
            {
                string toolResultJson = (string)entry.method.Invoke(null, new object[] { argsJson });
                return "{"
                    + "\"ok\":true,"
                    + "\"toolId\":" + AiJson.Quote(toolId) + ","
                    + "\"result\":" + AiJson.AsJsonValue(toolResultJson)
                    + "}";
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                throw new Exception(inner.Message);
            }
        }
    }
}
#endif
