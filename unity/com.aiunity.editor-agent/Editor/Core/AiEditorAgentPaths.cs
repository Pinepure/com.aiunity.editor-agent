#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    internal static class AiEditorAgentPaths
    {
        public const string FrameworkName = "AI Platform Agent Framework";
        public const string PackageName = "com.aiunity.editor-agent";
        public const string PlatformId = "unity";
        public const string ServiceVersion = "0.2.0";
        public const string ServiceId = "aiunity.editor-agent";
        public const string ProtocolVersion = "2.0";
        public const string PrimaryTokenHeader = "X-AI-Agent-Token";
        public const string LegacyTokenHeader = "X-Unity-Ai-Token";
        public const string GeneratedToolsFolder = "Assets/Editor/AiUnityEditorAgent/GeneratedTools";
        public const string LibraryFolderRelative = "Library/AiUnityEditorAgent";

        public static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/'); }
        }

        public static string LibraryFolderAbsolute
        {
            get { return Path.Combine(ProjectRoot, LibraryFolderRelative).Replace('\\', '/'); }
        }

        public static string TokenPath
        {
            get { return Path.Combine(LibraryFolderAbsolute, "token.txt").Replace('\\', '/'); }
        }

        public static string GeneratedToolsFolderAbsolute
        {
            get { return Path.Combine(ProjectRoot, GeneratedToolsFolder).Replace('\\', '/'); }
        }

        public static string AgentMdPath
        {
            get
            {
                try
                {
                    UnityPackageInfo info = UnityPackageInfo.FindForAssembly(typeof(AiEditorAgentPaths).Assembly);
                    if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                    {
                        string resolved = Path.Combine(info.resolvedPath, "AGENT.md").Replace('\\', '/');
                        if (File.Exists(resolved)) return resolved;
                    }
                }
                catch
                {
                }

                string packagePath = PackageInfoPath();
                if (!string.IsNullOrEmpty(packagePath))
                {
                    string p = Path.Combine(packagePath, "AGENT.md").Replace('\\', '/');
                    if (File.Exists(p)) return p;
                }

                string packagesPath = Path.Combine(ProjectRoot, "Packages", PackageName, "AGENT.md").Replace('\\', '/');
                if (File.Exists(packagesPath)) return packagesPath;

                return string.Empty;
            }
        }

        public static void EnsureGeneratedToolsFolder()
        {
            if (!Directory.Exists(GeneratedToolsFolderAbsolute))
            {
                Directory.CreateDirectory(GeneratedToolsFolderAbsolute);
            }
        }

        public static void EnsureLibraryFolder()
        {
            if (!Directory.Exists(LibraryFolderAbsolute))
            {
                Directory.CreateDirectory(LibraryFolderAbsolute);
            }
        }

        public static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;
            return assetPath.Replace('\\', '/').Trim();
        }

        public static bool IsSafeAssetPath(string assetPath, bool requireAssetsFolder, string requiredExtension)
        {
            string p = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(p)) return false;
            if (p.Contains("../") || p.Contains("/..") || p.StartsWith("/")) return false;
            if (p.IndexOf(':') >= 0) return false;
            if (requireAssetsFolder && !p.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(requiredExtension) && !p.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static string ToAbsolutePathFromAssetPath(string assetPath)
        {
            string p = NormalizeAssetPath(assetPath);
            return Path.Combine(ProjectRoot, p).Replace('\\', '/');
        }

        private static string PackageInfoPath()
        {
            try
            {
                // AssetDatabase can resolve package paths when the package is installed by Package Manager.
                string[] guids = AssetDatabase.FindAssets("AGENT t:TextAsset", new[] { "Packages/" + PackageName });
                if (guids != null && guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    if (!string.IsNullOrEmpty(path))
                    {
                        string absolute = Path.Combine(ProjectRoot, path).Replace('\\', '/');
                        return Directory.GetParent(absolute).FullName.Replace('\\', '/');
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
#endif
