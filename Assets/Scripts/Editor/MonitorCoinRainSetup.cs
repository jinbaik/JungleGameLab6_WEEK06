using System;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Animations;
using UnityEngine.Playables;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using KeyboardModeling;

public static class MonitorCoinRainSetup
{
    /// <summary>
    /// 원본 Main 씬에서 누락된 코인 수입 효과의 참조만 복구한다.
    /// 기존 코인 프리팹, UI 클립, 지갑과 화면을 연결해 씬을 저장하며 공유 에셋과 다른 씬 설정은 변경하지 않는다.
    /// </summary>
    public static void RestoreSceneReferences()
    {
        RestoreScene("Assets/Scenes/Main.unity");
    }

    /// <summary>
    /// 두 메인씬의 기존 돈 낙하 효과와 공통 자동공격 참조를 복구한다.
    /// 각 씬의 현재 배치를 유지하고 지갑, 화면 및 기존 코인 에셋 연결을 저장한다.
    /// </summary>
    public static void RestoreBothScenes()
    {
        foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/Main_AutoAttack.unity" })
        {
            RestoreScene(path);
            KeyboardModeling.Editor.KeyboardAutoAttackManagerSetup.ConfigureCurrentScene();
            KeyboardModeling.Editor.KeyboardAutoAttackRewardSetup.ConfigureCurrentScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }

    /// <summary>
    /// scenePath의 돈 낙하 효과에 현재 세션과 기존 UI 에셋을 연결한다.
    /// 지정 씬을 열고 효과를 활성화하여 저장하며 다른 공유 에셋은 변경하지 않는다.
    /// </summary>
    private static void RestoreScene(string scenePath)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("복사 프로젝트 배치에서 실행합니다.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/GoldCoin.prefab");
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resource/Sprites/GoldCoin.anim");
        if (prefab == null || clip == null) throw new InvalidOperationException("기존 코인 에셋을 찾을 수 없습니다.");
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
        MiniGameScreenView view = UnityEngine.Object.FindObjectsByType<MiniGameScreenView>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
        RectTransform background = view.transform.Find("Background").GetComponent<RectTransform>();
        MonitorCoinRain effect = UnityEngine.Object.FindObjectsByType<MonitorCoinRain>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (effect == null) effect = new GameObject("MonitorCoinRain").AddComponent<MonitorCoinRain>();
        var settings = new SerializedObject(effect);
        settings.FindProperty("_gameSession").objectReferenceValue = session;
        settings.FindProperty("_screenView").objectReferenceValue = view;
        settings.FindProperty("_background").objectReferenceValue = background;
        ConfigureCoinAssets(settings);
        settings.FindProperty("_effectEnabled").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        effect.enabled = true;
        effect.gameObject.SetActive(true);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("MONITOR_COIN_REFERENCES_RESTORED session=" + session.name + " canvas=" + view.name + " clip=" + clip.name);
    }

