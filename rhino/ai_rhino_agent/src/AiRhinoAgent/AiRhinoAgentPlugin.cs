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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.PlugIns;

namespace AiPlatform.RhinoAgent;

[System.Runtime.InteropServices.Guid("659C01D7-543D-4758-8EB8-491BB0DAA3AB")]
public sealed class AiRhinoAgentPlugin : PlugIn
{
    private AiRhinoHttpService? _service;

    public static AiRhinoAgentPlugin? Instance { get; private set; }

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    public AiRhinoAgentPlugin()
    {
        Instance = this;
    }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        try
        {
            _service = new AiRhinoHttpService(this);
            _service.Start();
            return LoadReturnCode.Success;
        }
        catch (Exception ex)
        {
            errorMessage = ex.ToString();
            return LoadReturnCode.ErrorShowDialog;
        }
    }

    protected override void OnShutdown()
    {
        _service?.Dispose();
        _service = null;
        base.OnShutdown();
    }
}

internal sealed class AiRhinoConfig
{
    public const string FrameworkName = "AI Platform Agent Framework";
    public const string ProtocolVersion = "2.0";
    public const string ServiceId = "airhino.agent";
    public const string ServiceName = "AI Rhino Agent";
    public const string PlatformId = "rhino";
    public const string PrimaryTokenHeader = "X-AI-Agent-Token";
    public const string LegacyTokenHeader = "X-Rhino-Ai-Token";

    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 19792;
    public bool RequireToken { get; init; } = true;
    public bool FullAccessEnabled { get; init; } = false;
    public int ToolTimeoutMs { get; init; } = 120000;
    public string ServiceVersion { get; init; } = "0.1.0";
    public string StateDirectoryPath { get; init; } = "";
    public string TokenFilePath { get; init; } = "";
    public string ConfigFilePath { get; init; } = "";

    public string ServerUrl => $"http://{Host}:{Port}";
    public IReadOnlyList<string> AcceptedTokenHeaders => new[] { PrimaryTokenHeader, LegacyTokenHeader };

    public static AiRhinoConfig Load(AiRhinoAgentPlugin plugin)
    {
        var root = Path.Combine(plugin.SettingsDirectory, "ai_platform_agent");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.json");
        AiRhinoConfigFile? file = null;
        if (File.Exists(path))
        {
            file = JsonConvert.DeserializeObject<AiRhinoConfigFile>(File.ReadAllText(path));
        }

        return new AiRhinoConfig
        {
            Host = string.IsNullOrWhiteSpace(file?.Host) ? "127.0.0.1" : file!.Host!,
            Port = file?.Port is > 0 ? file.Port.Value : 19792,
            RequireToken = file?.RequireToken ?? true,
            FullAccessEnabled = file?.FullAccessEnabled ?? false,
            ToolTimeoutMs = file?.ToolTimeoutMs is > 0 ? file.ToolTimeoutMs.Value : 120000,
            StateDirectoryPath = root,
            ConfigFilePath = path,
            TokenFilePath = Path.Combine(root, "token.txt"),
        };
    }

    private sealed class AiRhinoConfigFile
    {
        public string? Host { get; init; }
        public int? Port { get; init; }
        public bool? RequireToken { get; init; }
        public bool? FullAccessEnabled { get; init; }
        public int? ToolTimeoutMs { get; init; }
    }
}

internal sealed class AiRhinoRuntimeState
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

    private static void Trim<T>(Queue<T> queue, int maxCount)
    {
        while (queue.Count > maxCount)
        {
            queue.Dequeue();
        }
    }

    private static IEnumerable<T> LastEntries<T>(Queue<T> queue, int count)
    {
        var skip = Math.Max(0, queue.Count - count);
        return queue.Skip(skip).ToArray();
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}

