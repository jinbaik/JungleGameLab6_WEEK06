using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

public static class BackgroundKeyboardThrowVisitSetup
{
    /// <summary>
    /// Main을 새 방문 연출 씬으로 복사하고 던지기 순서 및 이동 목표를 연결한다.
    /// 기존 actor와 발사 위치를 사용하며 원본 Main은 변경하지 않고 복제 씬만 저장한다.
    /// </summary>
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch project.");
        const string DESTINATION = "Assets/Scenes/Main_ThrowVisit.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DESTINATION) == null && !AssetDatabase.CopyAsset("Assets/Scenes/Main.unity", DESTINATION))
            throw new InvalidOperationException("Scene copy failed.");
        var scene = EditorSceneManager.OpenScene(DESTINATION);
        Transform actor = Array.Find(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None), item => item.name == "eminemThrowingThings");
        actor.gameObject.SetActive(true);
        BackgroundKeyboardThrowSpawner spawner = UnityEngine.Object.FindFirstObjectByType<BackgroundKeyboardThrowSpawner>();
        SerializedObject spawnSettings = new SerializedObject(spawner);
        spawnSettings.FindProperty("_externalSequence").boolValue = true;
        spawnSettings.ApplyModifiedPropertiesWithoutUndo();
        Transform point = new GameObject("ThrowVisitPoint").transform;
        Vector3 position = spawner.transform.position - spawner.transform.forward;
        position.y = actor.position.y;
        point.position = position;
        BackgroundKeyboardThrowVisit visit = spawner.gameObject.AddComponent<BackgroundKeyboardThrowVisit>();
        SerializedObject settings = new SerializedObject(visit);
        settings.FindProperty("_actor").objectReferenceValue = actor;
        settings.FindProperty("_visitPoint").objectReferenceValue = point;
        settings.FindProperty("_spawner").objectReferenceValue = spawner;
        settings.FindProperty("_interval").floatValue = spawnSettings.FindProperty("_spawnInterval").floatValue;
        settings.FindProperty("_firstDelay").floatValue = spawnSettings.FindProperty("_firstSpawnDelay").floatValue;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"THROW_VISIT_READY scene={DESTINATION} actor={actor.name} home={actor.position} target={point.position}");
    }
}
