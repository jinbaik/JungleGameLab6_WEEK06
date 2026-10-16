#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEngine;

using UnityEditor;

public static class KeyboardSwitchSetup
{
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/SeperateKeyboard/Keyboard_Customize_Test.prefab";
    private const string DATA_FOLDER = "Assets/Data/Switches";

    /// <summary>
    /// 대상 프리팹의 Switch 오브젝트에 축 컴포넌트와 부품 및 SO 참조를 구성한다.
    /// 입력과 반환값 없이 기본 축 데이터와 프리팹을 저장하며 기존 데이터는 유지한다.
    /// </summary>
    [MenuItem("Tools/Keyboard/Setup Customize Test Switches")]
    public static void ExecuteSetup()
    {
        if (!AssetDatabase.IsValidFolder(DATA_FOLDER)) AssetDatabase.CreateFolder("Assets/Data", "Switches");
        using (var scope = new PrefabUtility.EditPrefabContentsScope(PREFAB_PATH))
        {
            GameObject root = scope.prefabContentsRoot;
            Transform keys = root.transform.Find("Keys_87_SharedPrefabs");
            Transform sample = keys.Find("Switch_0");
            SwitchData[] data =
            {
                CreateData(SwitchType.Blue, new Color(0.05f, 0.3f, 0.9f), sample),
                CreateData(SwitchType.Red, new Color(0.8f, 0.05f, 0.05f), sample),
                CreateData(SwitchType.Brown, new Color(0.4f, 0.17f, 0.06f), sample)
            };
            var switches = new List<KeyboardSwitch>();
            foreach (Transform mechanism in keys)
            {
                if (!mechanism.name.StartsWith("Switch_")) continue;
                KeyboardSwitch component = mechanism.GetComponent<KeyboardSwitch>();
                if (component == null) component = mechanism.gameObject.AddComponent<KeyboardSwitch>();
                AudioSource audio = mechanism.GetComponent<AudioSource>();
                audio.playOnAwake = false;
                var serialized = new SerializedObject(component);
                serialized.FindProperty("_topHousing").objectReferenceValue = mechanism.Find("Switch_TopHousing").GetComponent<MeshRenderer>();
                serialized.FindProperty("_bottomHousing").objectReferenceValue = mechanism.Find("Switch_BottomHousing").GetComponent<MeshRenderer>();
                serialized.FindProperty("_audioSource").objectReferenceValue = audio;
                SerializedProperty stems = serialized.FindProperty("_stems");
                stems.arraySize = 2;
                stems.GetArrayElementAtIndex(0).objectReferenceValue = mechanism.Find("MX_Stem_X").GetComponent<MeshRenderer>();
                stems.GetArrayElementAtIndex(1).objectReferenceValue = mechanism.Find("MX_Stem_Z").GetComponent<MeshRenderer>();
                SerializedProperty entries = serialized.FindProperty("_switchData");
                if (entries.arraySize == 0)
                {
                    entries.arraySize = data.Length;
                    for (int index = 0; index < data.Length; index++) entries.GetArrayElementAtIndex(index).objectReferenceValue = data[index];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                switches.Add(component);
            }

            KeyboardCustomization customizer = root.GetComponent<KeyboardCustomization>();
            var settings = new SerializedObject(customizer);
            SerializedProperty targets = settings.FindProperty("_switches");
            targets.arraySize = switches.Count;
            for (int index = 0; index < switches.Count; index++) targets.GetArrayElementAtIndex(index).objectReferenceValue = switches[index];
            settings.ApplyModifiedPropertiesWithoutUndo();
            customizer.SetSwitchType((SwitchType)settings.FindProperty("_switchType").intValue);
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// type와 color 및 sample의 기존 부품 재질로 축 SO의 초기 데이터를 생성한다.
    /// 기존 애셋이 있으면 그대로 반환하고 새 데이터의 사운드는 미지정으로 유지한다.
    /// </summary>
    private static SwitchData CreateData(SwitchType type, Color color, Transform sample)
    {
        string path = $"{DATA_FOLDER}/Switch_{type}.asset";
        SwitchData data = AssetDatabase.LoadAssetAtPath<SwitchData>(path);
        if (data != null) return data;
        Material stem = sample.Find("MX_Stem_X").GetComponent<MeshRenderer>().sharedMaterial;
        if (type != SwitchType.Red)
        {
            string materialPath = $"{DATA_FOLDER}/SwitchStem_{type}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing == null)
            {
                existing = new Material(stem) { name = $"SwitchStem_{type}" };
                existing.SetColor("_BaseColor", color);
                AssetDatabase.CreateAsset(existing, materialPath);
            }

            stem = existing;
        }

        data = ScriptableObject.CreateInstance<SwitchData>();
        var serialized = new SerializedObject(data);
        serialized.FindProperty("_type").intValue = (int)type;
        serialized.FindProperty("_topHousingMaterial").objectReferenceValue = sample.Find("Switch_TopHousing").GetComponent<MeshRenderer>().sharedMaterial;
        serialized.FindProperty("_bottomHousingMaterial").objectReferenceValue = sample.Find("Switch_BottomHousing").GetComponent<MeshRenderer>().sharedMaterial;
        serialized.FindProperty("_stemMaterial").objectReferenceValue = stem;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(data, path);
        return data;
    }
}
#endif
