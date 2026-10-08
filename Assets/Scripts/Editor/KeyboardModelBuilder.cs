using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class KeyboardModelBuilder
    {
        private const string RESOURCE_ROOT = "Assets/Resource";
        private const string PREFAB_ROOT = RESOURCE_ROOT + "/Prefabs";
        private const string MATERIAL_ROOT = RESOURCE_ROOT + "/Materials";
        private const string MESH_ROOT = RESOURCE_ROOT + "/Meshes";
        private const string PREVIEW_ROOT = ".local/KeyboardPreview";
        private const float PITCH = 0.01905f;
        private const int CORNER_SEGMENTS = 12;

        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> _caps = new Dictionary<string, GameObject>();
        private static Mesh _box;
        private static Mesh _cylinder;
        private static Font _font;
        private static int _keyCount;

        /// <summary>
        /// 메뉴 또는 배치 실행으로 키보드 모델을 생성한다.
        /// 고정 ANSI 배열과 모델 치수를 사용하여 메시, 재질, 프리팹, 새 씬을 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Detailed Keyboard Scene")]
        public static void Build()
        {
            foreach (string folder in new[] { RESOURCE_ROOT, MESH_ROOT, MATERIAL_ROOT, PREFAB_ROOT, PREFAB_ROOT + "/Keycaps", PREVIEW_ROOT })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            _materials.Clear();
            _caps.Clear();
            _keyCount = 0;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateMaterials();
            _box = SaveMesh(RoundedSolid("BeveledBlock", 1f, 1f, 1f, 0.08f, 0.035f), "BeveledBlock");
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cylinder = cylinder.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(cylinder);
            foreach (float width in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.75f, 6.25f })
                CreateCap(width, 1f);
            CreateCap(1f, 2f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            GameObject keyboard = new GameObject("Keyboard_ANSI_104");
            GameObject body = BuildBody();
            body.transform.localScale = Vector3.one * PITCH;
            GameObject bodyPrefab = PrefabUtility.SaveAsPrefabAsset(body, PREFAB_ROOT + "/KeyboardBody.prefab");
            UnityEngine.Object.DestroyImmediate(body);
            GameObject bodyInstance = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab);
            bodyInstance.transform.SetParent(keyboard.transform, false);
            bodyInstance.transform.localScale = Vector3.one;
            BuildLayout(keyboard.transform);
            if (_keyCount != 104)
                throw new InvalidOperationException("ANSI 키 개수가 104개가 아닙니다: " + _keyCount);
            KeyboardInputSetup.Configure(keyboard);
            keyboard.transform.localScale = Vector3.one * PITCH;
            GameObject keyboardPrefab = PrefabUtility.SaveAsPrefabAsset(keyboard, PREFAB_ROOT + "/Keyboard_ANSI_104.prefab");
            UnityEngine.Object.DestroyImmediate(keyboard);
            keyboard = (GameObject)PrefabUtility.InstantiatePrefab(keyboardPrefab);
            BuildStudio();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Keyboard.unity");
            AssetDatabase.SaveAssets();
            CapturePreview();
            File.WriteAllText(PREVIEW_ROOT + "/BuildReport.txt", "Key instances: " + _keyCount + "\nUnique keycap prefabs: " + _caps.Count + "\nScene: Assets/Scenes/Keyboard.unity\nKeyboard width: 0.452 m\n");
            Debug.Log("KEYBOARD_BUILD_COMPLETE keys=" + _keyCount + " uniqueCaps=" + _caps.Count);
        }

        /// <summary>
        /// 모델에 사용할 색상과 표면 특성을 정의한다.
        /// 고정 색상, 금속도, 매끄러움 값을 사용하여 공유 재질 사전을 변경한다.
        /// </summary>
        private static void CreateMaterials()
        {
            MakeMaterial("Porcelain_PBT", new Color(0.84f, 0.81f, 0.72f), 0f, 0.28f);
            MakeMaterial("DeepOcean_PBT", new Color(0.055f, 0.12f, 0.16f), 0f, 0.3f);
            MakeMaterial("BurntOrange_PBT", new Color(0.86f, 0.25f, 0.065f), 0f, 0.3f);
            MakeMaterial("AnodizedAluminum", new Color(0.09f, 0.135f, 0.16f), 0.75f, 0.5f);
            MakeMaterial("BrushedBrass", new Color(0.63f, 0.43f, 0.19f), 0.8f, 0.52f);
            MakeMaterial("Graphite", new Color(0.022f, 0.028f, 0.033f), 0.1f, 0.3f);
            MakeMaterial("Rubber", new Color(0.035f, 0.04f, 0.045f), 0f, 0.13f);
            MakeMaterial("SwitchHousing", new Color(0.24f, 0.29f, 0.3f), 0f, 0.5f);
            MakeMaterial("SwitchStem", new Color(0.67f, 0.22f, 0.13f), 0f, 0.3f);
            MakeMaterial("Steel", new Color(0.5f, 0.55f, 0.58f), 0.85f, 0.7f);
            MakeMaterial("Desk", new Color(0.13f, 0.155f, 0.17f), 0f, 0.18f);
            Material led = MakeMaterial("StatusLED", new Color(0.3f, 0.85f, 0.65f), 0f, 0.4f);
            led.EnableKeyword("_EMISSION");
            led.SetColor("_EmissionColor", new Color(0.2f, 1.5f, 0.75f));
            AddPbtGrain();
        }

        /// <summary>
        /// 키캡에 미세한 사출 표면 질감을 추가한다.
        /// 고정 난수와 공유 PBT 재질을 사용하여 노멀 텍스처를 저장하고 재질의 표면을 변경한다.
        /// </summary>
        private static void AddPbtGrain()
        {
            string path = MATERIAL_ROOT + "/PBT_MicroGrain.asset";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(128, 128, TextureFormat.RGBA32, true, true);
                texture.name = "PBT_MicroGrain";
                var random = new System.Random(104);
                var pixels = new Color[128 * 128];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = new Color(0.5f + (float)(random.NextDouble() - 0.5) * 0.12f, 0.5f + (float)(random.NextDouble() - 0.5) * 0.12f, 1f, 1f);
                texture.SetPixels(pixels);
                texture.Apply();
                AssetDatabase.CreateAsset(texture, path);
            }
            foreach (string name in new[] { "Porcelain_PBT", "DeepOcean_PBT", "BurntOrange_PBT" })
            {
                Material material = _materials[name];
                material.SetTexture("_BumpMap", texture);
                material.SetTextureScale("_BumpMap", new Vector2(3f, 3f));
                material.SetFloat("_BumpScale", 0.22f);
                material.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>
        /// 이름과 색상, 금속도, 매끄러움으로 URP 재질을 만든다.
        /// name, color, metallic, smoothness를 사용하여 저장된 공유 재질을 반환한다.
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
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            _materials[name] = material;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 둥근 사각형 둘레의 좌표를 계산한다.
        /// width, depth, radius와 height를 사용하여 반시계 방향의 단면 좌표 목록을 반환한다.
        /// </summary>
        private static List<Vector3> Ring(float width, float depth, float radius, float height)
        {
            var points = new List<Vector3>();
            radius = Mathf.Min(radius, Mathf.Min(width, depth) * 0.49f);
            for (int corner = 0; corner < 4; corner++)
            {
                float angleStart = corner * 90f;
                float cx = corner == 0 || corner == 3 ? width * 0.5f - radius : -width * 0.5f + radius;
                float cz = corner < 2 ? depth * 0.5f - radius : -depth * 0.5f + radius;
                for (int step = 0; step <= CORNER_SEGMENTS; step++)
                {
                    float angle = (angleStart + step * 90f / CORNER_SEGMENTS) * Mathf.Deg2Rad;
                    points.Add(new Vector3(cx + Mathf.Cos(angle) * radius, height, cz + Mathf.Sin(angle) * radius));
                }
                Vector3 edgeStart = points[points.Count - 1];
                int nextCorner = (corner + 1) % 4;
                float nextCx = nextCorner == 0 || nextCorner == 3 ? width * 0.5f - radius : -width * 0.5f + radius;
                float nextCz = nextCorner < 2 ? depth * 0.5f - radius : -depth * 0.5f + radius;
                float nextAngle = nextCorner * 90f * Mathf.Deg2Rad;
                Vector3 edgeEnd = new Vector3(nextCx + Mathf.Cos(nextAngle) * radius, height, nextCz + Mathf.Sin(nextAngle) * radius);
                for (int step = 1; step <= CORNER_SEGMENTS; step++)
                    points.Add(Vector3.Lerp(edgeStart, edgeEnd, step / (float)(CORNER_SEGMENTS + 1)));
            }
            return points;
        }

        /// <summary>
        /// 여러 단면을 연결하고 선택한 끝면을 막아 메시를 만든다.
        /// name, rings, capBottom, capTop을 사용하여 UV와 법선이 포함된 메시를 반환한다.
        /// </summary>
        private static Mesh Loft(string name, List<List<Vector3>> rings, bool capBottom, bool capTop)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int count = rings[0].Count;
            foreach (List<Vector3> ring in rings)
                vertices.AddRange(ring);
            for (int layer = 0; layer < rings.Count - 1; layer++)
            {
                for (int index = 0; index < count; index++)
                {
                    int a = layer * count + index;
                    int b = layer * count + (index + 1) % count;
                    int c = a + count;
                    int d = b + count;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }
            for (int end = 0; end < 2; end++)
            {
                if (end == 0 ? !capBottom : !capTop)
                    continue;
                int start = end == 0 ? 0 : (rings.Count - 1) * count;
                Vector3 center = Vector3.zero;
                for (int index = 0; index < count; index++)
                    center += vertices[start + index] / count;
                int centerIndex = vertices.Count;
                vertices.Add(center);
                for (int index = 0; index < count; index++)
                {
                    int next = start + (index + 1) % count;
                    triangles.AddRange(end == 0 ? new[] { centerIndex, start + index, next } : new[] { centerIndex, next, start + index });
                }
            }
            var uv = new List<Vector2>();
            foreach (Vector3 vertex in vertices)
                uv.Add(new Vector2(vertex.x, vertex.z));
            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// 모서리에 베벨을 가진 둥근 직육면체를 생성한다.
        /// name, width, depth, height, radius, bevel을 사용하여 중심 기준의 닫힌 메시를 반환한다.
        /// </summary>
        private static Mesh RoundedSolid(string name, float width, float depth, float height, float radius, float bevel)
        {
            return Loft(name, new List<List<Vector3>>
            {
                Ring(width - bevel * 2f, depth - bevel * 2f, radius, -height / 2f),
                Ring(width, depth, radius, -height / 2f + bevel),
                Ring(width, depth, radius, height / 2f - bevel),
                Ring(width - bevel * 2f, depth - bevel * 2f, radius, height / 2f)
            }, true, true);
        }

        /// <summary>
        /// 메시를 프로젝트 에셋으로 저장한다.
        /// mesh와 name을 사용하여 기존 에셋 참조를 보존하고 저장된 메시를 반환한다.
        /// </summary>
        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = MESH_ROOT + "/" + name + ".asset";
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, saved);
            UnityEngine.Object.DestroyImmediate(mesh);
            return saved;
        }

        /// <summary>
        /// 폭과 깊이가 같은 키에서 공유할 키캡 프리팹을 만든다.
        /// units와 depthUnits로 사다리꼴 외벽, 오목한 윗면, 내부 벽, 십자 소켓을 저장한다.
        /// </summary>
        private static void CreateCap(float units, float depthUnits)
        {
            string id = CapId(units, depthUnits);
            float width = units - 0.075f;
            float depth = depthUnits - 0.075f;
            var rings = new List<List<Vector3>>
            {
                Ring(width - 0.025f, depth - 0.025f, 0.085f, 0f),
                Ring(width, depth, 0.09f, 0.045f),
                Ring(width - 0.21f, depth - 0.21f, 0.1f, 0.47f),
                Ring(width - 0.25f, depth - 0.25f, 0.095f, 0.515f),
                Ring(width - 0.29f, depth - 0.29f, 0.09f, 0.53f)
            };
            for (int layer = 1; layer <= 14; layer++)
            {
                float scale = 1f - layer / 15f;
                List<Vector3> ring = Ring((width - 0.29f) * scale, (depth - 0.29f) * scale, 0.09f * scale, 0f);
                for (int index = 0; index < ring.Count; index++)
                {
                    Vector3 point = ring[index];
                    float normalizedZ = point.z / ((depth - 0.29f) * 0.5f);
                    point.y = 0.475f + 0.055f * normalizedZ * normalizedZ;
                    ring[index] = point;
                }
                rings.Add(ring);
            }
            GameObject cap = new GameObject("Keycap_" + id);
            Mesh shell = SaveMesh(Loft("Keycap_" + id, rings, false, true), "Keycap_" + id);
            Vector2[] shellUv = shell.uv;
            Vector3[] shellVertices = shell.vertices;
            int ringCount = rings[0].Count;
            for (int index = 0; index < ringCount * 4; index++)
                shellUv[index] = new Vector2((index % ringCount) / (float)ringCount * (width + depth) * 2f, shellVertices[index].y);
            shell.uv = shellUv;
            shell.RecalculateTangents();
            EditorUtility.SetDirty(shell);
            Part("PBT_SculptedShell", cap.transform, shell, Vector3.zero, Vector3.one, "Porcelain_PBT");
            var innerRings = new List<List<Vector3>>
            {
                Ring(width - 0.15f, depth - 0.15f, 0.05f, 0.012f),
                Ring(width - 0.34f, depth - 0.34f, 0.06f, 0.395f)
            };
            Mesh inner = Loft("InnerWall_" + id, innerRings, false, true);
            int[] indices = inner.triangles;
            for (int index = 0; index < indices.Length; index += 3)
                (indices[index], indices[index + 1]) = (indices[index + 1], indices[index]);
            inner.triangles = indices;
            inner.RecalculateNormals();
            Part("Hollow_Interior", cap.transform, SaveMesh(inner, "InnerWall_" + id), Vector3.zero, Vector3.one, "Porcelain_PBT");
            Part("Bottom_WallRim", cap.transform, SaveMesh(Loft("Rim_" + id, new List<List<Vector3>> { rings[0], innerRings[0] }, false, false), "Rim_" + id), Vector3.zero, Vector3.one, "Porcelain_PBT");
            Socket(cap.transform, 0f);
            if (units >= 2f || depthUnits > 1f)
            {
                float spacing = units >= 6f ? 2.625f : 0.6f;
                Socket(cap.transform, depthUnits > 1f ? 0f : -spacing, depthUnits > 1f ? -spacing : 0f);
                Socket(cap.transform, depthUnits > 1f ? 0f : spacing, depthUnits > 1f ? spacing : 0f);
                Block("Underside_Reinforcement", cap.transform, new Vector3(0f, 0.345f, 0.2f), new Vector3(width - 0.34f, 0.09f, 0.045f), "Porcelain_PBT");
                Block("Underside_Reinforcement", cap.transform, new Vector3(0f, 0.345f, -0.2f), new Vector3(width - 0.34f, 0.09f, 0.045f), "Porcelain_PBT");
            }
            BoxCollider collider = cap.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.26f, 0f);
            collider.size = new Vector3(width, 0.53f, depth);
            cap.transform.localScale = Vector3.one * PITCH;
            _caps[id] = PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_ROOT + "/Keycaps/Keycap_" + id + ".prefab");
            UnityEngine.Object.DestroyImmediate(cap);
        }

        /// <summary>
        /// 키캡 하부에 십자 체결용 빈 소켓을 만든다.
        /// parent, x, z를 사용하여 십자 구멍 둘레의 벽을 자식 오브젝트로 추가한다.
        /// </summary>
        private static void Socket(Transform parent, float x, float z = 0f)
        {
            Transform socket = new GameObject("MX_CrossSocket").transform;
            socket.SetParent(parent, false);
            socket.localPosition = new Vector3(x, 0.21f, z);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Block("Socket_Quadrant", socket, new Vector3(sx * 0.075f, 0f, sz * 0.075f), new Vector3(0.1f, 0.33f, 0.1f), "Porcelain_PBT");
            Block("Socket_Left", socket, new Vector3(-0.14f, 0f, 0f), new Vector3(0.03f, 0.33f, 0.25f), "Porcelain_PBT");
            Block("Socket_Right", socket, new Vector3(0.14f, 0f, 0f), new Vector3(0.03f, 0.33f, 0.25f), "Porcelain_PBT");
            Block("Socket_Front", socket, new Vector3(0f, 0f, -0.14f), new Vector3(0.25f, 0.33f, 0.03f), "Porcelain_PBT");
            Block("Socket_Back", socket, new Vector3(0f, 0f, 0.14f), new Vector3(0.25f, 0.33f, 0.03f), "Porcelain_PBT");
        }

        /// <summary>
        /// 폭과 깊이를 파일명에 사용할 식별자로 변환한다.
        /// units와 depthUnits를 사용하여 문화권과 무관한 프리팹 식별자를 반환한다.
        /// </summary>
        private static string CapId(float units, float depthUnits)
        {
            return units.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_') + "u" + (depthUnits > 1f ? "_Vertical2u" : "");
        }

        /// <summary>
        /// 메시와 재질로 모델 부품을 생성한다.
        /// name, parent, mesh, position, scale, material을 사용하여 생성된 오브젝트를 반환한다.
        /// </summary>
        private static GameObject Part(string name, Transform parent, Mesh mesh, Vector3 position, Vector3 scale, string material)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = _materials[material];
            return part;
        }

        /// <summary>
        /// 공유 베벨 메시로 직육면체 부품을 추가한다.
        /// name, parent, position, scale, material을 사용하여 생성된 부품을 반환한다.
        /// </summary>
        private static GameObject Block(string name, Transform parent, Vector3 position, Vector3 scale, string material)
        {
            return Part(name, parent, _box, position, scale, material);
        }

        /// <summary>
        /// 하우징과 외부 세부 부품을 조립한다.
        /// 고정 치수와 공유 재질로 보강판, 테두리, 포트, 나사, 고무발을 가진 본체를 반환한다.
        /// </summary>
        private static GameObject BuildBody()
        {
            GameObject body = new GameObject("KeyboardBody");
            Transform root = body.transform;
            Mesh baseMesh = SaveMesh(RoundedSolid("LowerCase", 23.75f, 7.4f, 0.56f, 0.28f, 0.08f), "LowerCase");
            Part("CNC_LowerCase", root, baseMesh, new Vector3(11.5f, 0.4f, 2.8f), Vector3.one, "AnodizedAluminum");
            Mesh seam = SaveMesh(RoundedSolid("CaseSeam", 23.71f, 7.36f, 0.038f, 0.27f, 0.012f), "CaseSeam");
            Part("Case_AssemblySeam", root, seam, new Vector3(11.5f, 0.695f, 2.8f), Vector3.one, "Graphite");
            Mesh plate = SaveMesh(RoundedSolid("Plate", 23.3f, 6.98f, 0.08f, 0.18f, 0.025f), "Plate");
            Part("Brass_SwitchPlate", root, plate, new Vector3(11.5f, 0.76f, 2.8f), Vector3.one, "BrushedBrass");
            Block("TopBezel_Front", root, new Vector3(11.5f, 0.81f, -0.74f), new Vector3(23.6f, 0.22f, 0.34f), "AnodizedAluminum");
            Block("TopBezel_Rear", root, new Vector3(11.5f, 0.81f, 6.36f), new Vector3(23.6f, 0.22f, 0.28f), "AnodizedAluminum");
            Block("TopBezel_Left", root, new Vector3(-0.22f, 0.81f, 2.8f), new Vector3(0.28f, 0.22f, 7.18f), "AnodizedAluminum");
            Block("TopBezel_Right", root, new Vector3(23.22f, 0.81f, 2.8f), new Vector3(0.28f, 0.22f, 7.18f), "AnodizedAluminum");
            foreach (float x in new[] { 0.2f, 22.8f })
            {
                foreach (float z in new[] { -0.55f, 6.17f })
                {
                    Part("Recessed_CaseScrew", root, _cylinder, new Vector3(x, 0.93f, z), new Vector3(0.105f, 0.012f, 0.105f), "Steel");
                    Block("Screw_Drive_X", root, new Vector3(x, 0.944f, z), new Vector3(0.062f, 0.006f, 0.014f), "Graphite");
                    Block("Screw_Drive_Z", root, new Vector3(x, 0.944f, z), new Vector3(0.014f, 0.006f, 0.062f), "Graphite");
                    Block("AntiSlip_Foot", root, new Vector3(x + (x < 1f ? 0.65f : -0.65f), 0.075f, z + (z < 0f ? 0.55f : -0.55f)), new Vector3(1.1f, 0.15f, 0.5f), "Rubber");
                }
                Block("Foldout_Riser", root, new Vector3(x + (x < 1f ? 0.65f : -0.65f), 0.125f, 5.4f), new Vector3(1.1f, 0.19f, 0.65f), "Graphite");
            }
            Block("USB_C_Recess", root, new Vector3(2.2f, 0.42f, 6.507f), new Vector3(0.8f, 0.31f, 0.02f), "Graphite");
            Block("USB_C_MetalSleeve", root, new Vector3(2.2f, 0.42f, 6.525f), new Vector3(0.59f, 0.19f, 0.018f), "Steel");
            Block("USB_C_Opening", root, new Vector3(2.2f, 0.42f, 6.54f), new Vector3(0.5f, 0.13f, 0.012f), "Graphite");
            Block("USB_C_Tongue", root, new Vector3(2.2f, 0.42f, 6.55f), new Vector3(0.37f, 0.025f, 0.01f), "SwitchHousing");
            for (int contact = 0; contact < 8; contact++)
                Block("USB_Contact", root, new Vector3(2.04f + contact * 0.045f, 0.446f, 6.558f), new Vector3(0.018f, 0.01f, 0.008f), "BrushedBrass");
            Block("Rear_Weight_Insert", root, new Vector3(11.5f, 0.425f, 6.513f), new Vector3(5.6f, 0.22f, 0.018f), "BrushedBrass");
            Block("Indicator_Inset", root, new Vector3(20.25f, 0.824f, 5.72f), new Vector3(3.1f, 0.05f, 0.68f), "DeepOcean_PBT");
            for (int index = 0; index < 3; index++)
            {
                float x = 19.4f + index * 0.85f;
                Part("StatusLens_" + index, root, _cylinder, new Vector3(x, 0.859f, 5.82f), new Vector3(0.07f, 0.012f, 0.07f), index == 0 ? "StatusLED" : "Rubber");
                Legend("Status_Label", root, index == 0 ? "NUM" : index == 1 ? "CAPS" : "SCROLL", new Vector3(x, 0.86f, 5.57f), 0.11f, new Color(0.77f, 0.8f, 0.78f));
            }
            Legend("Model_Badge", root, "A T E L I E R   /   1 0 4", new Vector3(17f, 0.935f, 6.36f), 0.1f, new Color(0.78f, 0.59f, 0.32f));
            BoxCollider collider = body.AddComponent<BoxCollider>();
            collider.center = new Vector3(11.5f, 0.5f, 2.8f);
            collider.size = new Vector3(23.75f, 0.85f, 7.4f);
            return body;
        }

        /// <summary>
        /// ANSI 104키 배열을 생성한다.
        /// parent를 사용하여 공유 키캡 인스턴스와 각 키의 각인을 키보드에 추가한다.
        /// </summary>
        private static void BuildLayout(Transform parent)
        {
            Transform keys = new GameObject("Keys_104_SharedPrefabs").transform;
            keys.SetParent(parent, false);
            Key(keys, "Esc", 0.5f, 5.65f, 1f, "BurntOrange_PBT");
            for (int index = 0; index < 12; index++)
                Key(keys, "F" + (index + 1), 2.5f + index + (index / 4) * 0.5f, 5.65f);
            Key(keys, "Print\nScreen", 15.9f, 5.65f, 1f, "DeepOcean_PBT");
            Key(keys, "Scroll\nLock", 16.9f, 5.65f, 1f, "DeepOcean_PBT");
            Key(keys, "Pause", 17.9f, 5.65f, 1f, "DeepOcean_PBT");
            Row(keys, 4.4f, new[] { "~\n`", "!\n1", "@\n2", "#\n3", "$\n4", "%\n5", "^\n6", "&\n7", "*\n8", "(\n9", ")\n0", "_\n-", "+\n=", "Backspace" }, new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 2f });
            Row(keys, 3.4f, new[] { "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "{\n[", "}\n]", "|\n\\" }, new[] { 1.5f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1.5f });
            Row(keys, 2.4f, new[] { "Caps Lock", "A", "S", "D", "F", "G", "H", "J", "K", "L", ":\n;", "\"\n'", "Enter" }, new[] { 1.75f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 2.25f });
            Row(keys, 1.4f, new[] { "Shift", "Z", "X", "C", "V", "B", "N", "M", "<\n,", ">\n.", "?\n/", "Shift" }, new[] { 2.25f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 2.75f });
            Row(keys, 0.4f, new[] { "Ctrl", "Win", "Alt", "", "Alt", "Win", "Menu", "Ctrl" }, new[] { 1.25f, 1.25f, 1.25f, 6.25f, 1.25f, 1.25f, 1.25f, 1.25f });
            string[] navigation = { "Insert", "Home", "Page\nUp", "Delete", "End", "Page\nDown" };
            for (int index = 0; index < 6; index++)
                Key(keys, navigation[index], 15.9f + index % 3, index < 3 ? 4.4f : 3.4f, 1f, "DeepOcean_PBT");
            Key(keys, "^", 16.9f, 1.4f, 1f, "DeepOcean_PBT");
            Key(keys, "<", 15.9f, 0.4f, 1f, "DeepOcean_PBT");
            Key(keys, "v", 16.9f, 0.4f, 1f, "DeepOcean_PBT");
            Key(keys, ">", 17.9f, 0.4f, 1f, "DeepOcean_PBT");
            string[] numTop = { "Num\nLock", "/", "*", "-" };
            for (int index = 0; index < 4; index++)
                Key(keys, numTop[index], 19.4f + index, 4.4f, 1f, "DeepOcean_PBT");
            string[] num = { "7\nHome", "8", "9\nPgUp", "4", "5", "6", "1\nEnd", "2", "3\nPgDn" };
            for (int index = 0; index < 9; index++)
                Key(keys, num[index], 19.4f + index % 3, 3.4f - index / 3);
            Key(keys, "+", 22.4f, 2.9f, 1f, "DeepOcean_PBT", 2f);
            Key(keys, "Enter", 22.4f, 0.9f, 1f, "BurntOrange_PBT", 2f);
            Key(keys, "0   Ins", 19.9f, 0.4f, 2f);
            Key(keys, ".\nDel", 21.4f, 0.4f);
        }

        /// <summary>
        /// 폭이 다른 키를 한 행에 순서대로 배치한다.
        /// parent, z, labels, widths를 사용하여 누적 폭의 중심에 각 키를 추가한다.
        /// </summary>
        private static void Row(Transform parent, float z, string[] labels, float[] widths)
        {
            float x = 0f;
            for (int index = 0; index < labels.Length; index++)
            {
                string material = widths[index] > 1f ? "DeepOcean_PBT" : "Porcelain_PBT";
                if (labels[index] == "Enter")
                    material = "BurntOrange_PBT";
                Key(parent, labels[index], x + widths[index] / 2f, z, widths[index], material);
                x += widths[index];
            }
        }

        /// <summary>
        /// 공유 프리팹으로 키와 하부 스위치를 배치한다.
        /// parent, label, x, z, width, material, depth로 인스턴스와 각인을 만들고 키 수를 증가시킨다.
        /// </summary>
        private static void Key(Transform parent, string label, float x, float z, float width = 1f, string material = "Porcelain_PBT", float depth = 1f)
        {
            GameObject key = (GameObject)PrefabUtility.InstantiatePrefab(_caps[CapId(width, depth)]);
            key.name = "Key_" + (label.Length == 0 ? "Space" : label.Replace('\n', '_'));
            key.transform.SetParent(parent, false);
            key.transform.localScale = Vector3.one;
            key.transform.localPosition = new Vector3(x, 0.94f, z);
            foreach (MeshRenderer renderer in key.GetComponentsInChildren<MeshRenderer>())
                renderer.sharedMaterial = _materials[material];
            if (label.Length > 0)
                Legend("Printed_Legend", key.transform, label, new Vector3(0f, 0.542f, 0f), label.Replace("\n", "").Length > 5 ? 0.14f : 0.19f, material == "Porcelain_PBT" ? new Color(0.035f, 0.09f, 0.12f) : new Color(0.94f, 0.9f, 0.8f));
            if (label == "F" || label == "J")
                Block("Tactile_HomingBar", key.transform, new Vector3(0f, 0.506f, -0.23f), new Vector3(0.19f, 0.018f, 0.034f), material);
            Transform mechanism = new GameObject("Switch_" + _keyCount).transform;
            mechanism.SetParent(parent, false);
            mechanism.localPosition = new Vector3(x, 0.81f, z);
            Block("Switch_BottomHousing", mechanism, Vector3.zero, new Vector3(0.73f, 0.12f, 0.73f), "Graphite");
            Block("Switch_TopHousing", mechanism, new Vector3(0f, 0.08f, 0f), new Vector3(0.62f, 0.13f, 0.62f), "SwitchHousing");
            Block("MX_Stem_X", mechanism, new Vector3(0f, 0.175f, 0f), new Vector3(0.21f, 0.14f, 0.06f), "SwitchStem");
            Block("MX_Stem_Z", mechanism, new Vector3(0f, 0.175f, 0f), new Vector3(0.06f, 0.14f, 0.21f), "SwitchStem");
            if (width >= 2f || depth > 1f)
            {
                bool vertical = depth > 1f;
                float spacing = width >= 6f ? 2.625f : 0.6f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 offset = vertical ? new Vector3(0f, 0f, side * spacing) : new Vector3(side * spacing, 0f, 0f);
                    Block("Stabilizer_Housing", mechanism, offset + new Vector3(0f, 0.025f, 0f), new Vector3(0.25f, 0.14f, 0.32f), "Graphite");
                    Block("Stabilizer_Stem", mechanism, offset + new Vector3(0f, 0.145f, 0f), new Vector3(0.1f, 0.14f, 0.1f), "SwitchStem");
                }
                Block("Stabilizer_SteelWire", mechanism, new Vector3(0f, 0.045f, -0.17f), vertical ? new Vector3(0.035f, 0.035f, spacing * 2f) : new Vector3(spacing * 2f, 0.035f, 0.035f), "Steel");
            }
            _keyCount++;
        }

        /// <summary>
        /// 키 윗면에 글자 각인을 배치한다.
        /// name, parent, text, position, size, color로 위를 향하는 텍스트 메시를 반환한다.
        /// </summary>
        private static GameObject Legend(string name, Transform parent, string text, Vector3 position, float size, Color color)
        {
            GameObject label = new GameObject(name);
            label.transform.SetParent(parent, false);
            label.transform.localPosition = position;
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh mesh = label.AddComponent<TextMesh>();
            mesh.font = _font;
            mesh.text = text;
            mesh.fontSize = 96;
            mesh.characterSize = size / 9f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.lineSpacing = 0.9f;
            mesh.color = color;
            label.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            label.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return label;
        }

        /// <summary>
        /// 키보드를 감상할 카메라와 스튜디오 조명을 배치한다.
        /// 고정 구도와 색상으로 바닥, 주광, 보조광, 카메라와 환경광 상태를 설정한다.
        /// </summary>
        private static void BuildStudio()
        {
            GameObject studio = new GameObject("Presentation_Studio");
            Block("Studio_Surface", studio.transform, new Vector3(0.22f, -0.007f, 0.055f), new Vector3(2f, 0.012f, 1.5f), "Desk");
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0.46f, 0.48f, -0.4f);
            camera.transform.LookAt(new Vector3(0.219f, 0.018f, 0.055f));
            camera.orthographic = true;
            camera.orthographicSize = 0.175f;
            camera.nearClipPlane = 0.005f;
            camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.155f, 0.17f);
            LightSetup("Key_Softbox", new Vector3(50f, -35f, 0f), new Color(1f, 0.91f, 0.79f), 1.15f);
            LightSetup("Fill_Softbox", new Vector3(35f, 145f, 0f), new Color(0.7f, 0.83f, 1f), 0.4f);
            LightSetup("Rim_Softbox", new Vector3(15f, -170f, 0f), new Color(1f, 0.77f, 0.53f), 0.35f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.32f, 0.35f);
            RenderSettings.skybox = null;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0.219f, 0.018f, 0.055f), camera.transform.rotation, 0.34f);
        }

        /// <summary>
        /// 방향성 스튜디오 조명을 만든다.
        /// name, rotation, color, intensity를 사용하여 그림자와 색상이 지정된 광원을 추가한다.
        /// </summary>
        private static void LightSetup(string name, Vector3 rotation, Color color, float intensity)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.rotation = Quaternion.Euler(rotation);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            light.shadowBias = 0.02f;
            light.shadowNormalBias = 0.01f;
        }

        /// <summary>
        /// 완성된 씬의 미리보기 이미지를 저장한다.
        /// 주 카메라와 고정 해상도를 사용하여 PNG를 저장하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void CapturePreview()
        {
            Camera camera = null;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Main Camera")
                    camera = root.GetComponent<Camera>();
            SaveCameraImage(camera, "Keyboard");
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float size = camera.orthographicSize;
            int mask = camera.cullingMask;
            GameObject sample = UnityEngine.Object.Instantiate(_caps[CapId(1f, 1f)]);
            sample.transform.position = Vector3.zero;
            foreach (Transform child in sample.GetComponentsInChildren<Transform>())
                child.gameObject.layer = 31;
            camera.cullingMask = 1 << 31;
            camera.orthographicSize = 0.014f;
            camera.transform.position = new Vector3(0.026f, 0.028f, -0.033f);
            camera.transform.LookAt(new Vector3(0f, 0.005f, 0f));
            SaveCameraImage(camera, "Keycap_Detail");
            camera.transform.position = new Vector3(0.024f, -0.025f, -0.028f);
            camera.transform.LookAt(new Vector3(0f, 0.003f, 0f));
            SaveCameraImage(camera, "Keycap_Underside");
            UnityEngine.Object.DestroyImmediate(sample);
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.orthographicSize = size;
            camera.cullingMask = mask;
        }

        /// <summary>
        /// 지정한 카메라의 렌더 결과를 PNG로 저장한다.
        /// camera와 name을 사용하여 미리보기 파일을 만들고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void SaveCameraImage(Camera camera, string name)
        {
            RenderTexture target = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW_ROOT + "/" + name + ".png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}

