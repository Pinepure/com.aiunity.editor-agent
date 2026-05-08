#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;

namespace AiUnity.EditorAgent
{
    internal sealed class AiApiResult
    {
        public int statusCode;
        public string json;

        public AiApiResult(int statusCode, string json)
        {
            this.statusCode = statusCode;
            this.json = json;
        }
    }

    [InitializeOnLoad]
    public static class AiEditorApiServer
    {
        [Serializable]
        private sealed class ManifestSearchArgs
        {
            public string query;
            public int limit;
            public string namespaceId;
            public string bundleId;
        }

        [Serializable]
        private sealed class DescribeManyArgs
        {
            public string[] ids;
        }

        private static readonly object ServerLock = new object();
        private static readonly Queue<MainThreadJob> Jobs = new Queue<MainThreadJob>();
        private static HttpListener listener;
        private static Thread listenerThread;
        private static bool updateHooked;
        private static bool initialized;
        private static string currentToken;
        private static string lastError = string.Empty;
        private static bool requireTokenCached = true;
        private static int toolTimeoutMsCached = 60000;

        private sealed class MainThreadJob
        {
            public Func<AiApiResult> work;
            public AiApiResult result;
            public Exception exception;
            public ManualResetEvent done = new ManualResetEvent(false);
        }

        public static bool IsRunning
        {
            get { return listener != null && listener.IsListening; }
        }

        public static string LastError
        {
            get { return lastError; }
        }

        public static string Token
        {
            get
            {
                if (string.IsNullOrEmpty(currentToken)) currentToken = AiEditorAgentSettings.ReadTokenNoThrow();
                return currentToken;
            }
        }

        static AiEditorApiServer()
        {
            Initialize();
        }

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            AiEditorAgentState.EnsureSubscribed();
            AiToolRegistry.Rebuild();
            RefreshSettingsCache();
            HookUpdate();

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;

            if (AiEditorAgentSettings.AutoStart)
            {
                Start();
            }
        }

        public static bool Start()
        {
            lock (ServerLock)
            {
                if (IsRunning) return true;

                try
                {
                    RefreshSettingsCache();
                    if (!HttpListener.IsSupported)
                    {
                        lastError = "HttpListener is not supported on this platform.";
                        AiEditorAgentState.Error(lastError);
                        return false;
                    }

                    currentToken = AiEditorAgentSettings.EnsureToken();
                    listener = new HttpListener();
                    listener.Prefixes.Add(AiEditorAgentSettings.Prefix);
                    listener.Start();

                    listenerThread = new Thread(ListenLoop);
                    listenerThread.IsBackground = true;
                    listenerThread.Name = "AI Unity Editor Agent HTTP Listener";
                    listenerThread.Start();

                    lastError = string.Empty;
                    AiEditorAgentState.Info("Service started at " + AiEditorAgentSettings.Prefix);
                    return true;
                }
                catch (Exception e)
                {
                    lastError = e.Message;
                    AiEditorAgentState.Error("Failed to start service: " + e.Message);
                    SafeCloseListener();
                    return false;
                }
            }
        }

        public static void Stop()
        {
            lock (ServerLock)
            {
                SafeCloseListener();
                AiEditorAgentState.Log("info", "Service stopped.");
            }
        }

        public static bool Restart()
        {
            Stop();
            return Start();
        }

        public static void RebuildRegistry()
        {
            AiToolRegistry.Rebuild();
        }

        public static string BuildManifestJson()
        {
            return AiToolRegistry.BuildManifestFullJson(false, false);
        }

        public static string RegenerateToken()
        {
            RefreshSettingsCache();
            currentToken = AiEditorAgentSettings.RegenerateToken();
            AiEditorAgentState.Info("Token regenerated.");
            return currentToken;
        }

        private static void RefreshSettingsCache()
        {
            requireTokenCached = AiEditorAgentSettings.RequireToken;
            toolTimeoutMsCached = AiEditorAgentSettings.ToolTimeoutMs;
        }

        private static void HookUpdate()
        {
            if (updateHooked) return;
            updateHooked = true;
            EditorApplication.update += Pump;
        }

        private static void OnBeforeAssemblyReload()
        {
            Stop();
            AiEditorAgentState.Dispose();
        }

        private static void OnEditorQuitting()
        {
            Stop();
            AiEditorAgentState.Dispose();
        }

