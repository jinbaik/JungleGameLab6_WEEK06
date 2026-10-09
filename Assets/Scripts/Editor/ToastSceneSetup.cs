using System;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Economy;
using Game.Session;
using Game.UI;

namespace KeyboardModeling.Editor
{
    public static class ToastSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/Dev_MiniGame.unity";
        private const string PREFAB_FOLDER = "Assets/Resource/Prefabs/UI";
        private const string ITEM_PATH = PREFAB_FOLDER + "/ToastItem.prefab";
        private const string CANVAS_PATH = PREFAB_FOLDER + "/ToastCanvas.prefab";

        /// <summary>
        /// Dev_MiniGame의 피해 및 보상 알림과 독립된 풀링 Canvas를 구성하고 저장한다.
        /// 현재 씬의 상호작용 컨트롤러와 Session을 연결하고 UI 폴더에 재사용 프리팹을 생성한다.
        /// </summary>
        [MenuItem("Tools/Toast/Configure Dev_MiniGame")]
        public static void Configure()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE_PATH || Application.isPlaying)
                throw new InvalidOperationException("Open Dev_MiniGame in Edit Mode before configuring Toast UI.");

            KeyboardInteractionController interaction = FindInScene<KeyboardInteractionController>(scene);
            GameSession session = FindInScene<GameSession>(scene);
            Camera camera = (Camera)new SerializedObject(interaction).FindProperty("_camera").objectReferenceValue;
            if (camera == null) throw new InvalidOperationException("Assign the game camera before configuring Toast UI.");
            if (!AssetDatabase.IsValidFolder(PREFAB_FOLDER))
                AssetDatabase.CreateFolder("Assets/Resource/Prefabs", "UI");

            ToastView item = GetOrCreateItem();
            GameObject prefab = GetOrCreateCanvas(item);
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure damage and reward Toast UI");

            ToastPool pool = FindOptionalInScene<ToastPool>(scene);
            if (pool == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Create independent Toast Canvas");
                pool = instance.GetComponent<ToastPool>();
            }
            SetReference(pool, "_camera", camera);

            KeyboardRewardController reward = FindOptionalInScene<KeyboardRewardController>(scene);
            if (reward == null) reward = Undo.AddComponent<KeyboardRewardController>(session.gameObject);
            SetReference(reward, "_gameSession", session);
            SetReference(reward, "_interactionController", interaction);

            KeyboardToastPresenter presenter = FindOptionalInScene<KeyboardToastPresenter>(scene);
            if (presenter == null) presenter = Undo.AddComponent<KeyboardToastPresenter>(session.gameObject);
            SetReference(presenter, "_interactionController", interaction);
            SetReference(presenter, "_rewardController", reward);
            SetReference(presenter, "_toastPool", pool);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("TOAST_CONFIGURED Scene=Dev_MiniGame Prefabs=ToastCanvas,ToastItem Pool=128/256");
        }

        /// <summary>
        /// 기존 ToastItem을 재사용하거나 숫자, 외곽선과 CanvasGroup을 가진 항목을 생성한다.
        /// ITEM_PATH에 저장된 ToastView 프리팹을 반환한다.
        /// </summary>
        private static ToastView GetOrCreateItem()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ITEM_PATH);
            if (existing != null) return existing.GetComponent<ToastView>();

            GameObject item = new GameObject("ToastItem", typeof(RectTransform), typeof(CanvasGroup));
            item.layer = LayerMask.NameToLayer("UI");
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(240f, 48f);
            CanvasGroup group = item.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Text label = item.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.text = string.Empty;
            Outline outline = item.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.02f, 0.02f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            ToastView view = item.AddComponent<ToastView>();
            SetReference(view, "_label", label);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(item, ITEM_PATH);
            UnityEngine.Object.DestroyImmediate(item);
            return saved.GetComponent<ToastView>();
        }

        /// <summary>
        /// 기존 ToastCanvas를 재사용하거나 전용 Overlay Canvas와 ToastPool을 생성한다.
        /// item을 대여할 풀과 표시 루트를 연결하여 CANVAS_PATH에 저장된 프리팹을 반환한다.
        /// </summary>
        private static GameObject GetOrCreateCanvas(ToastView item)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(CANVAS_PATH);
            if (existing != null) return existing;

            GameObject canvasObject = new GameObject("ToastCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasGroup group = canvasObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            RectTransform root = new GameObject("ToastLayer", typeof(RectTransform)).GetComponent<RectTransform>();
            root.gameObject.layer = canvasObject.layer;
            root.SetParent(canvasObject.transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.sizeDelta = Vector2.zero;
            root.anchoredPosition = Vector2.zero;

            ToastPool pool = canvasObject.AddComponent<ToastPool>();
            SetReference(pool, "_root", root);
            SetReference(pool, "_toastPrefab", item);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(canvasObject, CANVAS_PATH);
            UnityEngine.Object.DestroyImmediate(canvasObject);
            return saved;
        }

        /// <summary>
        /// target의 fieldName으로 지정한 직렬화 참조에 value를 연결한다.
        /// Undo와 프리팹 인스턴스 변경 기록을 보존하며 설정한 참조를 저장한다.
        /// </summary>
        private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            Undo.RecordObject(target, "Connect Toast dependency");
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        /// <summary>
        /// scene의 루트와 자식에서 T 컴포넌트를 찾아 필수 의존성으로 반환한다.
        /// 일치하는 컴포넌트가 없으면 잘못된 씬 설정을 예외로 알린다.
        /// </summary>
        private static T FindInScene<T>(Scene scene) where T : Component
        {
            T component = FindOptionalInScene<T>(scene);
            if (component == null) throw new InvalidOperationException($"Dev_MiniGame requires {typeof(T).Name}.");
            return component;
        }

        /// <summary>
        /// scene의 루트와 비활성 자식까지 검색하여 첫 번째 T 컴포넌트를 반환한다.
        /// 일치하는 컴포넌트가 없으면 null을 반환하여 필요한 구성 요소를 생성할 수 있게 한다.
        /// </summary>
        private static T FindOptionalInScene<T>(Scene scene) where T : Component
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
