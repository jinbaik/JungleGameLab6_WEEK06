using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

public static class KeyboardStorageBoxSetup
{
    /// <summary>
    /// 기존 종이상자 프리팹의 윗면만 밝은 갈색 덮개와 접힘선으로 구분한다.
    /// 물리와 보관 설정을 유지하고 장식 메시 및 재질만 저장한다.
    /// </summary>
    public static void DecorateBox()
    {
        string path = "Assets/Resource/Prefabs/KeyboardStorageBox.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        Transform previous = root.transform.Find("Decorations");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        Transform decoration = new GameObject("Decorations").transform;
        decoration.SetParent(root.transform, false);
        Material edge = CreateDecorationMaterial("KeyboardStorageBoxTrim", new Color(0.36f, 0.21f, 0.10f));
        Material lid = CreateDecorationMaterial("KeyboardStorageBoxLid", new Color(0.60f, 0.39f, 0.20f));
        Material tape = CreateDecorationMaterial("KeyboardStorageBoxTape", new Color(0.69f, 0.48f, 0.27f));
        CreateDecoration("CardboardTop", decoration, new Vector3(0f, 1.105f, 0f), new Vector3(3.16f, 0.01f, 1.46f), lid);
        CreateDecoration("FlapSeam", decoration, new Vector3(0f, 1.112f, 0f), new Vector3(3.1f, 0.004f, 0.022f), edge);
        CreateDecoration("PackingTape", decoration, new Vector3(0f, 1.116f, 0f), new Vector3(0.24f, 0.004f, 1.44f), tape);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        SaveDecorationPreview(root);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log("STORAGE_BOX_DECORATION_COMPLETE cardboardLid=true keyboardIcon=false collidersUnchanged=true");
    }

    /// <summary>
    /// name과 color로 장식용 재질을 준비하여 반환한다.
    /// 기존 재질이 있으면 색만 갱신하고 없으면 에셋으로 생성한다.
    /// </summary>
    private static Material CreateDecorationMaterial(string name, Color color)
    {
        string path = "Assets/Resource/Prefabs/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// name, parent, position, size와 material로 장식 메시를 생성하여 반환한다.
    /// 충돌체를 제거하여 기존 클릭과 바닥 배치 판정을 유지한다.
    /// </summary>
    private static GameObject CreateDecoration(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = size;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    /// <summary>
    /// root의 장식 결과를 임시 카메라로 촬영하여 Logs에 미리보기 이미지를 저장한다.
    /// 촬영에 사용한 카메라, 조명과 텍스처를 정리한다.
    /// </summary>
    private static void SaveDecorationPreview(GameObject root)
    {
        GameObject cameraObject = new GameObject("DecorationPreviewCamera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, root.scene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.cameraType = CameraType.Preview;
        camera.scene = root.scene;
        camera.transform.position = new Vector3(4f, 3.8f, -5f);
        camera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
        camera.orthographic = true;
        camera.orthographicSize = 2.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.17f, 0.19f, 0.22f);
        GameObject lightObject = new GameObject("DecorationPreviewLight");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, root.scene);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2f;
        light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        RenderTexture target = new RenderTexture(640, 640, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(640, 640, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
        image.Apply();
        System.IO.File.WriteAllBytes("Logs/KeyboardStorageBoxDecoration.png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(lightObject);
    }

    /// <summary>
    /// Main 1의 상자 프리팹을 닫힌 네모 박스와 위쪽 클릭 영역으로 구성한다.
    /// 기존 배치를 유지하며 상자가 없는 경우에만 씬에 하나를 추가한다.
    /// </summary>
    public static void ConfigureMainScene()
    {
        EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main 1.unity");
        KeyboardInteractionController interaction = Object.FindAnyObjectByType<KeyboardInteractionController>();
        BoxPickupController pickup = interaction.GetComponent<BoxPickupController>();
        if (pickup == null) pickup = interaction.gameObject.AddComponent<BoxPickupController>();
        SerializedObject interactionSettings = new SerializedObject(interaction);
        SerializedObject pickupSettings = new SerializedObject(pickup);
        pickupSettings.FindProperty("_camera").objectReferenceValue = interactionSettings.FindProperty("_camera").objectReferenceValue;
        pickupSettings.FindProperty("_holdViewportPosition").vector2Value = new Vector2(0.2f, 0.18f);
        pickupSettings.FindProperty("_holdDistance").floatValue = 4f;
        pickupSettings.ApplyModifiedPropertiesWithoutUndo();
        string prefabPath = "Assets/Resource/Prefabs/KeyboardStorageBox.prefab";
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Prefabs/KeyboardStorageBox.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.48f, 0.29f, 0.13f));
            AssetDatabase.CreateAsset(material, "Assets/Resource/Prefabs/KeyboardStorageBox.mat");
        }
        GameObject root = existingPrefab != null ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject("KeyboardStorageBox");
        for (int index = root.transform.childCount - 1; index >= 0; index--)
            Object.DestroyImmediate(root.transform.GetChild(index).gameObject);
        Rigidbody body = root.GetComponent<Rigidbody>();
        if (body == null) body = root.AddComponent<Rigidbody>();
        body.mass = 3f;
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        KeyboardStorageBox box = root.GetComponent<KeyboardStorageBox>();
        if (box == null) box = root.AddComponent<KeyboardStorageBox>();
        CreateSurface("BoxBody", root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(3.2f, 1.1f, 1.5f), material);
        GameObject top = new GameObject("TopInteractionArea");
        top.transform.SetParent(root.transform, false);
        BoxCollider topCollider = top.AddComponent<BoxCollider>();
        topCollider.isTrigger = true;
        topCollider.center = new Vector3(0f, 1.12f, 0f);
        topCollider.size = new Vector3(3f, 0.06f, 1.3f);
        SerializedObject settings = new SerializedObject(box);
        settings.FindProperty("_topInteractionArea").objectReferenceValue = topCollider;
        settings.FindProperty("_capacity").intValue = 5;
        settings.ApplyModifiedPropertiesWithoutUndo();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        if (existingPrefab != null) PrefabUtility.UnloadPrefabContents(root);
        else Object.DestroyImmediate(root);
        if (Object.FindAnyObjectByType<KeyboardStorageBox>() == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(-3.7f, -2.48f, 1.5f);
            EditorSceneManager.SaveScene(instance.scene);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("STORAGE_BOX_SETUP_COMPLETE closedBox=true gravity=false capacity=5 scene=Main 1");
    }

    /// <summary>
    /// name, parent, position, size와 material로 상자 표면을 생성한다.
    /// 메시와 물리 충돌체를 갖는 자식 오브젝트를 배치한다.
    /// </summary>
    private static void CreateSurface(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = name;
        surface.transform.SetParent(parent, false);
        surface.transform.localPosition = position;
        surface.transform.localScale = size;
        surface.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
