#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace AiUnity.EditorAgent
{
    internal static class AiEditorAgentSettings
    {
        private const string KeyPrefix = "AiUnity.EditorAgent.";
        private const int DefaultPort = 18777;

        public static bool AutoStart
        {
            get { return EditorPrefs.GetBool(KeyPrefix + "AutoStart", true); }
            set { EditorPrefs.SetBool(KeyPrefix + "AutoStart", value); }
        }

        public static bool RequireToken
        {
            get { return EditorPrefs.GetBool(KeyPrefix + "RequireToken", true); }
            set { EditorPrefs.SetBool(KeyPrefix + "RequireToken", value); }
        }

        public static int Port
        {
            get { return Math.Max(1024, Math.Min(65535, EditorPrefs.GetInt(KeyPrefix + "Port", DefaultPort))); }
            set { EditorPrefs.SetInt(KeyPrefix + "Port", Math.Max(1024, Math.Min(65535, value))); }
        }

        public static int ToolTimeoutMs
        {
            get { return Math.Max(1000, EditorPrefs.GetInt(KeyPrefix + "ToolTimeoutMs", 60000)); }
            set { EditorPrefs.SetInt(KeyPrefix + "ToolTimeoutMs", Math.Max(1000, value)); }
        }

        public static bool ConfirmHighRiskTools
        {
            get { return EditorPrefs.GetBool(KeyPrefix + "ConfirmHighRiskTools", true); }
            set { EditorPrefs.SetBool(KeyPrefix + "ConfirmHighRiskTools", value); }
        }

        public static bool FullAccessEnabled
        {
            get { return EditorPrefs.GetBool(KeyPrefix + "FullAccessEnabled", false); }
            set { EditorPrefs.SetBool(KeyPrefix + "FullAccessEnabled", value); }
        }

        public static string ServerUrl
        {
            get { return "http://127.0.0.1:" + Port; }
        }

        public static string Prefix
        {
            get { return ServerUrl + "/"; }
        }

        public static string EnsureToken()
        {
            AiEditorAgentPaths.EnsureLibraryFolder();
            string tokenPath = AiEditorAgentPaths.TokenPath;
            if (File.Exists(tokenPath))
            {
                string existing = File.ReadAllText(tokenPath).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }

            string token = Guid.NewGuid().ToString("N");
            File.WriteAllText(tokenPath, token);
            return token;
        }

        public static string RegenerateToken()
        {
            AiEditorAgentPaths.EnsureLibraryFolder();
            string token = Guid.NewGuid().ToString("N");
            File.WriteAllText(AiEditorAgentPaths.TokenPath, token);
            return token;
        }

        public static string ReadTokenNoThrow()
        {
            try
            {
                return EnsureToken();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool ShouldConfirmTool(AiToolInfo info)
        {
            if (FullAccessEnabled) return false;
            if (info == null) return false;
            if (info.requiresConfirmation) return true;
            return info.danger == "high" && ConfirmHighRiskTools;
        }
    }
}
#endif
