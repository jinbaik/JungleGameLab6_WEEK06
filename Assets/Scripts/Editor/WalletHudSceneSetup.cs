using System;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using Game.UI;

namespace KeyboardModeling.Editor
{
    public static class WalletHudSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/Dev_MiniGame.unity";
        private const string PREFAB_FOLDER = "Assets/Resource/Prefabs/UI";
        private const string ITEM_PATH = PREFAB_FOLDER + "/WalletHUDToastItem.prefab";
        private const string CANVAS_PATH = PREFAB_FOLDER + "/WalletHUDCanvas.prefab";

        /// <summary>
        /// Dev_MiniGame에 별도 Overlay Canvas를 배치하고 Session의 지갑 HUD를 연결한다.
        /// 우측 상단 보유 금액과 풀링 획득 알림의 프리팹을 UI 폴더에 저장하고 씬 연결을 저장한다.
        /// </summary>
        [MenuItem("Tools/Toast/Configure Wallet HUD")]
        public static void Configure()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE_PATH || Application.isPlaying)
                throw new InvalidOperationException("Open Dev_MiniGame in Edit Mode before configuring the Wallet HUD.");

            GameSession session = FindInScene<GameSession>(scene);
            if (session == null) throw new InvalidOperationException("Dev_MiniGame requires an existing GameSession.");
            if (!AssetDatabase.IsValidFolder(PREFAB_FOLDER))
                AssetDatabase.CreateFolder("Assets/Resource/Prefabs", "UI");

            WalletHudToastView item = GetOrCreateItem();
            GameObject prefab = GetOrCreateCanvas(item);
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure Wallet HUD");
            WalletHudController hud = FindInScene<WalletHudController>(scene);
            if (hud == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Create Wallet HUD Canvas");
                hud = instance.GetComponent<WalletHudController>();
            }
            SetReference(hud, "_gameSession", session);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("WALLET_HUD_CONFIGURED Scene=Dev_MiniGame Anchor=TopRight Prefabs=WalletHUDCanvas,WalletHUDToastItem");
        }

        /// <summary>
        /// 획득량을 우측 정렬로 표시할 HUD 전용 Toast 프리팹을 생성하거나 재사용한다.
        /// ITEM_PATH에 저장된 CanvasGroup, 텍스트 및 WalletHudToastView 항목을 반환한다.
        /// </summary>
        private static WalletHudToastView GetOrCreateItem()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ITEM_PATH);
            if (existing != null) return existing.GetComponent<WalletHudToastView>();

            RectTransform rect = CreateRect("WalletHUDToastItem", null, Vector2.zero, new Vector2(248f, 36f));
            Text label = AddText(rect, string.Empty, 26, new Color(1f, 0.85f, 0.25f), TextAnchor.MiddleRight);
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 26;
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.02f, 0.02f, 0.95f);
            outline.effectDistance = new Vector2(1f, -1f);
            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            WalletHudToastView view = rect.gameObject.AddComponent<WalletHudToastView>();
            SetReference(view, "_label", label);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(rect.gameObject, ITEM_PATH);
            UnityEngine.Object.DestroyImmediate(rect.gameObject);
            return saved.GetComponent<WalletHudToastView>();
        }

        /// <summary>
        /// 현재 보유 금액과 획득 알림을 표시하는 독립 Overlay Canvas를 생성하거나 재사용한다.
        /// item을 사용할 풀과 우측 상단 앵커를 연결하여 CANVAS_PATH에 저장된 프리팹을 반환한다.
        /// </summary>
        private static GameObject GetOrCreateCanvas(WalletHudToastView item)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(CANVAS_PATH);
            if (existing != null) return existing;

            GameObject canvasObject = new GameObject("WalletHUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasGroup canvasGroup = canvasObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            RectTransform panel = CreateRect("WalletPanel", canvasObject.transform, new Vector2(-32f, -32f), new Vector2(280f, 84f));
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.018f, 0.035f, 0.055f, 0.94f);
            background.raycastTarget = false;
            RectTransform accent = CreateRect("Accent", panel, Vector2.zero, new Vector2(280f, 3f));
            Image stripe = accent.gameObject.AddComponent<Image>();
            stripe.color = new Color(0.25f, 0.95f, 0.72f);
            stripe.raycastTarget = false;
            RectTransform titleRect = CreateRect("CurrencyLabel", panel, new Vector2(-16f, -8f), new Vector2(248f, 24f));
            AddText(titleRect, "CREDITS", 16, new Color(0.48f, 0.65f, 0.70f), TextAnchor.MiddleRight);
            RectTransform balanceRect = CreateRect("Balance", panel, new Vector2(-16f, -32f), new Vector2(248f, 44f));
            Text balance = AddText(balanceRect, "0", 34, new Color(0.9f, 0.97f, 0.95f), TextAnchor.MiddleRight);
            balance.fontStyle = FontStyle.Bold;
            balance.resizeTextForBestFit = true;
            balance.resizeTextMinSize = 12;
            balance.resizeTextMaxSize = 34;

            RectTransform toastRoot = CreateRect("HUDToastLayer", canvasObject.transform, new Vector2(-32f, -134f), new Vector2(280f, 240f));
            WalletHudController hud = canvasObject.AddComponent<WalletHudController>();
            SetReference(hud, "_balanceLabel", balance);
            SetReference(hud, "_toastRoot", toastRoot);
            SetReference(hud, "_toastPrefab", item);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(canvasObject, CANVAS_PATH);
            UnityEngine.Object.DestroyImmediate(canvasObject);
            return saved;
        }

        /// <summary>
        /// name, parent, position, size로 우측 상단 기준의 RectTransform을 생성한다.
        /// 부모가 있을 경우 UI 좌표를 유지하여 배치하고 새 사각형을 반환한다.
        /// </summary>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>
        /// rect에 비상호작용 텍스트를 추가한다.
        /// content, fontSize, color와 alignment를 사용하여 설정한 Text를 반환한다.
        /// </summary>
        private static Text AddText(RectTransform rect, string content, int fontSize, Color color, TextAnchor alignment)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.text = content;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// target의 fieldName에 value를 직렬화된 참조로 저장한다.
        /// Undo 및 프리팹 인스턴스 변경 기록을 함께 보존한다.
        /// </summary>
        private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            Undo.RecordObject(target, "Connect Wallet HUD dependency");
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        /// <summary>
        /// scene의 루트와 비활성 자식에서 첫 번째 T 컴포넌트를 검색한다.
        /// 해당 씬의 컴포넌트를 반환하며 없으면 null을 반환한다.
        /// </summary>
        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
    }
}
