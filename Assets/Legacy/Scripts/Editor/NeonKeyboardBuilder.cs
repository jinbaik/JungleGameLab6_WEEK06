using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class NeonKeyboardBuilder
    {
        private const string ROOT = "Assets/Resource/";
        private const string GAME_PATH = ROOT + "Prefabs/Keyboard_TKL_87_Neon_Game.prefab";
        private const string BACKGROUND_PATH = ROOT + "Prefabs/Keyboard_TKL_87_Neon_Background.prefab";


        /// <summary>
        /// 87키 네온 게임용과 배경용 프리팹을 만들고 키보드 씬에 추가한다.
        /// 기존 네온 키캡 및 경량 메시를 재사용하고 게임용 각인 깊이 검사 설정과 집계 및 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Neon Keyboard Pair")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("별도 복사 프로젝트의 숨김 배치에서 실행합니다.");
            Directory.CreateDirectory(".local/NeonKeyboardPreview");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.WriteAllText(".local/NeonKeyboardPreview/KeycapsReport.txt", "");
            CreateNeonKeycaps();
            GameObject game = BuildGame();
            GameObject background = BuildBackground();
            AddToKeyboardScene(game, background);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderPreview(game, background);
            AssetDatabase.SaveAssets();
            Debug.Log("NEON_KEYBOARD_PAIR_COMPLETE");
        }

        /// <summary>
        /// 기존 8종 네온 키캡 버리언트의 존재와 메시 수치를 확인한다.
        /// 기존 프리팹과 공유 재질을 변경하지 않고 각인용 깊이 검사 재질만 저장한다.
        /// </summary>
        private static void CreateNeonKeycaps()
        {
            Material sourceInk = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeyboardBackgroundLegend.mat");
            Material ink = new Material(Shader.Find("Keyboard/LegendDepth")) { name = "KeyboardNeonLegend" };
            ink.mainTexture = sourceInk.mainTexture;
            ink.SetFloat("_UseUniformColor", 1f);
            ink.SetColor("_InkColor", new Color(0.65f, 0.85f, 0.9f));
            SaveMaterial(ink, ROOT + "Materials/KeyboardNeonLegend.mat");
            foreach (string size in new[] { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u" })
            {
                GameObject cap = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keycaps/Neon/Keycap_" + size + "_Neon.prefab");
                if (cap == null) throw new InvalidOperationException("기존 네온 키캡 없음: " + size);
                GameObject sample = (GameObject)PrefabUtility.InstantiatePrefab(cap);
                File.AppendAllText(".local/NeonKeyboardPreview/KeycapsReport.txt", size + "\n" + Report(sample) + "\n\n");
                UnityEngine.Object.DestroyImmediate(sample);
            }
        }
        /// <summary>
        /// 기존 게임용 모델의 키캡을 네온 크기별 버리언트로 연결한다.
        /// 키 계층과 사용자 설정을 보존하며 본체 외부 재질 및 각인 색을 변경하고 새 게임용 프리팹을 반환한다.
        /// </summary>
        private static GameObject BuildGame()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keyboard_TKL_87.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Material neon = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Neon/NeonGraphite.mat");
            var settings = new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                gameObjectsNotMatchedBecomesOverride = true,
                componentsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = true,
                changeRootNameToAssetName = false
            };
            int count = 0;
            foreach (KeycapButton button in instance.GetComponentsInChildren<KeycapButton>(true))
            {
                GameObject key = button.gameObject;
                MeshRenderer shell = key.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                string size = shell.GetComponent<MeshFilter>().sharedMesh.name.Substring("Keycap_".Length);
                GameObject cap = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keycaps/Neon/Keycap_" + size + "_Neon.prefab");
                if (cap == null) throw new InvalidOperationException("네온 키캡 없음: " + size);
                string keyName = key.name;
                Vector3 position = key.transform.localPosition;
                Quaternion rotation = key.transform.localRotation;
                Vector3 scale = key.transform.localScale;
                if (PrefabUtility.IsPartOfPrefabInstance(key))
                    PrefabUtility.UnpackPrefabInstance(key, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                PrefabUtility.ConvertToPrefabInstance(key, cap, settings, InteractionMode.AutomatedAction);
                key.name = keyName;
                key.transform.SetLocalPositionAndRotation(position, rotation);
                key.transform.localScale = scale;
                shell = key.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                shell.GetComponent<MeshFilter>().sharedMesh = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>().sharedMesh;
                shell.sharedMaterials = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>().sharedMaterials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(shell.GetComponent<MeshFilter>());
                PrefabUtility.RecordPrefabInstancePropertyModifications(shell);
                foreach (TextMesh label in key.GetComponentsInChildren<TextMesh>(true))
                    label.color = new Color(0.65f, 0.85f, 0.9f);
                count++;
            }
            if (count != 87) throw new InvalidOperationException("키캡 개수 오류: " + count);
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "CNC_LowerCase" || renderer.name == "Brass_SwitchPlate" || renderer.name.StartsWith("TopBezel_", StringComparison.Ordinal))
                    renderer.sharedMaterial = neon;
            var legendRenderer = new SerializedObject(instance.GetComponent<KeyboardLegendRenderer>());
            legendRenderer.FindProperty("_materialTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeyboardNeonLegend.mat");
            legendRenderer.ApplyModifiedPropertiesWithoutUndo();
            var controller = new SerializedObject(instance.GetComponent<KeyboardInputController>());
            SerializedProperty bindings = controller.FindProperty("_bindings");
            if (bindings.arraySize != 87) throw new InvalidOperationException("바인딩 개수 오류");
            for (int index = 0; index < bindings.arraySize; index++)
                if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                    throw new InvalidOperationException("누락된 바인딩: " + index);
            instance.AddComponent<NeonKeyboardGradient>().Configure(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Neon/NeonBand_Gradient.mat"));
            instance.name = "Keyboard_TKL_87_Neon_Game";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, GAME_PATH);
            File.WriteAllText(".local/NeonKeyboardPreview/GameReport.txt", Report(instance) + "\nNeon variant keycaps: " + count);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>
        /// 기존 배경용의 경량 메시를 유지한 네온 재질 버리언트를 생성한다.
        /// 공유 메시와 네온 재질을 사용하며 입력 및 물리 컴포넌트를 제거하고 배경용 프리팹을 반환한다.
        /// </summary>
        private static GameObject BuildBackground()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keyboard_TKL_87_Background.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            Material neon = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Neon/NeonGraphite.mat");
            Material oldInk = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeyboardBackgroundLegend.mat");
            Material ink = new Material(Shader.Find("Keyboard/LegendDepth")) { name = "KeyboardNeonLegend" };
            ink.mainTexture = oldInk.mainTexture;
            ink.SetFloat("_UseUniformColor", 1f);
            ink.SetColor("_InkColor", new Color(0.65f, 0.85f, 0.9f));
            ink = SaveMaterial(ink, ROOT + "Materials/KeyboardNeonLegend.mat");
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                string material = renderer.sharedMaterial.name;
                if (material.EndsWith("_PBT", StringComparison.Ordinal) || material == "AnodizedAluminum" || material == "BrushedBrass")
                    renderer.sharedMaterial = neon;
                else if (material == "KeyboardBackgroundLegend")
                    renderer.sharedMaterial = ink;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
            ReplaceBackgroundCaps(instance);
            instance.name = "Keyboard_TKL_87_Neon_Background";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, BACKGROUND_PATH);
            File.WriteAllText(".local/NeonKeyboardPreview/BackgroundReport.txt", Report(instance));
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>
        /// instance의 기존 경량 키캡을 네온 상판과 하부 띠 메시로 교체한다.
        /// 기존 키 위치와 크기별 외형을 사용해 재질별 두 메시로 합치고 배경용 입력과 물리는 추가하지 않는다.
        /// </summary>
        private static void ReplaceBackgroundCaps(GameObject instance)
        {
            foreach (Transform child in instance.transform.Cast<Transform>().ToArray())
                if (child.name.EndsWith("_PBT", StringComparison.Ordinal) || child.name == "ProtectionCover")
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keyboard_TKL_87.prefab");
            var meshes = new Dictionary<Mesh, Mesh>();
            var topParts = new List<CombineInstance>();
            var bandParts = new List<CombineInstance>();
            foreach (KeycapButton button in source.GetComponentsInChildren<KeycapButton>(true))
            {
                MeshFilter shell = button.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>();
                if (!meshes.TryGetValue(shell.sharedMesh, out Mesh mesh))
                {
                    Mesh low = ReduceLoft(shell.sharedMesh);
                    mesh = CreateBandMesh(low, shell.sharedMesh.name.Substring("Keycap_".Length));
                    UnityEngine.Object.DestroyImmediate(low);
                    meshes.Add(shell.sharedMesh, mesh);
                }
                Matrix4x4 matrix = source.transform.worldToLocalMatrix * shell.transform.localToWorldMatrix;
                topParts.Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = matrix });
                bandParts.Add(new CombineInstance { mesh = mesh, subMeshIndex = 1, transform = matrix });
            }
            CreateMergedPart(instance, topParts, "NeonTop", AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Neon/NeonGraphite.mat"), ShadowCastingMode.On);
            Material band = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Neon/NeonBand_Gradient.mat")) { name = "NeonBackgroundBand" };
            band.SetFloat("_UseKeyboardSpace", 0f);
            band.shader = Shader.Find("Keyboard/NeonBackgroundBand");
            band.SetFloat("_GradientStart", 0f);
            band.SetFloat("_GradientEnd", 18.5f);
            band = SaveMaterial(band, ROOT + "Materials/Neon/NeonBackgroundBand.mat");
            CreateMergedPart(instance, bandParts, "NeonBand", band, ShadowCastingMode.Off);
        }

        /// <summary>
        /// parts를 하나의 name 메시로 합쳐 instance의 자식으로 저장한다.
        /// material과 shadows를 적용하며 배치용 경량 Mesh 에셋과 렌더러를 생성한다.
        /// </summary>
        private static void CreateMergedPart(GameObject instance, List<CombineInstance> parts, string name, Material material, ShadowCastingMode shadows)
        {
            Mesh mesh = new Mesh { name = "KeyboardBackground_" + name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            int[] indices = mesh.triangles;
            Vector3[] sourceVertices = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector2[] sourceUv = mesh.uv;
            var mapping = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            for (int index = 0; index < indices.Length; index++)
            {
                int original = indices[index];
                if (!mapping.TryGetValue(original, out int compact))
                {
                    compact = vertices.Count;
                    mapping.Add(original, compact);
                    vertices.Add(sourceVertices[original]);
                    normals.Add(sourceNormals[original]);
                    uv.Add(sourceUv[original]);
                }
                indices[index] = compact;
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            string path = ROOT + "Meshes/Neon/" + mesh.name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            GameObject part = new GameObject(name);
            part.transform.SetParent(instance.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = shadows != ShadowCastingMode.Off;
        }
        /// <summary>source에서 주요 로프트 단면을 추출하여 내부가 없는 경량 Mesh를 반환한다.</summary>
private static Mesh ReduceLoft(Mesh source)
        {
            bool keycap = source.name.StartsWith("Keycap_");
            int[] layers = keycap ? new[] { 0, 1, 2, 3, 4, 8, 13, 18 } : new[] { 0, 1, 2, 3 };
            if (!keycap && source.vertexCount != 402)
                return source;
            int ringCount = 100;
            int sampledCount = 20;
            Vector3[] original = source.vertices;
            Vector2[] originalUv = source.uv;
            var points = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            foreach (int layer in layers)
                for (int index = 0; index < ringCount; index += 5)
                {
                    points.Add(original[layer * ringCount + index]);
                    uv.Add(originalUv[layer * ringCount + index]);
                }
            for (int layer = 0; layer < layers.Length - 1; layer++)
                for (int index = 0; index < sampledCount; index++)
                {
                    int a = layer * sampledCount + index;
                    int b = layer * sampledCount + (index + 1) % sampledCount;
                    triangles.AddRange(new[] { a, a + sampledCount, b, b, a + sampledCount, b + sampledCount });
                }
            int center = points.Count;
            points.Add(original[original.Length - 1]);
            uv.Add(originalUv[originalUv.Length - 1]);
            int top = (layers.Length - 1) * sampledCount;
            for (int index = 0; index < sampledCount; index++)
                triangles.AddRange(new[] { center, top + (index + 1) % sampledCount, top + index });
            if (!keycap)
            {
                int bottom = points.Count;
                points.Add(original[original.Length - 2]);
                uv.Add(originalUv[originalUv.Length - 2]);
                for (int index = 0; index < sampledCount; index++)
                    triangles.AddRange(new[] { bottom, index, (index + 1) % sampledCount });
            }
            Mesh mesh = new Mesh { name = source.name + "_Background" };
            mesh.SetVertices(points);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
        /// <summary>source를 높이 0.34에서 분할하여 size별 두 재질 경량 Mesh를 저장하고 반환한다.</summary>
private static Mesh CreateBandMesh(Mesh source, string size)
        {
            List<Vector3> vertices = new List<Vector3>(source.vertices);
            List<Vector3> normals = new List<Vector3>(source.normals);
            List<Vector2> uv = new List<Vector2>(source.uv);
            Dictionary<long, int> intersections = new Dictionary<long, int>();
            List<int> top = new List<int>();
            List<int> band = new List<int>();
            int[] triangles = source.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int[] triangle = { triangles[index], triangles[index + 1], triangles[index + 2] };
                ClipTriangle(triangle, true, vertices, normals, uv, intersections, top);
                ClipTriangle(triangle, false, vertices, normals, uv, intersections, band);
            }
            Mesh mesh = new Mesh { name = "KeyboardBackgroundCap_" + size + "_Neon", subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(band, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string path = ROOT + "Meshes/Neon/" + mesh.name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            return mesh;
        }
        /// <summary>triangle을 upper 경계로 잘라 destination과 공유 정점 및 법선 및 UV 목록을 갱신한다.</summary>
private static void ClipTriangle(int[] triangle, bool upper, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, Dictionary<long, int> intersections, List<int> destination)
        {
            List<int> polygon = new List<int>(4);
            for (int index = 0; index < 3; index++)
            {
                int start = triangle[index];
                int end = triangle[(index + 1) % 3];
                bool startInside = upper ? vertices[start].y >= 0.34f : vertices[start].y <= 0.34f;
                bool endInside = upper ? vertices[end].y >= 0.34f : vertices[end].y <= 0.34f;
                if (startInside) polygon.Add(start);
                if (startInside == endInside) continue;
                long edge = ((long)Mathf.Min(start, end) << 32) | (uint)Mathf.Max(start, end);
                if (!intersections.TryGetValue(edge, out int intersection))
                {
                    float amount = (0.34f - vertices[start].y) / (vertices[end].y - vertices[start].y);
                    intersection = vertices.Count;
                    vertices.Add(Vector3.Lerp(vertices[start], vertices[end], amount));
                    normals.Add(Vector3.Lerp(normals[start], normals[end], amount).normalized);
                    uv.Add(Vector2.Lerp(uv[start], uv[end], amount));
                    intersections.Add(edge, intersection);
                }
                polygon.Add(intersection);
            }
            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                destination.Add(polygon[0]);
                destination.Add(polygon[index]);
                destination.Add(polygon[index + 1]);
            }
        }
        /// <summary>
        /// material을 path에 저장하고 기존 에셋이 있으면 GUID를 유지하여 갱신한다.
        /// 저장된 공유 재질을 반환한다.
        /// </summary>
        private static Material SaveMaterial(Material material, string path)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) { AssetDatabase.CreateAsset(material, path); return material; }
            EditorUtility.CopySerialized(material, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(material);
            return existing;
        }

        /// <summary>
        /// 키보드 씬의 기존 모델을 기준으로 게임용과 배경용 네온 모델을 추가한다.
        /// 두 프리팹을 기존 모델 뒤와 옆에 배치하고 다른 씬이나 기존 카메라 설정은 변경하지 않는다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject game, GameObject background)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Single);
            GameObject original = scene.GetRootGameObjects().First(root => root.GetComponent<KeyboardInputController>() != null);
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Keyboard_TKL_87_Neon_Game" || root.name == "Keyboard_TKL_87_Neon_Background" || root.name == "Neon_Keycap_1u_Sample" || root.name == "Neon_Keycap_Space_Sample")
                    UnityEngine.Object.DestroyImmediate(root);
            float row = scene.GetRootGameObjects()
                .Where(root => root.name.StartsWith("Keyboard_TKL_87", StringComparison.Ordinal))
                .Max(root => original.transform.InverseTransformPoint(root.transform.position).z) + 9f;
            GameObject gameInstance = (GameObject)PrefabUtility.InstantiatePrefab(game, scene);
            gameInstance.transform.localScale = original.transform.lossyScale;
            gameInstance.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, row)), original.transform.rotation);
            GameObject backgroundInstance = (GameObject)PrefabUtility.InstantiatePrefab(background, scene);
            backgroundInstance.transform.localScale = original.transform.lossyScale;
            backgroundInstance.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(22f, 0f, row)), original.transform.rotation);
            GameObject capSample = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keycaps/Neon/Keycap_1u_Neon.prefab"), scene);
            capSample.name = "Neon_Keycap_1u_Sample";
            capSample.transform.localScale = original.transform.lossyScale;
            capSample.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(3f, 0f, row - 2f)), original.transform.rotation);
            GameObject spaceSample = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keycaps/Neon/Keycap_6_25u_Neon.prefab"), scene);
            spaceSample.name = "Neon_Keycap_Space_Sample";
            spaceSample.transform.localScale = original.transform.lossyScale;
            spaceSample.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(8f, 0f, row - 2f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// model의 활성 렌더러와 메시 및 그림자 수치를 집계한다.
        /// 전체 배치 버텍스, 삼각형, 렌더러와 그림자 투사 수를 문자열로 반환한다.
        /// </summary>
        private static string Report(GameObject model)
        {
            int vertices = 0, triangles = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            return "Vertices: " + vertices + "\nTriangles: " + triangles + "\nActive renderers: " + renderers.Length +
                "\nShadow casters: " + renderers.Count(renderer => renderer.shadowCastingMode != ShadowCastingMode.Off) +
                "\nRuntime scripts: " + model.GetComponentsInChildren<MonoBehaviour>(true).Length +
                "\nColliders: " + model.GetComponentsInChildren<Collider>(true).Length;
        }

        /// <summary>
        /// 두 네온 프리팹을 임시 씬에서 렌더링하고 글씨 차폐 구도를 저장한다.
        /// game과 background의 실제 메시 및 각인 재질을 사용하여 개인 미리보기 이미지를 생성한다.
        /// </summary>
        private static void RenderPreview(GameObject game, GameObject background)
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(game);
            TextMesh[] labels = model.GetComponentsInChildren<TextMesh>(true);
            Font font = labels[0].font;
            font.RequestCharactersInTexture(string.Concat(labels.Select(label => label.text)), 96);
            Material previewInk = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeyboardNeonLegend.mat"));
            previewInk.mainTexture = font.material.mainTexture;
            foreach (TextMesh label in labels)
                label.GetComponent<MeshRenderer>().sharedMaterial = previewInk;
            model.transform.localScale = Vector3.one * 0.15f;
            model.GetComponent<NeonKeyboardGradient>().RefreshRenderers();
            GameObject backdrop = (GameObject)PrefabUtility.InstantiatePrefab(background);
            backdrop.transform.localScale = model.transform.localScale;
            backdrop.transform.position = new Vector3(0f, 0f, 1.7f);
            Camera camera = new GameObject("NeonPreview").AddComponent<Camera>();
            camera.transform.position = new Vector3(4f, 4.5f, -4.5f);
            camera.transform.LookAt(new Vector3(1.4f, 0.2f, 1.15f));
            camera.orthographic = true;
            camera.orthographicSize = 2.15f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.08f);
            RenderSettings.ambientLight = Color.gray;
            Light light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            SaveImage(camera, "NeonPair.png");
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraSize = camera.orthographicSize;
            Transform detail = model.GetComponentsInChildren<KeycapButton>().First(button => button.name == "Key_H").transform;
            camera.transform.position = detail.TransformPoint(new Vector3(2f, 2.7f, -3f));
            camera.transform.LookAt(detail.TransformPoint(new Vector3(0f, 0.3f, 0f)));
            camera.orthographicSize = detail.lossyScale.x * 1.4f;
            SaveImage(camera, "NeonDetail.png");
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            camera.orthographicSize = cameraSize;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0.6f, 0.65f, 0.3f);
            wall.transform.localScale = new Vector3(1.4f, 1.7f, 1.3f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Graphite.mat");
            SaveImage(camera, "NeonOcclusion.png");
        }

        /// <summary>
        /// camera 구도를 렌더 텍스처에 그려 name 이미지로 저장한다.
        /// 개인 미리보기 폴더에 PNG를 생성하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void SaveImage(Camera camera, string name)
        {
            RenderTexture target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(".local/NeonKeyboardPreview/" + name, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}









