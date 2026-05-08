#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    public sealed class AiEditorAgentWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Dashboard", "Tools", "Calls", "Logs", "Settings", "AGENT.md" };
        private const float HeroWindowSeconds = 90f;
        private const int HeroSampleCount = 56;

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
        private bool settingsFullAccess;
        private double lastAutoRefresh;

        private struct HeroHoverInfo
        {
            public bool valid;
            public Vector2 point;
            public AiToolCallEntry call;
        }

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
            if (EditorApplication.timeSinceStartup - lastAutoRefresh < 0.18) return;
            lastAutoRefresh = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void DrawHero()
        {
            List<AiToolCallEntry> calls = AiEditorAgentState.GetToolCalls(90);
            AiConsoleCounts counts = AiConsoleUtility.GetCounts();
            AiToolCallEntry latestCall = calls.Count > 0 ? calls[calls.Count - 1] : null;
            int recentErrors = 0;
            long peakDurationMs = 0;
            for (int i = 0; i < calls.Count; i++)
            {
                if (!calls[i].ok) recentErrors++;
                if (calls[i].durationMs > peakDurationMs) peakDurationMs = calls[i].durationMs;
            }

            Rect rect = GUILayoutUtility.GetRect(1, 214, GUILayout.ExpandWidth(true));
            DrawHeroBackground(rect);

            Rect chartRect = new Rect(rect.x + 18, rect.y + 84, rect.width - 296, rect.height - 122);
            HeroHoverInfo hover = DrawHeroTelemetry(chartRect, calls);

            Rect content = new Rect(rect.x + 22, rect.y + 16, rect.width - 286, 52);
            GUILayout.BeginArea(content);
            GUILayout.Label("AI Unity Editor Agent", Styles.heroTitle);
            GUILayout.Space(2);
            GUILayout.Label("Recent call activity and compiler state in a single live view. Hover points to inspect individual tool calls.", Styles.heroSubTitle);
            GUILayout.EndArea();

            Rect pillRow = new Rect(rect.x + 22, rect.y + 54, rect.width - 300, 22);
            GUILayout.BeginArea(pillRow);
            EditorGUILayout.BeginHorizontal();
            DrawPill(AiEditorApiServer.IsRunning ? "SERVICE ONLINE" : "SERVICE OFFLINE", AiEditorApiServer.IsRunning ? Styles.green : Styles.red);
            DrawPill(EditorApplication.isCompiling ? "COMPILING" : "COMPILER IDLE", EditorApplication.isCompiling ? Styles.orange : Styles.green);
            DrawPill(recentErrors > 0 ? (recentErrors + " FAILED CALLS") : "CALLS CLEAN", recentErrors > 0 ? Styles.red : Styles.teal);
            DrawPill(counts.errorCount > 0 ? (counts.errorCount + " CONSOLE ERRORS") : "NO CONSOLE ERRORS", counts.errorCount > 0 ? Styles.red : Styles.green);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();

            Rect sideRect = new Rect(rect.xMax - 258, rect.y + 16, 236, rect.height - 32);
            DrawHeroMetricGrid(sideRect, counts, calls.Count, peakDurationMs, latestCall);

            Rect footerRect = new Rect(rect.x + 18, rect.yMax - 28, rect.width - 36, 18);
            DrawHeroFooter(footerRect, peakDurationMs, latestCall);

            if (hover.valid)
            {
                DrawHeroTooltip(rect, hover);
            }
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

        private void DrawHeroBackground(Rect rect)
        {
            int slices = 24;
            for (int i = 0; i < slices; i++)
            {
                float t0 = i / (float)slices;
                float t1 = (i + 1) / (float)slices;
                float y = Mathf.Lerp(rect.y, rect.yMax, t0);
                float height = Mathf.Lerp(rect.y, rect.yMax, t1) - y + 1f;
                Color color = Color.Lerp(Styles.heroGradientTop, Styles.heroGradientBottom, t0);
                EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, height), color);
            }

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), AiEditorApiServer.IsRunning ? Styles.green : Styles.red);
            EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), new Color(1f, 1f, 1f, 0.015f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Styles.heroGrid);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.04f));
        }

        private HeroHoverInfo DrawHeroTelemetry(Rect rect, List<AiToolCallEntry> calls)
        {
            HeroHoverInfo hover = new HeroHoverInfo();
            if (rect.width < 32f || rect.height < 24f) return hover;

            Event current = Event.current;
            float[] latencySamples = new float[HeroSampleCount];
            float[] volumeSamples = new float[HeroSampleCount];
            DateTime latestTime = DateTime.Now;
            if (calls.Count > 0)
            {
                DateTime parsedLatest;
                if (TryParseToolCallTime(calls[calls.Count - 1], out parsedLatest))
                {
                    latestTime = parsedLatest;
                }
            }
            DateTime startTime = latestTime.AddSeconds(-HeroWindowSeconds);

            for (int i = 0; i < calls.Count; i++)
            {
                DateTime callTime;
                if (!TryParseToolCallTime(calls[i], out callTime))
                {
                    float fallbackOffset = Mathf.Lerp(0f, HeroWindowSeconds, calls.Count <= 1 ? 1f : i / (float)(calls.Count - 1));
                    callTime = startTime.AddSeconds(fallbackOffset);
                }

                float normalizedTime = Mathf.Clamp01((float)(callTime - startTime).TotalSeconds / HeroWindowSeconds);
                int sampleIndex = Mathf.Clamp(Mathf.RoundToInt(normalizedTime * (HeroSampleCount - 1)), 0, HeroSampleCount - 1);
                float durationValue = Mathf.Clamp01(calls[i].durationMs / 900f);
                latencySamples[sampleIndex] = Mathf.Max(latencySamples[sampleIndex], durationValue);
                volumeSamples[sampleIndex] += calls[i].ok ? 1f : 1.45f;
            }

            float now = (float)EditorApplication.timeSinceStartup;
            float maxVolume = 0f;
            for (int i = 0; i < HeroSampleCount; i++)
            {
                if (volumeSamples[i] > maxVolume) maxVolume = volumeSamples[i];
            }
            if (maxVolume > 0.001f)
            {
                for (int i = 0; i < HeroSampleCount; i++)
                {
                    volumeSamples[i] = Mathf.Clamp01(volumeSamples[i] / maxVolume);
                }
            }

            for (int i = 0; i < HeroSampleCount; i++)
            {
                float phase = now * 1.15f + i * 0.18f;
                float idle = 0.045f + Mathf.Sin(phase) * 0.008f + Mathf.Sin(phase * 0.58f) * 0.006f;
                latencySamples[i] = Mathf.Clamp01(Mathf.Max(latencySamples[i], idle));
            }

            SmoothSamples(latencySamples, 3);
            SmoothSamples(volumeSamples, 2);

            float left = rect.x + 2f;
            float right = rect.xMax - 2f;
            float top = rect.y + 6f;
            float bottom = rect.yMax - 8f;
            float height = bottom - top;
            float step = (right - left) / (HeroSampleCount - 1);

            Handles.BeginGUI();
            Handles.color = Styles.heroGrid;
            for (int i = 0; i <= 3; i++)
            {
                float y = Mathf.Lerp(top, bottom, i / 3f);
                Handles.DrawLine(new Vector3(left, y), new Vector3(right, y));
            }
            for (int i = 0; i < HeroSampleCount; i += 8)
            {
                float x = left + step * i;
                Handles.DrawLine(new Vector3(x, top), new Vector3(x, bottom));
            }

            Vector3[] trafficLine = new Vector3[HeroSampleCount];
            Vector3[] mainLine = new Vector3[HeroSampleCount];
            Vector3[] fill = new Vector3[HeroSampleCount + 2];
            fill[0] = new Vector3(left, bottom, 0f);
            for (int i = 0; i < HeroSampleCount; i++)
            {
                float x = left + step * i;
                float trafficY = Mathf.Lerp(bottom - 2f, top + height * 0.48f, Mathf.Clamp01(volumeSamples[i] * 0.75f));
                float amplitude = Mathf.Clamp01(latencySamples[i] * 0.74f + volumeSamples[i] * 0.18f + 0.04f);
                float y = Mathf.Lerp(bottom - 2f, top + 4f, amplitude);

                trafficLine[i] = new Vector3(x, trafficY, 0f);
                mainLine[i] = new Vector3(x, y, 0f);
                fill[i + 1] = mainLine[i];
            }
            fill[fill.Length - 1] = new Vector3(right, bottom, 0f);

            Handles.color = new Color(Styles.teal.r, Styles.teal.g, Styles.teal.b, 0.10f);
            Handles.DrawAAConvexPolygon(fill);
            Handles.color = new Color(Styles.teal.r, Styles.teal.g, Styles.teal.b, 0.20f);
            Handles.DrawAAPolyLine(6f, mainLine);
            Handles.color = Styles.teal;
            Handles.DrawAAPolyLine(2.25f, mainLine);
            Handles.color = new Color(Styles.blueSoft.r, Styles.blueSoft.g, Styles.blueSoft.b, 0.85f);
            Handles.DrawAAPolyLine(1.5f, trafficLine);

            int startMarker = Mathf.Max(0, calls.Count - 12);
            for (int i = startMarker; i < calls.Count; i++)
            {
                AiToolCallEntry call = calls[i];
                DateTime callTime;
                if (!TryParseToolCallTime(call, out callTime))
                {
                    float fallbackOffset = Mathf.Lerp(0f, HeroWindowSeconds, calls.Count <= 1 ? 1f : i / (float)(calls.Count - 1));
                    callTime = startTime.AddSeconds(fallbackOffset);
                }

                float normalizedTime = Mathf.Clamp01((float)(callTime - startTime).TotalSeconds / HeroWindowSeconds);
                float x = Mathf.Lerp(left, right, normalizedTime);
                float amplitude = Mathf.Clamp01(call.durationMs / 900f);
                float y = Mathf.Lerp(bottom - 2f, top + 4f, Mathf.Clamp01(amplitude * 0.74f + 0.10f));
                float pulse = 1f + Mathf.Sin(now * 3.2f + i * 0.45f) * 0.10f;
                float radius = (call.ok ? 2.7f : 3.5f) * pulse;
                Color markerColor = call.ok ? Styles.teal : Styles.red;

                Handles.color = new Color(markerColor.r, markerColor.g, markerColor.b, 0.14f);
                Handles.DrawSolidDisc(new Vector3(x, y, 0f), Vector3.forward, radius + 2.1f);
                Handles.color = markerColor;
                Handles.DrawSolidDisc(new Vector3(x, y, 0f), Vector3.forward, radius);

                if ((current.mousePosition - new Vector2(x, y)).sqrMagnitude <= 64f)
                {
                    hover.valid = true;
                    hover.point = new Vector2(x, y);
                    hover.call = call;
                }
            }
            Handles.EndGUI();

            GUI.Label(new Rect(rect.x + 2f, rect.y - 2f, 120f, 18f), "Latency curve", Styles.microLabel);
            GUI.Label(new Rect(rect.xMax - 140f, rect.y - 2f, 140f, 18f), "Traffic overlay", Styles.microLabelRight);
            return hover;
        }

        private void DrawHeroMetricGrid(Rect rect, AiConsoleCounts counts, int recentCallCount, long peakDurationMs, AiToolCallEntry latestCall)
        {
            float gap = 8f;
            float cardWidth = (rect.width - gap) * 0.5f;
            float cardHeight = (rect.height - gap) * 0.5f;

            DrawHeroMetricCard(new Rect(rect.x, rect.y, cardWidth, cardHeight),
                "Service",
                AiEditorApiServer.IsRunning ? "Online" : "Offline",
                AiEditorApiServer.IsRunning ? "localhost" : "service halted",
                AiEditorApiServer.IsRunning ? Styles.green : Styles.red);

            DrawHeroMetricCard(new Rect(rect.x + cardWidth + gap, rect.y, cardWidth, cardHeight),
                "Manifest",
                AiToolRegistry.Count.ToString(),
                "tools registered",
                Styles.blue);

            string compileValue = counts.errorCount > 0
                ? counts.errorCount + " err"
                : (EditorApplication.isCompiling ? "Compiling" : "Idle");
            string compileSubtitle = EditorApplication.isCompiling
                ? "script reload"
                : counts.warningCount + " warn";
            DrawHeroMetricCard(new Rect(rect.x, rect.y + cardHeight + gap, cardWidth, cardHeight),
                "Compile",
                compileValue,
                compileSubtitle,
                counts.errorCount > 0 ? Styles.red : (EditorApplication.isCompiling ? Styles.orange : Styles.green));

            string pulseValue = recentCallCount > 0 ? recentCallCount + " calls" : "No calls";
            string pulseSubtitle = latestCall != null
                ? ("peak " + FormatDuration(peakDurationMs))
                : "awaiting traffic";
            DrawHeroMetricCard(new Rect(rect.x + cardWidth + gap, rect.y + cardHeight + gap, cardWidth, cardHeight),
                "Pulse",
                pulseValue,
                pulseSubtitle,
                Styles.teal);
        }

        private void DrawHeroMetricCard(Rect rect, string title, string value, string subtitle, Color accent)
        {
            EditorGUI.DrawRect(rect, Styles.heroCardBackground);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), accent);

            float padding = 9f;
            float contentWidth = rect.width - padding * 2f;
            GUI.Label(new Rect(rect.x + padding, rect.y + 8f, contentWidth, 14f), title, Styles.microLabel);
            GUI.Label(new Rect(rect.x + padding, rect.y + 24f, contentWidth, 24f), value, Styles.heroMetricValue);
            GUI.Label(new Rect(rect.x + padding, rect.yMax - 20f, contentWidth, 14f), subtitle, Styles.heroMetricSubTitle);
        }

        private void DrawHeroFooter(Rect rect, long peakDurationMs, AiToolCallEntry latestCall)
        {
            float x = rect.x;
            x = DrawFooterChip(new Rect(x, rect.y, 180f, rect.height), "Endpoint", "127.0.0.1:18777", Styles.blue);
            if (latestCall != null)
            {
                x = DrawFooterChip(new Rect(x + 8f, rect.y, 240f, rect.height), "Latest", ShortenToolId(latestCall.toolId) + "  " + FormatDuration(latestCall.durationMs), latestCall.ok ? Styles.teal : Styles.red);
                DrawFooterChip(new Rect(x + 8f, rect.y, 140f, rect.height), "Peak", FormatDuration(peakDurationMs), Styles.orange);
            }
            else
            {
                DrawFooterChip(new Rect(x + 8f, rect.y, 220f, rect.height), "Latest", "Awaiting first call", Styles.orange);
            }
            GUI.Label(new Rect(rect.xMax - 220f, rect.y, 220f, rect.height), "Hover points to inspect exact timing", Styles.microLabelRight);
        }

        private float DrawFooterChip(Rect rect, string label, string value, Color accent)
        {
            EditorGUI.DrawRect(rect, Styles.heroChipBackground);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), accent);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 1f, rect.width - 12f, rect.height - 2f), label + "  " + value, Styles.chipLabel);
            return rect.xMax;
        }

        private void DrawHeroTooltip(Rect heroRect, HeroHoverInfo hover)
        {
            if (hover.call == null) return;

            float width = 236f;
            float height = 76f;
            float x = Mathf.Clamp(hover.point.x + 14f, heroRect.x + 12f, heroRect.xMax - width - 12f);
            float y = Mathf.Clamp(hover.point.y - height - 10f, heroRect.y + 12f, heroRect.yMax - height - 12f);
            Rect rect = new Rect(x, y, width, height);
            EditorGUI.DrawRect(rect, Styles.heroTooltipBackground);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), hover.call.ok ? Styles.teal : Styles.red);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 16f), ShortenToolId(hover.call.toolId), Styles.tooltipTitle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, 16f), (hover.call.ok ? "ok" : "failed") + "  " + FormatDuration(hover.call.durationMs) + "  " + hover.call.time, Styles.monoSmall);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 48f, rect.width - 20f, 22f), string.IsNullOrEmpty(hover.call.message) ? "No message" : hover.call.message, Styles.smallMuted);
        }

        private void SmoothSamples(float[] samples, int passes)
        {
            if (samples == null || samples.Length < 3 || passes <= 0) return;
            float[] temp = new float[samples.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                Array.Copy(samples, temp, samples.Length);
                for (int i = 1; i < samples.Length - 1; i++)
                {
                    samples[i] = temp[i - 1] * 0.2f + temp[i] * 0.6f + temp[i + 1] * 0.2f;
                }
            }
        }

        private bool TryParseToolCallTime(AiToolCallEntry call, out DateTime time)
        {
            time = DateTime.MinValue;
            if (call == null || string.IsNullOrEmpty(call.time)) return false;
            return DateTime.TryParseExact(call.time, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        private string FormatDuration(long durationMs)
        {
            return durationMs >= 1000 ? (durationMs / 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " s" : durationMs + " ms";
        }

        private string ShortenToolId(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return "unknown";
            if (toolId.Length <= 24) return toolId;
            return toolId.Substring(0, 10) + "..." + toolId.Substring(toolId.Length - 10);
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
            DrawTinyDanger(info.danger, AiEditorAgentSettings.ShouldConfirmTool(info));
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(info.description, Styles.smallMuted);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolDetails(AiToolInfo info)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(info.id, Styles.sectionTitle);
            GUILayout.FlexibleSpace();
            DrawTinyDanger(info.danger, AiEditorAgentSettings.ShouldConfirmTool(info));
            if (GUILayout.Button("Copy ID", GUILayout.Width(80))) Copy(info.id);
            if (GUILayout.Button("Copy curl", GUILayout.Width(90))) CopyCurl(info.id, toolArgsJson);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label(info.description, Styles.body);
            GUILayout.Space(8);
            KeyValue("Declaring type", info.declaringType);
            KeyValue("Method", info.methodName);
            KeyValue("Confirmation required now", AiEditorAgentSettings.ShouldConfirmTool(info) ? "Yes" : "No");

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
            settingsFullAccess = EditorGUILayout.ToggleLeft("Enable full access mode (skip all tool confirmation dialogs)", settingsFullAccess);
            using (new EditorGUI.DisabledScope(settingsFullAccess))
            {
                settingsConfirmHighRisk = EditorGUILayout.ToggleLeft("Always confirm high-risk tools", settingsConfirmHighRisk);
            }
            if (settingsFullAccess)
            {
                EditorGUILayout.HelpBox("Full access mode bypasses all tool confirmation dialogs, including tools that explicitly require confirmation. Token checks and tool-level path restrictions still apply.", MessageType.Warning);
            }
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

                bool shouldConfirm = AiEditorAgentSettings.ShouldConfirmTool(info);
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
            settingsFullAccess = AiEditorAgentSettings.FullAccessEnabled;
        }

        private void SaveSettingsFromFields()
        {
            AiEditorAgentSettings.Port = settingsPort;
            AiEditorAgentSettings.ToolTimeoutMs = settingsTimeout;
            AiEditorAgentSettings.AutoStart = settingsAutoStart;
            AiEditorAgentSettings.RequireToken = settingsRequireToken;
            AiEditorAgentSettings.ConfirmHighRiskTools = settingsConfirmHighRisk;
            AiEditorAgentSettings.FullAccessEnabled = settingsFullAccess;
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
            public static GUIStyle heroMetricValue;
            public static GUIStyle heroMetricSubTitle;
            public static GUIStyle microLabel;
            public static GUIStyle microLabelRight;
            public static GUIStyle heroChip;
            public static GUIStyle chipLabel;
            public static GUIStyle tooltipTitle;
            public static Color heroBackground;
            public static Color heroGradientTop;
            public static Color heroGradientBottom;
            public static Color heroCardBackground;
            public static Color heroChipBackground;
            public static Color heroTooltipBackground;
            public static Color heroGrid;
            public static Color green;
            public static Color red;
            public static Color orange;
            public static Color blue;
            public static Color blueSoft;
            public static Color teal;

            public static void Ensure()
            {
                if (ready) return;
                ready = true;

                bool pro = EditorGUIUtility.isProSkin;
                heroBackground = pro ? new Color(0.11f, 0.13f, 0.17f) : new Color(0.86f, 0.89f, 0.94f);
                heroGradientTop = pro ? new Color(0.08f, 0.12f, 0.18f) : new Color(0.81f, 0.87f, 0.94f);
                heroGradientBottom = pro ? new Color(0.06f, 0.08f, 0.11f) : new Color(0.90f, 0.93f, 0.97f);
                heroCardBackground = pro ? new Color(0.10f, 0.14f, 0.20f, 0.88f) : new Color(1f, 1f, 1f, 0.78f);
                heroChipBackground = pro ? new Color(0.08f, 0.11f, 0.16f, 0.92f) : new Color(1f, 1f, 1f, 0.84f);
                heroTooltipBackground = pro ? new Color(0.08f, 0.11f, 0.16f, 0.96f) : new Color(1f, 1f, 1f, 0.98f);
                heroGrid = pro ? new Color(0.44f, 0.55f, 0.66f, 0.12f) : new Color(0.20f, 0.30f, 0.38f, 0.10f);
                green = new Color(0.15f, 0.68f, 0.38f);
                red = new Color(0.88f, 0.24f, 0.24f);
                orange = new Color(0.95f, 0.58f, 0.16f);
                blue = new Color(0.24f, 0.49f, 0.90f);
                blueSoft = new Color(0.41f, 0.70f, 1.00f, 0.95f);
                teal = new Color(0.18f, 0.86f, 0.76f, 0.98f);

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

                heroMetricValue = new GUIStyle(EditorStyles.boldLabel);
                heroMetricValue.fontSize = 18;
                heroMetricValue.wordWrap = false;
                heroMetricValue.clipping = TextClipping.Clip;
                heroMetricValue.normal.textColor = pro ? Color.white : new Color(0.07f, 0.10f, 0.14f);

                heroMetricSubTitle = new GUIStyle(EditorStyles.label);
                heroMetricSubTitle.wordWrap = false;
                heroMetricSubTitle.clipping = TextClipping.Clip;
                heroMetricSubTitle.normal.textColor = pro ? new Color(0.68f, 0.75f, 0.82f) : new Color(0.25f, 0.31f, 0.36f);

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

                microLabel = new GUIStyle(EditorStyles.miniLabel);
                microLabel.normal.textColor = pro ? new Color(0.65f, 0.76f, 0.88f) : new Color(0.23f, 0.32f, 0.40f);
                microLabel.fontStyle = FontStyle.Bold;
                microLabel.wordWrap = false;
                microLabel.clipping = TextClipping.Clip;

                microLabelRight = new GUIStyle(microLabel);
                microLabelRight.alignment = TextAnchor.MiddleRight;

                heroChip = new GUIStyle(EditorStyles.helpBox);
                heroChip.padding = new RectOffset(8, 10, 2, 2);
                heroChip.margin = new RectOffset(0, 8, 0, 0);

                chipLabel = new GUIStyle(EditorStyles.miniLabel);
                chipLabel.normal.textColor = pro ? new Color(0.86f, 0.91f, 0.96f) : new Color(0.10f, 0.16f, 0.22f);
                chipLabel.fontStyle = FontStyle.Bold;
                chipLabel.alignment = TextAnchor.MiddleLeft;

                tooltipTitle = new GUIStyle(EditorStyles.boldLabel);
                tooltipTitle.normal.textColor = pro ? Color.white : new Color(0.08f, 0.10f, 0.14f);

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
