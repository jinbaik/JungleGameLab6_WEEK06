using System;

using UnityEngine;

using UnityEditor;

namespace KeyboardModeling.Editor
{
    public static class KeyboardPlayableVariantSetup
    {
        /// <summary>
        /// 기존 기본·원목·금·천공 키보드의 배경용과 게임용 프리팹을 대응 연결한다.
        /// 프리팹별 게임용 에셋을 저장하여 배치 시 원본 종류를 유지하며 메시와 재질은 변경하지 않는다.
        /// </summary>
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Use the isolated batch project.");
            if (!AssetDatabase.IsValidFolder("Assets/Data/KeyboardVariants"))
                AssetDatabase.CreateFolder("Assets/Data", "KeyboardVariants");
            foreach (string theme in new[] { "", "_Wood", "_Gold", "_Celestial" })
            {
                string stem = "Assets/Resource/Prefabs/Keyboard_TKL_87" + theme;
                string gamePath = stem + (theme == "" ? "" : "_Game") + ".prefab";
                GameObject gamePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gamePath);
                string definitionPath = "Assets/Data/KeyboardVariants/" + (theme == "" ? "Basic" : theme.Substring(1)) + ".asset";
                KeyboardPlayableVariantDefinition definition = AssetDatabase.LoadAssetAtPath<KeyboardPlayableVariantDefinition>(definitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<KeyboardPlayableVariantDefinition>();
                    AssetDatabase.CreateAsset(definition, definitionPath);
                }
                SerializedObject definitionSettings = new SerializedObject(definition);
                definitionSettings.FindProperty("_gamePrefab").objectReferenceValue = gamePrefab;
                definitionSettings.ApplyModifiedPropertiesWithoutUndo();
                foreach (string path in new[] { gamePath, stem + "_Background.prefab" })
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        KeyboardPlayableVariant variant = root.GetComponent<KeyboardPlayableVariant>();
                        if (variant == null)
                            variant = root.AddComponent<KeyboardPlayableVariant>();
                        SerializedObject settings = new SerializedObject(variant);
                        settings.FindProperty("_definition").objectReferenceValue = definition;
                        settings.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log($"KEYBOARD_VARIANT_LINKED {path} -> {gamePath}");
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