internal sealed class AiRhinoResultHandleStore
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

    public AiRhinoResultHandleStore(int maxHandles = 96)
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

    public JObject BuildTextResult(string sourceToolId, string text, JObject summary, int length)
    {
        var safeLength = Clamp(length, 1, 32768);
        var count = Math.Min(safeLength, text.Length);
        var result = new JObject
        {
            ["summary"] = summary,
            ["offset"] = 0,
            ["limit"] = safeLength,
            ["count"] = count,
            ["totalChars"] = text.Length,
            ["hasMore"] = count < text.Length,
            ["content"] = text[..count],
        };

        if (count < text.Length)
        {
            result["resultHandle"] = Add(new Entry
            {
                Id = NewHandleId(),
                Kind = "text",
                SourceToolId = sourceToolId,
                FieldName = "content",
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Summary = summary,
                Text = text,
            });
        }

        return result;
    }

    public JObject? BuildPage(string handleId, int offset, int limit)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(handleId, out var entry))
            {
                return null;
            }

            if (entry.Kind == "text")
            {
                var safeOffset = Clamp(offset, 0, entry.Text.Length);
                var safeLimit = Clamp(limit == 0 ? 4096 : limit, 1, 32768);
                var count = Math.Min(safeLimit, entry.Text.Length - safeOffset);
                return new JObject
                {
                    ["ok"] = true,
                    ["handleId"] = handleId,
                    ["kind"] = "text",
                    ["sourceToolId"] = entry.SourceToolId,
                    ["createdAt"] = entry.CreatedAt,
                    ["offset"] = safeOffset,
                    ["limit"] = safeLimit,
                    ["count"] = count,
                    ["totalChars"] = entry.Text.Length,
                    ["hasMore"] = safeOffset + count < entry.Text.Length,
                    ["summary"] = entry.Summary,
                    ["content"] = entry.Text.Substring(safeOffset, count),
                };
            }

            var safeItemsOffset = Clamp(offset, 0, entry.Items.Count);
            var safeItemsLimit = Clamp(limit == 0 ? 20 : limit, 1, 200);
            var itemsCount = Math.Min(safeItemsLimit, entry.Items.Count - safeItemsOffset);
            return new JObject
            {
                ["ok"] = true,
                ["handleId"] = handleId,
                ["kind"] = "items",
                ["sourceToolId"] = entry.SourceToolId,
                ["fieldName"] = entry.FieldName,
                ["createdAt"] = entry.CreatedAt,
                ["offset"] = safeItemsOffset,
                ["limit"] = safeItemsLimit,
                ["count"] = itemsCount,
                ["total"] = entry.Items.Count,
                ["hasMore"] = safeItemsOffset + itemsCount < entry.Items.Count,
                ["summary"] = entry.Summary,
                [entry.FieldName] = new JArray(entry.Items.Skip(safeItemsOffset).Take(itemsCount)),
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

    private static string NewHandleId() => $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}{Guid.NewGuid():N}"[..24];
    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}

internal sealed class AiRhinoTool
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

internal sealed class AiRhinoBundle
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Prefixes { get; init; }
}

internal sealed class AiRhinoHttpService : IDisposable
{
    private readonly AiRhinoAgentPlugin _plugin;
    private readonly AiRhinoConfig _config;
    private readonly AiRhinoRuntimeState _runtimeState = new();
    private readonly AiRhinoResultHandleStore _resultHandleStore = new();
    private readonly Dictionary<string, AiRhinoTool> _tools = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<AiRhinoBundle> _bundles;
    private readonly string _agentManual;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private string _token = "";

