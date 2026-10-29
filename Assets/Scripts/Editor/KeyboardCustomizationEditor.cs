#if UNITY_EDITOR
using UnityEngine;

using UnityEditor;

[CustomEditor(typeof(KeyboardCustomization))]
public sealed class KeyboardCustomizationEditor : Editor
{
    private int _keyIndex;
    private string _photoMessage;
    /// <summary>
    /// Inspector 입력으로 직렬화된 설정을 표시하고 변경된 Case와 키캡 외관을 적용한다.
    /// 반환값 없이 선택한 Material을 렌더러에 반영하고 에디터 화면을 갱신한다.
    /// </summary>
    public override void OnInspectorGUI()
    {
        var customization = (KeyboardCustomization)target;
        int previousCase = serializedObject.FindProperty("_caseMaterialIndex").intValue;
        int previousKeycap = serializedObject.FindProperty("_keycapMaterialIndex").intValue;
        if (DrawDefaultInspector())
        {
            int selectedCase = serializedObject.FindProperty("_caseMaterialIndex").intValue;
            int selectedKeycap = serializedObject.FindProperty("_keycapMaterialIndex").intValue;
            if (previousCase != selectedCase) customization.SetCaseMaterialIndex(selectedCase);
            if (previousKeycap != selectedKeycap) customization.SetKeycapMaterialIndex(selectedKeycap);
            customization.ApplyCaseEditorPreview();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Photo customization", EditorStyles.boldLabel);
        bool prefab = EditorUtility.IsPersistent(target) || UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(customization.gameObject) != null;
        EditorGUILayout.HelpBox("씬 키보드 인스턴스에 사진을 적용합니다. 사진 상태는 Save / Load로 저장하세요.", MessageType.Info);
        using (new EditorGUI.DisabledScope(prefab))
        {
            if (GUILayout.Button("사진 선택 → 전체 키캡")) ChooseImage(customization, false, -1);
            if (GUILayout.Button("사진 선택 → Case")) ChooseImage(customization, true, -1);
            _keyIndex = EditorGUILayout.IntField("Key index", _keyIndex);
            if (GUILayout.Button("사진 선택 → 개별 키캡")) ChooseImage(customization, false, _keyIndex);
            if (GUILayout.Button("키캡 사진 제거")) customization.ClearKeycapImage();
            if (GUILayout.Button("Case 사진 제거")) customization.ClearCaseImage();
        }
        if (!string.IsNullOrEmpty(_photoMessage)) EditorGUILayout.HelpBox(_photoMessage, MessageType.Info);
    }

    /// <summary>
    /// 파일 선택 창에서 고른 사진을 customization의 Case 또는 index 키캡에 적용한다.
    /// 반환값 없이 결과를 Inspector에 표시하고 씬 화면을 갱신한다.
    /// </summary>
    private void ChooseImage(KeyboardCustomization customization, bool caseImage, int index)
    {
        if (index < -1) { _photoMessage = "키 인덱스는 0 이상이어야 합니다."; return; }
        string path = EditorUtility.OpenFilePanel("Choose photo", "", "png,jpg,jpeg");
        if (string.IsNullOrEmpty(path)) return;
        bool applied = caseImage ? customization.LoadCaseImage(path) : customization.LoadKeycapImage(path, index);
        _photoMessage = applied ? "사진을 적용했습니다. Save로 저장할 수 있습니다." : customization.ImageError;
        SceneView.RepaintAll();
    }
}
#endif
