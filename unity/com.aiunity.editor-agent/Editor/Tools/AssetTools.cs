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
            public int pageSize;
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
            public int pageSize;
            public bool includePackages;
        }

        [Serializable]
        private sealed class ReadTextArgs
        {
            public string path;
            public int maxChars;
            public int offset;
            public int length;
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
            "Searches project assets using Unity filter syntax and returns the first page plus a resultHandle when more items are available. Args: filter, folders, maxResults, pageSize, includePackages.",
            @"{""type"":""object"",""properties"":{""filter"":{""type"":""string""},""folders"":{""type"":""array""},""maxResults"":{""type"":""integer""},""pageSize"":{""type"":""integer""},""includePackages"":{""type"":""boolean""}}}",
            @"{""type"":""object""}"
        )]
        public static string Find(string argsJson)
        {
            AssetFindArgs args = AiJson.FromJsonOrThrow<AssetFindArgs>(argsJson);
            if (args == null) args = new AssetFindArgs();
            string filter = args.filter ?? string.Empty;
            int maxResults = args.maxResults <= 0 ? 200 : Math.Min(args.maxResults, 5000);
            int pageSize = args.pageSize <= 0 ? 20 : Math.Min(args.pageSize, 100);
            string[] folders = NormalizeFolders(args.folders, args.includePackages);
            string[] guids = folders == null ? AssetDatabase.FindAssets(filter) : AssetDatabase.FindAssets(filter, folders);

            int storedCount = Math.Min(maxResults, guids.Length);
            List<string> items = new List<string>(storedCount);
            for (int i = 0; i < storedCount; i++)
            {
                string guid = guids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                items.Add("{\"guid\":" + AiJson.Quote(guid)
                    + ",\"path\":" + AiJson.Quote(path)
                    + ",\"name\":" + AiJson.Quote(Path.GetFileNameWithoutExtension(path))
                    + ",\"type\":" + AiJson.Quote(type == null ? string.Empty : type.FullName)
                    + "}");
            }

            string summary = "{"
                + "\"filter\":" + AiJson.Quote(filter) + ","
                + "\"folders\":" + AiJson.StringArray(folders) + ","
                + "\"includePackages\":" + AiJson.Bool(args.includePackages) + ","
                + "\"totalFound\":" + AiJson.Number(guids.Length) + ","
                + "\"storedCount\":" + AiJson.Number(storedCount) + ","
                + "\"truncated\":" + AiJson.Bool(guids.Length > maxResults)
                + "}";
            return AiResultResponseBuilder.BuildJsonItemsResult("asset.find", "items", items, summary, pageSize);
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
            "Scans assets that reference targetPath and returns the first page plus a resultHandle when more items are available. Args: targetPath, searchFolders, filter, recursive, maxResults, pageSize, includePackages.",
            @"{""type"":""object"",""properties"":{""targetPath"":{""type"":""string""},""searchFolders"":{""type"":""array""},""filter"":{""type"":""string""},""recursive"":{""type"":""boolean""},""maxResults"":{""type"":""integer""},""pageSize"":{""type"":""integer""},""includePackages"":{""type"":""boolean""}}}",
            @"{""type"":""object""}"
        )]
        public static string ReverseDependencies(string argsJson)
        {
            ReverseDependencyArgs args = AiJson.FromJsonOrThrow<ReverseDependencyArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.targetPath)) throw new Exception("targetPath is required.");

            string targetPath = AiEditorAgentPaths.NormalizeAssetPath(args.targetPath);
            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            if (string.IsNullOrEmpty(targetGuid)) throw new Exception("Target asset not found: " + targetPath);

            int maxResults = args.maxResults <= 0 ? 200 : Math.Min(args.maxResults, 10000);
            int pageSize = args.pageSize <= 0 ? 20 : Math.Min(args.pageSize, 100);
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
                    references.Add(AiJson.Quote(candidatePath));
                    if (references.Count >= maxResults) break;
                }
            }

            string summary = "{"
                + "\"targetPath\":" + AiJson.Quote(targetPath) + ","
                + "\"targetGuid\":" + AiJson.Quote(targetGuid) + ","
                + "\"filter\":" + AiJson.Quote(filter) + ","
                + "\"searchFolders\":" + AiJson.StringArray(folders) + ","
                + "\"scanned\":" + AiJson.Number(guids.Length) + ","
                + "\"truncated\":" + AiJson.Bool(references.Count >= maxResults && guids.Length > references.Count)
                + "}";
            return AiResultResponseBuilder.BuildJsonItemsResult("asset.reverse_dependencies", "references", references, summary, pageSize);
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
            "Reads a text asset under Assets/ as a chunked response. Args: path, offset, length, maxChars.",
            @"{""type"":""object"",""properties"":{""path"":{""type"":""string""},""offset"":{""type"":""integer""},""length"":{""type"":""integer""},""maxChars"":{""type"":""integer""}}}",
            @"{""type"":""object""}"
        )]
        public static string ReadText(string argsJson)
        {
            ReadTextArgs args = AiJson.FromJsonOrThrow<ReadTextArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.path)) throw new Exception("path is required.");
            string path = AiEditorAgentPaths.NormalizeAssetPath(args.path);
            if (!AiEditorAgentPaths.IsSafeAssetPath(path, true, null)) throw new Exception("Only safe paths under Assets/ are allowed.");
            string absolute = AiEditorAgentPaths.ToAbsolutePathFromAssetPath(path);
            if (!File.Exists(absolute)) throw new Exception("File not found: " + path);

            int readLimit = args.maxChars <= 0 ? 200000 : Math.Min(args.maxChars, 200000);
            int chunkLength = args.length > 0 ? Math.Min(args.length, 32768) : (args.maxChars > 0 ? Math.Min(args.maxChars, 32768) : 4096);
            int offset = Math.Max(0, args.offset);

            string text = File.ReadAllText(absolute, Encoding.UTF8);
            bool truncatedByReadLimit = text.Length > readLimit;
            if (truncatedByReadLimit) text = text.Substring(0, readLimit);

            string summary = "{"
                + "\"path\":" + AiJson.Quote(path) + ","
                + "\"readLimit\":" + AiJson.Number(readLimit) + ","
                + "\"truncatedByReadLimit\":" + AiJson.Bool(truncatedByReadLimit)
                + "}";
            return AiResultResponseBuilder.BuildTextChunkResult("asset.read_text", text, summary, offset, chunkLength);
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
