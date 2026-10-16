using System;

using UnityEngine;
using UnityEngine.UI;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.UI;
using KeyboardModeling;

public static class KeyboardScoreComboSceneSetup
{
    private const string SCENE_PATH = "Assets/Legacy/Scene/Week7/Test 1.unity";
    private const string SCORE_COMPARISON_SCENE_PATH = "Assets/Scenes/Week7/ManagerMerge.unity";

    /// <summary>
    /// ManagerMerge 씬의 기존 플레이어 점수 아래에 독립적인 적 점수를 연결한다.
    /// 기존 적 점수 연결이 있으면 재사용하고 없으면 입력 대상이 없는 0점 상태를 생성하여 UI 참조와 씬을 저장한다.
    /// </summary>
    [MenuItem("Tools/Keyboard/Configure ManagerMerge Enemy Score")]
    public static void ConfigureEnemyScore()
    {
        if (Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("편집 중인 씬을 저장하고 Edit Mode에서 실행합니다.");

        var scene = EditorSceneManager.OpenScene(SCORE_COMPARISON_SCENE_PATH);
        GameManager manager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        SerializedObject managerSettings = new SerializedObject(manager);
        KeyboardScoreCombo playerScore = (KeyboardScoreCombo)managerSettings.FindProperty("_comboHUD").objectReferenceValue;
        KeyboardScoreComboHud playerHud = playerScore.GetComponent<KeyboardScoreComboHud>();
        SerializedObject playerSettings = new SerializedObject(playerHud);
        Text playerText = (Text)playerSettings.FindProperty("_scoreText").objectReferenceValue;
        playerSettings.FindProperty("_scoreLabel").stringValue = "PLAYER SCORE";
        playerSettings.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(playerHud);

        playerText.text = "PLAYER SCORE: 0";
        playerText.color = Color.white;
        playerText.fontSize = 36;
        playerText.fontStyle = FontStyle.Bold;
        playerText.alignment = TextAnchor.MiddleLeft;
        playerText.resizeTextForBestFit = true;
        playerText.resizeTextMinSize = 24;
        playerText.resizeTextMaxSize = 36;
        playerText.rectTransform.sizeDelta = new Vector2(600f, 52f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(playerText);
        PrefabUtility.RecordPrefabInstancePropertyModifications(playerText.rectTransform);

        Transform enemyTransform = playerText.transform.parent.Find("EnemyScore");
        Text enemyText = enemyTransform != null ? enemyTransform.GetComponent<Text>()
            : UnityEngine.Object.Instantiate(playerText, playerText.transform.parent, false);
        enemyText.name = "EnemyScore";
        enemyText.text = "ENEMY SCORE: 0";
        enemyText.fontSize = 28;
        enemyText.fontStyle = FontStyle.Normal;
        enemyText.color = new Color(1f, 0.35f, 0.35f);
        enemyText.resizeTextMinSize = 20;
        enemyText.resizeTextMaxSize = 28;
        enemyText.rectTransform.sizeDelta = new Vector2(600f, 44f);
        enemyText.rectTransform.anchoredPosition = playerText.rectTransform.anchoredPosition + new Vector2(0f, -66f);

        EnemyScoreComboBinding enemyBinding = UnityEngine.Object.FindFirstObjectByType<EnemyScoreComboBinding>();
        KeyboardScoreCombo enemyScore;
        if (enemyBinding != null)
        {
            SerializedObject bindingSettings = new SerializedObject(enemyBinding);
            enemyScore = (KeyboardScoreCombo)bindingSettings.FindProperty("_scoreCombo").objectReferenceValue;
        }
        else
        {
            Transform scoreTransform = playerHud.transform.Find("EnemyScoreState");
            if (scoreTransform == null)
            {
                GameObject scoreObject = new GameObject("EnemyScoreState");
                scoreObject.transform.SetParent(playerHud.transform, false);
                enemyScore = scoreObject.AddComponent<KeyboardScoreCombo>();
            }
            else
            {
                enemyScore = scoreTransform.GetComponent<KeyboardScoreCombo>();
            }
        }

        if (!playerHud.TryGetComponent(out EnemyScoreHud enemyHud))
            enemyHud = playerHud.gameObject.AddComponent<EnemyScoreHud>();
        SetReference(enemyHud, "_scoreCombo", enemyScore);
        SetReference(enemyHud, "_scoreText", enemyText);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("ENEMY_SCORE_UI_CONFIGURED " + SCORE_COMPARISON_SCENE_PATH);
    }

    /// <summary>
    /// Test 1 씬의 키보드에 스코어와 콤보 상태 및 화면 UI를 구성한다.
    /// 기존 씬을 열어 필요한 참조와 앵커를 연결하고 씬을 저장한다.
    /// </summary>
    [MenuItem("Tools/Keyboard/Configure Test 1 Score Combo")]
    public static void Configure()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Edit Mode에서 스코어 UI를 구성합니다.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("현재 씬의 변경을 저장한 뒤 실행합니다.");

        var scene = EditorSceneManager.OpenScene(SCENE_PATH);
        KeyboardInputController keyboard = UnityEngine.Object.FindFirstObjectByType<KeyboardInputController>();
        KeyboardScoreCombo scoreCombo = UnityEngine.Object.FindFirstObjectByType<KeyboardScoreCombo>();
        if (scoreCombo != null)
            throw new InvalidOperationException("스코어와 콤보가 이미 구성되어 있습니다.");

        GameObject manager = new GameObject("TemporaryGameManager");
        scoreCombo = manager.AddComponent<KeyboardScoreCombo>();
        SetReference(scoreCombo, "_keyboard", keyboard);
        CreateHud(scoreCombo);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("SCORE_COMBO_SCENE_CONFIGURED " + SCENE_PATH);
    }

    /// <summary>
    /// scoreCombo에 연결된 기존 점수와 콤보 화면을 현재 씬에 생성한다.
    /// 화면 앵커와 글꼴 및 게이지 설정을 사용하여 참조가 연결된 HUD 컴포넌트를 반환한다.
    /// </summary>
    public static KeyboardScoreComboHud CreateHud(KeyboardScoreCombo scoreCombo)
    {
        RectTransform root = CreateRect("ScoreComboHUD", null, Vector2.zero, Vector2.zero, Vector2.zero);
        Canvas canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text score = CreateText("Score", root, new Vector2(0f, 1f), new Vector2(32f, -124f),
            new Vector2(360f, 48f), "score: 0", font, 30, TextAnchor.MiddleLeft);
        RectTransform comboGroup = CreateRect("Combo", root, new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(280f, 132f));
        Text combo = CreateText("ComboText", comboGroup, new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(280f, 62f), "0콤보", font, 46, TextAnchor.MiddleCenter);
        Text multiplier = CreateText("Multiplier", comboGroup, new Vector2(0.5f, 1f), new Vector2(0f, -62f),
            new Vector2(280f, 32f), "x1배", font, 24, TextAnchor.MiddleCenter);

        RectTransform track = CreateRect("TimerTrack", comboGroup, new Vector2(0.5f, 1f),
            new Vector2(0f, -103f), new Vector2(480f, 20f));
        Image background = track.gameObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.45f);
        background.raycastTarget = false;
        RectTransform fillRect = CreateRect("TimerFill", track, Vector2.zero, Vector2.zero, Vector2.zero);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(1f, 1f);
        fillRect.offsetMax = new Vector2(-1f, -1f);
        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;

        KeyboardScoreComboHud hud = scoreCombo.gameObject.AddComponent<KeyboardScoreComboHud>();
        SetReference(hud, "_scoreCombo", scoreCombo);
        SetReference(hud, "_scoreText", score);
        SetReference(hud, "_comboText", combo);
        SetReference(hud, "_multiplierText", multiplier);
        SetReference(hud, "_timerFill", fill);
        return hud;
    }

    /// <summary>
    /// 이름과 부모, 앵커, 위치 및 크기로 상단 기준 UI 사각형을 생성한다.
    /// 화면 UI 레이어에 배치한 RectTransform을 반환한다.
    /// </summary>
    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = 5;
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>
    /// 부모와 배치, 문구, 폰트 및 크기 설정으로 흰색 UI 텍스트를 생성한다.
    /// 배경 위 가독성을 위한 그림자와 클릭 통과를 적용한 Text를 반환한다.
    /// </summary>
    private static Text CreateText(string name, Transform parent, Vector2 anchor, Vector2 position,
        Vector2 size, string value, Font font, int fontSize, TextAnchor alignment)
    {
        Text text = CreateRect(name, parent, anchor, position, size).gameObject.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    /// <summary>
    /// target의 직렬화된 fieldName에 value를 연결한다.
    /// Inspector에 저장될 컴포넌트 참조를 갱신한다.
    /// </summary>
    private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(fieldName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
