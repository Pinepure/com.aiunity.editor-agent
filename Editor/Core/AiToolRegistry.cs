#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    public sealed class AiToolInfo
    {
        public string id;
        public string namespaceId;
        public string description;
        public string argsSchemaJson;
        public string returnSchemaJson;
        public string danger;
        public bool requiresConfirmation;
        public string declaringType;
        public string methodName;
    }

    [Serializable]
    public sealed class AiToolNamespaceInfo
    {
        public string id;
        public int count;
    }

    [Serializable]
    public sealed class AiToolSummaryInfo
    {
        public string id;
        public string namespaceId;
        public string description;
        public string danger;
        public bool requiresConfirmation;
    }

    [Serializable]
    public sealed class AiToolManifest
    {
        public string protocolVersion;
        public string serviceVersion;
        public string manifestHash;
        public int toolCount;
        public string unityVersion;
        public string projectPath;
        public string serverUrl;
        public bool serverRunning;
        public bool isCompiling;
        public bool isUpdating;
        public bool hasCompileErrors;
        public int consoleErrorCount;
        public int consoleWarningCount;
        public int consoleLogCount;
        public string generatedAt;
        public AiToolNamespaceInfo[] namespaces;
        public AiToolInfo[] tools;
    }

    [Serializable]
    public sealed class AiToolSummaryManifest
    {
        public string protocolVersion;
        public string serviceVersion;
        public string manifestHash;
        public int toolCount;
        public string generatedAt;
        public AiToolNamespaceInfo[] namespaces;
        public AiToolSummaryInfo[] tools;
    }

    [Serializable]
    internal sealed class AiToolSearchHit
    {
        public string id;
        public string namespaceId;
        public string description;
        public string danger;
        public bool requiresConfirmation;
        public int score;
        public string whyMatched;
    }

    [Serializable]
    internal sealed class AiManifestSearchResponse
    {
        public string protocolVersion;
        public string serviceVersion;
        public string manifestHash;
        public string query;
        public string namespaceId;
        public string bundleId;
        public int limit;
        public int totalMatches;
        public AiToolSearchHit[] items;
    }

    [Serializable]
    internal sealed class AiManifestBundleInfo
    {
        public string id;
        public string description;
        public int toolCount;
        public string[] namespaces;
    }

    [Serializable]
    internal sealed class AiManifestBundleIndexResponse
    {
        public string protocolVersion;
        public string serviceVersion;
        public string manifestHash;
        public int bundleCount;
        public AiManifestBundleInfo[] bundles;
    }

    [Serializable]
    internal sealed class AiManifestBundleResponse
    {
        public string protocolVersion;
        public string serviceVersion;
        public string manifestHash;
        public string id;
        public string description;
        public int toolCount;
        public string[] namespaces;
        public AiToolSummaryInfo[] tools;
    }

    [Serializable]
    internal sealed class AiToolDescribeManyResponse
    {
        public string protocolVersion;
        public string serviceVersion;
        public string manifestHash;
        public int requestedCount;
        public int returnedCount;
        public string[] missingIds;
        public AiToolInfo[] tools;
    }

    internal sealed class AiToolEntry
    {
        public AiToolInfo info;
        public MethodInfo method;
    }

    internal sealed class AiManifestBundleDefinition
    {
        public string id;
        public string description;
        public string[] prefixes;
    }

    internal static class AiToolRegistry
    {
        private static readonly Dictionary<string, AiToolEntry> Map = new Dictionary<string, AiToolEntry>();
        private static readonly List<AiToolInfo> ToolList = new List<AiToolInfo>();
        private static readonly List<AiToolNamespaceInfo> NamespaceList = new List<AiToolNamespaceInfo>();
        private static readonly AiManifestBundleDefinition[] BundleDefinitions =
        {
            new AiManifestBundleDefinition
            {
                id = "asset-analysis",
                description = "Asset search, text inspection, dependency analysis, GUID/path conversion, and AssetDatabase refresh.",
                prefixes = new[] { "asset." }
            },
            new AiManifestBundleDefinition
            {
                id = "scene-editing",
                description = "Selection inspection, scene object search, object creation, and scene save helpers.",
                prefixes = new[] { "selection.", "scene." }
            },
            new AiManifestBundleDefinition
            {
                id = "prefab-authoring",
                description = "Prefab creation and structured prefab authoring utilities.",
                prefixes = new[] { "prefab." }
            },
            new AiManifestBundleDefinition
            {
                id = "tool-authoring",
                description = "Generated tool templates, installation, listing, and removal.",
                prefixes = new[] { "tool." }
            },
            new AiManifestBundleDefinition
            {
                id = "service-diagnostics",
                description = "Health checks, manifest fallback access, compile diagnostics, logs, and service configuration.",
                prefixes = new[] { "system.", "service.", "manifest.", "compile.", "console.", "agent." }
            }
        };

        private static DateTime lastRebuild = DateTime.MinValue;
        private static string manifestHash = string.Empty;

        public static DateTime LastRebuild
        {
            get { return lastRebuild; }
        }

        public static int Count
        {
            get
            {
                EnsureBuilt();
                return ToolList.Count;
            }
        }

        public static string ManifestHash
        {
            get
            {
                EnsureBuilt();
                return manifestHash;
            }
        }

        public static bool HasBundle(string bundleId)
        {
            EnsureBuilt();
            return FindBundle(bundleId) != null;
        }

        public static void Rebuild()
        {
            Map.Clear();
            ToolList.Clear();

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                ScanAssembly(assemblies[i]);
            }

            ToolList.Sort(delegate (AiToolInfo a, AiToolInfo b)
            {
                return string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase);
            });

            lastRebuild = DateTime.Now;
            RebuildDerivedData();
            AiEditorAgentState.Log("info", "Tool registry rebuilt. Tool count: " + ToolList.Count);
        }

        public static bool TryGet(string id, out AiToolEntry entry)
        {
            EnsureBuilt();
            return Map.TryGetValue(id, out entry);
        }

        public static List<AiToolInfo> GetToolsCopy()
        {
            EnsureBuilt();
            return new List<AiToolInfo>(ToolList);
        }

        public static List<AiToolNamespaceInfo> GetNamespaceInfosCopy()
        {
            EnsureBuilt();
            List<AiToolNamespaceInfo> copy = new List<AiToolNamespaceInfo>(NamespaceList.Count);
            for (int i = 0; i < NamespaceList.Count; i++)
            {
                copy.Add(new AiToolNamespaceInfo
                {
                    id = NamespaceList[i].id,
                    count = NamespaceList[i].count
                });
            }
            return copy;
        }

        public static AiToolManifest BuildManifest()
        {
            EnsureBuilt();
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            return new AiToolManifest
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
                manifestHash = manifestHash,
                toolCount = ToolList.Count,
                unityVersion = Application.unityVersion,
                projectPath = AiEditorAgentPaths.ProjectRoot,
                serverUrl = AiEditorAgentSettings.ServerUrl,
                serverRunning = AiEditorApiServer.IsRunning,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                hasCompileErrors = counts.available && counts.errorCount > 0,
                consoleErrorCount = counts.errorCount,
                consoleWarningCount = counts.warningCount,
                consoleLogCount = counts.logCount,
                generatedAt = FormatTimestamp(lastRebuild),
                namespaces = GetNamespaceInfosCopy().ToArray(),
                tools = ToolList.ToArray()
            };
        }

        public static string BuildManifestJson(bool rebuild)
        {
            return BuildManifestFullJson(rebuild, false);
        }

        public static string BuildManifestFullJson(bool rebuild, bool pretty)
        {
            if (rebuild) Rebuild();
            else EnsureBuilt();
            return JsonUtility.ToJson(BuildManifest(), pretty);
        }

        public static string BuildManifestSummaryJson(bool rebuild, bool pretty)
        {
            if (rebuild) Rebuild();
            else EnsureBuilt();
            return JsonUtility.ToJson(BuildSummaryManifest(), pretty);
        }

        public static string BuildManifestSearchJson(string query, int limit, string namespaceId, string bundleId, bool rebuild)
        {
            if (rebuild) Rebuild();
            else EnsureBuilt();

            AiManifestBundleDefinition bundle = FindBundle(bundleId);
            if (!string.IsNullOrEmpty(bundleId) && bundle == null)
            {
                return AiJson.Error("Unknown manifest bundle: " + bundleId);
            }
            string normalizedNamespace = NormalizeId(namespaceId);
            string normalizedQuery = NormalizeQuery(query);
            int safeLimit = limit <= 0 ? 8 : Math.Min(limit, 24);

            List<AiToolSearchHit> hits = new List<AiToolSearchHit>();
            for (int i = 0; i < ToolList.Count; i++)
            {
                AiToolInfo info = ToolList[i];
                if (!string.IsNullOrEmpty(normalizedNamespace) && info.namespaceId != normalizedNamespace) continue;
                if (bundle != null && !MatchesBundle(info, bundle)) continue;

                string whyMatched;
                int score = ScoreTool(info, normalizedQuery, out whyMatched);
                if (!string.IsNullOrEmpty(normalizedQuery) && score <= 0) continue;
                if (string.IsNullOrEmpty(normalizedQuery)) score = 1;

                hits.Add(new AiToolSearchHit
                {
                    id = info.id,
                    namespaceId = info.namespaceId,
                    description = info.description,
                    danger = info.danger,
                    requiresConfirmation = info.requiresConfirmation,
                    score = score,
                    whyMatched = string.IsNullOrEmpty(whyMatched)
                        ? (!string.IsNullOrEmpty(normalizedNamespace) ? "namespace filter" : (bundle != null ? "bundle filter" : "default listing"))
                        : whyMatched
                });
            }

            hits.Sort(delegate (AiToolSearchHit a, AiToolSearchHit b)
            {
                int scoreCompare = b.score.CompareTo(a.score);
                if (scoreCompare != 0) return scoreCompare;
                return string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase);
            });

            int count = Math.Min(safeLimit, hits.Count);
            AiToolSearchHit[] items = new AiToolSearchHit[count];
            for (int i = 0; i < count; i++) items[i] = hits[i];

            AiManifestSearchResponse response = new AiManifestSearchResponse
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
                manifestHash = manifestHash,
                query = query ?? string.Empty,
                namespaceId = normalizedNamespace ?? string.Empty,
                bundleId = bundle == null ? string.Empty : bundle.id,
                limit = safeLimit,
                totalMatches = hits.Count,
                items = items
            };
            return JsonUtility.ToJson(response, false);
        }

        public static string BuildToolDescribeManyJson(string[] ids, bool rebuild, bool pretty)
        {
            if (rebuild) Rebuild();
            else EnsureBuilt();

            List<AiToolInfo> found = new List<AiToolInfo>();
            List<string> missing = new List<string>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            int requested = ids == null ? 0 : ids.Length;

            if (ids != null)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    string id = ids[i] == null ? string.Empty : ids[i].Trim();
                    if (string.IsNullOrEmpty(id)) continue;
                    if (seen.ContainsKey(id)) continue;
                    seen[id] = true;

                    AiToolEntry entry;
                    if (Map.TryGetValue(id, out entry))
                    {
                        found.Add(CopyToolInfo(entry.info));
                    }
                    else
                    {
                        missing.Add(id);
                    }
                }
            }

            AiToolDescribeManyResponse response = new AiToolDescribeManyResponse
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
                manifestHash = manifestHash,
                requestedCount = requested,
                returnedCount = found.Count,
                missingIds = missing.ToArray(),
                tools = found.ToArray()
            };
            return JsonUtility.ToJson(response, pretty);
        }

        public static string BuildManifestBundleIndexJson(bool rebuild, bool pretty)
        {
            if (rebuild) Rebuild();
            else EnsureBuilt();

            List<AiManifestBundleInfo> items = new List<AiManifestBundleInfo>();
            for (int i = 0; i < BundleDefinitions.Length; i++)
            {
                AiManifestBundleInfo info = BuildBundleInfo(BundleDefinitions[i]);
                if (info.toolCount > 0) items.Add(info);
            }

            AiManifestBundleIndexResponse response = new AiManifestBundleIndexResponse
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
                manifestHash = manifestHash,
                bundleCount = items.Count,
                bundles = items.ToArray()
            };
            return JsonUtility.ToJson(response, pretty);
        }

        public static bool TryBuildManifestBundleJson(string bundleId, bool rebuild, bool pretty, out string json)
        {
            if (rebuild) Rebuild();
            else EnsureBuilt();

            AiManifestBundleDefinition definition = FindBundle(bundleId);
            if (definition == null)
            {
                json = string.Empty;
                return false;
            }

            List<AiToolSummaryInfo> tools = new List<AiToolSummaryInfo>();
            Dictionary<string, bool> namespaces = new Dictionary<string, bool>();
            for (int i = 0; i < ToolList.Count; i++)
            {
                AiToolInfo info = ToolList[i];
                if (!MatchesBundle(info, definition)) continue;
                tools.Add(ToSummary(info));
                namespaces[info.namespaceId] = true;
            }

            List<string> namespaceList = new List<string>(namespaces.Keys);
            namespaceList.Sort(StringComparer.OrdinalIgnoreCase);

            AiManifestBundleResponse response = new AiManifestBundleResponse
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
                manifestHash = manifestHash,
                id = definition.id,
                description = definition.description,
                toolCount = tools.Count,
                namespaces = namespaceList.ToArray(),
                tools = tools.ToArray()
            };
            json = JsonUtility.ToJson(response, pretty);
            return true;
        }

        private static void EnsureBuilt()
        {
            if (ToolList.Count == 0 || string.IsNullOrEmpty(manifestHash)) Rebuild();
        }

        private static AiToolSummaryManifest BuildSummaryManifest()
        {
            List<AiToolSummaryInfo> tools = new List<AiToolSummaryInfo>(ToolList.Count);
            for (int i = 0; i < ToolList.Count; i++) tools.Add(ToSummary(ToolList[i]));
            return new AiToolSummaryManifest
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
                manifestHash = manifestHash,
                toolCount = ToolList.Count,
                generatedAt = FormatTimestamp(lastRebuild),
                namespaces = GetNamespaceInfosCopy().ToArray(),
                tools = tools.ToArray()
            };
        }

        private static void RebuildDerivedData()
        {
            NamespaceList.Clear();

            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < ToolList.Count; i++)
            {
                AiToolInfo info = ToolList[i];
                int count;
                counts.TryGetValue(info.namespaceId, out count);
                counts[info.namespaceId] = count + 1;
            }

            foreach (KeyValuePair<string, int> pair in counts)
            {
                NamespaceList.Add(new AiToolNamespaceInfo
                {
                    id = pair.Key,
                    count = pair.Value
                });
            }

            NamespaceList.Sort(delegate (AiToolNamespaceInfo a, AiToolNamespaceInfo b)
            {
                return string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase);
            });

            manifestHash = ComputeManifestHash();
        }

        private static string ComputeManifestHash()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(AiEditorAgentPaths.ProtocolVersion).Append('|');
            for (int i = 0; i < ToolList.Count; i++)
            {
                AiToolInfo info = ToolList[i];
                sb.Append(info.id).Append('|')
                    .Append(info.namespaceId).Append('|')
                    .Append(info.description).Append('|')
                    .Append(info.argsSchemaJson).Append('|')
                    .Append(info.returnSchemaJson).Append('|')
                    .Append(info.danger).Append('|')
                    .Append(info.requiresConfirmation ? '1' : '0').Append('|')
                    .Append(info.declaringType).Append('|')
                    .Append(info.methodName).Append('\n');
            }

            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < sb.Length; i++)
            {
                hash ^= sb[i];
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        private static string FormatTimestamp(DateTime time)
        {
            return (time == DateTime.MinValue ? DateTime.Now : time).ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        private static AiToolSummaryInfo ToSummary(AiToolInfo info)
        {
            return new AiToolSummaryInfo
            {
                id = info.id,
                namespaceId = info.namespaceId,
                description = info.description,
                danger = info.danger,
                requiresConfirmation = info.requiresConfirmation
            };
        }

        private static AiToolInfo CopyToolInfo(AiToolInfo info)
        {
            return new AiToolInfo
            {
                id = info.id,
                namespaceId = info.namespaceId,
                description = info.description,
                argsSchemaJson = info.argsSchemaJson,
                returnSchemaJson = info.returnSchemaJson,
                danger = info.danger,
                requiresConfirmation = info.requiresConfirmation,
                declaringType = info.declaringType,
                methodName = info.methodName
            };
        }

        private static string NormalizeId(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Trim().ToLowerInvariant();
        }

        private static string NormalizeQuery(string query)
        {
            return string.IsNullOrEmpty(query) ? string.Empty : query.Trim().ToLowerInvariant();
        }

        private static string[] Tokenize(string value)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrEmpty(value)) return tokens.ToArray();

            StringBuilder current = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    current.Append(c);
                }
                else
                {
                    FlushToken(tokens, current);
                }
            }
            FlushToken(tokens, current);
            return tokens.ToArray();
        }

        private static void FlushToken(List<string> tokens, StringBuilder current)
        {
            if (current.Length == 0) return;
            string token = current.ToString();
            current.Length = 0;
            if (token.Length < 2) return;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i] == token) return;
            }
            tokens.Add(token);
        }

        private static int ScoreTool(AiToolInfo info, string query, out string whyMatched)
        {
            string id = (info.id ?? string.Empty).ToLowerInvariant();
            string namespaceId = (info.namespaceId ?? string.Empty).ToLowerInvariant();
            string description = (info.description ?? string.Empty).ToLowerInvariant();
            string declaringType = (info.declaringType ?? string.Empty).ToLowerInvariant();
            string methodName = (info.methodName ?? string.Empty).ToLowerInvariant();

            if (string.IsNullOrEmpty(query))
            {
                whyMatched = string.Empty;
                return 0;
            }

            int score = 0;
            List<string> reasons = new List<string>();

            if (id == query)
            {
                score += 400;
                AddReason(reasons, "exact tool id");
            }
            if (namespaceId == query)
            {
                score += 220;
                AddReason(reasons, "exact namespace");
            }
            if (id.StartsWith(query, StringComparison.Ordinal))
            {
                score += 160;
                AddReason(reasons, "tool id prefix");
            }
            else if (id.IndexOf(query, StringComparison.Ordinal) >= 0)
            {
                score += 110;
                AddReason(reasons, "tool id contains query");
            }
            if (description.IndexOf(query, StringComparison.Ordinal) >= 0)
            {
                score += 70;
                AddReason(reasons, "description contains query");
            }
            if (declaringType.IndexOf(query, StringComparison.Ordinal) >= 0 || methodName.IndexOf(query, StringComparison.Ordinal) >= 0)
            {
                score += 25;
                AddReason(reasons, "source method contains query");
            }

            string[] tokens = Tokenize(query);
            int usefulTokens = 0;
            int matchedTokens = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token.Length < 2) continue;
                usefulTokens++;

                bool matched = false;
                if (namespaceId == token)
                {
                    score += 95;
                    matched = true;
                    AddReason(reasons, "namespace token: " + token);
                }
                if (id.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    score += id.StartsWith(token, StringComparison.Ordinal) ? 70 : 50;
                    matched = true;
                    AddReason(reasons, "id token: " + token);
                }
                if (description.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    score += 20;
                    matched = true;
                    AddReason(reasons, "description token: " + token);
                }
                if (declaringType.IndexOf(token, StringComparison.Ordinal) >= 0 || methodName.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    score += 10;
                    matched = true;
                    AddReason(reasons, "source token: " + token);
                }
                if (matched) matchedTokens++;
            }

            if (usefulTokens > 1 && matchedTokens == usefulTokens)
            {
                score += 40;
                AddReason(reasons, "all query tokens matched");
            }

            whyMatched = BuildReasonString(reasons);
            return score;
        }

        private static void AddReason(List<string> reasons, string reason)
        {
            if (string.IsNullOrEmpty(reason)) return;
            for (int i = 0; i < reasons.Count; i++)
            {
                if (reasons[i] == reason) return;
            }
            reasons.Add(reason);
        }

        private static string BuildReasonString(List<string> reasons)
        {
            if (reasons == null || reasons.Count == 0) return string.Empty;
            StringBuilder sb = new StringBuilder();
            int count = Math.Min(3, reasons.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(reasons[i]);
            }
            return sb.ToString();
        }

        private static AiManifestBundleDefinition FindBundle(string bundleId)
        {
            string normalized = NormalizeId(bundleId);
            if (string.IsNullOrEmpty(normalized)) return null;
            for (int i = 0; i < BundleDefinitions.Length; i++)
            {
                if (BundleDefinitions[i].id == normalized) return BundleDefinitions[i];
            }
            return null;
        }

        private static AiManifestBundleInfo BuildBundleInfo(AiManifestBundleDefinition definition)
        {
            Dictionary<string, bool> namespaces = new Dictionary<string, bool>();
            int toolCount = 0;
            for (int i = 0; i < ToolList.Count; i++)
            {
                AiToolInfo info = ToolList[i];
                if (!MatchesBundle(info, definition)) continue;
                toolCount++;
                namespaces[info.namespaceId] = true;
            }

            List<string> namespaceList = new List<string>(namespaces.Keys);
            namespaceList.Sort(StringComparer.OrdinalIgnoreCase);

            return new AiManifestBundleInfo
            {
                id = definition.id,
                description = definition.description,
                toolCount = toolCount,
                namespaces = namespaceList.ToArray()
            };
        }

        private static bool MatchesBundle(AiToolInfo info, AiManifestBundleDefinition definition)
        {
            if (definition == null || info == null || string.IsNullOrEmpty(info.id)) return false;
            for (int i = 0; i < definition.prefixes.Length; i++)
            {
                if (info.id.StartsWith(definition.prefixes[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void ScanAssembly(Assembly assembly)
        {
            if (assembly == null) return;
            if (assembly.IsDynamic) return;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }
            catch
            {
                return;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null) continue;
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch
                {
                    continue;
                }

                for (int j = 0; j < methods.Length; j++)
                {
                    MethodInfo method = methods[j];
                    object[] attrs = method.GetCustomAttributes(typeof(AiToolAttribute), false);
                    if (attrs == null || attrs.Length == 0) continue;
                    AiToolAttribute attr = attrs[0] as AiToolAttribute;
                    if (attr == null) continue;
                    RegisterMethod(type, method, attr);
                }
            }
        }

        private static void RegisterMethod(Type type, MethodInfo method, AiToolAttribute attr)
        {
            if (string.IsNullOrEmpty(attr.Id))
            {
                AiEditorAgentState.Warn("Ignored AI tool with empty id: " + type.FullName + "." + method.Name);
                return;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (method.ReturnType != typeof(string) || parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            {
                AiEditorAgentState.Warn("Invalid AI tool signature: " + type.FullName + "." + method.Name + ". Required: static string Method(string argsJson).");
                return;
            }

            if (Map.ContainsKey(attr.Id))
            {
                AiEditorAgentState.Warn("Duplicate AI tool id ignored: " + attr.Id + " at " + type.FullName + "." + method.Name);
                return;
            }

            AiToolInfo info = new AiToolInfo
            {
                id = attr.Id,
                namespaceId = GetNamespaceId(attr.Id),
                description = attr.Description ?? string.Empty,
                argsSchemaJson = string.IsNullOrEmpty(attr.ArgsSchemaJson) ? "{}" : attr.ArgsSchemaJson,
                returnSchemaJson = string.IsNullOrEmpty(attr.ReturnSchemaJson) ? "{}" : attr.ReturnSchemaJson,
                danger = attr.Danger.ToString().ToLowerInvariant(),
                requiresConfirmation = attr.RequiresConfirmation,
                declaringType = type.FullName,
                methodName = method.Name
            };

            Map[attr.Id] = new AiToolEntry
            {
                info = info,
                method = method
            };
            ToolList.Add(info);
        }

        private static string GetNamespaceId(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return "misc";
            int dot = toolId.IndexOf('.');
            if (dot <= 0) return toolId.ToLowerInvariant();
            return toolId.Substring(0, dot).ToLowerInvariant();
        }
    }
}
#endif