        private static void SafeCloseListener()
        {
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                }
            }
            catch
            {
            }
            listener = null;
            listenerThread = null;
        }

        private static void ListenLoop()
        {
            while (listener != null && listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(delegate { HandleContext(context); });
                }
                catch
                {
                    // Expected when the listener is stopped during reload or quit.
                }
            }
        }

        private static void HandleContext(HttpListenerContext context)
        {
            try
            {
                AddCommonHeaders(context.Response);

                if (context.Request.HttpMethod == "OPTIONS")
                {
                    Send(context, 204, "{}");
                    return;
                }

                string path = context.Request.Url.AbsolutePath.Trim('/');
                if (string.IsNullOrEmpty(path)) path = "health";

                if (path == "health" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(BuildHealthResult, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (!IsAuthorized(context.Request))
                {
                    Send(context, 401, AiJson.Error("Unauthorized. Provide X-Unity-Ai-Token."));
                    return;
                }

                if ((path == "manifest" || path == "manifest/summary") && context.Request.HttpMethod == "GET")
                {
                    string detail = context.Request.QueryString["detail"];
                    AiApiResult result = EnqueueAndWait(
                        string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase) ? BuildManifestFullResult : BuildManifestSummaryResult,
                        toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "manifest/full" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(BuildManifestFullResult, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "manifest/bundles" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(BuildManifestBundlesResult, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path.StartsWith("manifest/bundle/", StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
                {
                    string bundleId = Uri.UnescapeDataString(path.Substring("manifest/bundle/".Length));
                    AiApiResult result = EnqueueAndWait(delegate { return BuildManifestBundleResult(bundleId); }, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "manifest/search" && context.Request.HttpMethod == "POST")
                {
                    string body = ReadBody(context.Request);
                    AiApiResult result = EnqueueAndWait(delegate { return BuildManifestSearchResult(body); }, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "tool/describe_many" && context.Request.HttpMethod == "POST")
                {
                    string body = ReadBody(context.Request);
                    AiApiResult result = EnqueueAndWait(delegate { return BuildToolDescribeManyResult(body); }, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "agent" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(ReadAgentManual, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "agent/brief" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(BuildAgentBriefResult, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path.StartsWith("result/", StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
                {
                    string handleId = Uri.UnescapeDataString(path.Substring("result/".Length));
                    int offset = ReadQueryInt(context.Request, "offset", 0);
                    int limit = ReadQueryInt(context.Request, "limit", 0);
                    int statusCode;
                    string json;
                    AiResultHandleStore.TryBuildPageJson(handleId, offset, limit, out statusCode, out json);
                    Send(context, statusCode, json);
                    return;
                }

                if (path.StartsWith("call/", StringComparison.Ordinal) && context.Request.HttpMethod == "POST")
                {
                    string toolId = Uri.UnescapeDataString(path.Substring("call/".Length));
                    string body = ReadBody(context.Request);
                    if (string.IsNullOrWhiteSpace(body)) body = "{}";

                    AiApiResult result = EnqueueAndWait(delegate
                    {
                        return InvokeTool(toolId, body);
                    }, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                Send(context, 404, AiJson.Error("Not found: " + path));
            }
            catch (Exception e)
            {
                try
                {
                    Send(context, 500, AiJson.Error(e.Message));
                }
                catch
                {
                }
            }
        }

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            if (!requireTokenCached) return true;
            string expected = Token;
            if (string.IsNullOrEmpty(expected)) return false;
            string provided = request.Headers["X-Unity-Ai-Token"];
            return string.Equals(provided, expected, StringComparison.Ordinal);
        }

        private static AiApiResult EnqueueAndWait(Func<AiApiResult> work, int timeoutMs)
        {
            MainThreadJob job = new MainThreadJob { work = work };
            lock (Jobs)
            {
                Jobs.Enqueue(job);
            }

            if (!job.done.WaitOne(timeoutMs))
            {
                return new AiApiResult(504, AiJson.Error("Timed out waiting for Unity Editor main thread."));
            }

            if (job.exception != null)
            {
                return new AiApiResult(500, AiJson.Error(job.exception.Message));
            }

            return job.result ?? new AiApiResult(500, AiJson.Error("No result."));
        }

        private static void Pump()
        {
            int guard = 0;
            while (guard < 100)
            {
                guard++;
                MainThreadJob job = null;
                lock (Jobs)
                {
                    if (Jobs.Count > 0) job = Jobs.Dequeue();
                }

                if (job == null) break;

                try
                {
                    job.result = job.work();
                }
                catch (Exception e)
                {
                    job.exception = e;
                }
                finally
                {
                    job.done.Set();
                }
            }
        }

        private static AiApiResult BuildHealthResult()
        {
            return new AiApiResult(200, AiProtocolUtility.BuildHealthJson(true, IsRunning, requireTokenCached));
        }

        private static AiApiResult BuildManifestSummaryResult()
        {
            return new AiApiResult(200, AiToolRegistry.BuildManifestSummaryJson(false, false));
        }

        private static AiApiResult BuildManifestFullResult()
        {
            return new AiApiResult(200, AiToolRegistry.BuildManifestFullJson(false, false));
        }

        private static AiApiResult BuildManifestBundlesResult()
        {
            return new AiApiResult(200, AiToolRegistry.BuildManifestBundleIndexJson(false, false));
        }

        private static AiApiResult BuildManifestBundleResult(string bundleId)
        {
            string json;
            if (!AiToolRegistry.TryBuildManifestBundleJson(bundleId, false, false, out json))
            {
                return new AiApiResult(404, AiJson.Error("Unknown manifest bundle: " + (bundleId ?? string.Empty)));
            }
            return new AiApiResult(200, json);
        }

        private static AiApiResult BuildManifestSearchResult(string body)
        {
            ManifestSearchArgs args = AiJson.FromJsonOrThrow<ManifestSearchArgs>(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (args != null && !string.IsNullOrEmpty(args.bundleId) && !AiToolRegistry.HasBundle(args.bundleId))
            {
                return new AiApiResult(404, AiJson.Error("Unknown manifest bundle: " + args.bundleId));
            }
            return new AiApiResult(200, AiToolRegistry.BuildManifestSearchJson(
                args == null ? string.Empty : args.query,
                args == null ? 0 : args.limit,
                args == null ? string.Empty : args.namespaceId,
                args == null ? string.Empty : args.bundleId,
                false));
        }

        private static AiApiResult BuildToolDescribeManyResult(string body)
        {
            DescribeManyArgs args = AiJson.FromJsonOrThrow<DescribeManyArgs>(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            return new AiApiResult(200, AiToolRegistry.BuildToolDescribeManyJson(args == null ? null : args.ids, false, false));
        }

        private static AiApiResult BuildAgentBriefResult()
        {
            return new AiApiResult(200, AiProtocolUtility.BuildAgentBriefJson(true));
        }

        private static AiApiResult InvokeTool(string toolId, string argsJson)
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool ok = false;
            string message = string.Empty;

            try
            {
                AiToolEntry entry;
                if (!AiToolRegistry.TryGet(toolId, out entry))
                {
                    AiToolRegistry.Rebuild();
                    if (!AiToolRegistry.TryGet(toolId, out entry))
                    {
                        message = "Unknown tool: " + toolId;
                        AiEditorAgentState.RecordCall(toolId, false, sw.ElapsedMilliseconds, message);
                        return new AiApiResult(404, "{\"ok\":false,\"toolId\":" + AiJson.Quote(toolId) + ",\"error\":" + AiJson.Quote(message) + "}");
                    }
                }

                bool shouldConfirm = AiEditorAgentSettings.ShouldConfirmTool(entry.info);

                if (shouldConfirm)
                {
                    bool allowed = EditorUtility.DisplayDialog(
                        "AI Editor Agent Confirmation",
                        "AI wants to execute a tool.\n\nTool: " + toolId + "\nDanger: " + entry.info.danger + "\n\nArguments:\n" + argsJson,
                        "Allow",
                        "Deny");
                    if (!allowed)
                    {
                        message = "User denied execution.";
                        AiEditorAgentState.RecordCall(toolId, false, sw.ElapsedMilliseconds, message);
                        return new AiApiResult(403, "{\"ok\":false,\"toolId\":" + AiJson.Quote(toolId) + ",\"error\":" + AiJson.Quote(message) + "}");
                    }
                }

                string toolResultJson = (string)entry.method.Invoke(null, new object[] { argsJson });
                ok = true;
                message = "ok";
                string json = "{\"ok\":true,\"toolId\":" + AiJson.Quote(toolId) + ",\"durationMs\":" + AiJson.Number(sw.ElapsedMilliseconds) + ",\"result\":" + AiJson.AsJsonValue(toolResultJson) + "}";
                AiEditorAgentState.RecordCall(toolId, ok, sw.ElapsedMilliseconds, message);
                return new AiApiResult(200, json);
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                message = inner.Message;
                AiEditorAgentState.RecordCall(toolId, false, sw.ElapsedMilliseconds, message);
                return new AiApiResult(500, "{\"ok\":false,\"toolId\":" + AiJson.Quote(toolId) + ",\"durationMs\":" + AiJson.Number(sw.ElapsedMilliseconds) + ",\"error\":" + AiJson.Quote(message) + "}");
            }
            catch (Exception e)
            {
                message = e.Message;
                AiEditorAgentState.RecordCall(toolId, false, sw.ElapsedMilliseconds, message);
                return new AiApiResult(500, "{\"ok\":false,\"toolId\":" + AiJson.Quote(toolId) + ",\"durationMs\":" + AiJson.Number(sw.ElapsedMilliseconds) + ",\"error\":" + AiJson.Quote(message) + "}");
            }
        }

        private static AiApiResult ReadAgentManual()
        {
            try
            {
                string path = AiEditorAgentPaths.AgentMdPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return new AiApiResult(404, AiJson.Error("AGENT.md was not found in the package."));
                }
                string text = File.ReadAllText(path, Encoding.UTF8);
                return new AiApiResult(200, "{\"ok\":true,\"path\":" + AiJson.Quote(path) + ",\"content\":" + AiJson.Quote(text) + "}");
            }
            catch (Exception e)
            {
                return new AiApiResult(500, AiJson.Error(e.Message));
            }
        }

        private static int ReadQueryInt(HttpListenerRequest request, string key, int defaultValue)
        {
            string raw = request.QueryString[key];
            int value;
            return int.TryParse(raw, out value) ? value : defaultValue;
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            Encoding encoding = request.ContentEncoding ?? Encoding.UTF8;
            using (StreamReader reader = new StreamReader(request.InputStream, encoding))
            {
                return reader.ReadToEnd();
            }
        }

        private static void Send(HttpListenerContext context, int statusCode, string json)
        {
            if (json == null) json = "{}";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private static void AddCommonHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "http://127.0.0.1";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Unity-Ai-Token";
            response.Headers["Cache-Control"] = "no-store";
        }
    }
}
#endif