    public AiRhinoHttpService(AiRhinoAgentPlugin plugin)
    {
        _plugin = plugin;
        _config = AiRhinoConfig.Load(plugin);
        _bundles = new[]
        {
            new AiRhinoBundle { Id = "service", Description = "Adapter health, logs, config, and recent calls.", Prefixes = new[] { "service." } },
            new AiRhinoBundle { Id = "rhino.document", Description = "Rhino document, layer, object, selection, and viewport inspection tools.", Prefixes = new[] { "rhino." } },
        };
        _agentManual = File.Exists(Path.Combine(AppContext.BaseDirectory, "AGENT.md"))
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AGENT.md"))
            : "Use the Rhino adapter to inspect live Rhino document state instead of inferring it from exported files alone.";
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
                    ["error"] = $"Unauthorized. Provide {AiRhinoConfig.PrimaryTokenHeader} or {AiRhinoConfig.LegacyTokenHeader}.",
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
                var bundle = BuildBundle(path["manifest/bundle/".Length..]);
                await SendJsonAsync(context.Response, bundle is null ? 404 : 200, bundle ?? new JObject { ["ok"] = false, ["error"] = "Unknown manifest bundle." }).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("result/", StringComparison.Ordinal) && context.Request.HttpMethod == "GET")
            {
                var handleId = path["result/".Length..];
                var offset = ParseInt(context.Request.QueryString["offset"], 0);
                var limit = ParseInt(context.Request.QueryString["limit"], 0);
                var page = _resultHandleStore.BuildPage(handleId, offset, limit);
                await SendJsonAsync(context.Response, page is null ? 404 : 200, page ?? new JObject { ["ok"] = false, ["error"] = $"Unknown result handle: {handleId}" }).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("call/", StringComparison.Ordinal) && context.Request.HttpMethod == "POST")
            {
                var toolId = path["call/".Length..];
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
        RegisterTool(new AiRhinoTool
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

        RegisterTool(new AiRhinoTool
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

        RegisterTool(new AiRhinoTool
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

        RegisterTool(new AiRhinoTool
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

        RegisterTool(new AiRhinoTool
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

        RegisterTool(new AiRhinoTool
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

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.installation_probe",
            Description = "Return Rhino host and runtime information.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.installation_probe",
            Handler = async _ => await InvokeOnUiThreadAsync(() => new JObject
            {
                ["exeVersion"] = RhinoApp.ExeVersion,
                ["exeServiceRelease"] = RhinoApp.ExeServiceRelease,
                ["buildDate"] = RhinoApp.BuildDate.ToUniversalTime().ToString("O"),
                ["installFolder"] = RhinoApp.InstallFolder,
                ["runtimeSerialNumber"] = RhinoApp.RuntimeSerialNumber,
            }).ConfigureAwait(false),
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.document_summary_get",
            Description = "Return summary information for the active Rhino document.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.document_summary_get",
            Handler = async _ => await InvokeOnUiThreadAsync(BuildDocumentSummary).ConfigureAwait(false),
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.layers_list",
            Description = "List layers from the active Rhino document.",
            ArgsSchema = SchemaObject(("pageSize", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.layers_list",
            Handler = async args =>
            {
                var pageSize = ParseInt(args["pageSize"], 100);
                var items = await InvokeOnUiThreadAsync(() =>
                {
                    var doc = RequireActiveDoc();
                    return new JArray(doc.Layers.Select(layer => new JObject
                    {
                        ["id"] = layer.Id.ToString(),
                        ["index"] = layer.Index,
                        ["name"] = layer.Name,
                        ["fullPath"] = layer.FullPath,
                        ["isVisible"] = layer.IsVisible,
                        ["isLocked"] = layer.IsLocked,
                    }));
                }).ConfigureAwait(false);
                return _resultHandleStore.BuildItemsResult("rhino.layers_list", "layers", items, new JObject { ["kind"] = "layers" }, pageSize);
            },
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.objects_list",
            Description = "List objects from the active Rhino document.",
            ArgsSchema = SchemaObject(
                ("pageSize", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 }),
                ("maxResults", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 20000 })
            ),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.objects_list",
            Handler = async args =>
            {
                var pageSize = ParseInt(args["pageSize"], 100);
                var maxResults = ParseInt(args["maxResults"], 1000);
                var items = await InvokeOnUiThreadAsync(() =>
                {
                    var doc = RequireActiveDoc();
                    return new JArray(doc.Objects.GetObjectList(ObjectType.AnyObject)
                        .Take(Clamp(maxResults, 1, 20000))
                        .Select(ToObjectSummary));
                }).ConfigureAwait(false);
                return _resultHandleStore.BuildItemsResult("rhino.objects_list", "objects", items, new JObject { ["kind"] = "objects" }, pageSize);
            },
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.object_detail_get",
            Description = "Inspect one Rhino object by id.",
            ArgsSchema = SchemaObject(new[] { "objectId" }, ("objectId", new JObject { ["type"] = "string" })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.object_detail_get",
            Handler = async args =>
            {
                var objectId = args.Value<string>("objectId") ?? throw new InvalidOperationException("objectId is required.");
                return await InvokeOnUiThreadAsync(() =>
                {
                    var doc = RequireActiveDoc();
                    if (!Guid.TryParse(objectId, out var guid))
                    {
                        throw new InvalidOperationException($"Invalid object id: {objectId}");
                    }

                    var rhinoObject = doc.Objects.FindId(guid) ?? throw new InvalidOperationException($"Object not found: {objectId}");
                    return BuildObjectDetail(rhinoObject);
                }).ConfigureAwait(false);
            },
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.selection_get",
            Description = "Return the current Rhino selection.",
            ArgsSchema = SchemaObject(),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.selection_get",
            Handler = async _ =>
            {
                var items = await InvokeOnUiThreadAsync(() =>
                {
                    var doc = RequireActiveDoc();
                    return new JArray(doc.Objects.GetSelectedObjects(includeLights: true, includeGrips: true).Select(ToObjectSummary));
                }).ConfigureAwait(false);
                return _resultHandleStore.BuildItemsResult("rhino.selection_get", "selection", items, new JObject { ["kind"] = "selection" }, 100);
            },
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.viewports_list",
            Description = "List active document viewports and views.",
            ArgsSchema = SchemaObject(("pageSize", new JObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "low",
            RequiresConfirmation = false,
            HandlerName = "rhino.viewports_list",
            Handler = async args =>
            {
                var pageSize = ParseInt(args["pageSize"], 100);
                var items = await InvokeOnUiThreadAsync(() =>
                {
                    var doc = RequireActiveDoc();
                    return new JArray(doc.Views.GetViewList(true, false).Select(view => new JObject
                    {
                        ["runtimeSerialNumber"] = view.RuntimeSerialNumber,
                        ["title"] = view.ActiveViewport.Name,
                        ["mainViewportId"] = view.MainViewport.Id.ToString(),
                        ["isPageView"] = view.MainViewport.IsPageView,
                    }));
                }).ConfigureAwait(false);
                return _resultHandleStore.BuildItemsResult("rhino.viewports_list", "views", items, new JObject { ["kind"] = "views" }, pageSize);
            },
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.layer_visibility_set",
            Description = "Change one layer's visibility in the active Rhino document.",
            ArgsSchema = SchemaObject(
                new[] { "layerId", "isVisible" },
                ("layerId", new JObject { ["type"] = "string" }),
                ("isVisible", new JObject { ["type"] = "boolean" })
            ),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "medium",
            RequiresConfirmation = true,
            HandlerName = "rhino.layer_visibility_set",
            Handler = async args =>
            {
                var layerId = args.Value<string>("layerId") ?? throw new InvalidOperationException("layerId is required.");
                var isVisible = args.Value<bool?>("isVisible") ?? throw new InvalidOperationException("isVisible is required.");
                return await InvokeOnUiThreadAsync(() =>
                {
                    var doc = RequireActiveDoc();
                    if (!Guid.TryParse(layerId, out var guid))
                    {
                        throw new InvalidOperationException($"Invalid layer id: {layerId}");
                    }

                    var index = doc.Layers.FindId(guid);
                    if (index < 0)
                    {
                        throw new InvalidOperationException($"Layer not found: {layerId}");
                    }

                    var layer = doc.Layers[index];
                    layer.IsVisible = isVisible;
                    doc.Layers.Modify(layer, index, quiet: false);
                    doc.Views.Redraw();
                    return new JObject
                    {
                        ["layerId"] = layerId,
                        ["isVisible"] = isVisible,
                    };
                }).ConfigureAwait(false);
            },
        });

        RegisterTool(new AiRhinoTool
        {
            Id = "rhino.command_run",
            Description = "Run one Rhino command string in the host application.",
            ArgsSchema = SchemaObject(new[] { "command" }, ("command", new JObject { ["type"] = "string" })),
            ReturnSchema = new JObject { ["type"] = "object" },
            Danger = "high",
            RequiresConfirmation = true,
            HandlerName = "rhino.command_run",
            Handler = async args =>
            {
                var command = args.Value<string>("command") ?? throw new InvalidOperationException("command is required.");
                return await InvokeOnUiThreadAsync(() =>
                {
                    var ok = RhinoApp.RunScript(command, echo: false);
                    return new JObject
                    {
                        ["command"] = command,
                        ["ok"] = ok,
                    };
                }).ConfigureAwait(false);
            },
        });
    }

    private JObject BuildHealthPayload(bool includeOk)
    {
        var payload = new JObject
        {
            ["framework"] = AiRhinoConfig.FrameworkName,
            ["service"] = AiRhinoConfig.ServiceName,
            ["serviceId"] = AiRhinoConfig.ServiceId,
            ["version"] = _config.ServiceVersion,
            ["platformId"] = AiRhinoConfig.PlatformId,
            ["protocolVersion"] = AiRhinoConfig.ProtocolVersion,
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
            ["supportsTextChunking"] = true,
            ["supportsDynamicToolRegistration"] = false,
            ["recommendedFlow"] = new JArray
            {
                "GET /health and compare manifestHash before refreshing capabilities.",
                "Use POST /manifest/search or GET /manifest/bundle/{id} to narrow candidate tools.",
                "Use POST /tool/describe_many for exact argument and return schemas before calling tools.",
                "Use GET /result/{handleId} for additional pages or text chunks when a tool returns a resultHandle.",
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
                ["openDocumentCount"] = RhinoDoc.OpenDocumentsCount,
                ["exeVersion"] = RhinoApp.ExeVersion,
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
            ["framework"] = AiRhinoConfig.FrameworkName,
            ["platformId"] = AiRhinoConfig.PlatformId,
            ["summary"] = "Use Rhino document, layer, object, selection, viewport, and command tools through the shared discovery-first protocol instead of inferring host state from exported geometry or screenshots.",
            ["steps"] = new JArray
            {
                "Call GET /health and reuse cached capabilities while manifestHash stays unchanged.",
                "Search tools with POST /manifest/search or load a focused bundle with GET /manifest/bundle/{id}.",
                "Inspect document, layer, object, and selection state before any mutation or command execution.",
                "Request exact tool schemas with POST /tool/describe_many before calling unfamiliar tools.",
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
            ["framework"] = AiRhinoConfig.FrameworkName,
            ["serviceId"] = AiRhinoConfig.ServiceId,
            ["service"] = AiRhinoConfig.ServiceName,
            ["platformId"] = AiRhinoConfig.PlatformId,
            ["version"] = _config.ServiceVersion,
            ["protocolVersion"] = AiRhinoConfig.ProtocolVersion,
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
            ["platformId"] = AiRhinoConfig.PlatformId,
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
            ["platformId"] = AiRhinoConfig.PlatformId,
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
            ["platformId"] = AiRhinoConfig.PlatformId,
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
            ["platformId"] = AiRhinoConfig.PlatformId,
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
        builder.Append(AiRhinoConfig.ProtocolVersion);
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

    private static int SearchScore(AiRhinoTool tool, IReadOnlyList<string> tokens)
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
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-AI-Agent-Token, X-Rhino-Ai-Token";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (!_config.RequireToken)
        {
            return true;
        }

        var provided = request.Headers[AiRhinoConfig.PrimaryTokenHeader] ?? request.Headers[AiRhinoConfig.LegacyTokenHeader];
        return !string.IsNullOrWhiteSpace(provided) && string.Equals(provided, _token, StringComparison.Ordinal);
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
        var value = $"{Guid.NewGuid():N}{Guid.NewGuid():N}"[..48];
        File.WriteAllText(_config.TokenFilePath, value);
        _token = value;
        _runtimeState.Log("info", "Token regenerated.");
        return value;
    }

    private async Task<T> InvokeOnUiThreadAsync<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            try
            {
                tcs.TrySetResult(action());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }));
        return await tcs.Task.ConfigureAwait(false);
    }

    private static RhinoDoc RequireActiveDoc() => RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No active Rhino document.");

    private static JObject BuildDocumentSummary()
    {
        var doc = RequireActiveDoc();
        return new JObject
        {
            ["name"] = doc.Name,
            ["path"] = doc.Path,
            ["isModified"] = doc.Modified,
            ["unitSystem"] = doc.ModelUnitSystem.ToString(),
            ["modelAbsoluteTolerance"] = doc.ModelAbsoluteTolerance,
            ["layerCount"] = doc.Layers.Count,
            ["objectCount"] = doc.Objects.Count,
            ["viewCount"] = doc.Views.Count,
        };
    }

    private static JObject ToObjectSummary(RhinoObject rhinoObject)
    {
        return new JObject
        {
            ["id"] = rhinoObject.Id.ToString(),
            ["name"] = rhinoObject.Name,
            ["objectType"] = rhinoObject.ObjectType.ToString(),
            ["layerIndex"] = rhinoObject.Attributes.LayerIndex,
            ["isSelected"] = rhinoObject.IsSelected(false) != 0,
            ["isHidden"] = rhinoObject.IsHidden,
            ["isLocked"] = rhinoObject.IsLocked,
        };
    }

    private static JObject BuildObjectDetail(RhinoObject rhinoObject)
    {
        var bbox = rhinoObject.Geometry?.GetBoundingBox(accurate: true);
        return new JObject
        {
            ["summary"] = ToObjectSummary(rhinoObject),
            ["attributes"] = new JObject
            {
                ["displayOrder"] = rhinoObject.Attributes.DisplayOrder,
                ["materialIndex"] = rhinoObject.Attributes.MaterialIndex,
                ["mode"] = rhinoObject.Attributes.Mode.ToString(),
                ["plotColor"] = rhinoObject.Attributes.PlotColor.ToArgb(),
                ["plotWeight"] = rhinoObject.Attributes.PlotWeight,
            },
            ["geometry"] = new JObject
            {
                ["typeName"] = rhinoObject.Geometry?.ObjectType.ToString(),
                ["boundingBox"] = bbox is null
                    ? null
                    : new JObject
                    {
                        ["min"] = new JObject { ["x"] = bbox.Value.Min.X, ["y"] = bbox.Value.Min.Y, ["z"] = bbox.Value.Min.Z },
                        ["max"] = new JObject { ["x"] = bbox.Value.Max.X, ["y"] = bbox.Value.Max.Y, ["z"] = bbox.Value.Max.Z },
                    },
            },
        };
    }

    private void RegisterTool(AiRhinoTool tool)
    {
        _tools.Add(tool.Id, tool);
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
            props[name] = schema;
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
