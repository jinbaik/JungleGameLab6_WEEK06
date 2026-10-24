using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Tournament;
using KeyboardModeling.Assembly;

public static class AssemblyCatalogPrefabSetup
{
    private const string PREFAB_FOLDER = "Assets/Resource/Prefabs/AssemblyCatalog";

    /// <summary>
    /// 열린 조립 테스트 씬의 기존 데이터로 전용 Prefab과 항목 연결을 생성한다.
    /// 공유 원본은 읽기만 하고 Esc 스위치 슬롯 및 현재 씬의 참조만 저장한다.
    /// </summary>
    [MenuItem("Tools/Keyboard Assembly/Configure Catalog Drag")]
    public static void Configure()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().name != "TestScene_KeyboardAssembly")
            throw new InvalidOperationException("조립 테스트 씬을 편집 모드로 열어야 합니다.");

        AssemblyCatalogUI ui = UnityEngine.Object.FindAnyObjectByType<AssemblyCatalogUI>();
        AssemblyDragInput input = UnityEngine.Object.FindAnyObjectByType<AssemblyDragInput>();
        SerializedObject uiData = new SerializedObject(ui);
        SerializedObject inputData = new SerializedObject(input);
        AssemblyPartSlot keycapSlot = (AssemblyPartSlot)inputData.FindProperty("_partSlot").objectReferenceValue;
        GameObject keycapSource = (GameObject)uiData.FindProperty("_previewKeycapPrefab").objectReferenceValue;
        Transform switchSource = FindEscSwitch(keycapSlot);
        AssemblyPartSlot switchSlot = (AssemblyPartSlot)inputData.FindProperty("_switchSlot").objectReferenceValue;
        if (switchSlot == null)
        {
            GameObject slotObject = new GameObject("EscSwitchSlot");
            slotObject.transform.SetParent(switchSource.transform.parent, false);
            slotObject.transform.SetPositionAndRotation(switchSource.transform.position, switchSource.transform.rotation);
            slotObject.transform.localScale = switchSource.transform.localScale;
            switchSlot = slotObject.AddComponent<AssemblyPartSlot>();
        }
        BoxCollider switchCollider = switchSource.GetComponent<BoxCollider>();
        if (switchCollider == null)
            switchCollider = AddBoundsCollider(switchSource.gameObject);
        SerializedObject slotData = new SerializedObject(switchSlot);
        slotData.FindProperty("_part").objectReferenceValue = switchSource.transform;
        slotData.FindProperty("_partCollider").objectReferenceValue = switchCollider;
        slotData.FindProperty("_startsInstalled").boolValue = true;
        slotData.FindProperty("_restoreOnInvalidDrop").boolValue = false;
        slotData.ApplyModifiedPropertiesWithoutUndo();
        inputData.FindProperty("_switchSlot").objectReferenceValue = switchSlot;
        inputData.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(switchSource);

        if (!AssetDatabase.IsValidFolder(PREFAB_FOLDER))
            AssetDatabase.CreateFolder("Assets/Resource/Prefabs", "AssemblyCatalog");
        SerializedProperty entries = uiData.FindProperty("_spawnEntries");
        entries.arraySize = 0;
        List<KeycapDefinition> definitions = ReadAssets<KeycapDefinition>(uiData.FindProperty("_keycapDefinitions"));
        foreach (Material material in ReadAssets<Material>(uiData.FindProperty("_keycapMaterials")))
        {
            KeycapDefinition definition = definitions.Find(candidate => candidate.ShellMesh == null && candidate.Material == material);
            AssemblyDraggablePart prefab = CreateKeycap(keycapSource, keycapSlot.Part.lossyScale, material, definition);
            AddEntry(entries, material, definition, null, prefab);
        }
        foreach (KeycapDefinition definition in definitions)
        {
            if (definition.ShellMesh == null)
                continue;
            AssemblyDraggablePart prefab = CreateKeycap(keycapSource, keycapSlot.Part.lossyScale, definition.Material, definition);
            AddEntry(entries, definition.Material, definition, null, prefab);
        }

        MeshRenderer top = switchSource.Find("Switch_TopHousing").GetComponent<MeshRenderer>();
        MeshRenderer bottom = switchSource.Find("Switch_BottomHousing").GetComponent<MeshRenderer>();
        MeshRenderer stemX = switchSource.Find("MX_Stem_X").GetComponent<MeshRenderer>();
        MeshRenderer stemZ = switchSource.Find("MX_Stem_Z").GetComponent<MeshRenderer>();
        foreach (SwitchData data in ReadAssets<SwitchData>(uiData.FindProperty("_switchData")))
        {
            GameObject root = CopyVisuals(switchSource.gameObject, switchSource.transform.lossyScale,
                renderer => new[] { renderer == top ? data.TopHousingMaterial
                    : renderer == bottom ? data.BottomHousingMaterial
                    : renderer == stemX || renderer == stemZ ? data.StemMaterial : renderer.sharedMaterial });
            root.name = "AssemblySwitch_" + data.Type;
            AssemblyDraggablePart prefab = SavePart(root, data);
            AddEntry(entries, null, null, data, prefab);
        }
        uiData.FindProperty("_dragInput").objectReferenceValue = input;
        uiData.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        EditorSceneManager.SaveScene(ui.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("키캡 24개와 스위치 8개의 전용 Prefab 연결을 저장했습니다. Esc 스위치: " + switchSource.name);
    }

    /// <summary>
    /// keycapSlot과 XZ 위치가 가장 가까운 기존 스위치를 반환한다.
    /// 같은 키보드 인스턴스에서만 검색하며 기존 부품의 위치는 변경하지 않는다.
    /// </summary>
    private static Transform FindEscSwitch(AssemblyPartSlot keycapSlot)
    {
        Transform result = null;
        float nearest = float.PositiveInfinity;
        foreach (MeshFilter filter in keycapSlot.transform.root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name != "Switch_TopHousing")
                continue;
            Transform candidate = filter.transform.parent;
            Vector3 offset = candidate.transform.position - keycapSlot.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < nearest)
            {
                nearest = offset.sqrMagnitude;
                result = candidate;
            }
        }
        if (result == null)
            throw new InvalidOperationException("Esc 위치의 스위치를 찾지 못했습니다.");
        return result;
    }

    /// <summary>
    /// source와 scale, material 및 definition으로 독립 키캡 Prefab을 만든다.
    /// 아티산은 전용 메시와 재질을 사용하고 원본 에셋은 변경하지 않는다.
    /// </summary>
    private static AssemblyDraggablePart CreateKeycap(GameObject source, Vector3 scale,
        Material material, KeycapDefinition definition)
    {
        bool artisan = definition != null && definition.ShellMesh != null;
        GameObject root;
        if (artisan)
        {
            root = new GameObject("AssemblyKeycap_" + definition.Id);
            root.transform.localScale = scale;
            GameObject shell = new GameObject("Shell", typeof(MeshFilter), typeof(MeshRenderer));
            shell.transform.SetParent(root.transform, false);
            shell.GetComponent<MeshFilter>().sharedMesh = definition.ShellMesh;
            shell.GetComponent<MeshRenderer>().sharedMaterials = definition.ModelMaterials;
        }
        else
        {
            root = CopyVisuals(source, scale,
                renderer => renderer.name == "PBT_SculptedShell" ? new[] { material } : renderer.sharedMaterials);
            root.name = "AssemblyKeycap_" + (definition != null ? definition.Id : material.name);
        }
        return SavePart(root, artisan ? (UnityEngine.Object)definition : material);
    }

    /// <summary>
    /// source의 메시와 로컬 배치를 복사하고 materials 함수의 재질 및 scale을 적용한다.
    /// 게임플레이 스크립트나 입력 및 사운드 컴포넌트 없이 시각 오브젝트만 반환한다.
    /// </summary>
    private static GameObject CopyVisuals(GameObject source, Vector3 scale,
        Func<MeshRenderer, Material[]> materials)
    {
        GameObject root = new GameObject("AssemblyPart");
        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || filter.GetComponent<TextMesh>() != null)
                continue;
            Matrix4x4 matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            GameObject child = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = matrix.GetColumn(3);
            child.transform.localRotation = matrix.rotation;
            child.transform.localScale = matrix.lossyScale;
            child.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            child.GetComponent<MeshRenderer>().sharedMaterials = materials(renderer);
        }
        root.transform.localScale = scale;
        return root;
    }

    /// <summary>
    /// root의 메시 경계를 루트 로컬 좌표로 합쳐 BoxCollider를 추가하고 반환한다.
    /// 기존 시각 요소의 위치, 회전과 크기는 변경하지 않는다.
    /// </summary>
    private static BoxCollider AddBoundsCollider(GameObject root)
    {
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds meshBounds = filter.sharedMesh.bounds;
            Matrix4x4 matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                point = matrix.MultiplyPoint3x4(point);
                if (first)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    first = false;
                }
                else
                    bounds.Encapsulate(point);
            }
        }
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = bounds.size;
        return collider;
    }

    /// <summary>
    /// root에 Rigidbody와 드래그 컴포넌트를 연결하고 identity의 GUID 경로로 Prefab을 저장한다.
    /// 생성한 임시 root는 정리하고 저장된 Prefab의 부품 컴포넌트를 반환한다.
    /// </summary>
    private static AssemblyDraggablePart SavePart(GameObject root, UnityEngine.Object identity)
    {
        BoxCollider collider = AddBoundsCollider(root);
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 0.02f;
        body.useGravity = false;
        body.isKinematic = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        AssemblyDraggablePart part = root.AddComponent<AssemblyDraggablePart>();
        SerializedObject partData = new SerializedObject(part);
        partData.FindProperty("_body").objectReferenceValue = body;
        partData.FindProperty("_partCollider").objectReferenceValue = collider;
        partData.ApplyModifiedPropertiesWithoutUndo();
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(identity));
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_FOLDER + "/Part_" + guid + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab.GetComponent<AssemblyDraggablePart>();
    }

    /// <summary>
    /// entries에 material, definition 또는 data와 prefab의 직접 에셋 연결을 추가한다.
    /// 표시 이름을 이용한 런타임 검색 없이 직렬화 배열을 갱신한다.
    /// </summary>
    private static void AddEntry(SerializedProperty entries, Material material, KeycapDefinition definition,
        SwitchData data, AssemblyDraggablePart prefab)
    {
        int index = entries.arraySize++;
        SerializedProperty entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("_keycapMaterial").objectReferenceValue = material;
        entry.FindPropertyRelative("_keycapDefinition").objectReferenceValue = definition;
        entry.FindPropertyRelative("_switchData").objectReferenceValue = data;
        entry.FindPropertyRelative("_prefab").objectReferenceValue = prefab;
    }

    /// <summary>
    /// property 배열에 저장된 T 타입 참조를 순서대로 반환한다.
    /// 원본 직렬화 데이터는 변경하지 않는다.
    /// </summary>
    private static List<T> ReadAssets<T>(SerializedProperty property) where T : UnityEngine.Object
    {
        List<T> values = new List<T>();
        for (int index = 0; index < property.arraySize; index++)
            values.Add((T)property.GetArrayElementAtIndex(index).objectReferenceValue);
        return values;
    }
}