    /// <summary>
    /// 기존 코인 클립을 검사하고 재사용 프리팹과 메인 씬 수입 효과를 구성한다.
    /// 살아 있는 SpriteRenderer 프레임을 확인하며 원본 클립과 지갑 로직은 수정하지 않고 연결 및 렌더 결과를 저장한다.
    /// </summary>
    [MenuItem("Tools/Keyboard/Setup Monitor Coin Rain")]
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("복사 프로젝트 배치에서 실행합니다.");
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resource/Sprites/Coin.anim");
        EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single(item => item.type == typeof(SpriteRenderer) && item.propertyName == "m_Sprite" && item.path == "");
        ObjectReferenceKeyframe[] frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
        if (clip.length <= 0f || frames.Any(frame => !(frame.value is Sprite))) throw new InvalidOperationException("코인 프레임 참조 오류");
        string uiPath = "Assets/Resource/Sprites/GoldCoin.anim";
        AnimationClip uiClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(uiPath);
        if (uiClip == null)
        {
            uiClip = new AnimationClip { name = "CoinUI" };
            AssetDatabase.CreateAsset(uiClip, uiPath);
        }
        uiClip.frameRate = clip.frameRate;
        AnimationUtility.SetObjectReferenceCurve(uiClip, EditorCurveBinding.PPtrCurve("", typeof(Image), "m_Sprite"), frames);
        AnimationUtility.SetAnimationClipSettings(uiClip, AnimationUtility.GetAnimationClipSettings(clip));
        EditorUtility.SetDirty(uiClip);
        GameObject coin = new GameObject("MonitorCoin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        coin.AddComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Image imageComponent = coin.GetComponent<Image>();
        imageComponent.sprite = (Sprite)frames[0].value;
        imageComponent.raycastTarget = false;
        imageComponent.preserveAspect = true;
        RectTransform rect = coin.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(28f, 28f);
        PlayableGraph graph = PlayableGraph.Create("CoinUIValidation");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, uiClip);
        playable.SetSpeed(0);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "CoinUI", coin.GetComponent<Animator>());
        output.SetSourcePlayable(playable);
        graph.Play(); graph.Evaluate(0);
        playable.SetTime(frames[2].time + 0.001f); graph.Evaluate(0);
        if (imageComponent.sprite != frames[2].value) throw new InvalidOperationException("UI 코인 프레임 재생 오류");
        Debug.Log("COIN_UI_FRAME_OK expected=" + frames[2].value.name + " actual=" + imageComponent.sprite.name);
        playable.SetTime(0); graph.Evaluate(0);
        graph.Destroy();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(coin, "Assets/Resource/Prefabs/GoldCoin.prefab");
        UnityEngine.Object.DestroyImmediate(coin);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        GameSession session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
        MiniGameScreenView view = UnityEngine.Object.FindObjectsByType<MiniGameScreenView>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();

        RectTransform canvasRoot = view.GetComponent<RectTransform>();
        RectTransform background = canvasRoot.Find("Background").GetComponent<RectTransform>();
        MonitorCoinRain existingEffect = UnityEngine.Object.FindObjectsByType<MonitorCoinRain>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        GameObject effectObject = existingEffect != null ? existingEffect.gameObject : null;
        if (effectObject == null) effectObject = new GameObject("MonitorCoinRain");
        MonitorCoinRain effect = effectObject.GetComponent<MonitorCoinRain>();
        if (effect == null) effect = effectObject.AddComponent<MonitorCoinRain>();
        var serialized = new SerializedObject(effect);
        serialized.FindProperty("_gameSession").objectReferenceValue = session;
        serialized.FindProperty("_screenView").objectReferenceValue = view;
        serialized.FindProperty("_background").objectReferenceValue = background;
        ConfigureCoinAssets(serialized);
        serialized.FindProperty("_gravity").floatValue = 500f;
        serialized.FindProperty("_screenWidth").floatValue = 1f;
        serialized.FindProperty("_lifetime").floatValue = 3f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (session == null || canvasRoot == null) throw new InvalidOperationException("메인 씬 수입/카메라 참조 오류");
        Debug.Log("COIN_CANVAS_ROOT name=" + canvasRoot.name + " canvas=" + (canvasRoot.GetComponent<Canvas>() != null) + " width=" + canvasRoot.rect.width + " height=" + canvasRoot.rect.height);
        Debug.Log("COIN_BACKGROUND name=" + background.name + " width=" + background.rect.width + " height=" + background.rect.height + " managerParent=" + (effectObject.transform.parent == null ? "SceneRoot" : effectObject.transform.parent.name));
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(".local/MonitorCoinPreview");
        File.WriteAllText(".local/MonitorCoinPreview/Report.txt",
            "Frames: " + frames.Length + "\nFPS: " + clip.frameRate + "\nClip length: " + clip.length +
            "\nMax sprite vertices: " + frames.Max(frame => ((Sprite)frame.value).vertices.Length) +
            "\nMax sprite triangles: " + frames.Max(frame => ((Sprite)frame.value).triangles.Length / 3) +
            "\nCoin CanvasRenderers: 1; visible SpriteRenderers: 0\nCoin shadow casters: 0\nPool: 24\n");
        Debug.Log("MONITOR_COIN_RAIN_UI_COMPLETE");
    }
    /// <summary>
    /// settings의 4종 코인 프리팹과 클립 참조를 현재 에셋 경로로 설정한다.
    /// 물리 재질과 생성 간격 및 깜빡임 설정을 연결하며 실제 반영은 호출자가 수행한다.
    /// </summary>
    private static void ConfigureCoinAssets(SerializedObject settings)
    {
        foreach (string name in new[] { "Gold", "Silver", "Iron", "Copper" })
        {
            string field = "_" + name.ToLowerInvariant();
            settings.FindProperty(field + "Prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/" + name + "Coin.prefab");
            settings.FindProperty(field + "Animation").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resource/Sprites/" + name + "Coin.anim");
        }
        settings.FindProperty("_collisionMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Resource/Materials/MonitorCoin.physicsMaterial2D");
        settings.FindProperty("_spawnInterval").floatValue = 0.1f;
        settings.FindProperty("_blinkRemainingTime").floatValue = 1f;
        settings.FindProperty("_blinkFadeDuration").floatValue = 0.1f;
    }
}










