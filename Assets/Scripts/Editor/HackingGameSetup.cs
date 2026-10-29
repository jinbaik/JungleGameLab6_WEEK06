using System;
using System.IO;

using UnityEngine;
using UnityEngine.UI;

using UnityEditor;

using Game.Player;

namespace KeyboardModeling.Editor
{
    public static class HackingGameSetup
    {
        private const string PREFAB_PATH = "Assets/Resource/Prefabs/MiniGame/MonitorHackingScreen.prefab";
        private const string ASSET_PATH = "Assets/Data/Minigame/HackingGame.asset";

        private static readonly Color BACKGROUND_COLOR = new Color(0.027f, 0.039f, 0.059f, 1f);
        private static readonly Color PANEL_BG_COLOR = new Color(0.051f, 0.075f, 0.114f, 0.95f);
        private static readonly Color CONSOLE_BG_COLOR = new Color(0.02f, 0.031f, 0.047f, 1f);
        private static readonly Color CYAN_ACCENT = new Color(0f, 0.95f, 1f, 1f);
        private static readonly Color GREEN_TEXT = new Color(0.22f, 1f, 0.08f, 1f);
        private static readonly Color MUTED_TEXT = new Color(0.5f, 0.65f, 0.75f, 1f);

        /// <summary>
        /// 해킹 미니게임 화면 프리팹과 ScriptableObject 에셋을 자동 생성하고 프로젝트에 저장한다.
        /// 메뉴 실행 입력을 받으며 프리팹 파일과 HackingGame.asset을 생성 및 갱신한다.
        /// </summary>
        [MenuItem("Tools/Mini Game/Setup Hacking MiniGame")]
        public static void Setup()
        {
            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                defaultFont = Font.CreateDynamicFontFromOSFont("Arial", 24);
            }

            GameObject root = new GameObject("MonitorHackingScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.sizeDelta = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;

            Image bgImage = root.GetComponent<Image>();
            bgImage.color = BACKGROUND_COLOR;

            MonitorHackingGame hackingGame = root.AddComponent<MonitorHackingGame>();

            // 1. HeaderHUD
            GameObject headerHud = CreatePanel(rootRect, "HeaderHUD", new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.98f), PANEL_BG_COLOR);
            RectTransform headerRect = headerHud.GetComponent<RectTransform>();

            Text titleText = CreateText(headerRect, "TitleText", defaultFont, 20, CYAN_ACCENT, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.03f, 0.50f);
            titleRect.anchorMax = new Vector2(0.65f, 0.95f);
            titleText.text = "CYBER_BREACH v3.2 // INFILTRATION SYSTEM";

            Text hostText = CreateText(headerRect, "TargetHost", defaultFont, 14, MUTED_TEXT, TextAnchor.MiddleLeft);
            RectTransform hostRect = hostText.rectTransform;
            hostRect.anchorMin = new Vector2(0.03f, 0.05f);
            hostRect.anchorMax = new Vector2(0.65f, 0.50f);
            hostText.text = "<color=#39FF14>● LIVE</color>  HOST: 192.168.0.254  [SECURITY MAINFRAME]";

            GameObject badgeObj = CreatePanel(headerRect, "StageBadge", new Vector2(0.67f, 0.15f), new Vector2(0.98f, 0.85f), new Color(0.086f, 0.133f, 0.188f, 1f));
            Text badgeText = CreateText(badgeObj.GetComponent<RectTransform>(), "StageBadgeText", defaultFont, 15, CYAN_ACCENT, TextAnchor.MiddleCenter, FontStyle.Bold);
            badgeText.rectTransform.anchorMin = Vector2.zero;
            badgeText.rectTransform.anchorMax = Vector2.one;
            badgeText.text = "LAYER 1 / 3 : FIREWALL PORT 80";

            GameObject lineObj = CreatePanel(headerRect, "AccentLine", new Vector2(0f, 0f), new Vector2(1f, 0.04f), new Color(0f, 0.95f, 1f, 0.7f));

            // 2. BreachProgressPanel
            GameObject progressPanel = CreatePanel(rootRect, "BreachProgressPanel", new Vector2(0.03f, 0.77f), new Vector2(0.97f, 0.86f), new Color(0.051f, 0.075f, 0.114f, 0.8f));
            RectTransform progressPanelRect = progressPanel.GetComponent<RectTransform>();

            Text progressTitle = CreateText(progressPanelRect, "ProgressTitle", defaultFont, 14, new Color(0.45f, 0.65f, 0.70f, 1f), TextAnchor.MiddleLeft, FontStyle.Bold);
            progressTitle.rectTransform.anchorMin = new Vector2(0.03f, 0.52f);
            progressTitle.rectTransform.anchorMax = new Vector2(0.65f, 0.95f);
            progressTitle.text = "INFILTRATION PROGRESS";

            Text percentText = CreateText(progressPanelRect, "ProgressPercent", defaultFont, 16, GREEN_TEXT, TextAnchor.MiddleRight, FontStyle.Bold);
            percentText.rectTransform.anchorMin = new Vector2(0.65f, 0.52f);
            percentText.rectTransform.anchorMax = new Vector2(0.97f, 0.95f);
            percentText.text = "0%  [ 0 / 50 ]";

