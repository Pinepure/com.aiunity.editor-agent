#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    internal static class ToolManagementTools
    {
        [Serializable]
        private sealed class UpsertScriptArgs
        {
            public string fileName;
            public string source;
        }

        [Serializable]
        private sealed class DeleteScriptArgs
        {
            public string fileName;
        }

        [Serializable]
        private sealed class TemplateArgs
        {
            public string toolId;
            public string className;
            public string description;
        }

        [AiTool(
            "tool.upsert_script",
            "Creates or updates an AI-generated Editor tool script under Assets/Editor/AiUnityEditorAgent/GeneratedTools/.",
            @"{""type"":""object"",""properties"":{""fileName"":{""type"":""string""},""source"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}",
            Danger = AiToolDanger.High,
            RequiresConfirmation = true
        )]
        public static string UpsertScript(string argsJson)
        {
            UpsertScriptArgs args = AiJson.FromJsonOrThrow<UpsertScriptArgs>(argsJson);
            if (args == null) throw new Exception("Arguments are required.");
            if (string.IsNullOrEmpty(args.fileName)) throw new Exception("fileName is required.");
            if (string.IsNullOrEmpty(args.source)) throw new Exception("source is required.");

            string safeName = Path.GetFileName(args.fileName.Trim());
            if (safeName != args.fileName.Trim()) throw new Exception("Subdirectories are not allowed. Provide only a file name.");
            if (!safeName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) throw new Exception("Only .cs files are allowed.");
            if (safeName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new Exception("Invalid file name: " + safeName);

            AiEditorAgentPaths.EnsureGeneratedToolsFolder();
            string absolutePath = Path.Combine(AiEditorAgentPaths.GeneratedToolsFolderAbsolute, safeName).Replace('\\', '/');
            File.WriteAllText(absolutePath, args.source, Encoding.UTF8);

            string assetPath = AiEditorAgentPaths.GeneratedToolsFolder + "/" + safeName;
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            bool hasToolAttribute = args.source.IndexOf("[AiTool", StringComparison.Ordinal) >= 0 || args.source.IndexOf("AiTool(", StringComparison.Ordinal) >= 0;
            return "{\"path\":" + AiJson.Quote(assetPath)
                + ",\"hasAiToolAttribute\":" + AiJson.Bool(hasToolAttribute)
                + ",\"message\":" + AiJson.Quote(hasToolAttribute ? "Script written. Unity will compile and the manifest will refresh after domain reload." : "Script written, but no [AiTool] attribute was detected.")
                + "}";
        }

        [AiTool(
            "tool.list_generated",
            "Lists AI-generated Editor tool scripts.",
            "{}",
            @"{""type"":""object"",""properties"":{""items"":{""type"":""array""}}}"
        )]
        public static string ListGenerated(string argsJson)
        {
            AiEditorAgentPaths.EnsureGeneratedToolsFolder();
            string[] files = Directory.GetFiles(AiEditorAgentPaths.GeneratedToolsFolderAbsolute, "*.cs", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"folder\":").Append(AiJson.Quote(AiEditorAgentPaths.GeneratedToolsFolder)).Append(",\"items\":[");
            for (int i = 0; i < files.Length; i++)
            {
                if (i > 0) sb.Append(',');
                FileInfo info = new FileInfo(files[i]);
                string assetPath = AiEditorAgentPaths.GeneratedToolsFolder + "/" + info.Name;
                sb.Append('{');
                sb.Append("\"fileName\":").Append(AiJson.Quote(info.Name)).Append(',');
                sb.Append("\"path\":").Append(AiJson.Quote(assetPath)).Append(',');
                sb.Append("\"size\":").Append(AiJson.Number(info.Length)).Append(',');
                sb.Append("\"modified\":").Append(AiJson.Quote(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "tool.delete_generated",
            "Deletes one AI-generated Editor tool script by fileName.",
            @"{""type"":""object"",""properties"":{""fileName"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""deleted"":{""type"":""boolean""}}}",
            Danger = AiToolDanger.High,
            RequiresConfirmation = true
        )]
        public static string DeleteGenerated(string argsJson)
        {
            DeleteScriptArgs args = AiJson.FromJsonOrThrow<DeleteScriptArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.fileName)) throw new Exception("fileName is required.");
            string safeName = Path.GetFileName(args.fileName.Trim());
            if (safeName != args.fileName.Trim()) throw new Exception("Subdirectories are not allowed.");
            if (!safeName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) throw new Exception("Only .cs files are allowed.");

            string assetPath = AiEditorAgentPaths.GeneratedToolsFolder + "/" + safeName;
            bool deleted = AssetDatabase.DeleteAsset(assetPath);
            if (!deleted)
            {
                string absolutePath = Path.Combine(AiEditorAgentPaths.GeneratedToolsFolderAbsolute, safeName).Replace('\\', '/');
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                    deleted = true;
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return "{\"path\":" + AiJson.Quote(assetPath) + ",\"deleted\":" + AiJson.Bool(deleted) + "}";
        }

        [AiTool(
            "tool.get_template",
            "Returns a safe C# template for a generated AI Editor tool.",
            @"{""type"":""object"",""properties"":{""toolId"":{""type"":""string""},""className"":{""type"":""string""},""description"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""source"":{""type"":""string""}}}"
        )]
        public static string GetTemplate(string argsJson)
        {
            TemplateArgs args = AiJson.FromJsonOrThrow<TemplateArgs>(argsJson);
            string toolId = args == null || string.IsNullOrEmpty(args.toolId) ? "generated.example" : args.toolId;
            string className = args == null || string.IsNullOrEmpty(args.className) ? "GeneratedExampleTools" : SanitizeIdentifier(args.className);
            string description = args == null || string.IsNullOrEmpty(args.description) ? "Generated example tool." : args.description;
            string source = BuildTemplate(toolId, className, description);
            return "{\"fileName\":" + AiJson.Quote(className + ".cs") + ",\"source\":" + AiJson.Quote(source) + "}";
        }

        private static string BuildTemplate(string toolId, string className, string description)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("using System;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using AiUnity.EditorAgent;");
            sb.AppendLine();
            sb.AppendLine("namespace AiUnity.EditorAgent.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class " + className);
            sb.AppendLine("    {");
            sb.AppendLine("        [Serializable]");
            sb.AppendLine("        private sealed class Args");
            sb.AppendLine("        {");
            sb.AppendLine("            public string name;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [AiTool(");
            sb.AppendLine("            \"" + EscapeForCSharp(toolId) + "\",");
            sb.AppendLine("            \"" + EscapeForCSharp(description) + "\",");
            sb.AppendLine("            @\"{\"\"type\"\":\"\"object\"\",\"\"properties\"\":{\"\"name\"\":{\"\"type\"\":\"\"string\"\"}}}\",");
            sb.AppendLine("            @\"{\"\"type\"\":\"\"object\"\",\"\"properties\"\":{\"\"message\"\":{\"\"type\"\":\"\"string\"\"}}}\"");
            sb.AppendLine("        )]");
            sb.AppendLine("        public static string Run(string argsJson)");
            sb.AppendLine("        {");
            sb.AppendLine("            Args args = JsonUtility.FromJson<Args>(string.IsNullOrEmpty(argsJson) ? \"{}\" : argsJson);");
            sb.AppendLine("            string name = args == null || string.IsNullOrEmpty(args.name) ? \"Unity\" : args.name;");
            sb.AppendLine("            return \"{\\\"message\\\":\" + AiJson.Quote(\"Hello, \" + name) + \"}\";");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            return sb.ToString();
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return "GeneratedTool";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || (i > 0 && c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
            }
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, "Generated");
            return sb.ToString();
        }

        private static string EscapeForCSharp(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
#endif
