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
            return AiToolRegistry.BuildManifestJson(true);
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
                    Send(context, 200, "{\"ok\":true,\"service\":\"AI Unity Editor Agent\",\"version\":" + AiJson.Quote(AiEditorAgentPaths.ServiceVersion) + ",\"serverRunning\":" + AiJson.Bool(IsRunning) + ",\"requiresToken\":" + AiJson.Bool(requireTokenCached) + "}");
                    return;
                }

                if (!IsAuthorized(context.Request))
                {
                    Send(context, 401, AiJson.Error("Unauthorized. Provide X-Unity-Ai-Token."));
                    return;
                }

                if (path == "manifest" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(delegate
                    {
                        return new AiApiResult(200, AiToolRegistry.BuildManifestJson(true));
                    }, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
                    return;
                }

                if (path == "agent" && context.Request.HttpMethod == "GET")
                {
                    AiApiResult result = EnqueueAndWait(ReadAgentManual, toolTimeoutMsCached);
                    Send(context, result.statusCode, result.json);
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
