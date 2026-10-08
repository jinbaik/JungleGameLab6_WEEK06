using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling.Editor
{
    public static class KeyboardInputSetup
    {
        private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keyboard_ANSI_104.prefab";

        /// <summary>
        /// 기존 키보드 프리팹에 실제 키 입력 연동을 추가한다.
        /// 저장된 키보드 프리팹을 사용하여 104키 매핑을 설정하고 기존 모델을 유지하여 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Configure Physical Keyboard Input")]
        public static void ConfigureExistingPrefab()
        {
            ConfigureExistingKeycaps();
            GameObject keyboard = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                Configure(keyboard);
                PrefabUtility.SaveAsPrefabAsset(keyboard, PREFAB_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log("KEYBOARD_INPUT_CONFIGURED keys=104 uniqueMappings=104");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(keyboard);
            }
        }

        /// <summary>
        /// 저장된 키캡 프리팹에 누름 이벤트를 추가한다.
        /// 기존 Keycaps 폴더의 프리팹을 사용하여 버튼과 HP 이벤트 연결을 저장한다.
        /// </summary>
        private static void ConfigureExistingKeycaps()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resource/Prefabs/Keycaps" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject keycap = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureKeycap(keycap);
                    PrefabUtility.SaveAsPrefabAsset(keycap, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(keycap);
                }
            }
        }

        /// <summary>
        /// 키캡에 누름 기능을 연결한다.
        /// keycap의 버튼을 준비하고 기존 KeycapHealth가 있으면 1회당 피해 1 이벤트를 중복 없이 등록한다.
        /// </summary>
        /// 

        public static void ConfigureKeycap(GameObject keycap)
        {
            KeycapButton button = keycap.GetComponent<KeycapButton>();
            if (button == null)
                button = keycap.AddComponent<KeycapButton>();

            KeycapHealth health = keycap.GetComponent<KeycapHealth>();
            if (health == null)
                return;

            for (int index = 0; index < button.OnPressed.GetPersistentEventCount(); index++)
            {
                if (button.OnPressed.GetPersistentTarget(index) == health && button.OnPressed.GetPersistentMethodName(index) == nameof(KeycapHealth.TakeDamage))
                    return;
            }


            //여기서 밑 코드로 추가하면 인스팩터에서 수정가능
            UnityEventTools.AddIntPersistentListener(button.OnPressed, health.TakeDamage, 1);
            EditorUtility.SetDirty(button);
        }

        /// <summary>
        /// 키보드 루트에 입력 제어 컴포넌트와 키 참조를 설정한다.
        /// keyboard의 키캡 각인과 위치를 사용하여 중복 없는 104개 물리 키 매핑을 직렬화한다.
        /// </summary>
        public static void Configure(GameObject keyboard)
        {
            KeyboardInputController controller = keyboard.GetComponent<KeyboardInputController>();
            if (controller == null)
                controller = keyboard.AddComponent<KeyboardInputController>();
            Transform keys = keyboard.transform.Find("Keys_104_SharedPrefabs");
            var keycaps = new List<Transform>();
            var mappedKeys = new List<Key>();
            var uniqueKeys = new HashSet<Key>();
            foreach (Transform child in keys)
            {
                if (!child.name.StartsWith("Key_", StringComparison.Ordinal))
                    continue;
                TextMesh legend = child.GetComponentInChildren<TextMesh>();
                string label = legend == null ? "" : legend.text;
                Key key = ResolveKey(label, child.localPosition.x);
                if (!uniqueKeys.Add(key))
                    throw new InvalidOperationException("중복 키 매핑: " + key);
                keycaps.Add(child);
                mappedKeys.Add(key);
            }
            if (keycaps.Count != 104)
                throw new InvalidOperationException("키보드 입력 매핑은 104개여야 합니다: " + keycaps.Count);
            foreach (Transform keycap in keycaps)
                ConfigureKeycap(keycap.gameObject);
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_blockShortcuts").boolValue = true;
            SerializedProperty bindings = serialized.FindProperty("_bindings");
            bindings.arraySize = keycaps.Count;
            for (int index = 0; index < keycaps.Count; index++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("_key").intValue = (int)mappedKeys[index];
                binding.FindPropertyRelative("_keycap").objectReferenceValue = keycaps[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 키캡 각인과 가로 위치를 물리 키 식별자로 변환한다.
        /// label과 x를 사용하여 좌우 보조 키와 숫자패드를 구분한 Input System Key 값을 반환한다.
        /// </summary>
        private static Key ResolveKey(string label, float x)
        {
            if (x >= 19f)
            {
                switch (label)
                {
                    case "Num\nLock": return Key.NumLock;
                    case "/": return Key.NumpadDivide;
                    case "*": return Key.NumpadMultiply;
                    case "-": return Key.NumpadMinus;
                    case "+": return Key.NumpadPlus;
                    case "Enter": return Key.NumpadEnter;
                    case "0   Ins": return Key.Numpad0;
                    case ".\nDel": return Key.NumpadPeriod;
                    default:
                        if (label.Length > 0 && label[0] >= '1' && label[0] <= '9')
                            return (Key)Enum.Parse(typeof(Key), "Numpad" + label[0]);
                        throw new InvalidOperationException("지원하지 않는 숫자패드 각인: " + label);
                }
            }
            if (label.Length == 1 && label[0] >= 'A' && label[0] <= 'Z')
                return (Key)Enum.Parse(typeof(Key), label);
            if (label.StartsWith("F", StringComparison.Ordinal) && int.TryParse(label.Substring(1), out int functionNumber) && functionNumber >= 1 && functionNumber <= 12)
                return (Key)Enum.Parse(typeof(Key), label);
            switch (label)
            {
                case "": return Key.Space;
                case "Esc": return Key.Escape;
                case "~\n`": return Key.Backquote;
                case "!\n1": return Key.Digit1;
                case "@\n2": return Key.Digit2;
                case "#\n3": return Key.Digit3;
                case "$\n4": return Key.Digit4;
                case "%\n5": return Key.Digit5;
                case "^\n6": return Key.Digit6;
                case "&\n7": return Key.Digit7;
                case "*\n8": return Key.Digit8;
                case "(\n9": return Key.Digit9;
                case ")\n0": return Key.Digit0;
                case "_\n-": return Key.Minus;
                case "+\n=": return Key.Equals;
                case "{\n[": return Key.LeftBracket;
                case "}\n]": return Key.RightBracket;
                case "|\n\\": return Key.Backslash;
                case ":\n;": return Key.Semicolon;
                case "\"\n'": return Key.Quote;
                case "<\n,": return Key.Comma;
                case ">\n.": return Key.Period;
                case "?\n/": return Key.Slash;
                case "Backspace": return Key.Backspace;
                case "Tab": return Key.Tab;
                case "Caps Lock": return Key.CapsLock;
                case "Enter": return Key.Enter;
                case "Shift": return x < 7.5f ? Key.LeftShift : Key.RightShift;
                case "Ctrl": return x < 7.5f ? Key.LeftCtrl : Key.RightCtrl;
                case "Alt": return x < 7.5f ? Key.LeftAlt : Key.RightAlt;
                case "Win": return x < 7.5f ? Key.LeftMeta : Key.RightMeta;
                case "Menu": return Key.ContextMenu;
                case "Print\nScreen": return Key.PrintScreen;
                case "Scroll\nLock": return Key.ScrollLock;
                case "Pause": return Key.Pause;
                case "Insert": return Key.Insert;
                case "Home": return Key.Home;
                case "Page\nUp": return Key.PageUp;
                case "Delete": return Key.Delete;
                case "End": return Key.End;
                case "Page\nDown": return Key.PageDown;
                case "^": return Key.UpArrow;
                case "<": return Key.LeftArrow;
                case "v": return Key.DownArrow;
                case ">": return Key.RightArrow;
                default: throw new InvalidOperationException("지원하지 않는 키캡 각인: " + label);
            }
        }
    }
}

