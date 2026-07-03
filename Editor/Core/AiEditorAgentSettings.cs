#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;

namespace AiUnity.EditorAgent
{
    internal static class AiEditorAgentSettings
    {
        private const string KeyPrefix = "AiUnity.EditorAgent.";
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public const int WorksetTtlSeconds = 1800;
        public const int SnapshotTtlSeconds = 1800;
        public const int MaxBatchSteps = 24;

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
            get
            {
                AiEndpointInfo endpoint = AiEndpointRegistry.GetCurrentOrCreate();
                return endpoint == null ? 18777 : endpoint.port;
            }
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
            get
            {
                AiEndpointInfo endpoint = AiEndpointRegistry.GetCurrentOrCreate();
                return endpoint == null ? "http://127.0.0.1:18777" : endpoint.baseUrl;
            }
        }

        public static string Prefix
        {
            get { return ServerUrl + "/"; }
        }

        public static string EnsureToken()
        {
            AiEditorAgentPaths.EnsureStateFolders();
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
            AiEditorAgentPaths.EnsureStateFolders();
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

        public static string FormatTimestamp(DateTime value)
        {
            return value.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        public static bool TryParseTimestamp(string value, out DateTime result)
        {
            if (DateTime.TryParseExact(
                value ?? string.Empty,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out result))
            {
                return true;
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
        }

        public static bool IsTimestampExpired(string value, int ttlSeconds)
        {
            if (ttlSeconds <= 0) return false;
            DateTime parsed;
            if (!TryParseTimestamp(value, out parsed)) return false;
            return (DateTime.Now - parsed).TotalSeconds > ttlSeconds;
        }
    }
}
#endif
