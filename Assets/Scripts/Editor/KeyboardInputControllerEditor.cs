using UnityEditor;

namespace KeyboardModeling.Editor
{
    [CustomEditor(typeof(KeyboardInputController))]
    internal sealed class KeyboardInputControllerEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 키보드 입력 상태와 Inspector 설정을 표시한다.
        /// 선택한 controller를 사용하여 차단 활성 상태와 마지막 키를 읽기 전용으로 표시한다.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var controller = (KeyboardInputController)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Input Status", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Shortcut Capture Active", controller.ShortcutCaptureActive);
                EditorGUILayout.TextField("Last Pressed Key", controller.LastPressedKey.ToString());
                EditorGUILayout.TextField("Currently Pressed Keys", controller.CurrentPressedKeys);
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
            if (EditorApplication.isPlaying)
                Repaint();
        }
    }
}
