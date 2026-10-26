using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

public static class KeyboardSpawnRegionsSetup
{
    /// <summary>
    /// Main의 기존 천장 생성 영역을 세 구역으로 나누어 컨트롤러에 연결한다.
    /// 전체 생성 개수와 진행 조건을 유지하며 구역별 개수를 배분하고 씬을 저장한다.
    /// </summary>
    public static void RestoreMainScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        KeyboardInteractionController controller = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>(FindObjectsInactive.Include);
        SerializedObject settings = new SerializedObject(controller);
        BoxCollider[] colliders = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        BoxCollider source = Array.Find(colliders, collider => collider.gameObject.name == "KeyboardCollider");
        if (source == null) throw new InvalidOperationException("Main ceiling region is missing.");
        string[] names = { "FirstRegion", "SecondRegion", "ThirdRegion" };
        string[] fields = { "_firstRegion", "_secondRegion", "_thirdRegion" };
        int total = 0;
        foreach (string field in fields)
            total += settings.FindProperty(field + "Count").intValue;
        source.isTrigger = true;
        for (int index = 0; index < names.Length; index++)
        {
            Transform child = source.transform.Find(names[index]);
            if (child == null)
            {
                child = new GameObject(names[index]).transform;
                child.SetParent(source.transform, false);
            }
            BoxCollider region = child.GetComponent<BoxCollider>();
            if (region == null) region = child.gameObject.AddComponent<BoxCollider>();
            region.isTrigger = true;
            region.size = new Vector3(source.size.x / 3f, source.size.y, source.size.z);
            region.center = source.center + Vector3.right * ((index - 1) * source.size.x / 3f);
            int count = total / 3 + (index < total % 3 ? 1 : 0);
            settings.FindProperty(fields[index]).objectReferenceValue = region;
            settings.FindProperty(fields[index] + "Count").intValue = count;
            Debug.Log($"SPAWN_REGION_RESTORED scene=Main index={index} name={names[index]} count={count} bounds={region.bounds}");
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Main save failed.");
        Debug.Log($"SPAWN_REGIONS_READY scene=Main regions=3 totalKeyboards={total}");
    }
}