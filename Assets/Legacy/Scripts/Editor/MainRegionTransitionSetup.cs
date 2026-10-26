using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

public static class MainRegionTransitionSetup
{
    /// <summary>
    /// Main의 거실과 마당 작업대 목적지를 Inspector용 지점으로 연결한다.
    /// 기존 이동 대상과 개별 설정을 유지하고 마당에도 같은 작업대 목록을 연결하여 씬을 저장한다.
    /// </summary>
    public static void ConfigureWorkbenchDestinations()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform desk = Array.Find(transforms, item => item.name == "RoomDesk");
        Transform livingFloor = Array.Find(transforms, item => item.name == "LivingRoom_Floor");
        Transform yardFloor = Array.Find(transforms, item => item.name == "Yard_GrassFloor");
        if (desk == null || livingFloor == null || yardFloor == null) throw new InvalidOperationException("Main workbench destinations are missing.");
        GameObject root = GameObject.Find("RegionWorkbenchDestinations");
        if (root == null) root = new GameObject("RegionWorkbenchDestinations");
        Transform living = CreatePoint(root.transform, "LivingRoomWorkbench", new Vector3(livingFloor.position.x, desk.position.y, desk.position.z), desk.rotation);
        Transform yard = CreatePoint(root.transform, "YardWorkbench", new Vector3(yardFloor.position.x, desk.position.y, yardFloor.position.z), desk.rotation);
        NextRegionWorkbenchTransition[] transitions = UnityEngine.Object.FindObjectsByType<NextRegionWorkbenchTransition>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        NextRegionWorkbenchTransition first = Array.Find(transitions, item => new SerializedObject(item).FindProperty("_nextSpawnRegion").intValue == 1);
        SerializedProperty originalTargets = new SerializedObject(first).FindProperty("_workbenchObjects");
        foreach (NextRegionWorkbenchTransition transition in transitions)
        {
            SerializedObject settings = new SerializedObject(transition);
            bool isYard = settings.FindProperty("_nextSpawnRegion").intValue == 2;
            settings.FindProperty("_workbenchAnchor").objectReferenceValue = desk;
            settings.FindProperty("_workbenchDestination").objectReferenceValue = isYard ? yard : living;
            if (isYard)
            {
                SerializedProperty targets = settings.FindProperty("_workbenchObjects");
                targets.arraySize = originalTargets.arraySize;
                for (int index = 0; index < targets.arraySize; index++)
                    targets.GetArrayElementAtIndex(index).objectReferenceValue = originalTargets.GetArrayElementAtIndex(index).objectReferenceValue;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"WORKBENCH_DESTINATION_READY trigger={transition.name} point={(isYard ? yard.name : living.name)}");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// Main의 거실과 마당 바닥을 상자 이동 목적지 Transform으로 연결한다.
    /// 두 진입 트리거의 목적지만 설정하고 기존 작업대와 문 설정은 유지한다.
    /// </summary>
    public static void ConfigureBoxTransitions()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform livingFloor = Array.Find(transforms, item => item.name == "LivingRoom_Floor");
        Transform yardFloor = Array.Find(transforms, item => item.name == "Yard_GrassFloor");
        if (livingFloor == null || yardFloor == null) throw new InvalidOperationException("Main region floors are missing.");
        NextRegionWorkbenchTransition[] transitions = UnityEngine.Object.FindObjectsByType<NextRegionWorkbenchTransition>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (NextRegionWorkbenchTransition transition in transitions)
        {
            SerializedObject settings = new SerializedObject(transition);
            bool yard = settings.FindProperty("_nextSpawnRegion").intValue == 2;
            Transform destination = yard ? yardFloor : livingFloor;
            settings.FindProperty("_boxDestination").objectReferenceValue = destination;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"BOX_TRANSITION_READY trigger={transition.name} destination={destination.name}");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// Main의 첫 번째와 두 번째 구역 키보드 프리팹 설정을 기존 기본값으로 연결한다.
    /// 비어 있는 새 항목만 초기화하며 기존 생성 개수와 임시 투척 체크 설정은 유지한다.
    /// </summary>
    public static void ConfigureRegionKeyboardPrefabs()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>(FindObjectsInactive.Include);
        SerializedObject settings = new SerializedObject(interaction);
        foreach (string field in new[] { "_firstRegionKeyboardPrefab", "_secondRegionKeyboardPrefab" })
            if (settings.FindProperty(field).objectReferenceValue == null)
                settings.FindProperty(field).objectReferenceValue = settings.FindProperty("_keyboardPrefab").objectReferenceValue;
        settings.ApplyModifiedPropertiesWithoutUndo();
        KeyboardCompletionProgress progress = UnityEngine.Object.FindFirstObjectByType<KeyboardCompletionProgress>(FindObjectsInactive.Include);
        SerializedObject progressSettings = new SerializedObject(progress);
        SerializedObject spawnerSettings = new SerializedObject(progressSettings.FindProperty("_spawner").objectReferenceValue);
        if (progressSettings.FindProperty("_firstRegionRewardPrefab").objectReferenceValue == null)
            progressSettings.FindProperty("_firstRegionRewardPrefab").objectReferenceValue = spawnerSettings.FindProperty("_keyboardPrefab").objectReferenceValue;
        progressSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("REGION_KEYBOARD_PREFABS_CONFIGURED");
    }

