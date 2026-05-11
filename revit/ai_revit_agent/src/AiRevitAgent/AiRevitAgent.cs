using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiPlatform.RevitAgent;

public sealed class AiRevitAgentApplication : IExternalApplication
{
    private AiRevitHttpService? _service;
    private RevitExternalEventBridge? _bridge;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            _bridge = new RevitExternalEventBridge();
            _service = new AiRevitHttpService(_bridge, application);
            _service.Start();
            return Result.Succeeded;
        }
        catch
        {
            _service?.Dispose();
            _bridge?.Dispose();
            _service = null;
            _bridge = null;
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _service?.Dispose();
        _bridge?.Dispose();
        _service = null;
        _bridge = null;
        return Result.Succeeded;
    }
}

internal sealed class AiRevitConfig
{
    public const string FrameworkName = "AI Platform Agent Framework";
    public const string ProtocolVersion = "2.0";
    public const string ServiceId = "airevit.agent";
    public const string ServiceName = "AI Revit Agent";
    public const string PlatformId = "revit";
    public const string PrimaryTokenHeader = "X-AI-Agent-Token";
    public const string LegacyTokenHeader = "X-Revit-Ai-Token";

    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 19793;
    public bool RequireToken { get; init; } = true;
    public bool FullAccessEnabled { get; init; } = false;
    public int ToolTimeoutMs { get; init; } = 120000;
    public string ServiceVersion { get; init; } = "0.1.0";
    public string StateDirectoryPath { get; init; } = "";
    public string TokenFilePath { get; init; } = "";
    public string ConfigFilePath { get; init; } = "";

    public string ServerUrl => $"http://{Host}:{Port}";
    public IReadOnlyList<string> AcceptedTokenHeaders => new[] { PrimaryTokenHeader, LegacyTokenHeader };

    public static AiRevitConfig Load()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "AIPlatformAgent", "revit");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.json");
        ConfigFile? file = null;
        if (File.Exists(path))
        {
            file = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(path));
        }

        return new AiRevitConfig
        {
            Host = string.IsNullOrWhiteSpace(file?.Host) ? "127.0.0.1" : file!.Host!,
            Port = file?.Port is > 0 ? file.Port.Value : 19793,
            RequireToken = file?.RequireToken ?? true,
            FullAccessEnabled = file?.FullAccessEnabled ?? false,
            ToolTimeoutMs = file?.ToolTimeoutMs is > 0 ? file.ToolTimeoutMs.Value : 120000,
            StateDirectoryPath = root,
            ConfigFilePath = path,
            TokenFilePath = Path.Combine(root, "token.txt"),
        };
    }

    private sealed class ConfigFile
    {
        public string? Host { get; init; }
        public int? Port { get; init; }
        public bool? RequireToken { get; init; }
        public bool? FullAccessEnabled { get; init; }
        public int? ToolTimeoutMs { get; init; }
    }
}

internal sealed class RevitExternalEventBridge : IExternalEventHandler, IDisposable
{
    private sealed class WorkItem
    {
        public required Func<UIApplication, JToken?> Action { get; init; }
        public required TaskCompletionSource<JToken?> Completion { get; init; }
    }

    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly ExternalEvent _externalEvent;

    public RevitExternalEventBridge()
    {
        _externalEvent = ExternalEvent.Create(this);
    }

    public Task<JToken?> InvokeAsync(Func<UIApplication, JToken?> action)
    {
        var completion = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new WorkItem
        {
            Action = action,
            Completion = completion,
        });
        _externalEvent.Raise();
        return completion.Task;
    }

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var workItem))
        {
            try
            {
                workItem.Completion.TrySetResult(workItem.Action(app));
            }
            catch (Exception ex)
            {
                workItem.Completion.TrySetException(ex);
            }
        }
    }

    public string GetName() => "AI Revit Agent External Event Bridge";

    public void Dispose()
    {
        _externalEvent.Dispose();
    }
}

internal sealed class AiRevitRuntimeState
{
    private readonly object _gate = new();
    private readonly Queue<JObject> _logs = new();
    private readonly Queue<JObject> _calls = new();

    public void Log(string level, string message)
    {
        lock (_gate)
        {
            _logs.Enqueue(new JObject
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["level"] = level,
                ["message"] = message,
            });
            Trim(_logs, 400);
        }
    }

    public void RecordCall(string toolId, bool ok, long durationMs, string message)
    {
        lock (_gate)
        {
            _calls.Enqueue(new JObject
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["toolId"] = toolId,
                ["ok"] = ok,
                ["durationMs"] = durationMs,
                ["message"] = message,
            });
            Trim(_calls, 400);
        }
    }

    public JArray RecentLogs(int maxEntries)
    {
        lock (_gate)
        {
            return new JArray(LastEntries(_logs, Clamp(maxEntries, 1, 300)));
        }
    }

    public JArray RecentCalls(int maxEntries)
    {
        lock (_gate)
        {
            return new JArray(LastEntries(_calls, Clamp(maxEntries, 1, 300)));
        }
    }

    private static IEnumerable<T> LastEntries<T>(Queue<T> queue, int count)
    {
        var skip = Math.Max(0, queue.Count - count);
        return queue.Skip(skip).ToArray();
    }

    private static void Trim<T>(Queue<T> queue, int maxCount)
    {
        while (queue.Count > maxCount)
        {
            queue.Dequeue();
        }
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}

