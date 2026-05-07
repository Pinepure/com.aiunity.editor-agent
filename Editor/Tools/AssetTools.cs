#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    internal static class AssetTools
    {
        [Serializable]
        private sealed class AssetFindArgs
        {
            public string filter;
            public string[] folders;
            public int maxResults;
            public bool includePackages;
        }

        [Serializable]
        private sealed class AssetPathArgs
        {
            public string path;
            public bool recursive;
        }

        [Serializable]
        private sealed class GuidArgs
        {
            public string guid;
        }

        [Serializable]
        private sealed class PathArgs
        {
            public string path;
        }

        [Serializable]
        private sealed class ReverseDependencyArgs
        {
            public string targetPath;
            public string[] searchFolders;
            public string filter;
            public bool recursive;
            public int maxResults;
            public bool includePackages;
        }

        [Serializable]
        private sealed class ReadTextArgs
        {
            public string path;
            public int maxChars;
        }

        [AiTool(
            "asset.refresh",
            "Refreshes Unity AssetDatabase so changed, created, or deleted assets are imported.",
            "{}",
            @"{""type"":""object"",""properties"":{""ok"":{""type"":""boolean""}}}"
        )]
        public static string Refresh(string argsJson)
        {
            AssetDatabase.Refresh();
            return "{\"ok\":true}";
        }

        [AiTool(
            "asset.find",
            "Searches project assets using Unity AssetDatabase.FindAssets filter syntax. Args: filter, folders, maxResults, includePackages.",
            @"{""type"":""object"",""properties"":{""filter"":{""type"":""string""},""folders"":{""type"":""array""},""maxResults"":{""type"":""integer""},""includePackages"":{""type"":""boolean""}}}",
            @"{""type"":""object"",""properties"":{""items"":{""type"":""array""},""totalFound"":{""type"":""integer""},""truncated"":{""type"":""boolean""}}}"
        )]
        public static string Find(string argsJson)
        {
            AssetFindArgs args = AiJson.FromJsonOrThrow<AssetFindArgs>(argsJson);
            if (args == null) args = new AssetFindArgs();
            string filter = args.filter ?? string.Empty;
            int max = args.maxResults <= 0 ? 200 : Math.Min(args.maxResults, 5000);
            string[] folders = NormalizeFolders(args.folders, args.includePackages);
            string[] guids = folders == null ? AssetDatabase.FindAssets(filter) : AssetDatabase.FindAssets(filter, folders);

            StringBuilder sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"filter\":").Append(AiJson.Quote(filter)).Append(',');
            sb.Append("\"totalFound\":").Append(AiJson.Number(guids.Length)).Append(',');
            sb.Append("\"truncated\":").Append(AiJson.Bool(guids.Length > max)).Append(',');
            sb.Append("\"items\":[");
            int count = Math.Min(max, guids.Length);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                string guid = guids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                sb.Append('{');
                sb.Append("\"guid\":").Append(AiJson.Quote(guid)).Append(',');
                sb.Append("\"path\":").Append(AiJson.Quote(path)).Append(',');
                sb.Append("\"name\":").Append(AiJson.Quote(Path.GetFileNameWithoutExtension(path))).Append(',');
                sb.Append("\"type\":").Append(AiJson.Quote(type == null ? string.Empty : type.FullName));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "asset.dependencies",
            "Returns assets referenced by a target asset using AssetDatabase.GetDependencies. Args: path, recursive.",
            @"{""type"":""object"",""required"":[""path""],""properties"":{""path"":{""type"":""string""},""recursive"":{""type"":""boolean""}}}",
            @"{""type"":""object"",""properties"":{""dependencies"":{""type"":""array""}}}"
        )]
        public static string Dependencies(string argsJson)
        {
            AssetPathArgs args = AiJson.FromJsonOrThrow<AssetPathArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.path)) throw new Exception("path is required.");
            string path = AiEditorAgentPaths.NormalizeAssetPath(args.path);
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) throw new Exception("Asset not found: " + path);
            string[] dependencies = AssetDatabase.GetDependencies(path, args.recursive);
            return "{\"path\":" + AiJson.Quote(path) + ",\"recursive\":" + AiJson.Bool(args.recursive) + ",\"dependencies\":" + AiJson.StringArray(dependencies) + "}";
        }

        [AiTool(
            "asset.reverse_dependencies",
            "Scans assets that reference targetPath. Args: targetPath, searchFolders, filter, recursive, maxResults, includePackages.",
            @"{""type"":""object"",""properties"":{""targetPath"":{""type"":""string""},""searchFolders"":{""type"":""array""},""filter"":{""type"":""string""},""recursive"":{""type"":""boolean""},""maxResults"":{""type"":""integer""},""includePackages"":{""type"":""boolean""}}}",
            @"{""type"":""object"",""properties"":{""references"":{""type"":""array""}}}"
        )]
        public static string ReverseDependencies(string argsJson)
        {
            ReverseDependencyArgs args = AiJson.FromJsonOrThrow<ReverseDependencyArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.targetPath)) throw new Exception("targetPath is required.");

            string targetPath = AiEditorAgentPaths.NormalizeAssetPath(args.targetPath);
            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            if (string.IsNullOrEmpty(targetGuid)) throw new Exception("Target asset not found: " + targetPath);

            int max = args.maxResults <= 0 ? 200 : Math.Min(args.maxResults, 10000);
            string filter = args.filter ?? string.Empty;
            string[] folders = NormalizeFolders(args.searchFolders, args.includePackages);
            string[] guids = folders == null ? AssetDatabase.FindAssets(filter) : AssetDatabase.FindAssets(filter, folders);

            List<string> references = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(candidatePath) || candidatePath == targetPath) continue;

                string[] dependencies;
                try
                {
                    dependencies = AssetDatabase.GetDependencies(candidatePath, args.recursive);
                }
                catch
                {
                    continue;
                }

                bool found = false;
                for (int d = 0; d < dependencies.Length; d++)
                {
                    string depPath = AiEditorAgentPaths.NormalizeAssetPath(dependencies[d]);
                    if (depPath == targetPath || AssetDatabase.AssetPathToGUID(depPath) == targetGuid)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    references.Add(candidatePath);
                    if (references.Count >= max) break;
                }
            }

            return "{\"targetPath\":" + AiJson.Quote(targetPath)
                + ",\"targetGuid\":" + AiJson.Quote(targetGuid)
                + ",\"scanned\":" + AiJson.Number(guids.Length)
                + ",\"truncated\":" + AiJson.Bool(references.Count >= max && guids.Length > references.Count)
                + ",\"references\":" + AiJson.StringArray(references) + "}";
        }

        [AiTool(
            "asset.guid_to_path",
            "Converts an asset GUID to an asset path.",
            @"{""type"":""object"",""properties"":{""guid"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}"
        )]
        public static string GuidToPath(string argsJson)
        {
            GuidArgs args = AiJson.FromJsonOrThrow<GuidArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.guid)) throw new Exception("guid is required.");
            string path = AssetDatabase.GUIDToAssetPath(args.guid);
            return "{\"guid\":" + AiJson.Quote(args.guid) + ",\"path\":" + AiJson.Quote(path) + "}";
        }

        [AiTool(
            "asset.path_to_guid",
            "Converts an asset path to a GUID.",
            @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""guid"":{""type"":""string""}}}"
        )]
        public static string PathToGuid(string argsJson)
        {
            PathArgs args = AiJson.FromJsonOrThrow<PathArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.path)) throw new Exception("path is required.");
            string path = AiEditorAgentPaths.NormalizeAssetPath(args.path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            return "{\"path\":" + AiJson.Quote(path) + ",\"guid\":" + AiJson.Quote(guid) + "}";
        }

        [AiTool(
            "asset.read_text",
            "Reads a text asset under Assets/ with a maximum character limit. Args: path, maxChars.",
            @"{""type"":""object"",""properties"":{""path"":{""type"":""string""},""maxChars"":{""type"":""integer""}}}",
            @"{""type"":""object"",""properties"":{""content"":{""type"":""string""}}}"
        )]
        public static string ReadText(string argsJson)
        {
            ReadTextArgs args = AiJson.FromJsonOrThrow<ReadTextArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.path)) throw new Exception("path is required.");
            string path = AiEditorAgentPaths.NormalizeAssetPath(args.path);
            if (!AiEditorAgentPaths.IsSafeAssetPath(path, true, null)) throw new Exception("Only safe paths under Assets/ are allowed.");
            string absolute = AiEditorAgentPaths.ToAbsolutePathFromAssetPath(path);
            if (!File.Exists(absolute)) throw new Exception("File not found: " + path);
            int max = args.maxChars <= 0 ? 20000 : Math.Min(args.maxChars, 200000);
            string text = File.ReadAllText(absolute, Encoding.UTF8);
            bool truncated = text.Length > max;
            if (truncated) text = text.Substring(0, max);
            return "{\"path\":" + AiJson.Quote(path) + ",\"truncated\":" + AiJson.Bool(truncated) + ",\"content\":" + AiJson.Quote(text) + "}";
        }

        private static string[] NormalizeFolders(string[] folders, bool includePackages)
        {
            if (folders == null || folders.Length == 0)
            {
                return includePackages ? null : new[] { "Assets" };
            }

            List<string> result = new List<string>();
            for (int i = 0; i < folders.Length; i++)
            {
                string f = AiEditorAgentPaths.NormalizeAssetPath(folders[i]);
                if (string.IsNullOrEmpty(f)) continue;
                if (!includePackages && !f.StartsWith("Assets", StringComparison.Ordinal)) continue;
                if (f.Contains("../") || f.Contains("/..") || f.IndexOf(':') >= 0) continue;
                result.Add(f);
            }

            if (result.Count == 0) return includePackages ? null : new[] { "Assets" };
            return result.ToArray();
        }
    }
}
#endif
