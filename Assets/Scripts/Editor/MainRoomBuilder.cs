using System;
using System.IO;
using System.Text;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class MainRoomBuilder
    {
        private const string ROOT = "Assets/Resource/";
        private const string PREVIEW = ".local/RoomPreview/";
        private static GameObject _block;

        /// <summary>
        /// Main 씬의 기존 기기 배치를 기준으로 책상과 방 프리팹을 생성한다.
        /// 기존 재질과 큐브 메시를 재사용하고 씬, 통계 및 렌더 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Room/Build Main Room")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("편집 중인 씬 보호를 위해 숨김 배치 실행을 사용하세요.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            if (GameObject.Find("MainRoom") != null)
                throw new InvalidOperationException("MainRoom이 이미 있어 자동으로 덮어쓰지 않습니다.");
            Directory.CreateDirectory(PREVIEW);
            Material white = MaterialAsset("RoomIvory", new Color(0.88f, 0.865f, 0.82f), 0.23f);
            Material plaster = MaterialAsset("RoomPlaster", new Color(0.76f, 0.745f, 0.70f), 0.08f);
            Material oak = MaterialAsset("RoomOak", new Color(0.47f, 0.30f, 0.16f), 0.24f);
            Material black = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Graphite.mat");
            Material brass = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/BrushedBrass.mat");
            Material glass = MaterialAsset("RoomWindowGlass", new Color(0.67f, 0.83f, 0.88f, 0.12f), 0.82f);
            glass.SetFloat("_Surface", 1f);
            glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite", 0f);
            glass.SetOverrideTag("RenderType", "Transparent");
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glass.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(glass);

            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "RoomBlock";
            _block = PrefabUtility.SaveAsPrefabAsset(block, ROOT + "Prefabs/RoomBlock.prefab");
            UnityEngine.Object.DestroyImmediate(block);

            GameObject desk = new GameObject("DeskFrame");
            Part("WhiteTop", desk.transform, new Vector3(0f, 2.09f, 0f), new Vector3(5f, 0.12f, 3.5f), white);
            GameObject side = new GameObject("DeskSideFrame");
            Part("FrontLeg", side.transform, new Vector3(0f, 1.02f, -1.5f), new Vector3(0.11f, 2.04f, 0.11f), black);
            Part("RearLeg", side.transform, new Vector3(0f, 1.02f, 1.5f), new Vector3(0.11f, 2.04f, 0.11f), black);
            Part("LowerRail", side.transform, new Vector3(0f, 0.055f, 0f), new Vector3(0.11f, 0.11f, 3.11f), black);
            Part("UpperRail", side.transform, new Vector3(0f, 1.98f, 0f), new Vector3(0.11f, 0.11f, 3.11f), black);
            GameObject sidePrefab = Save(side, "DeskSideFrame");
            Place(sidePrefab, desk.transform, new Vector3(-2.3f, 0f, 0f));
            Place(sidePrefab, desk.transform, new Vector3(2.3f, 0f, 0f));
            Part("RearSupport", desk.transform, new Vector3(0f, 1.95f, 1.5f), new Vector3(4.6f, 0.11f, 0.11f), black);
            GameObject deskPrefab = Save(desk, "RoomDesk");

            GameObject sash = new GameObject("WindowSash");
            Part("HingeStile", sash.transform, new Vector3(0.045f, 0f, 0f), new Vector3(0.09f, 2.6f, 0.10f), white);
            Part("LatchStile", sash.transform, new Vector3(1.35f, 0f, 0f), new Vector3(0.09f, 2.6f, 0.10f), white);
            for (int i = 0; i < 4; i++)
            {
                float y = -1.255f + i * (2.51f / 3f);
                Part("Crossbar_" + i, sash.transform, new Vector3(0.7f, y, 0f), new Vector3(1.31f, 0.07f, 0.10f), white);
            }
            GameObject pane = Part("Glass", sash.transform, new Vector3(0.7f, 0f, 0f), new Vector3(1.22f, 2.46f, 0.015f), glass);
            pane.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Part("HandleMount", sash.transform, new Vector3(1.345f, -0.65f, -0.085f), new Vector3(0.065f, 0.12f, 0.07f), brass);
            Part("Handle", sash.transform, new Vector3(1.27f, -0.65f, -0.13f), new Vector3(0.20f, 0.045f, 0.045f), brass);
            GameObject sashPrefab = Save(sash, "RoomWindowSash");
            GameObject window = new GameObject("CasementWindow");
            Part("LeftJamb", window.transform, new Vector3(-1.48f, 0f, 0f), new Vector3(0.16f, 2.94f, 0.26f), white);
            Part("RightJamb", window.transform, new Vector3(1.48f, 0f, 0f), new Vector3(0.16f, 2.94f, 0.26f), white);
            Part("Header", window.transform, new Vector3(0f, 1.40f, 0f), new Vector3(2.8f, 0.14f, 0.26f), white);
            Part("Sill", window.transform, new Vector3(0f, -1.40f, -0.10f), new Vector3(3.16f, 0.14f, 0.52f), white);
            Part("CenterMullion", window.transform, Vector3.zero, new Vector3(0.09f, 2.66f, 0.16f), white);
            Place(sashPrefab, window.transform, new Vector3(-1.40f, 0f, 0f));
            Transform openSash = Place(sashPrefab, window.transform, new Vector3(1.40f, 0f, 0f));
            openSash.localRotation = Quaternion.Euler(0f, -38f, 0f);
            openSash.localScale = new Vector3(-1f, 1f, 1f);
            GameObject windowPrefab = Save(window, "RoomCasementWindow");

            GameObject room = new GameObject("MainRoom");
            Transform shell = new GameObject("RoomShell").transform;
            shell.SetParent(room.transform, false);
            Part("Floor", shell, new Vector3(0f, -2.61f, 0.75f), new Vector3(8.4f, 0.22f, 9.9f), oak);
            Part("Ceiling", shell, new Vector3(0f, 4.61f, 0.75f), new Vector3(8.4f, 0.22f, 9.9f), white);
            Part("LeftWall", shell, new Vector3(-4.1f, 1f, 0.75f), new Vector3(0.2f, 7f, 9.5f), plaster);
            Part("RightWall", shell, new Vector3(4.1f, 1f, 0.75f), new Vector3(0.2f, 7f, 9.5f), plaster);
            Part("RearWall", shell, new Vector3(0f, 1f, -4.1f), new Vector3(8.4f, 7f, 0.2f), plaster);
            // 앞벽은 창문 개구부를 제외한 네 구간으로 구성한다.
            Part("FrontWallBelowWindow", shell, new Vector3(0f, -0.925f, 5.6f), new Vector3(8.4f, 3.15f, 0.2f), plaster);
            Part("FrontWallAboveWindow", shell, new Vector3(0f, 4.075f, 5.6f), new Vector3(8.4f, 0.85f, 0.2f), plaster);
            Part("FrontWallLeft", shell, new Vector3(-3.775f, 2.15f, 5.6f), new Vector3(0.85f, 3f, 0.2f), plaster);
            Part("FrontWallRight", shell, new Vector3(1.975f, 2.15f, 5.6f), new Vector3(4.45f, 3f, 0.2f), plaster);
            Place(windowPrefab, shell, new Vector3(-1.8f, 2.15f, 5.6f));
            Place(deskPrefab, room.transform, new Vector3(0f, -2.5f, 1f));
            GameObject roomPrefab = Save(room, "MainRoom");
            room = (GameObject)PrefabUtility.InstantiatePrefab(roomPrefab);

            // 교체된 임시 모델만 비활성화하고 기존 참조와 입력 트리거는 유지한다.
            foreach (string name in new[] { "Table", "Table (1)", "Table (2)", "Floor" })
            {
                Transform old = GameObject.Find("Setting").transform.Find(name);
                if (old != null)
                    old.gameObject.SetActive(false);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            StringBuilder report = new StringBuilder();
            foreach (GameObject asset in new[] { deskPrefab, sashPrefab, windowPrefab, roomPrefab })
            {
                int vertices = 0;
                int triangles = 0;
                foreach (MeshFilter filter in asset.GetComponentsInChildren<MeshFilter>())
                {
                    vertices += filter.sharedMesh.vertexCount;
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }
                report.AppendLine(asset.name + ": vertices=" + vertices + ", triangles=" + triangles);
            }
            report.AppendLine("New mesh assets: 0; all parts share Unity built-in Cube (24 vertices, 12 triangles).");
            File.WriteAllText(PREVIEW + "BuildReport.txt", report.ToString());
            Preview(new Vector3(3.55f, 2.8f, -3.5f), new Vector3(-0.3f, 0.55f, 1.8f), "RoomOverview");
            Preview(new Vector3(0f, 1.25f, -1f), new Vector3(0f, 0.98f, 1.5f), "MainCamera");
            Debug.Log("MAIN_ROOM_BUILD_COMPLETE\n" + report);
        }

        /// <summary>
        /// 이름과 색상, 매끄러움으로 방 전용 URP 재질을 생성한다.
        /// 같은 경로의 재질이 있으면 변경하지 않고 해당 재질을 반환한다.
        /// </summary>
        private static Material MaterialAsset(string name, Color color, float smoothness)
        {
            string path = ROOT + "Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// 공용 블록 프리팹을 parent 아래에 배치한다.
        /// name, position, scale, material을 적용하고 박스 콜라이더가 있는 부품을 반환한다.
        /// </summary>
        private static GameObject Part(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            Transform part = Place(_block, parent, position);
            part.name = name;
            part.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.gameObject;
        }

        /// <summary>
        /// prefab을 parent의 자식으로 인스턴스화한다.
        /// 로컬 position을 설정하고 생성한 Transform을 반환한다.
        /// </summary>
        private static Transform Place(GameObject prefab, Transform parent, Vector3 position)
        {
            Transform instance = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).transform;
            instance.localPosition = position;
            return instance;
        }

        /// <summary>
        /// root를 name 경로의 재사용 프리팹으로 저장한다.
        /// 임시 씬 오브젝트를 제거하고 저장된 프리팹을 반환한다.
        /// </summary>
        private static GameObject Save(GameObject root, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ROOT + "Prefabs/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// position에서 target을 바라보는 임시 카메라로 PNG를 렌더한다.
        /// name으로 로컬 파일을 저장하며 씬 카메라나 저장된 조명을 변경하지 않는다.
        /// </summary>
        private static void Preview(Vector3 position, Vector3 target, string name)
        {
            GameObject owner = new GameObject("RoomPreviewCamera");
            Camera camera = owner.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(target);
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.05f;
            RenderTexture texture = new RenderTexture(1280, 900, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            Texture2D pixels = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(PREVIEW + name + ".png", pixels.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
