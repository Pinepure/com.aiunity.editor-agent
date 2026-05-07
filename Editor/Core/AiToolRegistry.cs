#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    [Serializable]
    public sealed class AiToolInfo
    {
        public string id;
        public string description;
        public string argsSchemaJson;
        public string returnSchemaJson;
        public string danger;
        public bool requiresConfirmation;
        public string declaringType;
        public string methodName;
    }

    [Serializable]
    public sealed class AiToolManifest
    {
        public string protocolVersion;
        public string serviceVersion;
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
        public AiToolInfo[] tools;
    }

    internal sealed class AiToolEntry
    {
        public AiToolInfo info;
        public MethodInfo method;
    }

    internal static class AiToolRegistry
    {
        private static readonly Dictionary<string, AiToolEntry> Map = new Dictionary<string, AiToolEntry>();
        private static readonly List<AiToolInfo> ToolList = new List<AiToolInfo>();
        private static DateTime lastRebuild = DateTime.MinValue;

        public static DateTime LastRebuild
        {
            get { return lastRebuild; }
        }

        public static int Count
        {
            get { return ToolList.Count; }
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
            AiEditorAgentState.Log("info", "Tool registry rebuilt. Tool count: " + ToolList.Count);
        }

        public static bool TryGet(string id, out AiToolEntry entry)
        {
            return Map.TryGetValue(id, out entry);
        }

        public static List<AiToolInfo> GetToolsCopy()
        {
            return new List<AiToolInfo>(ToolList);
        }

        public static AiToolManifest BuildManifest()
        {
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            return new AiToolManifest
            {
                protocolVersion = AiEditorAgentPaths.ProtocolVersion,
                serviceVersion = AiEditorAgentPaths.ServiceVersion,
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
                generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                tools = ToolList.ToArray()
            };
        }

        public static string BuildManifestJson(bool rebuild)
        {
            if (rebuild) Rebuild();
            return JsonUtility.ToJson(BuildManifest(), true);
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
    }
}
#endif
