#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    internal sealed class AiEndpointInfo
    {
        public string host;
        public int port;
        public string baseUrl;
        public string tokenPath;
        public string updatedAt;
    }

    internal static class AiEndpointRegistry
    {
        private const string DefaultHost = "127.0.0.1";
        private static AiEndpointInfo cached;

        public static AiEndpointInfo GetCurrentOrCreate()
        {
            if (IsWellFormed(cached))
            {
                return Clone(cached);
            }

            AiEditorAgentPaths.EnsureStateFolders();

            AiEndpointInfo loaded = LoadNoThrow();
            if (IsWellFormed(loaded))
            {
                cached = Normalize(loaded, false);
                Save(cached);
                return Clone(cached);
            }

            cached = CreateFresh();
            Save(cached);
            return Clone(cached);
        }

        public static AiEndpointInfo RefreshForNewPort()
        {
            AiEditorAgentPaths.EnsureStateFolders();
            cached = CreateFresh();
            Save(cached);
            return Clone(cached);
        }

        public static AiEndpointInfo ReadNoThrow()
        {
            try
            {
                return GetCurrentOrCreate();
            }
            catch
            {
                return null;
            }
        }

        public static string BuildEndpointJson(AiEndpointInfo info)
        {
            AiEndpointInfo normalized = Normalize(info, true);
            return JsonUtility.ToJson(normalized, true);
        }

        public static bool IsPortFree(int port)
        {
            if (port < 1024 || port > 65535) return false;

            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (listener != null) listener.Stop();
            }
        }

        private static AiEndpointInfo CreateFresh()
        {
            return Normalize(new AiEndpointInfo
            {
                host = DefaultHost,
                port = FindFreePort(),
                tokenPath = AiEditorAgentPaths.TokenPath,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            }, true);
        }

        private static int FindFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                IPEndPoint endpoint = listener.LocalEndpoint as IPEndPoint;
                return endpoint == null ? 18777 : endpoint.Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static AiEndpointInfo LoadNoThrow()
        {
            string path = AiEditorAgentPaths.EndpointPath;
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<AiEndpointInfo>(json);
        }

        private static void Save(AiEndpointInfo info)
        {
            AiEndpointInfo normalized = Normalize(info, true);
            File.WriteAllText(AiEditorAgentPaths.EndpointPath, JsonUtility.ToJson(normalized, true));
        }

        private static bool IsWellFormed(AiEndpointInfo info)
        {
            if (info == null) return false;
            if (string.IsNullOrEmpty(info.host)) return false;
            if (info.port < 1024 || info.port > 65535) return false;
            return true;
        }

        private static AiEndpointInfo Normalize(AiEndpointInfo info, bool ensureFreePort)
        {
            AiEndpointInfo normalized = info == null ? new AiEndpointInfo() : Clone(info);
            normalized.host = string.IsNullOrEmpty(normalized.host) ? DefaultHost : normalized.host.Trim();
            if (normalized.port < 1024 || normalized.port > 65535 || (ensureFreePort && !IsPortFree(normalized.port)))
            {
                normalized.port = FindFreePort();
            }
            normalized.baseUrl = "http://" + normalized.host + ":" + normalized.port;
            normalized.tokenPath = string.IsNullOrEmpty(normalized.tokenPath) ? AiEditorAgentPaths.TokenPath : normalized.tokenPath.Replace('\\', '/');
            normalized.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            return normalized;
        }

        private static AiEndpointInfo Clone(AiEndpointInfo info)
        {
            if (info == null) return null;
            return new AiEndpointInfo
            {
                host = info.host,
                port = info.port,
                baseUrl = info.baseUrl,
                tokenPath = info.tokenPath,
                updatedAt = info.updatedAt
            };
        }
    }
}
#endif
