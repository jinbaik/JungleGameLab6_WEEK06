using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class KeyboardProtectionCoverBuilder
    {
        private const string RESOURCE_ROOT = "Assets/Resource/";
        private const string BACKGROUND_PATH = RESOURCE_ROOT + "Prefabs/Keyboard_TKL_87_Background.prefab";
        private const string COVER_PATH = RESOURCE_ROOT + "Prefabs/KeyboardProtectionCover.prefab";
        private const string MATERIAL_PATH = RESOURCE_ROOT + "Materials/KeyboardProtectionAcrylic.mat";
        private const string MESH_PATH = RESOURCE_ROOT + "Meshes/KeyboardProtectionCover.asset";
        private const string PREVIEW_ROOT = ".local/KeyboardProtectionCover";
        private const int CORNER_SEGMENTS = 8;

        /// <summary>
        /// 기존 키캡 경계로 아크릴 커버를 생성해 배경용 키보드에 연결한다.
        /// 원본 메시와 재질 참조를 보존하고 커버 에셋, 모델 집계와 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Acrylic Protection Cover")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Build the cover in Edit Mode.");

            Directory.CreateDirectory(PREVIEW_ROOT);
            GameObject background = PrefabUtility.LoadPrefabContents(BACKGROUND_PATH);
            try
            {
                string before = DescribeModel(background);
                GameObject game = AssetDatabase.LoadAssetAtPath<GameObject>(RESOURCE_ROOT + "Prefabs/Keyboard_TKL_87.prefab");
                Bounds keycaps = CalculateKeycapBounds(game);
                Material material = BuildMaterial();
                Mesh mesh = SaveMesh(BuildMesh(keycaps));
                GameObject cover = new GameObject("ProtectionCover");
                cover.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = cover.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                cover.AddComponent<KeyboardProtectionCover>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(cover, COVER_PATH);
                UnityEngine.Object.DestroyImmediate(cover);

                Transform previous = background.transform.Find("ProtectionCover");
                if (previous != null)
                    UnityEngine.Object.DestroyImmediate(previous.gameObject);
                GameObject nested = (GameObject)PrefabUtility.InstantiatePrefab(prefab, background.transform);
                nested.name = "ProtectionCover";
                KeyboardInputController backgroundInput = background.GetComponent<KeyboardInputController>();
                if (backgroundInput != null)
                    UnityEngine.Object.DestroyImmediate(backgroundInput);
                BoxCollider pickup = background.GetComponent<BoxCollider>();
                if (pickup != null)
                {
                    Bounds bounds = CalculateModelBounds(background);
                    pickup.center = bounds.center;
                    pickup.size = bounds.size;
                }
                PrefabUtility.SaveAsPrefabAsset(background, BACKGROUND_PATH);
                AssetDatabase.SaveAssets();

                string report = "Background before\n" + before + "\nBackground after\n" + DescribeModel(background)
                    + "\nCover only\n" + DescribeModel(nested)
                    + "\nOpacity: 0.8\nMovement: world Y +0.4, then world X -4\nDurations: 0.3s / 0.7s\n"
                    + "Rarity: Common=original, Rare=Wood, Epic=Gold, Legendary=Celestial\n"
                    + "Shared original meshes/materials unchanged. Cover has no collider.\n";
                File.WriteAllText(PREVIEW_ROOT + "/ModelReport.txt", report);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderPreview(AssetDatabase.LoadAssetAtPath<GameObject>(BACKGROUND_PATH), "CoveredKeyboard.png", false);
                RenderPreview(AssetDatabase.LoadAssetAtPath<GameObject>(BACKGROUND_PATH), "OpenKeyboard.png", true);
                Debug.Log("KEYBOARD_PROTECTION_COVER_COMPLETE\n" + report);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(background);
            }
        }

        /// <summary>
        /// keyboard의 키캡 외부 메시만 합쳐 루트 로컬 좌표의 Bounds를 반환한다.
        /// 바디와 내부 구조 및 각인 메시를 제외하여 보호 커버의 실루엣 범위를 정한다.
        /// </summary>
        private static Bounds CalculateKeycapBounds(GameObject keyboard)
        {
            Bounds result = new Bounds();
            bool first = true;
            foreach (KeycapHealth health in keyboard.GetComponentsInChildren<KeycapHealth>(true))
            {
                MeshFilter shell = health.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>();
                EncapsulateMesh(ref result, ref first, shell, keyboard.transform);
            }
            if (first)
                throw new InvalidOperationException("Keyboard requires keycap shells before building a cover.");
            return result;
        }

        /// <summary>
        /// model의 메시 경계를 루트 로컬 좌표로 합쳐 반환한다.
        /// 가시 모델과 커버를 감싸는 단순 들기용 Collider의 중심과 크기를 계산한다.
        /// </summary>
        private static Bounds CalculateModelBounds(GameObject model)
        {
            Bounds result = new Bounds();
            bool first = true;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                EncapsulateMesh(ref result, ref first, filter, model.transform);
            return result;
        }

        /// <summary>
        /// filter 메시의 경계 모서리를 root 좌표계로 변환해 bounds에 합친다.
        /// 첫 메시를 만나면 first를 해제하고 이후 메시의 범위를 누적한다.
        /// </summary>
        private static void EncapsulateMesh(ref Bounds bounds, ref bool first, MeshFilter filter, Transform root)
        {
            Bounds mesh = filter.sharedMesh.bounds;
            Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }

        /// <summary>
        /// 기존 창 유리의 URP 셰이더 설정을 복사해 불투명도 80%의 전용 아크릴 재질을 만든다.
        /// 기존 공유 재질은 유지하며 반투명 렌더 큐, 금속도와 매끄러움을 설정한 재질을 반환한다.
        /// </summary>
        private static Material BuildMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            if (material == null)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>(RESOURCE_ROOT + "Materials/RoomWindowGlass.mat");
                material = new Material(source) { name = "KeyboardProtectionAcrylic" };
                AssetDatabase.CreateAsset(material, MATERIAL_PATH);
            }
            Color tint = new Color(0.38f, 0.44f, 0.48f, 0.8f);
            material.SetColor("_BaseColor", tint);
            material.SetColor("_Color", tint);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.72f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// keys 경계와 여유 공간으로 둥근 모서리, 얇은 벽 및 닫힌 윗판을 가진 바닥 없는 커버를 만든다.
        /// 다섯 단면과 두 중심점을 연결하여 재사용할 단일 Mesh를 반환한다.
        /// </summary>
        private static Mesh BuildMesh(Bounds keys)
        {
            float halfWidth = keys.extents.x + 0.16f;
            float halfDepth = keys.extents.z + 0.16f;
            float bottom = keys.min.y - 0.04f;
            float top = keys.max.y + 0.2f;
            float wall = 0.055f;
            var points = new List<Vector3>();
            var triangles = new List<int>();
            AddRing(points, keys.center, halfWidth, halfDepth, 0.18f, bottom);
            AddRing(points, keys.center, halfWidth, halfDepth, 0.18f, top - 0.07f);
            AddRing(points, keys.center, halfWidth - 0.07f, halfDepth - 0.07f, 0.11f, top);
            AddRing(points, keys.center, halfWidth - wall, halfDepth - wall, 0.125f, bottom);
            AddRing(points, keys.center, halfWidth - wall, halfDepth - wall, 0.125f, top - wall);
            int count = CORNER_SEGMENTS * 4;
            ConnectRings(triangles, 0, count, count, true);
            ConnectRings(triangles, count, count * 2, count, true);
            ConnectRings(triangles, count * 3, count * 4, count, false);
            ConnectRings(triangles, 0, count * 3, count, false);
            int roof = points.Count;
            points.Add(new Vector3(keys.center.x, top, keys.center.z));
            int ceiling = points.Count;
            points.Add(new Vector3(keys.center.x, top - wall, keys.center.z));
            for (int index = 0; index < count; index++)
            {
                int next = (index + 1) % count;
                triangles.AddRange(new[] { roof, count * 2 + next, count * 2 + index });
                triangles.AddRange(new[] { ceiling, count * 4 + index, count * 4 + next });
            }
            Mesh mesh = new Mesh { name = "KeyboardProtectionCover" };
            mesh.SetVertices(points);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// center, 반폭, 반깊이, radius와 y를 사용해 둥근 사각 단면을 points에 추가한다.
        /// 네 모서리에 동일한 분할을 배분하여 커버의 수직 벽과 베벨에 사용할 정점을 저장한다.
        /// </summary>
        private static void AddRing(List<Vector3> points, Vector3 center, float halfWidth, float halfDepth, float radius, float y)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner == 0 || corner == 3 ? 1f : -1f) * (halfWidth - radius);
                float z = (corner < 2 ? 1f : -1f) * (halfDepth - radius);
                for (int segment = 0; segment < CORNER_SEGMENTS; segment++)
                {
                    float angle = (corner * 90f + segment * 90f / (CORNER_SEGMENTS - 1)) * Mathf.Deg2Rad;
                    points.Add(new Vector3(center.x + x + Mathf.Cos(angle) * radius, y, center.z + z + Mathf.Sin(angle) * radius));
                }
            }
        }

        /// <summary>
        /// a와 b 단면의 count개 정점을 연결하여 triangles에 벽 또는 하단 테두리를 추가한다.
        /// reverse에 따라 안쪽 또는 바깥쪽 면의 삼각형 방향을 설정한다.
        /// </summary>
        private static void ConnectRings(List<int> triangles, int a, int b, int count, bool reverse)
        {
            for (int index = 0; index < count; index++)
            {
                int next = (index + 1) % count;
                if (reverse)
                    triangles.AddRange(new[] { a + index, b + index, a + next, a + next, b + index, b + next });
                else
                    triangles.AddRange(new[] { a + index, a + next, b + index, a + next, b + next, b + index });
            }
        }

        /// <summary>
        /// mesh를 전용 경로에 저장하고 기존 에셋이 있으면 GUID를 유지하며 갱신한다.
        /// 저장된 Mesh 참조를 반환한다.
        /// </summary>
        private static Mesh SaveMesh(Mesh mesh)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, MESH_PATH); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }        

        /// <summary>
        /// model 한 개의 메시 정점, 삼각형, 활성 Renderer와 그림자 투사 수를 집계한다.
        /// 실제 에셋 수치를 문자열로 반환하며 드로우콜이나 FPS를 추정 수치로 기록하지 않는다.
        /// </summary>
        private static string DescribeModel(GameObject model)
        {
            int vertices = 0;
            int triangles = 0;
            int renderers = 0;
            int shadows = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                vertices += filter.sharedMesh.vertexCount;
                for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                    triangles += (int)filter.sharedMesh.GetIndexCount(submesh) / 3;
            }
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeSelf)
                    continue;
                renderers++;
                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                    shadows++;
            }
            return $"Vertices: {vertices}\nTriangles: {triangles}\nActive renderers: {renderers}\nShadow-casting renderers: {shadows}\n";
        }

        /// <summary>
        /// prefab을 빈 씬에 배치하여 covered 또는 open 상태의 개인 미리보기를 저장한다.
        /// filename과 removeCover를 사용하며 기존 씬, 조명 및 프로젝트 렌더 설정은 변경하지 않는다.
        /// </summary>
        private static void RenderPreview(GameObject prefab, string filename, bool removeCover)
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Rigidbody body = model.GetComponent<Rigidbody>();
            body.isKinematic = true;
            model.transform.localScale = Vector3.one * 0.15f;
            Transform cover = model.transform.Find("ProtectionCover");
            cover.gameObject.SetActive(!removeCover);
            GameObject lightObject = new GameObject("PreviewLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.5f;
            light.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.53f, 0.57f);
            Camera camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.position = model.transform.TransformPoint(new Vector3(22f, 17f, -17f));
            camera.transform.LookAt(model.transform.TransformPoint(new Vector3(9.25f, 0.9f, 2.8f)));
            camera.orthographic = true;
            camera.orthographicSize = 1.85f;
            camera.backgroundColor = new Color(0.065f, 0.08f, 0.095f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            RenderTexture target = new RenderTexture(1280, 800, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW_ROOT + "/" + filename, image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(model);
        }
    }
}