            GameObject trackObj = CreatePanel(progressPanelRect, "ProgressBarTrack", new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.45f), new Color(0.086f, 0.125f, 0.173f, 1f));
            GameObject fillObj = CreatePanel(trackObj.GetComponent<RectTransform>(), "ProgressBarFill", Vector2.zero, new Vector2(0f, 1f), new Color(0f, 1f, 0.533f, 1f));
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);

            Image fillImage = fillObj.GetComponent<Image>();
            fillImage.type = Image.Type.Simple;

            // 3. TerminalConsoleBox
            GameObject consoleObj = CreatePanel(rootRect, "TerminalConsoleBox", new Vector2(0.03f, 0.15f), new Vector2(0.97f, 0.75f), CONSOLE_BG_COLOR);
            RectTransform consoleRect = consoleObj.GetComponent<RectTransform>();

            GameObject consoleHeader = CreatePanel(consoleRect, "ConsoleHeader", new Vector2(0f, 0.93f), new Vector2(1f, 1f), new Color(0.051f, 0.078f, 0.114f, 1f));
            RectTransform headerTabRect = consoleHeader.GetComponent<RectTransform>();

            Text streamTitle = CreateText(headerTabRect, "StreamTitle", defaultFont, 13, MUTED_TEXT, TextAnchor.MiddleLeft);
            streamTitle.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            streamTitle.rectTransform.anchorMax = new Vector2(0.70f, 1f);
            streamTitle.text = ">_ KERNEL TTY0 // REAL-TIME INJECTION FEED";

            Text streamTag = CreateText(headerTabRect, "StreamingTag", defaultFont, 13, GREEN_TEXT, TextAnchor.MiddleRight, FontStyle.Bold);
            streamTag.rectTransform.anchorMin = new Vector2(0.70f, 0f);
            streamTag.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            streamTag.text = "<color=#39FF14>● STREAMING</color>";

            Text logText = CreateText(consoleRect, "TerminalLog", defaultFont, 17, new Color(0.655f, 0.953f, 0.816f, 1f), TextAnchor.UpperLeft);
            logText.rectTransform.anchorMin = new Vector2(0.02f, 0.03f);
            logText.rectTransform.anchorMax = new Vector2(0.98f, 0.91f);
            logText.lineSpacing = 1.15f;
            logText.text = "INITIALIZING CYBER INFILTRATION PROTOCOL...";

            // 4. ActionBanner
            GameObject actionBanner = CreatePanel(rootRect, "ActionBanner", new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.13f), new Color(0.04f, 0.16f, 0.12f, 0.85f));
            Image actionBgImage = actionBanner.GetComponent<Image>();

            Text promptText = CreateText(actionBanner.GetComponent<RectTransform>(), "ActionPrompt", defaultFont, 22, GREEN_TEXT, TextAnchor.MiddleCenter, FontStyle.Bold);
            promptText.rectTransform.anchorMin = new Vector2(0.02f, 0f);
            promptText.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            promptText.text = ">> MASH KEYBOARD TO INJECT PAYLOAD <<";

            // Wire SerializedFields via SerializedObject
            SerializedObject so = new SerializedObject(hackingGame);
            so.FindProperty("_titleLabel").objectReferenceValue = titleText;
            so.FindProperty("_stageLabel").objectReferenceValue = badgeText;
            so.FindProperty("_progressBarLabel").objectReferenceValue = null;
            so.FindProperty("_terminalLogLabel").objectReferenceValue = logText;
            so.FindProperty("_actionPromptLabel").objectReferenceValue = promptText;
            so.FindProperty("_statsLabel").objectReferenceValue = null;

            so.FindProperty("_progressBarFill").objectReferenceValue = fillImage;
            so.FindProperty("_progressPercentText").objectReferenceValue = percentText;
            so.FindProperty("_promptBoxBg").objectReferenceValue = actionBgImage;
            so.FindProperty("_targetHostLabel").objectReferenceValue = hostText;
            so.FindProperty("_stageBadgeLabel").objectReferenceValue = badgeText;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Save Prefab
            string directory = Path.GetDirectoryName(PREFAB_PATH);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            UnityEngine.Object.DestroyImmediate(root);

            // Create or update ScriptableObject
            MonitorHackingGameDefinition definition = AssetDatabase.LoadAssetAtPath<MonitorHackingGameDefinition>(ASSET_PATH);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MonitorHackingGameDefinition>();
                AssetDatabase.CreateAsset(definition, ASSET_PATH);
            }

            SerializedObject defSo = new SerializedObject(definition);
            defSo.FindProperty("_displayName").stringValue = "시스템 해킹";
            defSo.FindProperty("_description").stringValue = "키보드를 와다다 연타하여 보안 프로토콜을 해킹하고 시스템을 장악합니다.";
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resource/Sprites/MonitorDesktop/HackingIcon.png");
            if (icon != null)
            {
                defSo.FindProperty("_icon").objectReferenceValue = icon;
            }
            defSo.FindProperty("_screenPrefab").objectReferenceValue = prefab.GetComponent<MonitorMiniGame>();
            defSo.FindProperty("_totalStages").intValue = 3;
            defSo.FindProperty("_keystrokesPerStage").intValue = 50;
            defSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[HackingGameSetup] Successfully created prefab at {PREFAB_PATH} and definition at {ASSET_PATH}!");
        }

        /// <summary>
        /// 부모 RectTransform 아래에 지정된 앵커와 색상을 가진 패널 UI 오브젝트를 생성한다.
        /// parent, name, min, max, color를 받아 생성된 패널 GameObject를 반환한다.
        /// </summary>
        private static GameObject CreatePanel(RectTransform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            Image img = obj.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            return obj;
        }

        /// <summary>
        /// 부모 RectTransform 아래에 텍스트 UI 게임 오브젝트를 생성하고 폰트 속성을 설정한다.
        /// parent, name, font, size, color, alignment, style을 받아 생성된 Text 컴포넌트를 반환한다.
        /// </summary>
        private static Text CreateText(
            RectTransform parent,
            string name,
            Font font,
            int fontSize,
            Color color,
            TextAnchor alignment,
            FontStyle fontStyle = FontStyle.Normal)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            obj.transform.SetParent(parent, false);

            Text text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            return text;
        }
    }
}
