#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiValidationIssue
    {
        public string validatorId;
        public string severity;
        public string message;
        public string targetPath;
    }

    internal static class AiValidationCoordinator
    {
        public static AiContextSnapshotRecord CaptureBaseline(string label)
        {
            AiEditorAgentPaths.EnsureStateFolders();
            AiGitUtility.AiGitChangedFilesResult gitFiles = AiGitUtility.GetChangedFilesDetailed(10000);
            AiContextSnapshotRecord record = new AiContextSnapshotRecord();
            record.id = Guid.NewGuid().ToString("N");
            record.label = string.IsNullOrEmpty(label) ? "baseline" : label;
            record.createdAt = AiEditorAgentSettings.FormatTimestamp(DateTime.Now);
            record.compileFingerprints = BuildCompileFingerprints();
            record.compileHash = HashLines(record.compileFingerprints);
            record.selectionPaths = BuildSelectionPaths();
            record.selectionHash = HashLines(record.selectionPaths);
            record.gitStatusSummary = AiGitUtility.BuildStatusSummaryJson(10000);
            record.gitDiffSummary = AiGitUtility.BuildDiffSummaryJson(10000);
            record.changedFiles = gitFiles.changedFiles ?? new string[0];
            record.unstagedFiles = gitFiles.unstagedFiles ?? new string[0];
            record.stagedFiles = gitFiles.stagedFiles ?? new string[0];
            record.untrackedFiles = gitFiles.untrackedFiles ?? new string[0];
            record.prefabs = BuildPrefabEntries(record.changedFiles);
            record.scenes = BuildSceneEntries(record.changedFiles);
            AiSnapshotStore.Save(record);
            return record;
        }

        public static string Run(string baselineSnapshotId, string[] validatorIds)
        {
            AiContextSnapshotRecord baseline;
            if (!AiSnapshotStore.TryGet(baselineSnapshotId, out baseline))
            {
                throw new Exception("Unknown baseline snapshot: " + baselineSnapshotId);
            }

            AiContextSnapshotRecord current = CaptureBaseline("post-validation");
            string[] ids = validatorIds == null || validatorIds.Length == 0
                ? new[] { "compile.no_new_errors", "prefab.no_missing_references", "scene.no_missing_scripts", "git.diff_scope" }
                : validatorIds;

            List<string> issueJsons = new List<string>();
            bool ok = true;
            for (int i = 0; i < ids.Length; i++)
            {
                string validatorId = ids[i] == null ? string.Empty : ids[i].Trim();
                if (string.IsNullOrEmpty(validatorId)) continue;

                List<AiValidationIssue> issues = RunValidator(validatorId, baseline, current);
                for (int j = 0; j < issues.Count; j++)
                {
                    AiValidationIssue issue = issues[j];
                    issueJsons.Add("{"
                        + "\"validatorId\":" + AiJson.Quote(issue.validatorId) + ","
                        + "\"severity\":" + AiJson.Quote(issue.severity) + ","
                        + "\"message\":" + AiJson.Quote(issue.message) + ","
                        + "\"targetPath\":" + AiJson.Quote(issue.targetPath)
                        + "}");
                }

                if (issues.Count > 0) ok = false;
            }

            StringBuilderJsonArray idsJson = new StringBuilderJsonArray(ids);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append('{');
            sb.Append("\"ok\":").Append(AiJson.Bool(ok)).Append(',');
            sb.Append("\"baselineSnapshotId\":").Append(AiJson.Quote(baseline.id)).Append(',');
            sb.Append("\"currentSnapshotId\":").Append(AiJson.Quote(current.id)).Append(',');
            sb.Append("\"validatorIds\":").Append(idsJson.Build()).Append(',');
            sb.Append("\"checkedFiles\":").Append(AiJson.StringArray(current.changedFiles)).Append(',');
            sb.Append("\"unstagedFiles\":").Append(AiJson.StringArray(current.unstagedFiles)).Append(',');
            sb.Append("\"stagedFiles\":").Append(AiJson.StringArray(current.stagedFiles)).Append(',');
            sb.Append("\"untrackedFiles\":").Append(AiJson.StringArray(current.untrackedFiles)).Append(',');
            sb.Append("\"issueCount\":").Append(AiJson.Number(issueJsons.Count)).Append(',');
            sb.Append("\"issues\":[");
            for (int i = 0; i < issueJsons.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(issueJsons[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static List<AiValidationIssue> RunValidator(string validatorId, AiContextSnapshotRecord baseline, AiContextSnapshotRecord current)
        {
            List<AiValidationIssue> issues = new List<AiValidationIssue>();
            if (validatorId == "compile.no_new_errors")
            {
                CompareCompile(issues, baseline, current, "compile.no_new_errors");
            }
            else if (validatorId == "prefab.no_missing_references")
            {
                ComparePrefabs(issues, baseline, current);
            }
            else if (validatorId == "scene.no_missing_scripts")
            {
                CompareScenes(issues, baseline, current);
            }
            else if (validatorId == "git.diff_scope")
            {
                CompareGitScope(issues, current);
            }
            else if (validatorId == "generated_tool.compiles")
            {
                CompareCompile(issues, baseline, current, "generated_tool.compiles");
            }

            return issues;
        }

        private static void CompareCompile(List<AiValidationIssue> issues, AiContextSnapshotRecord baseline, AiContextSnapshotRecord current, string validatorId)
        {
            string[] before = baseline.compileFingerprints ?? new string[0];
            string[] after = current.compileFingerprints ?? new string[0];
            for (int i = 0; i < after.Length; i++)
            {
                bool exists = false;
                for (int j = 0; j < before.Length; j++)
                {
                    if (before[j] == after[i])
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    issues.Add(new AiValidationIssue
                    {
                        validatorId = validatorId,
                        severity = "error",
                        message = "New compile or console error introduced: " + after[i],
                        targetPath = string.Empty
                    });
                }
            }
        }

        private static void ComparePrefabs(List<AiValidationIssue> issues, AiContextSnapshotRecord baseline, AiContextSnapshotRecord current)
        {
            AiPrefabSnapshotEntry[] before = baseline.prefabs ?? new AiPrefabSnapshotEntry[0];
            AiPrefabSnapshotEntry[] after = current.prefabs ?? new AiPrefabSnapshotEntry[0];
            for (int i = 0; i < after.Length; i++)
            {
                AiPrefabSnapshotEntry item = after[i];
                AiPrefabSnapshotEntry previous = FindPrefab(before, item.path);
                int previousMissingRefs = previous == null ? 0 : previous.missingReferenceCount;
                int previousMissingScripts = previous == null ? 0 : previous.missingScriptCount;
                if (!string.IsNullOrEmpty(item.inspectionError))
                {
                    issues.Add(new AiValidationIssue
                    {
                        validatorId = "prefab.no_missing_references",
                        severity = "error",
                        message = "Prefab validation could not inspect this asset: " + item.inspectionError,
                        targetPath = item.path
                    });
                    continue;
                }
                if (item.missingReferenceCount > previousMissingRefs)
                {
                    issues.Add(new AiValidationIssue
                    {
                        validatorId = "prefab.no_missing_references",
                        severity = "error",
                        message = "Prefab has more missing references after the change.",
                        targetPath = item.path
                    });
                }
                if (item.missingScriptCount > previousMissingScripts)
                {
                    issues.Add(new AiValidationIssue
                    {
                        validatorId = "prefab.no_missing_references",
                        severity = "error",
                        message = "Prefab has more missing scripts after the change.",
                        targetPath = item.path
                    });
                }
            }
        }

        private static void CompareScenes(List<AiValidationIssue> issues, AiContextSnapshotRecord baseline, AiContextSnapshotRecord current)
        {
            AiSceneSnapshotEntry[] before = baseline.scenes ?? new AiSceneSnapshotEntry[0];
            AiSceneSnapshotEntry[] after = current.scenes ?? new AiSceneSnapshotEntry[0];
            for (int i = 0; i < after.Length; i++)
            {
                AiSceneSnapshotEntry item = after[i];
                AiSceneSnapshotEntry previous = FindScene(before, item.path);
                int previousMissingScripts = previous == null ? 0 : previous.missingScriptCount;
                if (!string.IsNullOrEmpty(item.inspectionError))
                {
                    issues.Add(new AiValidationIssue
                    {
                        validatorId = "scene.no_missing_scripts",
                        severity = "error",
                        message = "Scene validation could not inspect this asset: " + item.inspectionError,
                        targetPath = item.path
                    });
                    continue;
                }
                if (item.missingScriptCount > previousMissingScripts)
                {
                    issues.Add(new AiValidationIssue
                    {
                        validatorId = "scene.no_missing_scripts",
                        severity = "error",
                        message = "Scene has more missing scripts after the change.",
                        targetPath = item.path
                    });
                }
            }
        }

        private static void CompareGitScope(List<AiValidationIssue> issues, AiContextSnapshotRecord current)
        {
            string[] changedFiles = current.changedFiles ?? new string[0];
            if (changedFiles.Length > 40)
            {
                issues.Add(new AiValidationIssue
                {
                    validatorId = "git.diff_scope",
                    severity = "warning",
                    message = "This run changed a large number of files. Review scope before finishing.",
                    targetPath = string.Empty
                });
            }
        }

        private static string[] BuildCompileFingerprints()
        {
            List<AiCapturedConsoleEntry> entries = AiEditorAgentState.GetConsoleEntries(100, true);
            List<string> result = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                AiCapturedConsoleEntry entry = entries[i];
                string line = entry == null ? string.Empty : entry.condition;
                int newline = line.IndexOf('\n');
                if (newline >= 0) line = line.Substring(0, newline);
                result.Add((entry == null ? string.Empty : entry.type) + ":" + line);
            }
            return result.ToArray();
        }

        private static string[] BuildSelectionPaths()
        {
            List<string> result = new List<string>();
            UnityEngine.Object[] objects = Selection.objects;
            for (int i = 0; i < objects.Length; i++)
            {
                UnityEngine.Object obj = objects[i];
                if (obj == null) continue;
                string assetPath = AssetDatabase.GetAssetPath(obj);
                result.Add((assetPath ?? string.Empty) + "|" + obj.name);
            }
            return result.ToArray();
        }

        private static AiPrefabSnapshotEntry[] BuildPrefabEntries(string[] changedFiles)
        {
            List<AiPrefabSnapshotEntry> items = new List<AiPrefabSnapshotEntry>();
            if (changedFiles == null) return items.ToArray();
            for (int i = 0; i < changedFiles.Length; i++)
            {
                string path = NormalizeRepoPath(changedFiles[i]);
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                if (!AiEditorAgentPaths.IsSafeAssetPath(path, true, ".prefab")) continue;

                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    items.Add(new AiPrefabSnapshotEntry
                    {
                        path = path,
                        missingScriptCount = CountMissingScripts(root),
                        missingReferenceCount = CountMissingObjectReferences(root),
                        inspectionError = string.Empty
                    });
                }
                catch (Exception e)
                {
                    items.Add(new AiPrefabSnapshotEntry
                    {
                        path = path,
                        missingScriptCount = 0,
                        missingReferenceCount = 0,
                        inspectionError = e.Message
                    });
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return items.ToArray();
        }

        private static AiSceneSnapshotEntry[] BuildSceneEntries(string[] changedFiles)
        {
            List<AiSceneSnapshotEntry> items = new List<AiSceneSnapshotEntry>();
            if (changedFiles == null) return items.ToArray();
            for (int i = 0; i < changedFiles.Length; i++)
            {
                string path = NormalizeRepoPath(changedFiles[i]);
                if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                if (!AiEditorAgentPaths.IsSafeAssetPath(path, true, ".unity")) continue;

                Scene scene = default(Scene);
                bool opened = false;
                try
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened = true;
                    items.Add(new AiSceneSnapshotEntry
                    {
                        path = path,
                        missingScriptCount = CountMissingScripts(scene),
                        inspectionError = string.Empty
                    });
                }
                catch (Exception e)
                {
                    items.Add(new AiSceneSnapshotEntry
                    {
                        path = path,
                        missingScriptCount = 0,
                        inspectionError = e.Message
                    });
                }
                finally
                {
                    if (opened && scene.IsValid())
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
            return items.ToArray();
        }

        private static int CountMissingScripts(Scene scene)
        {
            int count = 0;
            if (!scene.IsValid()) return count;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                count += CountMissingScripts(roots[i]);
            }
            return count;
        }

        private static int CountMissingScripts(GameObject root)
        {
            if (root == null) return 0;
            int count = 0;
            Stack<Transform> stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                GameObject go = current.gameObject;
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                for (int i = 0; i < current.childCount; i++)
                {
                    stack.Push(current.GetChild(i));
                }
            }
            return count;
        }

        private static int CountMissingObjectReferences(GameObject root)
        {
            if (root == null) return 0;
            int count = 0;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty iterator = serialized.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (iterator.objectReferenceValue != null) continue;
                    if (iterator.objectReferenceInstanceIDValue == 0) continue;
                    count++;
                }
            }
            return count;
        }

        private static AiPrefabSnapshotEntry FindPrefab(AiPrefabSnapshotEntry[] items, string path)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].path == path) return items[i];
            }
            return null;
        }

        private static AiSceneSnapshotEntry FindScene(AiSceneSnapshotEntry[] items, string path)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && items[i].path == path) return items[i];
            }
            return null;
        }

        private static string NormalizeRepoPath(string path)
        {
            string value = path == null ? string.Empty : path.Replace('\\', '/').Trim();
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.StartsWith("Assets/", StringComparison.Ordinal) || value.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return value;
            }
            return value;
        }

        private static string HashLines(string[] lines)
        {
            ulong hash = 1469598103934665603UL;
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] ?? string.Empty;
                    for (int c = 0; c < line.Length; c++)
                    {
                        hash ^= line[c];
                        hash *= 1099511628211UL;
                    }
                    hash ^= '|';
                    hash *= 1099511628211UL;
                }
            }
            return hash.ToString("x16");
        }

        private sealed class StringBuilderJsonArray
        {
            private readonly string[] values;

            public StringBuilderJsonArray(string[] values)
            {
                this.values = values ?? new string[0];
            }

            public string Build()
            {
                return AiJson.StringArray(values);
            }
        }
    }
}
#endif
