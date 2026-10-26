using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class NeonKeycapBuilder
    {
        private const string ROOT = "Assets/Resource";
        private const string CAPS = ROOT + "/Prefabs/Keycaps";
        private const string OUTPUT = CAPS + "/Neon";
        private const string MATERIALS = ROOT + "/Materials/Neon";
        private const string MESHES = ROOT + "/Meshes/Neon";
        private const string PREVIEW = ".local/NeonKeycapPreview";
        private const float BAND_HEIGHT = 0.34f;

        /// <summary>
        /// 기존 크기별 키캡을 검은 윗면과 위치 기반 공용 네온 띠를 가진 버리언트로 만든다.
        /// 원본 파괴 및 입력 컴포넌트를 상속하며 키캡 프리팹만 저장하고 적용 키보드는 Keyboard 씬에 배치한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Neon Keycaps")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬을 보호하기 위해 별도 복사 프로젝트의 배치 모드에서 실행합니다.");
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory(OUTPUT);
            Directory.CreateDirectory(MATERIALS);
            Directory.CreateDirectory(MESHES);
            Directory.CreateDirectory(PREVIEW);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material top = CreateMaterial("NeonGraphite", new Color(0.025f, 0.03f, 0.038f), false);
            Material band = CreateMaterial("NeonBand_Gradient", Color.white, true);
            string[] sizes = { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u", "1u_Vertical2u" };
            foreach (string size in sizes)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(CAPS + "/Keycap_" + size + ".prefab");
                Mesh mesh = CreateBandMesh(source.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>().sharedMesh, size);
                string path = OUTPUT + "/Keycap_" + size + "_Neon.prefab";
                string previousPath = OUTPUT + "/Keycap_" + size + "_Neon_Pink.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null && AssetDatabase.LoadAssetAtPath<GameObject>(previousPath) != null)
                    AssetDatabase.MoveAsset(previousPath, path);
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    cap.name = "Keycap_" + size + "_Neon";
                    ApplyShell(cap.transform, mesh, top, band);
                    PrefabUtility.SaveAsPrefabAsset(cap, path);
                }
                finally { UnityEngine.Object.DestroyImmediate(cap); }
            }
            AssetDatabase.SaveAssets();
            GameObject keyboard = BuildDisplay(top, band);
            RenderPreview(keyboard);
            AddToKeyboardScene(keyboard);
            RemoveColorVariants(sizes);
            File.WriteAllText(PREVIEW + "/BuildReport.txt", "Keycap variants: 9\nSizes: 9\nBand materials: 1\nGradient: keyboard local X\nInput bindings: 87\nHealth components: 87\nKeyboard prefab assets created: 0\n");
            Debug.Log("NEON_KEYCAPS_COMPLETE variants=9 bandMaterials=1 keyboardPrefabs=0");
        }

        /// <summary>
        /// name과 color를 사용해 검은 상판 또는 발광 하부 띠 재질을 생성한다.
        /// emission 여부에 따라 PBT 기반 재질의 색상과 발광을 설정하고 저장된 Material을 반환한다.
        /// </summary>
        private static Material CreateMaterial(string name, Color color, bool emission)
        {
            string path = MATERIALS + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/Porcelain_PBT.mat"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = name;
            if (emission) material.shader = Shader.Find("Keyboard/NeonBand");
            if (emission)
            {
                material.SetFloat("_GlowStrength", 1.3f);
                material.SetColor("_PinkColor", new Color(1f, 0.045f, 0.58f));
                material.SetColor("_PurpleColor", new Color(0.47f, 0.08f, 1f));
                material.SetColor("_CyanColor", new Color(0.035f, 0.72f, 1f));
                material.SetColor("_MintColor", new Color(0.04f, 1f, 0.68f));
                material.SetFloat("_GradientStart", 0f);
                material.SetFloat("_GradientEnd", 18.5f);
            }
            else
            {
                material.SetColor("_BaseColor", color);
                material.SetColor("_Color", color);
                material.SetTexture("_BaseMap", null);
                material.SetFloat("_Smoothness", 0.25f);
                material.SetFloat("_BumpScale", 0.15f);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// source 외피의 삼각형을 BAND_HEIGHT에서 잘라 검은 상판과 네온 하부 서브메시로 나눈다.
        /// size별 기존 외형과 법선을 보존하며 두 재질을 적용할 Mesh 에셋을 저장해 반환한다.
        /// </summary>
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
            Mesh mesh = new Mesh { name = "Keycap_" + size + "_NeonPudding", subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(band, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string path = MESHES + "/" + mesh.name + ".asset";
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

        /// <summary>
        /// triangle을 upper가 지정하는 띠 경계의 위쪽 또는 아래쪽으로 잘라 destination에 삼각형을 추가한다.
        /// 공유 교차점 캐시와 정점 및 법선 및 UV 목록을 갱신하여 경계가 정확히 맞도록 한다.
        /// </summary>
        private static void ClipTriangle(int[] triangle, bool upper, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, Dictionary<long, int> intersections, List<int> destination)
        {
            List<int> polygon = new List<int>(4);
            for (int index = 0; index < 3; index++)
            {
                int start = triangle[index];
                int end = triangle[(index + 1) % 3];
                bool startInside = upper ? vertices[start].y >= BAND_HEIGHT : vertices[start].y <= BAND_HEIGHT;
                bool endInside = upper ? vertices[end].y >= BAND_HEIGHT : vertices[end].y <= BAND_HEIGHT;
                if (startInside) polygon.Add(start);
                if (startInside == endInside) continue;
                long edge = ((long)Mathf.Min(start, end) << 32) | (uint)Mathf.Max(start, end);
                if (!intersections.TryGetValue(edge, out int intersection))
                {
                    float amount = (BAND_HEIGHT - vertices[start].y) / (vertices[end].y - vertices[start].y);
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
        /// key의 외피에 mesh와 top 및 band 두 재질을 적용한다.
        /// 기존 키캡의 MeshFilter와 MeshRenderer만 갱신하고 프리팹 오버라이드를 기록한다.
        /// </summary>
        private static void ApplyShell(Transform key, Mesh mesh, Material top, Material band)
        {
            Transform shell = key.Find("PBT_SculptedShell");
            MeshFilter filter = shell.GetComponent<MeshFilter>();
            MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { top, band };
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        /// <summary>
        /// 기본 키보드에 top과 공용 band를 적용하여 네온 전시 인스턴스를 만든다.
        /// 위치 기반 배색과 87개 입력 연결을 보존하고 키보드 프리팹을 저장하지 않는다.
        /// </summary>
        private static GameObject BuildDisplay(Material top, Material band)
        {
            GameObject keyboard = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Prefabs/Keyboard_TKL_87.prefab"));
            PrefabUtility.UnpackPrefabInstance(keyboard, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            keyboard.name = "Keyboard_TKL_87_Neon_Display";
            UnityEngine.Object.DestroyImmediate(keyboard.GetComponent<KeyboardPlayableVariant>());
            foreach (KeycapButton button in keyboard.GetComponentsInChildren<KeycapButton>(true))
            {
                Transform key = button.transform;
                string size = key.Find("PBT_SculptedShell").GetComponent<MeshFilter>().sharedMesh.name.Substring("Keycap_".Length);
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESHES + "/Keycap_" + size + "_NeonPudding.asset");
                ApplyShell(key, mesh, top, band);
                foreach (TextMesh label in key.GetComponentsInChildren<TextMesh>(true))
                {
                    label.color = new Color(0.65f, 0.85f, 0.9f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
            }
            foreach (MeshRenderer renderer in keyboard.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "CNC_LowerCase" || renderer.name == "Brass_SwitchPlate" || renderer.name.StartsWith("TopBezel_", StringComparison.Ordinal))
                {
                    renderer.sharedMaterial = top;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            SerializedProperty bindings = new SerializedObject(keyboard.GetComponent<KeyboardInputController>()).FindProperty("_bindings");
            if (bindings.arraySize != 87 || keyboard.GetComponentsInChildren<KeycapHealth>(true).Length != 87)
                throw new InvalidOperationException("네온 키보드의 입력 또는 체력 개수가 87개가 아닙니다.");
            for (int index = 0; index < bindings.arraySize; index++)
                if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                    throw new InvalidOperationException("네온 키보드 입력 대상 누락: " + index);
            ConfigureGradient(keyboard, band);
            return keyboard;
        }

        /// <summary>
        /// keyboard의 네온 띠를 공용 band 재질로 연결하고 키보드 좌표 컴포넌트를 설정한다.
        /// 하부 재질 슬롯과 속성 블록을 갱신하며 기존 키캡 위치와 입력 상태는 유지한다.
        /// </summary>
        private static void ConfigureGradient(GameObject keyboard, Material band)
        {
            foreach (KeycapButton button in keyboard.GetComponentsInChildren<KeycapButton>(true))
            {
                MeshRenderer renderer = button.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                Material[] materials = renderer.sharedMaterials;
                materials[1] = band;
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            NeonKeyboardGradient gradient = keyboard.GetComponent<NeonKeyboardGradient>();
            if (gradient == null) gradient = keyboard.AddComponent<NeonKeyboardGradient>();
            SerializedObject settings = new SerializedObject(gradient);
            settings.FindProperty("_bandMaterial").objectReferenceValue = band;
            settings.ApplyModifiedPropertiesWithoutUndo();
            gradient.RefreshRenderers();
        }

        /// <summary>
        /// sizes의 공용 키캡 생성 이후 기존 색상별 버리언트와 띠 재질을 정리한다.
        /// 네온 전용 경로의 Pink 및 Purple 및 Cyan 및 Mint 에셋만 제거하여 중복 수를 줄인다.
        /// </summary>
        private static void RemoveColorVariants(string[] sizes)
        {
            foreach (string theme in new[] { "Pink", "Purple", "Cyan", "Mint" })
            {
                foreach (string size in sizes)
                    AssetDatabase.DeleteAsset(OUTPUT + "/Keycap_" + size + "_Neon_" + theme + ".prefab");
                AssetDatabase.DeleteAsset(MATERIALS + "/NeonBand_" + theme + ".mat");
            }
        }

        /// <summary>
        /// keyboard를 기존 Keyboard 씬의 새 비교 전시 줄로 이동한다.
        /// 기존 네온 전시는 공용 재질만 갱신하며 최초 배치에는 기본 키보드의 위치 및 크기를 사용한다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject keyboard)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Additive);
            GameObject original = null;
            GameObject existing = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Keyboard_TKL_87") original = root;
                if (root.name == "Keyboard_TKL_87_Neon_Display") existing = root;
            }
            if (existing != null)
            {
                ConfigureGradient(existing, AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "/NeonBand_Gradient.mat"));
                UnityEngine.Object.DestroyImmediate(keyboard);
                EditorSceneManager.SaveScene(scene);
                return;
            }
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyboard, scene);
            keyboard.transform.localScale = original.transform.lossyScale;
            keyboard.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, 45f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// keyboard와 생성된 키캡 샘플을 임시 전시 공간에서 렌더링한다.
        /// 발광 재질과 카메라로 PNG를 저장하고 렌더 리소스를 해제한다.
        /// </summary>
        private static void RenderPreview(GameObject keyboard)
        {
            GameObject model = UnityEngine.Object.Instantiate(keyboard);
            model.transform.localScale = Vector3.one;
            model.GetComponent<NeonKeyboardGradient>().RefreshRenderers();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string characters = "";
            foreach (TextMesh label in model.GetComponentsInChildren<TextMesh>()) characters += label.text;
            font.RequestCharactersInTexture(characters, 96);
            Material ink = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/KeyboardBackgroundLegend.mat"));
            ink.mainTexture = font.material.mainTexture;
            foreach (TextMesh label in model.GetComponentsInChildren<TextMesh>()) label.GetComponent<MeshRenderer>().sharedMaterial = ink;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.35f, 0.4f);
            Light light = new GameObject("PreviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Camera camera = new GameObject("NeonPreviewCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(24f, 24f, -25f);
            camera.transform.LookAt(new Vector3(9f, 0.4f, 3f));
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.allowHDR = false;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.017f, 0.025f);
            RenderTexture target = new RenderTexture(1600, 1000, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW + "/NeonKeyboard.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(ink);
            UnityEngine.Object.DestroyImmediate(model);
        }
    }
}