internal sealed class AiRevitResultHandleStore
{
    private sealed class Entry
    {
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string SourceToolId { get; init; } = "";
        public string FieldName { get; init; } = "";
        public string CreatedAt { get; init; } = "";
        public JObject Summary { get; init; } = new();
        public JArray Items { get; init; } = new();
        public string Text { get; init; } = "";
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Queue<string> _order = new();
    private readonly int _maxHandles;

    public AiRevitResultHandleStore(int maxHandles = 96)
    {
        _maxHandles = maxHandles;
    }

    public JObject BuildItemsResult(string sourceToolId, string fieldName, JArray items, JObject summary, int pageSize)
    {
        var safePageSize = Clamp(pageSize, 1, 200);
        var returned = Math.Min(safePageSize, items.Count);
        var result = new JObject
        {
            ["summary"] = summary,
            ["returned"] = returned,
            ["pageSize"] = safePageSize,
            ["total"] = items.Count,
            ["hasMore"] = returned < items.Count,
            [fieldName] = new JArray(items.Take(returned)),
        };

        if (returned < items.Count)
        {
            result["resultHandle"] = Add(new Entry
            {
                Id = NewHandleId(),
                Kind = "items",
                SourceToolId = sourceToolId,
                FieldName = fieldName,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Summary = summary,
                Items = items,
            });
        }

        return result;
    }

    public JObject BuildPage(string handleId, int offset, int limit)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(handleId, out var entry))
            {
                return new JObject { ["ok"] = false, ["error"] = $"Unknown result handle: {handleId}" };
            }

            var safeOffset = Clamp(offset, 0, entry.Items.Count);
            var safeLimit = Clamp(limit == 0 ? 20 : limit, 1, 200);
            var count = Math.Min(safeLimit, entry.Items.Count - safeOffset);
            return new JObject
            {
                ["ok"] = true,
                ["handleId"] = handleId,
                ["kind"] = "items",
                ["sourceToolId"] = entry.SourceToolId,
                ["fieldName"] = entry.FieldName,
                ["createdAt"] = entry.CreatedAt,
                ["offset"] = safeOffset,
                ["limit"] = safeLimit,
                ["count"] = count,
                ["total"] = entry.Items.Count,
                ["hasMore"] = safeOffset + count < entry.Items.Count,
                ["summary"] = entry.Summary,
                [entry.FieldName] = new JArray(entry.Items.Skip(safeOffset).Take(count)),
            };
        }
    }

    private string Add(Entry entry)
    {
        lock (_gate)
        {
            _entries[entry.Id] = entry;
            _order.Enqueue(entry.Id);
            while (_order.Count > _maxHandles)
            {
                var oldest = _order.Dequeue();
                _entries.Remove(oldest);
            }

            return entry.Id;
        }
    }

    private static string NewHandleId() => $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}{Guid.NewGuid():N}".Substring(0, 24);
    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}

internal sealed class AiRevitTool
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required JObject ArgsSchema { get; init; }
    public required JObject ReturnSchema { get; init; }
    public required string Danger { get; init; }
    public required bool RequiresConfirmation { get; init; }
    public required string HandlerName { get; init; }
    public required Func<JObject, Task<JToken?>> Handler { get; init; }
    public string NamespaceId => Id.Split('.')[0];

    public JObject ToSummaryJson() => new()
    {
        ["id"] = Id,
        ["namespaceId"] = NamespaceId,
        ["description"] = Description,
        ["danger"] = Danger,
        ["requiresConfirmation"] = RequiresConfirmation,
    };

    public JObject ToFullJson() => new()
    {
        ["id"] = Id,
        ["namespaceId"] = NamespaceId,
        ["description"] = Description,
        ["argsSchemaJson"] = ArgsSchema.ToString(Formatting.None),
        ["returnSchemaJson"] = ReturnSchema.ToString(Formatting.None),
        ["danger"] = Danger,
        ["requiresConfirmation"] = RequiresConfirmation,
        ["handlerName"] = HandlerName,
    };
}

internal sealed class AiRevitBundle
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Prefixes { get; init; }
}

