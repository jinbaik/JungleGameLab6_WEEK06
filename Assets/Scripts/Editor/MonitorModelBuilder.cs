using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class MonitorModelBuilder
    {
        private const string PREFAB_PATH = "Assets/Resource/Prefabs/Monitor_16x9.prefab";
        private const string MESH_ROOT = "Assets/Resource/Meshes";
        private const string MATERIAL_ROOT = "Assets/Resource/Materials";
        private const float SCREEN_WIDTH = 0.528f;
        private const float SCREEN_HEIGHT = 0.297f;

        /// <summary>
        /// Keyboard 씬에 저폴리 16:9 모니터를 배치하고 프리팹과 미리보기를 저장한다.
        /// 기존 리소스 폴더와 키보드 위치를 사용하여 화면, 베젤, 스탠드와 베이스를 생성한다.
        /// </summary>
        [MenuItem("Tools/Monitor/Build Low Poly 16x9 Monitor")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("현재 편집 중인 씬을 보호하기 위해 별도 배치 프로젝트에서 실행합니다.");
            EditorSceneManager.OpenScene("Assets/Scenes/Keyboard.unity", OpenSceneMode.Single);
            if (GameObject.Find("Monitor") != null)
                throw new InvalidOperationException("기존 모니터가 있어 덮어쓰지 않습니다.");

            Material housing = MakeMaterial("MonitorHousing", new Color(0.055f, 0.07f, 0.085f), 0.35f, 0.32f);
            Material trim = MakeMaterial("MonitorTrim", new Color(0.16f, 0.19f, 0.21f), 0.65f, 0.58f);
            Material screen = MakeMaterial("MonitorScreen", Color.white, 0f, 0.7f);
            Texture2D gradient = MakeScreenTexture();
            screen.SetTexture("_BaseMap", gradient);
            screen.SetTexture("_EmissionMap", gradient);
            screen.SetColor("_EmissionColor", Color.white * 0.3f);
            screen.EnableKeyword("_EMISSION");
            Material led = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_ROOT + "/StatusLED.mat");

            GameObject monitor = new GameObject("Monitor");
            Transform display = new GameObject("Display").transform;
            display.SetParent(monitor.transform, false);
            display.localPosition = new Vector3(0f, 0.3f, 0f);
            display.localRotation = Quaternion.Euler(6f, 0f, 0f);

            Mesh quad = SaveMesh(MakeQuad(), "MonitorSurfaceQuad");
            GameObject shell = Part("DisplayHousing", display, SaveMesh(MakeBeveledBox(0.548f, 0.322f, 0.026f, 0.002f, 3), "MonitorHousing"), Vector3.zero, Vector3.one, housing);
            BoxCollider shellCollider = shell.AddComponent<BoxCollider>();
            shellCollider.size = new Vector3(0.548f, 0.322f, 0.026f);
            Part("InnerBezel", display, SaveMesh(MakeFrame(), "MonitorInnerBezel"), new Vector3(0f, 0.004f, -0.0136f), Vector3.one, trim);
            GameObject surface = Part("Screen", display, quad, new Vector3(0f, 0.004f, -0.014f), new Vector3(SCREEN_WIDTH, SCREEN_HEIGHT, 1f), screen);
            surface.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            surface.GetComponent<MeshRenderer>().receiveShadows = false;
            Part("BrandMark", display, SaveMesh(MakeBrandMark(), "MonitorBrandMark"), new Vector3(0f, -0.151f, -0.014f), Vector3.one, trim);
            Part("PowerIndicator", display, quad, new Vector3(0.252f, -0.15f, -0.014f), new Vector3(0.0018f, 0.0018f, 1f), led);

            Part("StandPost", monitor.transform, SaveMesh(MakeBeveledBox(0.032f, 0.166f, 0.026f, 0.0015f), "MonitorStandPost"), new Vector3(0f, 0.095f, 0.014f), Vector3.one, housing);
            Part("StandBridge", monitor.transform, SaveMesh(MakeBeveledBox(0.044f, 0.028f, 0.052f, 0.001f), "MonitorStandBridge"), new Vector3(0f, 0.178f, 0.002f), Vector3.one, housing);
            Part("StandAccent", monitor.transform, quad, new Vector3(0f, 0.085f, 0.0005f), new Vector3(0.008f, 0.115f, 1f), trim);
            GameObject foot = Part("WeightedBase", monitor.transform, SaveMesh(MakeBeveledBox(0.22f, 0.012f, 0.16f, 0.002f, 2), "MonitorBase"), new Vector3(0f, 0.006f, 0.015f), Vector3.one, housing);
            BoxCollider footCollider = foot.AddComponent<BoxCollider>();
            footCollider.size = new Vector3(0.22f, 0.012f, 0.16f);

            int vertices = 0;
            int triangles = 0;
            foreach (MeshFilter filter in monitor.GetComponentsInChildren<MeshFilter>())
            {
                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            if (vertices > 250 || triangles > 400)
                throw new InvalidOperationException("모니터 저폴리 예산을 초과했습니다.");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(monitor, PREFAB_PATH);
            UnityEngine.Object.DestroyImmediate(monitor);
            monitor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            monitor.name = "Monitor";
            GameObject keyboard = GameObject.Find("Keyboard_TKL_87");
            float sceneScale = keyboard.transform.lossyScale.x / 0.01905f;
            monitor.transform.localScale = Vector3.one * sceneScale;
            Vector3 keyboardCenter = keyboard.transform.TransformPoint(new Vector3(9.25f, 0f, 2.8f));
            monitor.transform.SetPositionAndRotation(keyboardCenter + keyboard.transform.rotation * new Vector3(0f, 0f, 0.28f * sceneScale), keyboard.transform.rotation);
            Camera camera = Camera.main;
            camera.orthographic = false;
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.005f * sceneScale;
            camera.farClipPlane = 10f * sceneScale;
            camera.transform.position = monitor.transform.TransformPoint(new Vector3(0.314f, 0.64f, -1.13f));
            camera.transform.LookAt(monitor.transform.TransformPoint(new Vector3(0f, 0.23f, -0.13f)));
            EditorUtility.SetDirty(screen);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Keyboard.unity");
            SavePreview(camera);
            Directory.CreateDirectory(".local/MonitorPreview");
            File.WriteAllText(".local/MonitorPreview/BuildReport.txt", "Screen: 528 x 297 mm (16:9)\nMesh renderers: 9\nVertices: " + vertices + "\nTriangles: " + triangles + "\nRuntime scripts: 0\n");
            Debug.Log("MONITOR_BUILD_COMPLETE aspect=16:9 vertices=" + vertices + " triangles=" + triangles);
        }

        /// <summary>
        /// 모니터 부품에 사용할 URP 재질을 저장한다.
        /// name, color, metallic, smoothness로 기존 재질을 갱신하거나 새로 만들어 반환한다.
        /// </summary>
        private static Material MakeMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = MATERIAL_ROOT + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 화면에 사용할 작은 색상 그라데이션 텍스처를 저장한다.
        /// 고정 64x36 해상도와 색상을 사용하여 공유 Texture2D 에셋을 반환한다.
        /// </summary>
        private static Texture2D MakeScreenTexture()
        {
            string path = MATERIAL_ROOT + "/MonitorScreenGradient.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
                return texture;
            texture = new Texture2D(64, 36, TextureFormat.RGB24, false) { name = "MonitorScreenGradient", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 36; y++)
            {
                Color color = Color.Lerp(new Color(0.013f, 0.035f, 0.055f), new Color(0.045f, 0.14f, 0.19f), y / 35f);
                for (int x = 0; x < 64; x++)
                    texture.SetPixel(x, y, color);
            }
            texture.Apply();
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        /// <summary>
        /// 보이는 모서리의 분할 수를 조절한 베벨 상자를 만든다.
        /// width, height, depth, bevel, cornerSegments로 저폴리 메시를 반환한다.
        /// </summary>
        private static Mesh MakeBeveledBox(float width, float height, float depth, float bevel, int cornerSegments = 1)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int count = 4 * (cornerSegments + 1);
            float[] depths = { -depth / 2f, -depth / 2f + bevel, depth / 2f - bevel, depth / 2f };
            for (int layer = 0; layer < 4; layer++)
            {
                float inset = layer == 0 || layer == 3 ? bevel : 0f;
                float x = width / 2f - inset;
                float y = height / 2f - inset;
                float corner = Mathf.Min(0.006f, Mathf.Min(x, y) * 0.3f);
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    float centerX = quadrant == 0 || quadrant == 3 ? x - corner : -x + corner;
                    float centerY = quadrant < 2 ? y - corner : -y + corner;
                    for (int step = 0; step <= cornerSegments; step++)
                    {
                        float angle = (quadrant * 90f + step * 90f / cornerSegments) * Mathf.Deg2Rad;
                        vertices.Add(new Vector3(centerX + Mathf.Cos(angle) * corner, centerY + Mathf.Sin(angle) * corner, depths[layer]));
                    }
                }
            }
            for (int layer = 0; layer < 3; layer++)
                for (int index = 0; index < count; index++)
                {
                    int a = layer * count + index;
                    int b = layer * count + (index + 1) % count;
                    triangles.AddRange(new[] { a, b, a + count, b, b + count, a + count });
                }
            vertices.Add(new Vector3(0f, 0f, -depth / 2f));
            vertices.Add(new Vector3(0f, 0f, depth / 2f));
            for (int index = 0; index < count; index++)
            {
                triangles.AddRange(new[] { count * 4, (index + 1) % count, index, count * 4 + 1, count * 3 + index, count * 3 + (index + 1) % count });
            }
            return MakeMesh(vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>
        /// 16:9 표시 영역 주변의 얇은 베젤 링을 만든다.
        /// 화면 치수와 5mm 테두리를 사용하여 8개 버텍스의 메시를 반환한다.
        /// </summary>
        private static Mesh MakeFrame()
        {
            float outerX = SCREEN_WIDTH / 2f + 0.005f;
            float outerY = SCREEN_HEIGHT / 2f + 0.005f;
            float innerX = SCREEN_WIDTH / 2f;
            float innerY = SCREEN_HEIGHT / 2f;
            Vector3[] vertices = { new Vector3(-outerX, -outerY, 0f), new Vector3(outerX, -outerY, 0f), new Vector3(outerX, outerY, 0f), new Vector3(-outerX, outerY, 0f), new Vector3(-innerX, -innerY, 0f), new Vector3(innerX, -innerY, 0f), new Vector3(innerX, innerY, 0f), new Vector3(-innerX, innerY, 0f) };
            var triangles = new List<int>();
            for (int index = 0; index < 4; index++)
            {
                int next = (index + 1) % 4;
                triangles.AddRange(new[] { index, next + 4, next, index, index + 4, next + 4 });
            }
            return MakeMesh(vertices, triangles.ToArray());
        }

        /// <summary>
        /// 화면과 작은 표면 장식이 공유하는 사각 메시를 만든다.
        /// 1x1 평면과 0~1 UV를 사용하여 앞쪽이 -Z인 4개 버텍스 메시를 반환한다.
        /// </summary>
        private static Mesh MakeQuad()
        {
            Mesh mesh = MakeMesh(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) }, new[] { 0, 2, 1, 0, 3, 2 });
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            return mesh;
        }

        /// <summary>
        /// 하단 베젤의 작은 세 줄 표식을 하나의 메시로 만든다.
        /// 고정 길이와 간격을 사용하여 텍스트 대신 12개 버텍스의 장식 메시를 반환한다.
        /// </summary>
        private static Mesh MakeBrandMark()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int line = 0; line < 3; line++)
            {
                float width = 0.012f - line * 0.003f;
                float y = (line - 1) * 0.0015f;
                int start = vertices.Count;
                vertices.AddRange(new[] { new Vector3(-width / 2f, y - 0.0004f, 0f), new Vector3(width / 2f, y - 0.0004f, 0f), new Vector3(width / 2f, y + 0.0004f, 0f), new Vector3(-width / 2f, y + 0.0004f, 0f) });
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            return MakeMesh(vertices.ToArray(), triangles.ToArray());
        }

        /// <summary>
        /// 좌표와 삼각형 목록을 Unity 메시로 구성한다.
        /// vertices와 triangles를 사용하여 법선과 렌더링 범위가 계산된 메시를 반환한다.
        /// </summary>
        private static Mesh MakeMesh(Vector3[] vertices, int[] triangles)
        {
            Mesh mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 생성한 메시를 기존 메시 폴더에 저장한다.
        /// mesh와 name을 사용하여 저장된 에셋을 반환하고 기존 에셋이 있으면 참조를 유지한다.
        /// </summary>
        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = MESH_ROOT + "/" + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>
        /// 모니터의 한 부품을 지정한 부모 아래 생성한다.
        /// name, parent, mesh, position, scale, material을 사용하여 표시 부품을 반환한다.
        /// </summary>
        private static GameObject Part(string name, Transform parent, Mesh mesh, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }

        /// <summary>
        /// 현재 카메라로 키보드와 모니터의 미리보기를 저장한다.
        /// camera와 1280x720 렌더 타깃을 사용하여 PNG를 생성하고 임시 메모리를 해제한다.
        /// </summary>
        private static void SavePreview(Camera camera)
        {
            Directory.CreateDirectory(".local/MonitorPreview");
            RenderTexture target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            File.WriteAllBytes(".local/MonitorPreview/KeyboardAndMonitor.png", texture.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
