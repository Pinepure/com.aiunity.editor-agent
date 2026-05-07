#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    public sealed class AiEditorAgentWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Dashboard", "Tools", "Calls", "Logs", "Settings", "AGENT.md" };

        private int tab;
        private Vector2 mainScroll;
        private Vector2 toolListScroll;
        private Vector2 detailScroll;
        private Vector2 logScroll;
        private Vector2 agentScroll;
        private string manifestJson = string.Empty;
        private string agentText = string.Empty;
        private string toolSearch = string.Empty;
        private List<AiToolInfo> tools = new List<AiToolInfo>();
        private string selectedToolId = string.Empty;
        private string toolArgsJson = "{}";
        private string toolResultJson = string.Empty;
        private int settingsPort;
        private int settingsTimeout;
        private bool settingsAutoStart;
        private bool settingsRequireToken;
        private bool settingsConfirmHighRisk;
        private double lastAutoRefresh;

        [MenuItem("Tools/AI Editor Agent/Control Center")]
        public static void Open()
        {
            AiEditorAgentWindow window = GetWindow<AiEditorAgentWindow>("AI Editor Agent");
            window.minSize = new Vector2(920, 620);
            window.Show();
        }

        [MenuItem("Tools/AI Editor Agent/Rebuild Manifest")]
        public static void RebuildManifestMenu()
        {
            AiEditorApiServer.RebuildRegistry();
            UnityEngine.Debug.Log("[AI Editor Agent] Manifest rebuilt.");
        }

        private void OnEnable()
        {
            AiEditorApiServer.Initialize();
            LoadSettingsToFields();
            RefreshAll();
        }

        private void OnGUI()
        {
            Styles.Ensure();
            AutoRefreshLightweight();

            DrawHero();
            DrawToolbar();

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            switch (tab)
            {
                case 0: DrawDashboard(); break;
                case 1: DrawTools(); break;
                case 2: DrawCalls(); break;
                case 3: DrawLogs(); break;
                case 4: DrawSettings(); break;
                case 5: DrawAgentManual(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void AutoRefreshLightweight()
        {
            if (EditorApplication.timeSinceStartup - lastAutoRefresh < 1.0) return;
            lastAutoRefresh = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void DrawHero()
        {
            Rect rect = GUILayoutUtility.GetRect(1, 112, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, Styles.heroBackground);

            Rect accent = new Rect(rect.x, rect.y, 6, rect.height);
            EditorGUI.DrawRect(accent, AiEditorApiServer.IsRunning ? Styles.green : Styles.red);

            Rect content = new Rect(rect.x + 18, rect.y + 14, rect.width - 36, rect.height - 22);
            GUILayout.BeginArea(content);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            GUILayout.Label("AI Unity Editor Agent", Styles.heroTitle);
            GUILayout.Space(3);
            GUILayout.Label("Local API service, auto-registered Editor tools, manifest discovery, prefab generation, compile diagnostics, and asset reference search.", Styles.heroSubTitle);
            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            DrawPill(AiEditorApiServer.IsRunning ? "SERVICE ONLINE" : "SERVICE OFFLINE", AiEditorApiServer.IsRunning ? Styles.green : Styles.red);
            DrawPill(EditorApplication.isCompiling ? "COMPILING" : "COMPILER IDLE", EditorApplication.isCompiling ? Styles.orange : Styles.green);
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            DrawPill(counts.errorCount > 0 ? (counts.errorCount + " ERRORS") : "NO CONSOLE ERRORS", counts.errorCount > 0 ? Styles.red : Styles.green);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.Width(260));
            GUILayout.Label("Endpoint", Styles.smallMuted);
            SelectableCopyLine(AiEditorAgentSettings.ServerUrl);
            GUILayout.Space(6);
            GUILayout.Label("Tools", Styles.smallMuted);
            GUILayout.Label(AiToolRegistry.Count.ToString(), Styles.bigMetric);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int newTab = GUILayout.Toolbar(tab, Tabs, EditorStyles.toolbarButton, GUILayout.Height(26));
            if (newTab != tab)
            {
                tab = newTab;
                GUI.FocusControl(null);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(76))) RefreshAll();
            if (AiEditorApiServer.IsRunning)
            {
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(58))) AiEditorApiServer.Stop();
            }
            else
            {
                if (GUILayout.Button("Start", EditorStyles.toolbarButton, GUILayout.Width(58))) AiEditorApiServer.Start();
            }
            if (GUILayout.Button("Restart", EditorStyles.toolbarButton, GUILayout.Width(72))) AiEditorApiServer.Restart();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDashboard()
        {
            EditorGUILayout.Space(14);
            EditorGUILayout.BeginHorizontal();
            DrawMetricCard("Service", AiEditorApiServer.IsRunning ? "Online" : "Offline", AiEditorApiServer.IsRunning ? "Listening on localhost" : "Not listening", AiEditorApiServer.IsRunning ? Styles.green : Styles.red);
            DrawMetricCard("Manifest", AiToolRegistry.Count + " tools", "Auto-generated from [AiTool] methods", Styles.blue);
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            DrawMetricCard("Compile", EditorApplication.isCompiling ? "Compiling" : "Idle", counts.errorCount + " errors / " + counts.warningCount + " warnings", counts.errorCount > 0 ? Styles.red : Styles.green);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Connection", Styles.sectionTitle);
            KeyValue("Base URL", AiEditorAgentSettings.ServerUrl);
            KeyValue("Manifest", AiEditorAgentSettings.ServerUrl + "/manifest");
            KeyValue("Call", AiEditorAgentSettings.ServerUrl + "/call/{toolId}");
            KeyValue("Token file", AiEditorAgentPaths.TokenPath);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Token", GUILayout.Width(110))) Copy(AiEditorApiServer.Token);
            if (GUILayout.Button("Copy Manifest URL", GUILayout.Width(150))) Copy(AiEditorAgentSettings.ServerUrl + "/manifest");
            if (GUILayout.Button("Reveal Token", GUILayout.Width(110))) RevealFile(AiEditorAgentPaths.TokenPath);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Recommended AI Flow", Styles.sectionTitle);
            GUILayout.Label("1. GET /health", Styles.body);
            GUILayout.Label("2. GET /manifest", Styles.body);
            GUILayout.Label("3. Check compile.status", Styles.body);
            GUILayout.Label("4. Call existing tools", Styles.body);
            GUILayout.Label("5. If missing, install a generated tool with tool.upsert_script", Styles.body);
            GUILayout.Label("6. Wait for compile.status.isCompiling == false", Styles.body);
            GUILayout.Label("7. GET /manifest again", Styles.body);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Current Manifest Snapshot", Styles.sectionTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy Manifest", GUILayout.Width(120))) Copy(manifestJson);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.TextArea(manifestJson, Styles.codeArea, GUILayout.MinHeight(220));
            EditorGUILayout.EndVertical();
        }

        private void DrawTools()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(360));
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("Search", GUILayout.Width(48));
            toolSearch = EditorGUILayout.TextField(toolSearch);
            if (GUILayout.Button("x", GUILayout.Width(24))) toolSearch = string.Empty;
            EditorGUILayout.EndHorizontal();

            toolListScroll = EditorGUILayout.BeginScrollView(toolListScroll, EditorStyles.helpBox, GUILayout.MinHeight(460));
            for (int i = 0; i < tools.Count; i++)
            {
                AiToolInfo info = tools[i];
                if (!MatchesToolSearch(info)) continue;
                DrawToolListItem(info);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            AiToolInfo selected = FindSelectedTool();
            if (selected == null)
            {
                GUILayout.Label("Select a tool", Styles.sectionTitle);
                GUILayout.Label("Choose a tool from the left to inspect its schema, source method, risk level, and execute it with JSON arguments.", Styles.body);
            }
            else
            {
                DrawToolDetails(selected);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolListItem(AiToolInfo info)
        {
            bool selected = selectedToolId == info.id;
            GUIStyle style = selected ? Styles.selectedCard : EditorStyles.helpBox;
            EditorGUILayout.BeginVertical(style);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(info.id, Styles.toolIdButton))
            {
                selectedToolId = info.id;
                toolArgsJson = "{}";
                toolResultJson = string.Empty;
            }
            GUILayout.FlexibleSpace();
            DrawTinyDanger(info.danger, info.requiresConfirmation);
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(info.description, Styles.smallMuted);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolDetails(AiToolInfo info)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(info.id, Styles.sectionTitle);
            GUILayout.FlexibleSpace();
            DrawTinyDanger(info.danger, info.requiresConfirmation);
            if (GUILayout.Button("Copy ID", GUILayout.Width(80))) Copy(info.id);
            if (GUILayout.Button("Copy curl", GUILayout.Width(90))) CopyCurl(info.id, toolArgsJson);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label(info.description, Styles.body);
            GUILayout.Space(8);
            KeyValue("Declaring type", info.declaringType);
            KeyValue("Method", info.methodName);
            KeyValue("Requires confirmation", info.requiresConfirmation ? "Yes" : "No");

            GUILayout.Space(10);
            GUILayout.Label("Arguments JSON", Styles.subSectionTitle);
            toolArgsJson = EditorGUILayout.TextArea(toolArgsJson, Styles.codeArea, GUILayout.MinHeight(110));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Execute Tool", GUILayout.Width(130))) ExecuteSelectedTool(info);
            if (GUILayout.Button("Reset Args", GUILayout.Width(90))) toolArgsJson = "{}";
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("Argument Schema", Styles.subSectionTitle);
            EditorGUILayout.TextArea(info.argsSchemaJson, Styles.codeArea, GUILayout.MinHeight(82));
            GUILayout.Label("Return Schema", Styles.subSectionTitle);
            EditorGUILayout.TextArea(info.returnSchemaJson, Styles.codeArea, GUILayout.MinHeight(82));

            if (!string.IsNullOrEmpty(toolResultJson))
            {
                GUILayout.Space(10);
                GUILayout.Label("Last Result", Styles.subSectionTitle);
                EditorGUILayout.TextArea(toolResultJson, Styles.codeArea, GUILayout.MinHeight(150));
            }
        }

        private void DrawCalls()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Recent Tool Calls", Styles.sectionTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", GUILayout.Width(80))) AiEditorAgentState.ClearToolCalls();
            EditorGUILayout.EndHorizontal();

            List<AiToolCallEntry> calls = AiEditorAgentState.GetToolCalls(200);
            if (calls.Count == 0)
            {
                GUILayout.Label("No tool calls recorded yet.", Styles.body);
            }
            else
            {
                for (int i = calls.Count - 1; i >= 0; i--)
                {
                    AiToolCallEntry c = calls[i];
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUILayout.Label(c.ok ? "OK" : "ERR", c.ok ? Styles.okLabel : Styles.errorLabel, GUILayout.Width(42));
                    GUILayout.Label(c.time, Styles.smallMuted, GUILayout.Width(170));
                    GUILayout.Label(c.toolId, Styles.monoLabel, GUILayout.Width(240));
                    GUILayout.Label(c.durationMs + " ms", Styles.smallMuted, GUILayout.Width(75));
                    GUILayout.Label(c.message, Styles.body);
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawLogs()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Service Logs", GUILayout.Width(150))) AiEditorAgentState.ClearServiceLogs();
            if (GUILayout.Button("Clear Captured Console", GUILayout.Width(180))) AiEditorAgentState.ClearCapturedConsole();
            if (GUILayout.Button("Clear Unity Console", GUILayout.Width(150)))
            {
                string msg;
                AiConsoleUtility.Clear(out msg);
                AiEditorAgentState.ClearCapturedConsole();
                AiEditorAgentState.Log("info", msg);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            logScroll = EditorGUILayout.BeginScrollView(logScroll, EditorStyles.helpBox, GUILayout.MinHeight(500));
            GUILayout.Label("Service Logs", Styles.sectionTitle);
            List<AiServiceLogEntry> logs = AiEditorAgentState.GetServiceLogs(120);
            for (int i = logs.Count - 1; i >= 0; i--)
            {
                AiServiceLogEntry e = logs[i];
                GUILayout.Label("[" + e.time + "] " + e.level.ToUpperInvariant() + "  " + e.message, Styles.monoSmall);
            }

            GUILayout.Space(14);
            GUILayout.Label("Captured Console Errors and Warnings", Styles.sectionTitle);
            List<AiCapturedConsoleEntry> console = AiEditorAgentState.GetConsoleEntries(120, false);
            for (int i = console.Count - 1; i >= 0; i--)
            {
                AiCapturedConsoleEntry e = console[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("[" + e.time + "] " + e.type, e.type == "Warning" ? Styles.warningLabel : Styles.errorLabel);
                GUILayout.Label(e.condition, Styles.body);
                if (!string.IsNullOrEmpty(e.stackTrace)) GUILayout.Label(e.stackTrace, Styles.monoSmall);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Service Settings", Styles.sectionTitle);
            settingsAutoStart = EditorGUILayout.ToggleLeft("Auto-start service when Unity loads or scripts reload", settingsAutoStart);
            settingsRequireToken = EditorGUILayout.ToggleLeft("Require X-Unity-Ai-Token for protected endpoints", settingsRequireToken);
            settingsConfirmHighRisk = EditorGUILayout.ToggleLeft("Always confirm high-risk tools", settingsConfirmHighRisk);
            settingsPort = EditorGUILayout.IntField("Port", settingsPort);
            settingsTimeout = EditorGUILayout.IntField("Tool timeout ms", settingsTimeout);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Settings", GUILayout.Width(120))) SaveSettingsFromFields();
            if (GUILayout.Button("Save and Restart", GUILayout.Width(140)))
            {
                SaveSettingsFromFields();
                AiEditorApiServer.Restart();
            }
            if (GUILayout.Button("Reset Fields", GUILayout.Width(110))) LoadSettingsToFields();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Security", Styles.sectionTitle);
            KeyValue("Token path", AiEditorAgentPaths.TokenPath);
            KeyValue("Generated tools folder", AiEditorAgentPaths.GeneratedToolsFolder);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Token", GUILayout.Width(110))) Copy(AiEditorApiServer.Token);
            if (GUILayout.Button("Regenerate Token", GUILayout.Width(150)))
            {
                if (EditorUtility.DisplayDialog("Regenerate AI Editor Agent token?", "Existing AI clients must reload the token file after regeneration.", "Regenerate", "Cancel"))
                {
                    AiEditorApiServer.RegenerateToken();
                }
            }
            if (GUILayout.Button("Reveal Token", GUILayout.Width(110))) RevealFile(AiEditorAgentPaths.TokenPath);
            if (GUILayout.Button("Reveal Generated Tools", GUILayout.Width(170)))
            {
                AiEditorAgentPaths.EnsureGeneratedToolsFolder();
                EditorUtility.RevealInFinder(AiEditorAgentPaths.GeneratedToolsFolderAbsolute);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Package", Styles.sectionTitle);
            KeyValue("Package name", AiEditorAgentPaths.PackageName);
            KeyValue("Version", AiEditorAgentPaths.ServiceVersion);
            KeyValue("AGENT.md", AiEditorAgentPaths.AgentMdPath);
            if (GUILayout.Button("Copy AGENT.md", GUILayout.Width(130))) Copy(agentText);
            EditorGUILayout.EndVertical();
        }

        private void DrawAgentManual()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("AGENT.md", Styles.sectionTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", GUILayout.Width(80))) LoadAgentText();
            if (GUILayout.Button("Copy", GUILayout.Width(80))) Copy(agentText);
            if (GUILayout.Button("Reveal", GUILayout.Width(80))) RevealFile(AiEditorAgentPaths.AgentMdPath);
            EditorGUILayout.EndHorizontal();
            agentScroll = EditorGUILayout.BeginScrollView(agentScroll, GUILayout.MinHeight(520));
            EditorGUILayout.TextArea(agentText, Styles.codeArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawMetricCard(string title, string value, string subtitle, Color accent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(92));
            Rect r = GUILayoutUtility.GetRect(1, 4, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, accent);
            GUILayout.Label(title, Styles.smallMuted);
            GUILayout.Label(value, Styles.bigMetric);
            GUILayout.Label(subtitle, Styles.smallMuted);
            EditorGUILayout.EndVertical();
        }

        private void KeyValue(string key, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(key, Styles.smallMuted, GUILayout.Width(160));
            GUILayout.Label(value ?? string.Empty, Styles.monoLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void SelectableCopyLine(string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.SelectableLabel(value, Styles.monoLabel, GUILayout.Height(18));
            if (GUILayout.Button("Copy", GUILayout.Width(55))) Copy(value);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPill(string text, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, Styles.pill);
            GUI.color = old;
        }

        private void DrawTinyDanger(string danger, bool confirm)
        {
            string label = string.IsNullOrEmpty(danger) ? "low" : danger;
            if (confirm) label += " + confirm";
            GUIStyle style = danger == "high" ? Styles.highPill : (danger == "medium" ? Styles.mediumPill : Styles.lowPill);
            GUILayout.Label(label, style, GUILayout.Width(confirm ? 112 : 64));
        }

        private void RefreshAll()
        {
            AiToolRegistry.Rebuild();
            tools = AiToolRegistry.GetToolsCopy();
            manifestJson = AiToolRegistry.BuildManifestJson(false);
            LoadAgentText();
            if (string.IsNullOrEmpty(selectedToolId) && tools.Count > 0) selectedToolId = tools[0].id;
        }

        private void LoadAgentText()
        {
            try
            {
                string path = AiEditorAgentPaths.AgentMdPath;
                agentText = !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : "AGENT.md was not found.";
            }
            catch (Exception e)
            {
                agentText = e.Message;
            }
        }

        private bool MatchesToolSearch(AiToolInfo info)
        {
            if (string.IsNullOrEmpty(toolSearch)) return true;
            string s = toolSearch.ToLowerInvariant();
            return (info.id != null && info.id.ToLowerInvariant().Contains(s))
                || (info.description != null && info.description.ToLowerInvariant().Contains(s))
                || (info.declaringType != null && info.declaringType.ToLowerInvariant().Contains(s));
        }

        private AiToolInfo FindSelectedTool()
        {
            if (tools == null) return null;
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].id == selectedToolId) return tools[i];
            }
            return null;
        }

        private void ExecuteSelectedTool(AiToolInfo info)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                AiToolEntry entry;
                if (!AiToolRegistry.TryGet(info.id, out entry))
                {
                    AiToolRegistry.Rebuild();
                    if (!AiToolRegistry.TryGet(info.id, out entry)) throw new Exception("Tool not found: " + info.id);
                }

                bool shouldConfirm = info.requiresConfirmation || (info.danger == "high" && AiEditorAgentSettings.ConfirmHighRiskTools);
                if (shouldConfirm)
                {
                    bool allowed = EditorUtility.DisplayDialog("Execute Tool", "Execute " + info.id + "?\n\nArguments:\n" + toolArgsJson, "Execute", "Cancel");
                    if (!allowed) return;
                }

                string result = (string)entry.method.Invoke(null, new object[] { string.IsNullOrWhiteSpace(toolArgsJson) ? "{}" : toolArgsJson });
                toolResultJson = "{\n  \"ok\": true,\n  \"result\": " + AiJson.AsJsonValue(result) + "\n}";
                AiEditorAgentState.RecordCall(info.id, true, sw.ElapsedMilliseconds, "Executed from Control Center");
                RefreshAll();
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                toolResultJson = "{\n  \"ok\": false,\n  \"error\": " + AiJson.Quote(inner.Message) + "\n}";
                AiEditorAgentState.RecordCall(info.id, false, sw.ElapsedMilliseconds, inner.Message);
            }
            catch (Exception e)
            {
                toolResultJson = "{\n  \"ok\": false,\n  \"error\": " + AiJson.Quote(e.Message) + "\n}";
                AiEditorAgentState.RecordCall(info.id, false, sw.ElapsedMilliseconds, e.Message);
            }
        }

        private void LoadSettingsToFields()
        {
            settingsPort = AiEditorAgentSettings.Port;
            settingsTimeout = AiEditorAgentSettings.ToolTimeoutMs;
            settingsAutoStart = AiEditorAgentSettings.AutoStart;
            settingsRequireToken = AiEditorAgentSettings.RequireToken;
            settingsConfirmHighRisk = AiEditorAgentSettings.ConfirmHighRiskTools;
        }

        private void SaveSettingsFromFields()
        {
            AiEditorAgentSettings.Port = settingsPort;
            AiEditorAgentSettings.ToolTimeoutMs = settingsTimeout;
            AiEditorAgentSettings.AutoStart = settingsAutoStart;
            AiEditorAgentSettings.RequireToken = settingsRequireToken;
            AiEditorAgentSettings.ConfirmHighRiskTools = settingsConfirmHighRisk;
            LoadSettingsToFields();
            RefreshAll();
        }

        private void CopyCurl(string toolId, string body)
        {
            string curl = "curl -X POST \\\n  -H \"Content-Type: application/json\" \\\n  -H \"X-Unity-Ai-Token: <TOKEN>\" \\\n  -d '" + (string.IsNullOrWhiteSpace(body) ? "{}" : body.Replace("'", "'\\''")) + "' \\\n  " + AiEditorAgentSettings.ServerUrl + "/call/" + toolId;
            Copy(curl);
        }

        private void Copy(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
        }

        private void RevealFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path) || Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) EditorUtility.RevealInFinder(dir);
            }
        }

        private static class Styles
        {
            public static bool ready;
            public static GUIStyle heroTitle;
            public static GUIStyle heroSubTitle;
            public static GUIStyle sectionTitle;
            public static GUIStyle subSectionTitle;
            public static GUIStyle body;
            public static GUIStyle smallMuted;
            public static GUIStyle bigMetric;
            public static GUIStyle monoLabel;
            public static GUIStyle monoSmall;
            public static GUIStyle codeArea;
            public static GUIStyle pill;
            public static GUIStyle lowPill;
            public static GUIStyle mediumPill;
            public static GUIStyle highPill;
            public static GUIStyle selectedCard;
            public static GUIStyle toolIdButton;
            public static GUIStyle okLabel;
            public static GUIStyle warningLabel;
            public static GUIStyle errorLabel;
            public static Color heroBackground;
            public static Color green;
            public static Color red;
            public static Color orange;
            public static Color blue;

            public static void Ensure()
            {
                if (ready) return;
                ready = true;

                bool pro = EditorGUIUtility.isProSkin;
                heroBackground = pro ? new Color(0.11f, 0.13f, 0.17f) : new Color(0.86f, 0.89f, 0.94f);
                green = new Color(0.15f, 0.68f, 0.38f);
                red = new Color(0.88f, 0.24f, 0.24f);
                orange = new Color(0.95f, 0.58f, 0.16f);
                blue = new Color(0.24f, 0.49f, 0.90f);

                heroTitle = new GUIStyle(EditorStyles.boldLabel);
                heroTitle.fontSize = 24;
                heroTitle.normal.textColor = pro ? Color.white : new Color(0.08f, 0.10f, 0.14f);

                heroSubTitle = new GUIStyle(EditorStyles.label);
                heroSubTitle.wordWrap = true;
                heroSubTitle.normal.textColor = pro ? new Color(0.76f, 0.80f, 0.88f) : new Color(0.20f, 0.24f, 0.30f);

                sectionTitle = new GUIStyle(EditorStyles.boldLabel);
                sectionTitle.fontSize = 16;

                subSectionTitle = new GUIStyle(EditorStyles.boldLabel);
                subSectionTitle.fontSize = 12;

                body = new GUIStyle(EditorStyles.label);
                body.wordWrap = true;

                smallMuted = new GUIStyle(EditorStyles.label);
                smallMuted.wordWrap = true;
                smallMuted.normal.textColor = pro ? new Color(0.68f, 0.72f, 0.78f) : new Color(0.34f, 0.36f, 0.40f);

                bigMetric = new GUIStyle(EditorStyles.boldLabel);
                bigMetric.fontSize = 22;

                monoLabel = new GUIStyle(EditorStyles.label);
                monoLabel.font = EditorStyles.textArea.font;
                monoLabel.wordWrap = true;

                monoSmall = new GUIStyle(EditorStyles.miniLabel);
                monoSmall.font = EditorStyles.textArea.font;
                monoSmall.wordWrap = true;

                codeArea = new GUIStyle(EditorStyles.textArea);
                codeArea.font = EditorStyles.textArea.font;
                codeArea.wordWrap = false;

                pill = new GUIStyle(EditorStyles.miniButton);
                pill.fontStyle = FontStyle.Bold;
                pill.normal.textColor = Color.white;
                pill.alignment = TextAnchor.MiddleCenter;
                pill.padding = new RectOffset(8, 8, 3, 3);

                lowPill = new GUIStyle(EditorStyles.miniButton);
                lowPill.normal.textColor = green;
                lowPill.fontStyle = FontStyle.Bold;

                mediumPill = new GUIStyle(EditorStyles.miniButton);
                mediumPill.normal.textColor = orange;
                mediumPill.fontStyle = FontStyle.Bold;

                highPill = new GUIStyle(EditorStyles.miniButton);
                highPill.normal.textColor = red;
                highPill.fontStyle = FontStyle.Bold;

                selectedCard = new GUIStyle(EditorStyles.helpBox);
                selectedCard.normal.background = Texture2D.grayTexture;

                toolIdButton = new GUIStyle(EditorStyles.label);
                toolIdButton.fontStyle = FontStyle.Bold;
                toolIdButton.normal.textColor = blue;
                toolIdButton.hover.textColor = blue;

                okLabel = new GUIStyle(EditorStyles.boldLabel);
                okLabel.normal.textColor = green;
                warningLabel = new GUIStyle(EditorStyles.boldLabel);
                warningLabel.normal.textColor = orange;
                errorLabel = new GUIStyle(EditorStyles.boldLabel);
                errorLabel.normal.textColor = red;
            }
        }
    }
}
#endif
