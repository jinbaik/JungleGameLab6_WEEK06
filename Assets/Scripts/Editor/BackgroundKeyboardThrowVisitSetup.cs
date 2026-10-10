using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

public static class BackgroundKeyboardThrowVisitSetup
{
    /// <summary>
    /// 복제 씬의 방문점과 시간 설정을 현재 Main에 적용한다.
    /// Main_ThrowVisit의 설정을 사용하고 Main의 actor 시작 위치 및 기존 던지기 설정을 유지하여 저장한다.
    /// </summary>
    public static void ApplyToMain()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Main_ThrowVisit.unity");
        BackgroundKeyboardThrowVisit source = UnityEngine.Object.FindFirstObjectByType<BackgroundKeyboardThrowVisit>();
        SerializedObject sourceSettings = new SerializedObject(source);
        Transform sourcePoint = (Transform)sourceSettings.FindProperty("_visitPoint").objectReferenceValue;
        Vector3 pointPosition = sourcePoint.position;
        string[] timingFields = { "_interval", "_firstDelay", "_moveDuration", "_waitBeforeThrow", "_waitAfterThrow" };
        float[] timings = new float[timingFields.Length];
        for (int index = 0; index < timings.Length; index++)
            timings[index] = sourceSettings.FindProperty(timingFields[index]).floatValue;

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        Transform actor = Array.Find(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None), item => item.name == "eminemThrowingThings");
        actor.gameObject.SetActive(true);
        BackgroundKeyboardThrowSpawner spawner = UnityEngine.Object.FindFirstObjectByType<BackgroundKeyboardThrowSpawner>();
        SerializedObject spawnSettings = new SerializedObject(spawner);
        spawnSettings.FindProperty("_externalSequence").boolValue = true;
        spawnSettings.ApplyModifiedPropertiesWithoutUndo();
        GameObject existingPoint = GameObject.Find("ThrowVisitPoint");
        Transform point = existingPoint != null ? existingPoint.transform : new GameObject("ThrowVisitPoint").transform;
        point.position = pointPosition;
        BackgroundKeyboardThrowVisit visit = spawner.GetComponent<BackgroundKeyboardThrowVisit>();
        if (visit == null) visit = spawner.gameObject.AddComponent<BackgroundKeyboardThrowVisit>();
        SerializedObject settings = new SerializedObject(visit);
        settings.FindProperty("_actor").objectReferenceValue = actor;
        settings.FindProperty("_visitPoint").objectReferenceValue = point;
        settings.FindProperty("_spawner").objectReferenceValue = spawner;
        for (int index = 0; index < timings.Length; index++)
            settings.FindProperty(timingFields[index]).floatValue = timings[index];
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"MAIN_THROW_VISIT_READY actor={actor.name} home={actor.position} target={point.position} interval={timings[0]}");
    }

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
