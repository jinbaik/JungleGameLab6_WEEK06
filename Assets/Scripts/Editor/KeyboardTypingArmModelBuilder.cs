using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class KeyboardTypingArmModelBuilder
    {
        private const string PREFAB_PATH = "Assets/Resource/Prefabs/KeyboardTypingArm.prefab";
        private const string MESH_ROOT = "Assets/Resource/Meshes/TypingArm_";
        private static Mesh _box;
        private static Mesh _round;
        private static Material _steel;
        private static Material _dark;
        private static Material _rubber;
        private static Material _shell;

        /// <summary>
        /// Keyboard 씬에 작업실 보조 기계팔을 생성한다.
        /// 흰 외장과 검은 관절의 사진 형태 및 키보드 배율을 사용해 타건 막대 프리팹, 씬과 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard Arm/Build Reference Typing Arm")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬 보호를 위해 Unity 배치 모드로 실행합니다.");
            EditorSceneManager.OpenScene("Assets/Scenes/Keyboard.unity", OpenSceneMode.Single);
            if (GameObject.Find("KeyboardTypingArm") != null)
                throw new InvalidOperationException("기존 기계팔을 덮어쓰지 않습니다.");
            _steel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Steel.mat");
            _dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Graphite.mat");
            _rubber = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Rubber.mat");
            _shell = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/TypingArmCeramicWhite.mat");
            if (_shell == null)
            {
                _shell = new Material(_dark) { name = "TypingArmCeramicWhite" };
                AssetDatabase.CreateAsset(_shell, "Assets/Resource/Materials/TypingArmCeramicWhite.mat");
            }
            _shell.SetColor("_BaseColor", new Color(.88f, .9f, .91f));
            _shell.SetFloat("_Metallic", .05f);
            _shell.SetFloat("_Smoothness", .37f);
            EditorUtility.SetDirty(_shell);
            _box = SaveMesh(BevelBox(), "BeveledBlock");
            _round = SaveMesh(Drum(), "ChamferedDrum");
            Transform root = Pivot("KeyboardTypingArm", null, Vector3.zero, Vector3.zero);
            Cylinder("RubberFoot", root, new Vector3(0, .005f, 0), .074f, .010f, _rubber);
            Cylinder("BaseLowerRim", root, new Vector3(0, .015f, 0), .078f, .016f, _dark);
            Cylinder("WhitePedestal", root, new Vector3(0, .035f, 0), .076f, .025f, _shell);
            Cylinder("TurretBearing", root, new Vector3(0, .051f, 0), .064f, .010f, _dark);
            Cylinder("TurretTop", root, new Vector3(0, .059f, 0), .058f, .009f, _dark);
            Transform shoulder = Pivot("ShoulderPivot", root, new Vector3(0, .087f, 0), new Vector3(0, 0, -25));
            Joint(shoulder, .032f, .069f);
            Link(shoulder, .16f, .043f);
            Transform elbow = Pivot("ElbowPivot", shoulder, new Vector3(0, .16f, 0), new Vector3(0, 0, 100));
            Joint(elbow, .028f, .065f);
            Link(elbow, .21f, .048f);
            Transform wrist = Pivot("WristPitchPivot", elbow, new Vector3(0, .21f, 0), new Vector3(0, 0, 105));
            Joint(wrist, .025f, .058f);
            Cylinder("WristMotor", wrist, new Vector3(0, .021f, 0), .022f, .031f, _dark);
            Cylinder("WhiteToolCollar", wrist, new Vector3(0, .04f, 0), .023f, .012f, _shell);
            Transform tool = Pivot("TypingToolPivot", wrist, new Vector3(0, .047f, 0), Vector3.zero);
            CreateRod(tool);
            Block("PedestalStatusPanel", root, new Vector3(0, .035f, -.076f), new Vector3(.026f, .011f, .002f), _dark);
            Material led = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/StatusLED.mat");
            Block("StatusLight", root, new Vector3(.008f, .036f, -.0775f), new Vector3(.003f, .003f, .001f), led);
            Consolidate(root);
            ConfigureShadows(root);
            int vertices = 0;
            int triangles = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PREFAB_PATH);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            GameObject arm = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject keyboard = Array.Find(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects(), item => item.name == "Keyboard_TKL_87");
            float scale = keyboard.transform.lossyScale.x / .01905f;
            arm.transform.localScale = Vector3.one * scale;
            arm.transform.position = keyboard.transform.TransformPoint(new Vector3(9.25f, 0, 2.8f)) + new Vector3(.27f, 0, -.005f) * scale;
            arm.transform.rotation = keyboard.transform.rotation;
            Camera camera = Camera.main;
            Transform monitor = GameObject.Find("Monitor").transform;
            camera.transform.position = monitor.TransformPoint(new Vector3(.32f, .64f, -1.30f));
            camera.transform.LookAt(monitor.TransformPoint(new Vector3(.09f, .19f, -.12f)));
            camera.fieldOfView = 38;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Preview(camera, "Scene");
            camera.transform.position = arm.transform.TransformPoint(new Vector3(.45f, .39f, -.73f));
            camera.transform.LookAt(arm.transform.TransformPoint(new Vector3(-.08f, .155f, 0)));
            camera.fieldOfView = 37;
            Preview(camera, "Detail");
            WriteReport(arm.transform);
            Debug.Log("TYPING_ARM_COMPLETE vertices=" + vertices + " triangles=" + triangles);
        }

        /// <summary>
        /// 기존 기계팔의 장갑 손을 금속 타건 막대로 복구한다.
        /// 저장된 프리팹과 Keyboard 씬을 사용해 막대 및 고무 팁과 그림자 설정, 미리보기를 갱신한다.
        /// </summary>
        public static void UpdateHand()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬 보호를 위해 Unity 배치 모드로 실행합니다.");
            _round = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_ROOT + "ChamferedDrum.asset");
            _steel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Steel.mat");
            _dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Graphite.mat");
            _rubber = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Rubber.mat");
            GameObject prefab = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            Transform tool = prefab.transform.Find("ShoulderPivot/ElbowPivot/WristPitchPivot/TypingToolPivot");
            if (tool == null)
                tool = prefab.transform.Find("BaseYawPivot/ShoulderPivot/ElbowPivot/WristPitchPivot/TypingToolPivot");
            while (tool.childCount > 0)
                UnityEngine.Object.DestroyImmediate(tool.GetChild(0).gameObject);
            CreateRod(tool);
            Consolidate(tool);
            ConfigureShadows(prefab.transform);
            KeyboardTypingArmController animation = prefab.GetComponent<KeyboardTypingArmController>();
            if (animation != null)
            {
                SerializedObject animationSettings = new SerializedObject(animation);
                animationSettings.FindProperty("_contactPoint").objectReferenceValue = tool.Find("KeyContactPoint");
                animationSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, PREFAB_PATH);
            PrefabUtility.UnloadPrefabContents(prefab);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/Keyboard.unity", OpenSceneMode.Single);
            GameObject arm = GameObject.Find("KeyboardTypingArm");
            Camera camera = Camera.main;
            Preview(camera, "Scene");
            camera.transform.position = arm.transform.TransformPoint(new Vector3(.45f, .39f, -.73f));
            camera.transform.LookAt(arm.transform.TransformPoint(new Vector3(-.08f, .155f, 0)));
            camera.fieldOfView = 37;
            Preview(camera, "Detail");
            WriteReport(arm.transform);
            Debug.Log("TYPING_ROD_RESTORE_COMPLETE");
        }

        /// <summary>
        /// 이전 크기의 금속 막대와 고무 팁을 생성한다.
        /// tool 피벗을 사용해 지름 5mm 막대와 소켓, 잠금 링을 배치하고 KeyContactPoint를 설정한다.
        /// </summary>
        private static void CreateRod(Transform tool)
        {
            Cylinder("ToolSocket", tool, new Vector3(0, .006f, 0), .011f, .013f, _dark);
            Cylinder("SocketLockRing", tool, new Vector3(0, .014f, 0), .008f, .004f, _steel);
            Cylinder("TypingRod", tool, new Vector3(0, .112f, 0), .0025f, .194f, _steel);
            Cylinder("SoftKeyContactTip", tool, new Vector3(0, .213f, 0), .0032f, .008f, _rubber);
            Pivot("KeyContactPoint", tool, new Vector3(0, .217f, 0), Vector3.zero);
        }
        /// <summary>
        /// 큰 실루엣과 타건 도구 위주로 그림자 설정을 적용한다.
        /// root의 재질과 렌더러를 사용해 케이블, 고무 밑면과 LED의 그림자 투사를 끈다.
        /// </summary>
        private static void ConfigureShadows(Transform root)
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                string materialName = renderer.sharedMaterial.name;
                bool indicator = materialName == "StatusLED";
                renderer.shadowCastingMode = materialName == "Rubber" || indicator ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = !indicator;
            }
        }

        /// <summary>
        /// 모델의 기하 구조와 그림자 설정을 보고서에 기록한다.
        /// root의 메시와 렌더러를 사용해 전체 수치와 그림자 투사 렌더러 수를 저장한다.
        /// </summary>
        private static void WriteReport(Transform root)
        {
            int vertices = 0;
            int triangles = 0;
            int casters = 0;
            int handVertices = 0;
            int handTriangles = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer renderer in renderers)
                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                    casters++;
            Transform hand = root.Find("ShoulderPivot/ElbowPivot/WristPitchPivot/TypingToolPivot");
            if (hand == null)
                hand = root.Find("BaseYawPivot/ShoulderPivot/ElbowPivot/WristPitchPivot/TypingToolPivot");
            foreach (MeshFilter filter in hand.GetComponentsInChildren<MeshFilter>())
            {
                handVertices += filter.sharedMesh.vertexCount;
                handTriangles += filter.sharedMesh.triangles.Length / 3;
            }
            Directory.CreateDirectory(".local/TypingArmPreview");
            File.WriteAllText(".local/TypingArmPreview/Report.txt", "Prefab: " + PREFAB_PATH + "\nVertices: " + vertices + "\nTriangles: " + triangles + "\nRenderers: " + renderers.Length + "\nShadow casters: " + casters + "\nTool vertices: " + handVertices + "\nTool triangles: " + handTriangles + "\nRuntime scripts: " + root.GetComponentsInChildren<MonoBehaviour>().Length + "\nTool: 5 mm metal typing rod with rubber tip\n");
        }
        /// <summary>
        /// 부모 아래에 자세 변경용 관절을 생성한다.
        /// name, parent, position, angles를 사용하여 설정된 Transform을 반환한다.
        /// </summary>
        private static Transform Pivot(string name, Transform parent, Vector3 position, Vector3 angles)
        {
            Transform pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;
            pivot.localRotation = Quaternion.Euler(angles);
            return pivot;
        }

        /// <summary>
        /// 지정 관절에 원형 기어 하우징과 체결 볼트를 추가한다.
        /// parent, radius, width를 사용하여 관절 양쪽의 부품을 변경한다.
        /// </summary>
        private static void Joint(Transform parent, float radius, float width)
        {
            Part("AxleHousing", parent, _round, Vector3.zero, new Vector3(radius * 2, width, radius * 2), _dark, new Vector3(90, 0, 0));
            for (int side = -1; side <= 1; side += 2)
            {
                Part("BearingCover", parent, _round, new Vector3(0, 0, side * width * .48f), new Vector3(radius * 1.8f, .009f, radius * 1.8f), _dark, new Vector3(90, 0, 0));
                Part("AxleCap", parent, _round, new Vector3(0, 0, side * (width * .5f + .006f)), new Vector3(radius * .75f, .004f, radius * .75f), _dark, new Vector3(90, 0, 0));
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3;
                    Part("CoverBolt", parent, _round, new Vector3(Mathf.Cos(a) * radius * .67f, Mathf.Sin(a) * radius * .67f, side * (width * .5f + .006f)), new Vector3(.004f, .004f, .004f), _dark, new Vector3(90, 0, 0));
                }
            }
        }

        /// <summary>
        /// 팔 부분에 곡면 흰 외장과 주름 케이블을 생성한다.
        /// parent, length, width를 사용하여 관절 사이의 커버와 케이블 구조를 변경한다.
        /// </summary>
        private static void Link(Transform parent, float length, float width)
        {
            Block("StructuralSpine", parent, new Vector3(0, length * .5f, 0), new Vector3(width * .65f, length * .88f, width * .65f), _dark);
            Mesh shellMesh = Housing(length * .82f, width, width * .35f);
            for (int side = -1; side <= 1; side += 2)
            {
                Part("ContouredWhiteShell", parent, shellMesh, new Vector3(0, length * .09f, side * width * .36f), Vector3.one, _shell, Vector3.zero);
                Block("ShellSeam", parent, new Vector3(width * .41f, length * .5f, side * width * .36f), new Vector3(.0015f, length * .58f, .0015f), _rubber);
                for (int i = 0; i < 2; i++)
                    Part("ShellFastener", parent, _round, new Vector3(0, length * (.20f + i * .59f), side * width * .55f), new Vector3(.0035f, .0016f, .0035f), _dark, new Vector3(90, 0, 0));
            }
            var points = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f;
                points.Add(new Vector3(width * (.58f + .20f * Mathf.Sin(t * Mathf.PI)), length * t, 0));
            }
            Part("CorrugatedCable", parent, Tube(points, .006f), Vector3.zero, Vector3.one, _rubber, Vector3.zero);
            for (int i = 1; i < 24; i++)
                Part("CableRib", parent, _round, points[i], new Vector3(.014f, .0022f, .014f), _rubber, new Vector3(0, 0, -Mathf.Cos(i / 24f * Mathf.PI) * 9));
            for (int i = 1; i <= 2; i++)
                Block("HarnessClip", parent, points[i * 8], new Vector3(.020f, .006f, .018f), _dark);
        }
        /// <summary>
        /// 베벨 블록 부품을 추가한다.
        /// name, parent, position, size, material을 사용하여 생성된 오브젝트를 반환한다.
        /// </summary>
        private static GameObject Block(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            return Part(name, parent, _box, position, size, material, Vector3.zero);
        }

        /// <summary>
        /// 원형 부품을 Y축 방향으로 추가한다.
        /// name, parent, position, radius, height, material로 생성 부품을 반환한다.
        /// </summary>
        private static GameObject Cylinder(string name, Transform parent, Vector3 position, float radius, float height, Material material)
        {
            return Part(name, parent, _round, position, new Vector3(radius * 2, height, radius * 2), material, Vector3.zero);
        }

        /// <summary>
        /// 메시와 재질을 가진 표시 부품을 생성한다.
        /// name, parent, mesh, position, size, material, angles로 부품 오브젝트를 반환한다.
        /// </summary>
        private static GameObject Part(string name, Transform parent, Mesh mesh, Vector3 position, Vector3 size, Material material, Vector3 angles)
        {
            Transform part = Pivot(name, parent, position, angles);
            part.localScale = size;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part.gameObject;
        }

        /// <summary>
        /// 링 좌표로 모서리가 둥근 단위 블록 메시를 생성한다.
        /// 고정 베벨 비율을 사용하여 법선이 계산된 메시를 반환한다.
        /// </summary>
        private static Mesh BevelBox()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int layer = 0; layer < 4; layer++)
            {
                float inset = layer == 0 || layer == 3 ? .06f : 0;
                float z = layer == 0 ? -.5f : layer == 1 ? -.44f : layer == 2 ? .44f : .5f;
                float extent = .5f - inset;
                float corner = .08f;
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    float x = quadrant == 0 || quadrant == 3 ? extent - corner : -extent + corner;
                    float y = quadrant < 2 ? extent - corner : -extent + corner;
                    for (int step = 0; step <= 2; step++)
                    {
                        float a = (quadrant * 90 + step * 45) * Mathf.Deg2Rad;
                        vertices.Add(new Vector3(x + Mathf.Cos(a) * corner, y + Mathf.Sin(a) * corner, z));
                    }
                }
            }
            for (int layer = 0; layer < 3; layer++)
                for (int i = 0; i < 12; i++)
                {
                    int a = layer * 12 + i;
                    int b = layer * 12 + (i + 1) % 12;
                    triangles.AddRange(new[] { a, b, a + 12, b, b + 12, a + 12 });
                }
            vertices.Add(new Vector3(0, 0, -.5f));
            vertices.Add(new Vector3(0, 0, .5f));
            for (int i = 0; i < 12; i++)
                triangles.AddRange(new[] { 48, (i + 1) % 12, i, 49, 36 + i, 36 + (i + 1) % 12 });
            return MeshData(vertices, triangles);
        }

        /// <summary>
        /// 32분할의 베벨 원통 메시를 생성한다.
        /// 단위 높이와 지름을 사용하여 원형 관절 공유 메시를 반환한다.
        /// </summary>
        private static Mesh Drum()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] heights = { -.5f, -.40f, .40f, .5f };
            for (int ring = 0; ring < 4; ring++)
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI / 16;
                    float radius = ring == 0 || ring == 3 ? .46f : .5f;
                    vertices.Add(new Vector3(Mathf.Cos(a) * radius, heights[ring], Mathf.Sin(a) * radius));
                }
            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < 32; i++)
                {
                    int a = ring * 32 + i;
                    int b = ring * 32 + (i + 1) % 32;
                    triangles.AddRange(new[] { a, a + 32, b, b, a + 32, b + 32 });
                }
            vertices.Add(new Vector3(0, -.5f, 0));
            vertices.Add(new Vector3(0, .5f, 0));
            for (int i = 0; i < 32; i++)
                triangles.AddRange(new[] { 128, i, (i + 1) % 32, 129, 96 + (i + 1) % 32, 96 + i });
            return MeshData(vertices, triangles);
        }

        /// <summary>
        /// 양 끝이 좁아지는 둥근 사각 단면 외장 메시를 생성한다.
        /// length, width, depth를 사용해 기계팔의 흰 커버용 곡면 메시를 반환한다.
        /// </summary>
        private static Mesh Housing(float length, float width, float depth)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] heights = { 0, .035f, .13f, .83f, .965f, 1 };
            float[] sizes = { .52f, .8f, 1, .96f, .76f, .5f };
            const int SEGMENTS = 20;
            for (int ring = 0; ring < heights.Length; ring++)
            {
                float xExtent = width * .5f * sizes[ring];
                float zExtent = depth * .5f * sizes[ring];
                float corner = zExtent * .65f;
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    float x = quadrant == 0 || quadrant == 3 ? xExtent - corner : -xExtent + corner;
                    float z = quadrant < 2 ? zExtent - corner : -zExtent + corner;
                    for (int step = 0; step <= 4; step++)
                    {
                        float a = (quadrant * 90 + step * 22.5f) * Mathf.Deg2Rad;
                        vertices.Add(new Vector3(x + Mathf.Cos(a) * corner, heights[ring] * length, z + Mathf.Sin(a) * corner));
                    }
                }
            }
            for (int ring = 0; ring < heights.Length - 1; ring++)
                for (int i = 0; i < SEGMENTS; i++)
                {
                    int a = ring * SEGMENTS + i;
                    int b = ring * SEGMENTS + (i + 1) % SEGMENTS;
                    triangles.AddRange(new[] { a, a + SEGMENTS, b, b, a + SEGMENTS, b + SEGMENTS });
                }
            int bottom = vertices.Count;
            vertices.Add(Vector3.zero);
            vertices.Add(new Vector3(0, length, 0));
            for (int i = 0; i < SEGMENTS; i++)
                triangles.AddRange(new[] { bottom, i, (i + 1) % SEGMENTS, bottom + 1, 100 + (i + 1) % SEGMENTS, 100 + i });
            return MeshData(vertices, triangles);
        }

        /// <summary>
        /// 경로를 따라 유연한 호스 메시를 생성한다.
        /// points와 radius를 사용하여 8각 단면 튜브 메시를 반환한다.
        /// </summary>
        private static Mesh Tube(List<Vector3> points, float radius)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int p = 0; p < points.Count; p++)
            {
                Vector3 tangent = points[Mathf.Min(p + 1, points.Count - 1)] - points[Mathf.Max(p - 1, 0)];
                Vector3 right = Vector3.Cross(tangent.normalized, Vector3.forward).normalized;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    vertices.Add(points[p] + radius * (right * Mathf.Cos(a) + Vector3.forward * Mathf.Sin(a)));
                    if (p > 0)
                    {
                        int current = p * 8 + i;
                        int next = p * 8 + (i + 1) % 8;
                        triangles.AddRange(new[] { current - 8, next, current, current - 8, next - 8, next });
                    }
                }
            }
            return MeshData(vertices, triangles);
        }

        /// <summary>
        /// 버텍스와 삼각형을 렌더 메시로 구성한다.
        /// vertices와 triangles를 사용하여 법선과 범위가 갱신된 메시를 반환한다.
        /// </summary>
        private static Mesh MeshData(List<Vector3> vertices, List<int> triangles)
        {
            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 모델 메시를 에셋으로 저장한다.
        /// mesh와 name을 사용하여 영구 참조 가능한 메시를 반환한다.
        /// </summary>
        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            mesh.name = "TypingArm_" + name;
            string path = MESH_ROOT + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                existing.vertices = mesh.vertices;
                existing.triangles = mesh.triangles;
                existing.normals = mesh.normals;
                existing.uv = mesh.uv;
                existing.name = mesh.name;
                existing.RecalculateBounds();
                existing.UploadMeshData(false);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// 관절별 동일 재질의 정적 부품을 합친다.
        /// parent 하위 구조를 사용하여 관절 피벗을 유지하며 렌더러 수를 줄인다.
        /// </summary>
        private static void Consolidate(Transform parent)
        {
            var groups = new Dictionary<Material, List<MeshFilter>>();
            var children = new List<Transform>();
            foreach (Transform child in parent)
                children.Add(child);
            foreach (Transform child in children)
            {
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter == null)
                {
                    Consolidate(child);
                    continue;
                }
                Material material = child.GetComponent<MeshRenderer>().sharedMaterial;
                if (!groups.ContainsKey(material))
                    groups.Add(material, new List<MeshFilter>());
                groups[material].Add(filter);
            }
            foreach (KeyValuePair<Material, List<MeshFilter>> group in groups)
            {
                var instances = new List<CombineInstance>();
                foreach (MeshFilter filter in group.Value)
                    instances.Add(new CombineInstance { mesh = filter.sharedMesh, transform = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                Mesh mesh = new Mesh();
                mesh.CombineMeshes(instances.ToArray(), true, true);
                string meshName = parent.name;
                for (Transform ancestor = parent.parent; ancestor != null; ancestor = ancestor.parent)
                    meshName = ancestor.name + "_" + meshName;
                mesh = SaveMesh(mesh, meshName + "_" + group.Key.name);
                Part(group.Key.name + "Structure", parent, mesh, Vector3.zero, Vector3.one, group.Key, Vector3.zero);
                foreach (MeshFilter filter in group.Value)
                    UnityEngine.Object.DestroyImmediate(filter.gameObject);
            }
        }

        /// <summary>
        /// 카메라 시점의 모델 미리보기를 저장한다.
        /// camera와 name을 사용해 PNG를 기록하고 렌더 타깃을 원상복구한다.
        /// </summary>
        private static void Preview(Camera camera, string name)
        {
            Directory.CreateDirectory(".local/TypingArmPreview");
            RenderTexture target = new RenderTexture(1600, 900, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            texture.Apply();
            File.WriteAllBytes(".local/TypingArmPreview/" + name + ".png", texture.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}