    /// <summary>
    /// Main의 두 번째 발사 지점을 임시 연속 투척 모드로 연결한다.
    /// 기존 간격과 최대 개수를 유지하고 두 번째 구역 보상 프리팹을 테스트용으로 사용하여 씬을 저장한다.
    /// </summary>
    public static void EnableTemporarySecondPointThrow()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        KeyboardCompletionProgress progress = UnityEngine.Object.FindFirstObjectByType<KeyboardCompletionProgress>(FindObjectsInactive.Include);
        SerializedObject progressSettings = new SerializedObject(progress);
        SerializedObject visitSettings = new SerializedObject(progressSettings.FindProperty("_throwVisit").objectReferenceValue);
        SerializedObject spawnerSettings = new SerializedObject(progressSettings.FindProperty("_spawner").objectReferenceValue);
        spawnerSettings.FindProperty("_temporarySecondPointThrow").boolValue = true;
        spawnerSettings.FindProperty("_temporaryThrowPoint").objectReferenceValue = visitSettings.FindProperty("_secondRegionThrowPoint").objectReferenceValue;
        spawnerSettings.FindProperty("_temporaryKeyboardPrefab").objectReferenceValue = progressSettings.FindProperty("_secondRegionRewardPrefab").objectReferenceValue;
        spawnerSettings.FindProperty("_temporaryVisit").objectReferenceValue = progressSettings.FindProperty("_throwVisit").objectReferenceValue;
        spawnerSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("TEMPORARY_SECOND_POINT_THROW_ENABLED");
    }

    /// <summary>
    /// 복구한 Main의 거실 입구에 일회성 진입 트리거를 구성한다.
    /// 기존 책상, 모니터, 기준 키보드, 배치 판정과 카메라 및 자동 작업대를 복제 없이 연결하고 씬을 저장한다.
    /// </summary>
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>(FindObjectsInactive.Include);
        KeyboardCompletionProgress progress = UnityEngine.Object.FindFirstObjectByType<KeyboardCompletionProgress>(FindObjectsInactive.Include);
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(FindObjectsInactive.Include);
        SerializedObject interactionSettings = new SerializedObject(interaction);
        Transform reference = ((GameObject)interactionSettings.FindProperty("_referenceKeyboard").objectReferenceValue).transform;
        List<Transform> objects = new List<Transform> { reference, interaction.transform };
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform item in transforms)
        {
            if (item.name == "RoomDesk" || item.name == "AutomaticWorkbench" || item.name == "Pad"
                || item.name == "FixedCam" || item.name == "MonitorCam"
                || item.name == "Monitor_16x9_ScaleUp" || item.name == "Monitor_16x9"
                || item.name == "Computer" || item.name == "ComputerTower" || item.name == "PC")
                objects.Add(item);
        }
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (canvas.renderMode == RenderMode.WorldSpace) objects.Add(canvas.transform);
        Transform[] candidates = objects.ToArray();
        objects.RemoveAll(item => Array.Exists(candidates, parent => parent != item && item.IsChildOf(parent)));
        HashSet<Transform> unique = new HashSet<Transform>(objects);
        objects = new List<Transform>(unique);
        Transform floor = Array.Find(transforms, item => item.name == "LivingRoom_Floor");
        Transform desk = Array.Find(transforms, item => item.name == "RoomDesk");
        Transform yardFloor = Array.Find(transforms, item => item.name == "Yard_GrassFloor");
        Transform yardDoor = Array.Find(transforms, item => item.name == "LivingYardDoor");
        if (floor == null || desk == null) throw new InvalidOperationException("Main living room or workbench is missing.");
        if (yardFloor == null || yardDoor == null) throw new InvalidOperationException("Main yard is missing.");
        interactionSettings.FindProperty("_spawnRegionsOnEntry").boolValue = true;
        interactionSettings.FindProperty("_thirdRegionCount").intValue = 1;
        if (interactionSettings.FindProperty("_yardKeyboardPrefab").objectReferenceValue == null)
            interactionSettings.FindProperty("_yardKeyboardPrefab").objectReferenceValue = interactionSettings.FindProperty("_keyboardPrefab").objectReferenceValue;
        SerializedObject progressSettings = new SerializedObject(progress);
        SerializedObject spawnerSettings = new SerializedObject((BackgroundKeyboardThrowSpawner)progressSettings.FindProperty("_spawner").objectReferenceValue);
        if (progressSettings.FindProperty("_secondRegionRewardPrefab").objectReferenceValue == null)
            progressSettings.FindProperty("_secondRegionRewardPrefab").objectReferenceValue = spawnerSettings.FindProperty("_keyboardPrefab").objectReferenceValue;
        progressSettings.ApplyModifiedPropertiesWithoutUndo();
        BoxCollider firstRegion = (BoxCollider)interactionSettings.FindProperty("_firstRegion").objectReferenceValue;
        BoxCollider secondRegion = (BoxCollider)interactionSettings.FindProperty("_secondRegion").objectReferenceValue;
        BoxCollider thirdRegion = (BoxCollider)interactionSettings.FindProperty("_thirdRegion").objectReferenceValue;
        BoxCollider original = firstRegion.transform.parent.GetComponent<BoxCollider>();
        ConfigureSpawnRegion(firstRegion, original.transform.TransformPoint(original.center), original.size);
        ConfigureSpawnRegion(secondRegion, new Vector3(floor.position.x, firstRegion.transform.position.y, floor.position.z), original.size);
        ConfigureSpawnRegion(thirdRegion, new Vector3(yardFloor.position.x, firstRegion.transform.position.y, yardFloor.position.z), original.size);
        interactionSettings.ApplyModifiedPropertiesWithoutUndo();
        Vector3 offset = new Vector3(floor.position.x - desk.position.x, 0f, 0f);
        BackgroundKeyboardThrowVisit visit = (BackgroundKeyboardThrowVisit)progressSettings.FindProperty("_throwVisit").objectReferenceValue;
        SerializedObject visitSettings = new SerializedObject(visit);
        Transform actor = (Transform)visitSettings.FindProperty("_actor").objectReferenceValue;
        Transform visitPoint = (Transform)visitSettings.FindProperty("_visitPoint").objectReferenceValue;
        GameObject pointsRoot = GameObject.Find("SecondRegionBossPoints");
        if (pointsRoot == null) pointsRoot = new GameObject("SecondRegionBossPoints");
        Transform homePoint = CreatePoint(pointsRoot.transform, "HomePoint", actor.position + offset, actor.rotation);
        Transform animationPoint = CreatePoint(pointsRoot.transform, "VisitPoint", visitPoint.position + offset, visitPoint.rotation);
        Vector3 throwPosition = spawnerSettings.targetObject is Component emitter ? emitter.transform.position + offset : animationPoint.position;
        Transform emitterTransform = ((Component)spawnerSettings.targetObject).transform;
        if (emitterTransform.IsChildOf(actor)) throwPosition += visitPoint.position - actor.position;
        Transform throwPoint = CreatePoint(pointsRoot.transform, "ThrowPoint", throwPosition, emitterTransform.rotation);
        visitSettings.FindProperty("_secondRegionHomePoint").objectReferenceValue = homePoint;
        visitSettings.FindProperty("_secondRegionVisitPoint").objectReferenceValue = animationPoint;
        visitSettings.FindProperty("_secondRegionThrowPoint").objectReferenceValue = throwPoint;
        visitSettings.ApplyModifiedPropertiesWithoutUndo();
        GameObject trigger = GameObject.Find("NextRegionWorkbenchTransition");
        if (trigger == null) trigger = new GameObject("NextRegionWorkbenchTransition");
        NextRegionWorkbenchTransition transition = trigger.GetComponent<NextRegionWorkbenchTransition>();
        if (transition == null) transition = trigger.AddComponent<NextRegionWorkbenchTransition>();
        BoxCollider collider = trigger.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(2f, 8f, 5.2f);
        Rigidbody body = trigger.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        Transform door = (Transform)new SerializedObject(progress).FindProperty("_door").objectReferenceValue;
        Collider leaf = door.GetComponentInChildren<Collider>();
        trigger.transform.position = new Vector3(door.position.x - 3.4f, 0.35f, leaf.bounds.center.z);
        SerializedObject settings = new SerializedObject(transition);
        settings.FindProperty("_completionProgress").objectReferenceValue = progress;
        settings.FindProperty("_interactionController").objectReferenceValue = interaction;
        settings.FindProperty("_player").objectReferenceValue = player;
        settings.FindProperty("_workbenchOffset").vector3Value = offset;
        settings.FindProperty("_nextSpawnRegion").intValue = 1;
        settings.FindProperty("_nextDoor").objectReferenceValue = yardDoor;
        SerializedProperty targets = settings.FindProperty("_workbenchObjects");
        targets.arraySize = objects.Count;
        for (int index = 0; index < objects.Count; index++)
        {
            targets.GetArrayElementAtIndex(index).objectReferenceValue = objects[index];
            Debug.Log($"WORKBENCH_MOVE_TARGET name={objects[index].name} from={objects[index].position} to={objects[index].position + offset}");
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        GameObject yardTrigger = GameObject.Find("YardWorkbenchTransition");
        if (yardTrigger == null) yardTrigger = new GameObject("YardWorkbenchTransition");
        NextRegionWorkbenchTransition yardTransition = yardTrigger.GetComponent<NextRegionWorkbenchTransition>();
        if (yardTransition == null) yardTransition = yardTrigger.AddComponent<NextRegionWorkbenchTransition>();
        BoxCollider yardCollider = yardTrigger.GetComponent<BoxCollider>();
        yardCollider.isTrigger = true;
        yardCollider.size = new Vector3(5.2f, 8f, 2f);
        Rigidbody yardBody = yardTrigger.GetComponent<Rigidbody>();
        yardBody.isKinematic = true;
        yardBody.useGravity = false;
        Bounds yardDoorBounds = yardDoor.GetComponentInChildren<Collider>().bounds;
        yardTrigger.transform.position = new Vector3(yardDoorBounds.center.x, 0.35f, yardDoorBounds.center.z + 3.4f);
        SerializedObject yardSettings = new SerializedObject(yardTransition);
        yardSettings.FindProperty("_completionProgress").objectReferenceValue = progress;
        yardSettings.FindProperty("_interactionController").objectReferenceValue = interaction;
        yardSettings.FindProperty("_player").objectReferenceValue = player;
        yardSettings.FindProperty("_nextSpawnRegion").intValue = 2;
        yardSettings.FindProperty("_nextDoor").objectReferenceValue = null;
        yardSettings.FindProperty("_workbenchOffset").vector3Value = Vector3.zero;
        SerializedProperty yardTargets = yardSettings.FindProperty("_workbenchObjects");
        yardTargets.arraySize = 0;
        yardSettings.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Main save failed.");
        Debug.Log($"MAIN_REGION_TRANSITION_READY targets={objects.Count} offset={offset} trigger={trigger.transform.position}");
        Debug.Log($"REGION_ENTRY_SPAWN_READY first={firstRegion.bounds.center} living={secondRegion.bounds.center} yard={thirdRegion.bounds.center}");
    }

    /// <summary>
    /// region을 position의 구역별 천장 생성 위치에 배치하고 size를 적용한다.
    /// 기존 부모의 배율과 구역별 생성 개수를 유지한다.
    /// </summary>
    private static void ConfigureSpawnRegion(BoxCollider region, Vector3 position, Vector3 size)
    {
        region.transform.position = position;
        region.transform.localRotation = Quaternion.identity;
        region.transform.localScale = Vector3.one;
        region.center = Vector3.zero;
        region.size = size;
        region.isTrigger = true;
    }

    /// <summary>
    /// parent 아래 name 지점을 준비하고 새 지점만 position과 rotation으로 배치한다.
    /// 이미 존재하는 지점은 Inspector에서 수정한 자세를 유지하여 반환한다.
    /// </summary>
    private static Transform CreatePoint(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        Transform point = parent.Find(name);
        if (point != null) return point;
        point = new GameObject(name).transform;
        point.SetParent(parent, false);
        point.SetPositionAndRotation(position, rotation);
        return point;
    }

    /// <summary>
    /// 복구한 Main 씬의 문, 방과 작업대 배치를 조사해 전환 연결에 사용할 위치 기록을 저장한다.
    /// 씬 오브젝트는 변경하지 않는다.
    /// </summary>
    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main.unity");
        StringBuilder report = new StringBuilder();
        foreach (Transform item in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string name = item.name;
            if (name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("room", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("desk", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("monitor", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("computer", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("automatic", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) < 0
                && name != "Pad" && name != "FixedCam") continue;
            report.AppendLine($"{name} parent={(item.parent != null ? item.parent.name : "ROOT")} position={item.position} rotation={item.eulerAngles} scale={item.lossyScale} active={item.gameObject.activeInHierarchy}");
        }
        File.WriteAllText("Logs/MainRegionLayout.txt", report.ToString());
        Debug.Log("MAIN_REGION_INSPECTION_COMPLETE");
    }
}