internal sealed class AiRevitHttpService : IDisposable
{
    private readonly RevitExternalEventBridge _bridge;
    private readonly UIControlledApplication _application;
    private readonly AiRevitConfig _config;
    private readonly AiRevitRuntimeState _runtimeState = new();
    private readonly AiRevitResultHandleStore _resultHandleStore = new();
    private readonly Dictionary<string, AiRevitTool> _tools = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<AiRevitBundle> _bundles;
    private readonly string _agentManual;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private string _token = "";

    public AiRevitHttpService(RevitExternalEventBridge bridge, UIControlledApplication application)
    {
        _bridge = bridge;
        _application = application;
        _config = AiRevitConfig.Load();
        _bundles = new[]
        {
            new AiRevitBundle { Id = "service", Description = "Adapter health, logs, config, and recent calls.", Prefixes = new[] { "service." } },
            new AiRevitBundle { Id = "revit.application", Description = "Revit application, document, view, element, and selection tools.", Prefixes = new[] { "revit." } },
        };
        _agentManual = "Use Revit document, view, element, selection, and parameter tools through the shared discovery-first protocol instead of inferring model state from exports or screenshots.";
        RegisterTools();
    }

    public void Start()
    {
        if (_config.RequireToken)
        {
            _token = EnsureToken();
        }

        _listener.Prefixes.Add($"{_config.ServerUrl}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _runtimeState.Log("info", $"Service started at {_config.ServerUrl}");
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }

            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore shutdown exceptions.
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            if (context is not null)
            {
                _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        AddCommonHeaders(context.Response);
        if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        try
        {
            var path = context.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
            if (path == "health" && context.Request.HttpMethod == "GET")
            {
                await SendJsonAsync(context.Response, 200, BuildHealthPayload(includeOk: true)).ConfigureAwait(false);
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                await SendJsonAsync(context.Response, 401, new JObject
                {
                    ["ok"] = false,
                    ["error"] = $"Unauthorized. Provide {AiRevitConfig.PrimaryTokenHeader} or {AiRevitConfig.LegacyTokenHeader}.",
                }).ConfigureAwait(false);
                return;
            }

            switch (path, context.Request.HttpMethod.ToUpperInvariant())
            {
                case ("manifest", "GET"):
                case ("manifest/summary", "GET"):
                    await SendJsonAsync(context.Response, 200, BuildManifestSummary()).ConfigureAwait(false);
                    return;
                case ("manifest/full", "GET"):
                    await SendJsonAsync(context.Response, 200, BuildManifestFull()).ConfigureAwait(false);
                    return;
                case ("manifest/bundles", "GET"):
                    await SendJsonAsync(context.Response, 200, BuildBundleIndex()).ConfigureAwait(false);
                    return;
                case ("manifest/search", "POST"):
                    await SendJsonAsync(context.Response, 200, BuildManifestSearch(await ReadBodyAsync(context.Request).ConfigureAwait(false))).ConfigureAwait(false);
                    return;
                case ("tool/describe_many", "POST"):
                    await SendJsonAsync(context.Response, 200, BuildDescribeMany(await ReadBodyAsync(context.Request).ConfigureAwait(false))).ConfigureAwait(false);
                    return;
                case ("agent/brief", "GET"):
                    await SendJsonAsync(context.Response, 200, BuildAgentBriefPayload(includeOk: true)).ConfigureAwait(false);
                    return;
                case ("agent", "GET"):
                    await SendJsonAsync(context.Response, 200, new JObject { ["ok"] = true, ["content"] = _agentManual }).ConfigureAwait(false);
                    return;
            }

            if (path.StartsWith("manifest/bundle/", StringComparison.Ordinal))
            {
                var bundle = BuildBundle(path.Substring("manifest/bundle/".Length));
                await SendJsonAsync(context.Response, bundle is null ? 404 : 200, bundle ?? new JObject { ["ok"] = false, ["error"] = "Unknown manifest bundle." }).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("result/", StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
            {
                var handleId = path.Substring("result/".Length);
                var offset = ParseInt(context.Request.QueryString["offset"], 0);
                var limit = ParseInt(context.Request.QueryString["limit"], 0);
                await SendJsonAsync(context.Response, 200, _resultHandleStore.BuildPage(handleId, offset, limit)).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("call/", StringComparison.Ordinal) && context.Request.HttpMethod == "POST")
            {
                var toolId = path.Substring("call/".Length);
                if (!_tools.TryGetValue(toolId, out var tool))
                {
                    await SendJsonAsync(context.Response, 404, new JObject
                    {
                        ["ok"] = false,
                        ["toolId"] = toolId,
                        ["error"] = $"Unknown tool: {toolId}",
                    }).ConfigureAwait(false);
                    return;
                }

                if (tool.RequiresConfirmation && !_config.FullAccessEnabled)
                {
                    await SendJsonAsync(context.Response, 403, new JObject
                    {
                        ["ok"] = false,
                        ["toolId"] = toolId,
                        ["error"] = "High-risk tool requires full access.",
                    }).ConfigureAwait(false);
                    return;
                }

                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                var startedAt = Environment.TickCount64;
                try
                {
                    var result = await tool.Handler(body).ConfigureAwait(false);
                    var duration = Environment.TickCount64 - startedAt;
                    _runtimeState.RecordCall(toolId, true, duration, "ok");
                    await SendJsonAsync(context.Response, 200, new JObject
                    {
                        ["ok"] = true,
                        ["toolId"] = toolId,
                        ["durationMs"] = duration,
                        ["result"] = result ?? JValue.CreateNull(),
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var duration = Environment.TickCount64 - startedAt;
                    _runtimeState.RecordCall(toolId, false, duration, ex.Message);
                    await SendJsonAsync(context.Response, 500, new JObject
                    {
                        ["ok"] = false,
                        ["toolId"] = toolId,
                        ["durationMs"] = duration,
                        ["error"] = ex.ToString(),
                    }).ConfigureAwait(false);
                }

                return;
            }

            await SendJsonAsync(context.Response, 404, new JObject { ["ok"] = false, ["error"] = $"Not found: {path}" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _runtimeState.Log("error", ex.ToString());
            await SendJsonAsync(context.Response, 500, new JObject { ["ok"] = false, ["error"] = ex.ToString() }).ConfigureAwait(false);
        }
    }

    private void RegisterTools()
    {
        Register(new AiRevitTool
        {
            Id = "service.health_get",
            Description = "Return the adapter health payload.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "service.health_get",
            Handler = _ => Task.FromResult<JToken?>(BuildHealthPayload(includeOk: true)),
        });

        Register(new AiRevitTool
        {
            Id = "service.agent_brief_get",
            Description = "Return the concise operating brief for this adapter.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "service.agent_brief_get",
            Handler = _ => Task.FromResult<JToken?>(BuildAgentBriefPayload(includeOk: true)),
        });

        Register(new AiRevitTool
        {
            Id = "service.config_get",
            Description = "Return effective adapter configuration.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "service.config_get",
            Handler = _ => Task.FromResult<JToken?>(new JObject
            {
                ["stateDirectoryPath"] = _config.StateDirectoryPath,
                ["configFilePath"] = _config.ConfigFilePath,
                ["requireToken"] = _config.RequireToken,
                ["fullAccessEnabled"] = _config.FullAccessEnabled,
                ["acceptedTokenHeaders"] = new JArray(_config.AcceptedTokenHeaders),
            }),
        });

        Register(new AiRevitTool
        {
            Id = "service.logs_get",
            Description = "List recent service logs.",
            ArgsSchema = SchemaObject(("maxEntries", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 300 })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "service.logs_get",
            Handler = args =>
            {
                var maxEntries = ParseInt(args["maxEntries"], 100);
                return Task.FromResult<JToken?>(_resultHandleStore.BuildItemsResult("service.logs_get", "logs", _runtimeState.RecentLogs(maxEntries), new JObject { ["kind"] = "logs" }, maxEntries));
            },
        });

        Register(new AiRevitTool
        {
            Id = "service.recent_tool_calls_get",
            Description = "List recent tool call outcomes recorded by the adapter.",
            ArgsSchema = SchemaObject(("maxEntries", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 300 })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "service.recent_tool_calls_get",
            Handler = args =>
            {
                var maxEntries = ParseInt(args["maxEntries"], 100);
                return Task.FromResult<JToken?>(_resultHandleStore.BuildItemsResult("service.recent_tool_calls_get", "calls", _runtimeState.RecentCalls(maxEntries), new JObject { ["kind"] = "toolCalls" }, maxEntries));
            },
        });

        Register(new AiRevitTool
        {
            Id = "service.token_regenerate",
            Description = "Regenerate the adapter token and return the new token value.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "medium",
            RequiresConfirmation = true,
            HandlerName = "service.token_regenerate",
            Handler = _ => Task.FromResult<JToken?>(new JObject
            {
                ["token"] = RegenerateToken(),
                ["acceptedTokenHeaders"] = new JArray(_config.AcceptedTokenHeaders),
            }),
        });

        Register(new AiRevitTool
        {
            Id = "revit.application_summary_get",
            Description = "Return summary information for the running Revit host.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.application_summary_get",
            Handler = async _ => await _bridge.InvokeAsync(uiapp => new JObject
            {
                ["versionName"] = uiapp.Application.VersionName,
                ["versionNumber"] = uiapp.Application.VersionNumber,
                ["versionBuild"] = uiapp.Application.VersionBuild,
                ["language"] = uiapp.Application.Language.ToString(),
                ["openDocumentCount"] = uiapp.Application.Documents.Cast<Document>().Count(),
                ["hasActiveDocument"] = uiapp.ActiveUIDocument is not null,
            }).ConfigureAwait(false),
        });

        Register(new AiRevitTool
        {
            Id = "revit.documents_list",
            Description = "List open Revit documents.",
            ArgsSchema = SchemaObject(("pageSize", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.documents_list",
            Handler = async args =>
            {
                var pageSize = ParseInt(args["pageSize"], 100);
                var items = await _bridge.InvokeAsync(uiapp => new JArray(uiapp.Application.Documents.Cast<Document>().Select(ToDocumentSummary))).ConfigureAwait(false) as JArray ?? new JArray();
                return _resultHandleStore.BuildItemsResult("revit.documents_list", "documents", items, new JObject { ["kind"] = "documents" }, pageSize);
            },
        });

        Register(new AiRevitTool
        {
            Id = "revit.active_document_summary_get",
            Description = "Return summary information for the active Revit document.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.active_document_summary_get",
            Handler = async _ => await _bridge.InvokeAsync(uiapp => ToDocumentSummary(RequireActiveDocument(uiapp))).ConfigureAwait(false),
        });

        Register(new AiRevitTool
        {
            Id = "revit.views_list",
            Description = "List views from the active Revit document.",
            ArgsSchema = SchemaObject(("pageSize", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.views_list",
            Handler = async args =>
            {
                var pageSize = ParseInt(args["pageSize"], 100);
                var items = await _bridge.InvokeAsync(uiapp =>
                {
                    var doc = RequireActiveDocument(uiapp);
                    var views = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Select(view => new JObject
                        {
                            ["id"] = view.Id.IntegerValue,
                            ["name"] = view.Name,
                            ["viewType"] = view.ViewType.ToString(),
                            ["isTemplate"] = view.IsTemplate,
                        });
                    return new JArray(views);
                }).ConfigureAwait(false) as JArray ?? new JArray();
                return _resultHandleStore.BuildItemsResult("revit.views_list", "views", items, new JObject { ["kind"] = "views" }, pageSize);
            },
        });

        Register(new AiRevitTool
        {
            Id = "revit.elements_list",
            Description = "List elements from the active Revit document.",
            ArgsSchema = SchemaObject(
                ("categoryName", new JObject { ["type"] = "string" }),
                ("maxResults", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 20000 }),
                ("pageSize", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 })
            ),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.elements_list",
            Handler = async args =>
            {
                var pageSize = ParseInt(args["pageSize"], 100);
                var maxResults = Clamp(ParseInt(args["maxResults"], 1000), 1, 20000);
                var categoryName = args.Value<string>("categoryName");
                var items = await _bridge.InvokeAsync(uiapp =>
                {
                    var doc = RequireActiveDocument(uiapp);
                    var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
                    var filtered = collector.Where(element => string.IsNullOrWhiteSpace(categoryName) || string.Equals(element.Category?.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                    return new JArray(filtered.Take(maxResults).Select(ToElementSummary));
                }).ConfigureAwait(false) as JArray ?? new JArray();
                return _resultHandleStore.BuildItemsResult("revit.elements_list", "elements", items, new JObject { ["kind"] = "elements", ["categoryName"] = categoryName ?? "" }, pageSize);
            },
        });

        Register(new AiRevitTool
        {
            Id = "revit.element_detail_get",
            Description = "Inspect one element from the active Revit document by id.",
            ArgsSchema = SchemaObject(new[] { "elementId" }, ("elementId", new JObject { ["type"] = "integer" })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.element_detail_get",
            Handler = async args =>
            {
                var elementId = args.Value<int?>("elementId") ?? throw new InvalidOperationException("elementId is required.");
                return await _bridge.InvokeAsync(uiapp =>
                {
                    var doc = RequireActiveDocument(uiapp);
                    var element = doc.GetElement(new ElementId(elementId)) ?? throw new InvalidOperationException($"Element not found: {elementId}");
                    return new JObject
                    {
                        ["summary"] = ToElementSummary(element),
                        ["parameters"] = new JArray(element.Parameters.Cast<Parameter>().Select(parameter => new JObject
                        {
                            ["name"] = parameter.Definition?.Name,
                            ["storageType"] = parameter.StorageType.ToString(),
                            ["hasValue"] = parameter.HasValue,
                            ["isReadOnly"] = parameter.IsReadOnly,
                            ["value"] = ReadParameterValue(parameter),
                        })),
                    };
                }).ConfigureAwait(false);
            },
        });

        Register(new AiRevitTool
        {
            Id = "revit.selection_get",
            Description = "Return the current active selection.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "revit.selection_get",
            Handler = async _ =>
            {
                var items = await _bridge.InvokeAsync(uiapp =>
                {
                    var uidoc = uiapp.ActiveUIDocument ?? throw new InvalidOperationException("No active UI document.");
                    var doc = uidoc.Document;
                    return new JArray(uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(element => element is not null).Select(ToElementSummary));
                }).ConfigureAwait(false) as JArray ?? new JArray();
                return _resultHandleStore.BuildItemsResult("revit.selection_get", "selection", items, new JObject { ["kind"] = "selection" }, 100);
            },
        });

        Register(new AiRevitTool
        {
            Id = "revit.parameter_set",
            Description = "Set one writable parameter on one active-document element.",
            ArgsSchema = SchemaObject(
                new[] { "elementId", "parameterName", "value" },
                ("elementId", new JObject { ["type"] = "integer" }),
                ("parameterName", new JObject { ["type"] = "string" }),
                ("value", new JObject())
            ),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "high",
            RequiresConfirmation = true,
            HandlerName = "revit.parameter_set",
            Handler = async args =>
            {
                if (args["value"] is null)
                {
                    throw new InvalidOperationException("value is required.");
                }

                var elementId = args.Value<int?>("elementId") ?? throw new InvalidOperationException("elementId is required.");
                var parameterName = args.Value<string>("parameterName") ?? throw new InvalidOperationException("parameterName is required.");
                var value = args["value"]!;
                return await _bridge.InvokeAsync(uiapp =>
                {
                    var doc = RequireActiveDocument(uiapp);
                    var element = doc.GetElement(new ElementId(elementId)) ?? throw new InvalidOperationException($"Element not found: {elementId}");
                    var parameter = element.LookupParameter(parameterName) ?? throw new InvalidOperationException($"Parameter not found: {parameterName}");
                    if (parameter.IsReadOnly)
                    {
                        throw new InvalidOperationException($"Parameter is read-only: {parameterName}");
                    }

                    using var transaction = new Transaction(doc, $"AI Platform Agent: set {parameterName}");
                    transaction.Start();
                    SetParameterValue(parameter, value);
                    transaction.Commit();
                    return new JObject
                    {
                        ["elementId"] = elementId,
                        ["parameterName"] = parameterName,
                        ["value"] = ReadParameterValue(parameter),
                    };
                }).ConfigureAwait(false);
            },
        });
    }

    private JObject BuildHealthPayload(bool includeOk)
    {
        var payload = new JObject
        {
            ["framework"] = AiRevitConfig.FrameworkName,
            ["service"] = AiRevitConfig.ServiceName,
            ["serviceId"] = AiRevitConfig.ServiceId,
            ["version"] = _config.ServiceVersion,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["protocolVersion"] = AiRevitConfig.ProtocolVersion,
            ["serverRunning"] = _listener.IsListening,
            ["requiresToken"] = _config.RequireToken,
            ["acceptedTokenHeaders"] = new JArray(_config.AcceptedTokenHeaders),
            ["serverUrl"] = _config.ServerUrl,
            ["manifestHash"] = ComputeManifestHash(),
            ["toolCount"] = _tools.Count,
            ["namespaces"] = BuildNamespaceInfos(),
            ["supportsManifestSearch"] = true,
            ["supportsDescribeMany"] = true,
            ["supportsResultHandles"] = true,
            ["supportsBundles"] = true,
            ["supportsTextChunking"] = false,
            ["supportsDynamicToolRegistration"] = false,
            ["recommendedFlow"] = new JArray
            {
                "GET /health and compare manifestHash before refreshing capabilities.",
                "Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.",
                "Use POST /tool/describe_many for exact argument and return schemas before calling tools.",
                "Use GET /result/{handleId} for additional pages when a tool returns a resultHandle.",
                "Use GET /manifest/full only as a fallback when search is insufficient.",
            },
            ["paths"] = new JObject
            {
                ["health"] = "/health",
                ["manifestSummary"] = "/manifest",
                ["manifestFull"] = "/manifest/full",
                ["manifestSearch"] = "/manifest/search",
                ["manifestBundles"] = "/manifest/bundles",
                ["toolDescribeMany"] = "/tool/describe_many",
                ["call"] = "/call/{toolId}",
                ["agent"] = "/agent",
                ["agentBrief"] = "/agent/brief",
                ["resultPage"] = "/result/{handleId}",
            },
            ["platform"] = new JObject
            {
                ["versionName"] = _application.ControlledApplication.VersionName,
                ["versionNumber"] = _application.ControlledApplication.VersionNumber,
            },
        };

        if (includeOk)
        {
            payload["ok"] = true;
        }

        return payload;
    }

    private JObject BuildAgentBriefPayload(bool includeOk)
    {
        var payload = new JObject
        {
            ["framework"] = AiRevitConfig.FrameworkName,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["summary"] = "Use Revit application, document, view, element, selection, and parameter tools through the shared discovery-first protocol instead of inferring BIM model state from exports or screenshots.",
            ["steps"] = new JArray
            {
                "Call GET /health and reuse cached capabilities while manifestHash stays unchanged.",
                "Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.",
                "Inspect documents, views, elements, and selections before calling any write tool.",
                "Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.",
            },
        };

        if (includeOk)
        {
            payload["ok"] = true;
        }

        return payload;
    }

    private JObject BuildManifestSummary()
    {
        return new JObject
        {
            ["ok"] = true,
            ["framework"] = AiRevitConfig.FrameworkName,
            ["serviceId"] = AiRevitConfig.ServiceId,
            ["service"] = AiRevitConfig.ServiceName,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["version"] = _config.ServiceVersion,
            ["protocolVersion"] = AiRevitConfig.ProtocolVersion,
            ["manifestHash"] = ComputeManifestHash(),
            ["toolCount"] = _tools.Count,
            ["namespaces"] = BuildNamespaceInfos(),
            ["tools"] = new JArray(_tools.Values.OrderBy(tool => tool.Id).Select(tool => tool.ToSummaryJson())),
        };
    }

    private JObject BuildManifestFull()
    {
        var payload = BuildManifestSummary();
        payload["tools"] = new JArray(_tools.Values.OrderBy(tool => tool.Id).Select(tool => tool.ToFullJson()));
        return payload;
    }

    private JObject BuildBundleIndex()
    {
        return new JObject
        {
            ["ok"] = true,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["manifestHash"] = ComputeManifestHash(),
            ["bundles"] = new JArray(_bundles.Select(bundle => new JObject
            {
                ["id"] = bundle.Id,
                ["description"] = bundle.Description,
                ["toolCount"] = _tools.Values.Count(tool => bundle.Prefixes.Any(prefix => tool.Id.StartsWith(prefix, StringComparison.Ordinal))),
            })),
        };
    }

    private JObject? BuildBundle(string bundleId)
    {
        var bundle = _bundles.FirstOrDefault(candidate => candidate.Id == bundleId);
        if (bundle is null)
        {
            return null;
        }

        return new JObject
        {
            ["ok"] = true,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["manifestHash"] = ComputeManifestHash(),
            ["bundle"] = new JObject
            {
                ["id"] = bundle.Id,
                ["description"] = bundle.Description,
            },
            ["tools"] = new JArray(_tools.Values
                .Where(tool => bundle.Prefixes.Any(prefix => tool.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .OrderBy(tool => tool.Id)
                .Select(tool => tool.ToSummaryJson())),
        };
    }

    private JObject BuildManifestSearch(JObject body)
    {
        var query = body.Value<string>("query") ?? "";
        var limit = Clamp(ParseInt(body["limit"], 8), 1, 64);
        var namespaceId = body.Value<string>("namespaceId") ?? "";
        var bundleId = body.Value<string>("bundleId") ?? "";
        var tokens = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var bundle = _bundles.FirstOrDefault(candidate => candidate.Id == bundleId);

        var tools = _tools.Values
            .Where(tool => string.IsNullOrWhiteSpace(namespaceId) || tool.NamespaceId == namespaceId)
            .Where(tool => bundle is null || bundle.Prefixes.Any(prefix => tool.Id.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(tool => new { Tool = tool, Score = SearchScore(tool, tokens) })
            .Where(entry => tokens.Length == 0 || entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Tool.Id, StringComparer.Ordinal)
            .Take(limit)
            .Select(entry => entry.Tool.ToSummaryJson());

        return new JObject
        {
            ["ok"] = true,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["manifestHash"] = ComputeManifestHash(),
            ["query"] = query,
            ["namespaceId"] = namespaceId,
            ["bundleId"] = bundleId,
            ["returned"] = tools.Count(),
            ["tools"] = new JArray(tools),
        };
    }

    private JObject BuildDescribeMany(JObject body)
    {
        var ids = body["ids"] as JArray ?? new JArray();
        var found = new JArray();
        var missing = new JArray();
        foreach (var token in ids)
        {
            var id = token.Value<string>() ?? "";
            if (_tools.TryGetValue(id, out var tool))
            {
                found.Add(tool.ToFullJson());
            }
            else
            {
                missing.Add(id);
            }
        }

        return new JObject
        {
            ["ok"] = true,
            ["platformId"] = AiRevitConfig.PlatformId,
            ["manifestHash"] = ComputeManifestHash(),
            ["returned"] = found.Count,
            ["missing"] = missing,
            ["tools"] = found,
        };
    }

    private string ComputeManifestHash()
    {
        using var sha256 = SHA256.Create();
        var builder = new StringBuilder();
        builder.Append(AiRevitConfig.ProtocolVersion);
        foreach (var tool in _tools.Values.OrderBy(tool => tool.Id, StringComparer.Ordinal))
        {
            builder.Append(tool.Id)
                .Append(tool.NamespaceId)
                .Append(tool.Description)
                .Append(tool.ArgsSchema.ToString(Formatting.None))
                .Append(tool.ReturnSchema.ToString(Formatting.None))
                .Append(tool.Danger)
                .Append(tool.HandlerName)
                .Append(tool.RequiresConfirmation);
        }

        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return string.Concat(bytes.Select(value => value.ToString("x2")));
    }

    private JArray BuildNamespaceInfos()
    {
        return new JArray(_tools.Values
            .GroupBy(tool => tool.NamespaceId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new JObject
            {
                ["id"] = group.Key,
                ["count"] = group.Count(),
            }));
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (!_config.RequireToken)
        {
            return true;
        }

        var provided = request.Headers[AiRevitConfig.PrimaryTokenHeader] ?? request.Headers[AiRevitConfig.LegacyTokenHeader];
        return !string.IsNullOrWhiteSpace(provided) && string.Equals(provided, _token, StringComparison.Ordinal);
    }

    private async Task<JObject> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return new JObject();
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? new JObject() : JObject.Parse(content);
    }

    private static async Task SendJsonAsync(HttpListenerResponse response, int statusCode, JToken payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.Indented));
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.LongLength;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        response.Close();
    }

    private static void AddCommonHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-AI-Agent-Token, X-Revit-Ai-Token";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    }

    private string EnsureToken()
    {
        if (File.Exists(_config.TokenFilePath))
        {
            var existing = File.ReadAllText(_config.TokenFilePath).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        return RegenerateToken();
    }

    private string RegenerateToken()
    {
        var value = $"{Guid.NewGuid():N}{Guid.NewGuid():N}".Substring(0, 48);
        File.WriteAllText(_config.TokenFilePath, value);
        _token = value;
        _runtimeState.Log("info", "Token regenerated.");
        return value;
    }

    private void Register(AiRevitTool tool)
    {
        _tools.Add(tool.Id, tool);
    }

    private static Document RequireActiveDocument(UIApplication uiapp)
        => uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active Revit document.");

    private static JObject ToDocumentSummary(Document document)
    {
        return new JObject
        {
            ["title"] = document.Title,
            ["pathName"] = document.PathName,
            ["isModified"] = document.IsModified,
            ["isFamilyDocument"] = document.IsFamilyDocument,
            ["isWorkshared"] = document.IsWorkshared,
        };
    }

    private static JObject ToElementSummary(Element element)
    {
        return new JObject
        {
            ["id"] = element.Id.IntegerValue,
            ["uniqueId"] = element.UniqueId,
            ["name"] = element.Name,
            ["categoryName"] = element.Category?.Name,
            ["className"] = element.GetType().FullName,
        };
    }

    private static JToken? ReadParameterValue(Parameter parameter)
    {
        if (!parameter.HasValue)
        {
            return JValue.CreateNull();
        }

        switch (parameter.StorageType)
        {
            case StorageType.String:
                return parameter.AsString();
            case StorageType.Integer:
                return parameter.AsInteger();
            case StorageType.Double:
                return parameter.AsDouble();
            case StorageType.ElementId:
                return parameter.AsElementId().IntegerValue;
            default:
                return parameter.AsValueString();
        }
    }

    private static void SetParameterValue(Parameter parameter, JToken value)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                parameter.Set(value.Type == JTokenType.Null ? string.Empty : value.Value<string>() ?? string.Empty);
                break;
            case StorageType.Integer:
                parameter.Set(value.Value<int>());
                break;
            case StorageType.Double:
                parameter.Set(value.Value<double>());
                break;
            case StorageType.ElementId:
                parameter.Set(new ElementId(value.Value<int>()));
                break;
            default:
                throw new InvalidOperationException($"Unsupported parameter storage type: {parameter.StorageType}");
        }
    }

    private static int SearchScore(AiRevitTool tool, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return 1;
        }

        var haystack = $"{tool.Id} {tool.NamespaceId} {tool.Description}".ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            if (tool.Id.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }
            else if (tool.Description.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (haystack.Contains(token, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score;
    }

    private static JObject SchemaObject(params (string Name, JObject Schema)[] properties)
        => SchemaObject(Array.Empty<string>(), properties);

    private static JObject SchemaObject(string required, params (string Name, JObject Schema)[] properties)
        => SchemaObject(new[] { required }, properties);

    private static JObject SchemaObject(string required1, string required2, params (string Name, JObject Schema)[] properties)
        => SchemaObject(new[] { required1, required2 }, properties);

    private static JObject SchemaObject(IEnumerable<string> required, params (string Name, JObject Schema)[] properties)
    {
        var props = new JObject();
        foreach (var (name, schema) in properties)
        {
            if (name != "_")
            {
                props[name] = schema;
            }
        }

        var result = new JObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = props,
        };

        var requiredArray = new JArray(required.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (requiredArray.Count > 0)
        {
            result["required"] = requiredArray;
        }

        return result;
    }

    private static int ParseInt(JToken? token, int fallback) => token?.Value<int?>() ?? fallback;
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
    private static int Clamp(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
}
