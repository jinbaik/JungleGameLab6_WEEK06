#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

namespace KeyboardCustomizationTools.Editor
{
    public static class KeyboardCustomizerAssigner
    {
        private const string PREFAB_PATH = "Assets/Resource/Prefabs/SeperateKeyboard/Keyboard_Customize.prefab";
        private const string MESH_PATH = "Assets/Resource/Meshes/KeyboardCustomizeLED.asset";
        private const string MATERIAL_PATH = "Assets/Resource/Materials/KeyboardCustomizeLED.mat";
        private const float WIDE_KEY_THRESHOLD = 1.35f;
        private const float WIDE_KEY_LED_WIDTH = 1.8f;

        /// <summary>
        /// 대상 프리팹의 키캡 아래에서 각인을 제외한 외형 렌더러를 수집한다.
        /// 입력과 반환값 없이 키캡 재질 교체용 참조를 저장하며 Material 배열은 유지한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Setup Customize Test Keycaps")]
        public static void SetupKeycaps()
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PREFAB_PATH))
            {
                GameObject root = scope.prefabContentsRoot;
                var renderers = new List<MeshRenderer>();
                foreach (Transform key in root.transform.Find("Keys_87_SharedPrefabs"))
                {
                    if (!key.name.StartsWith("Key_"))
                    {
                        continue;
                    }

                    foreach (MeshRenderer renderer in key.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (renderer.name != "Printed_Legend")
                        {
                            renderers.Add(renderer);
                        }
                    }
                }

                var serialized = new SerializedObject(root.GetComponent<KeyboardCustomization>());
                SerializedProperty targets = serialized.FindProperty("_keycapRenderers");
                targets.arraySize = renderers.Count;
                for (int index = 0; index < renderers.Count; index++)
                {
                    targets.GetArrayElementAtIndex(index).objectReferenceValue = renderers[index];
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 대상 프리팹의 하부 케이스와 상단 테두리를 찾아 하우징 참조를 구성한다.
        /// 입력과 반환값 없이 렌더러 목록과 초기 Material을 프리팹에 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Setup Customize Test Housing")]
        public static void SetupHousing()
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PREFAB_PATH))
            {
                GameObject root = scope.prefabContentsRoot;
                var renderers = new List<MeshRenderer>();
                Material defaultMaterial = null;
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name == "CNC_LowerCase")
                    {
                        defaultMaterial = renderer.sharedMaterial;
                        renderers.Add(renderer);
                    }
                    else if (renderer.name.StartsWith("TopBezel_"))
                    {
                        renderers.Add(renderer);
                    }
                }

                KeyboardCustomization customizer = root.GetComponent<KeyboardCustomization>();
                var serialized = new SerializedObject(customizer);
                SerializedProperty targets = serialized.FindProperty("_caseRenderers");
                targets.arraySize = renderers.Count;
                for (int index = 0; index < renderers.Count; index++)
                {
                    targets.GetArrayElementAtIndex(index).objectReferenceValue = renderers[index];
                }

                SerializedProperty material = serialized.FindProperty("_caseMaterial");
                if (material.objectReferenceValue == null)
                {
                    material.objectReferenceValue = defaultMaterial;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                customizer.SetCaseMaterial((Material)material.objectReferenceValue);
                EditorUtility.SetDirty(customizer);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 대상 프리팹의 키캡 배치로 독립 LED 레이어와 제어 컴포넌트를 구성한다.
        /// 입력과 반환값 없이 Mesh, Material과 프리팹 참조를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Setup Customize Test LED")]
        public static void ExecuteSetup()
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PREFAB_PATH))
            {
                GameObject root = scope.prefabContentsRoot;
                Transform layer = root.transform.Find("LED_Layer");
                if (layer == null)
                {
                    layer = new GameObject("LED_Layer", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                    layer.SetParent(root.transform, false);
                }

                Mesh mesh = CreateLEDMesh(root.transform);
                Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
                if (existingMesh == null)
                {
                    AssetDatabase.CreateAsset(mesh, MESH_PATH);
                }
                else
                {
                    EditorUtility.CopySerialized(mesh, existingMesh);
                    Object.DestroyImmediate(mesh);
                    mesh = existingMesh;
                }

                Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
                if (material == null)
                {
                    material = new Material(Shader.Find("Keyboard/LED Layer"));
                    AssetDatabase.CreateAsset(material, MATERIAL_PATH);
                }

                layer.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = layer.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                KeyboardLED led = layer.GetComponent<KeyboardLED>();
                if (led == null) led = layer.gameObject.AddComponent<KeyboardLED>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                KeyboardCustomization customizer = root.GetComponent<KeyboardCustomization>();
                if (customizer == null)
                {
                    customizer = root.AddComponent<KeyboardCustomization>();
                }

                var serialized = new SerializedObject(customizer);
                serialized.FindProperty("_ledRenderer").objectReferenceValue = renderer;
                serialized.FindProperty("_ledLayer").objectReferenceValue = led;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                customizer.SetLEDEnabled(serialized.FindProperty("_ledEnabled").boolValue);
                EditorUtility.SetDirty(customizer);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Keyboard_Customize_Test LED 레이어 설정 완료");
        }

        /// <summary>
        /// 현재 커스터마이즈 프리팹의 축 배치로 LED Mesh 애셋만 다시 생성한다.
        /// 입력과 반환값 없이 Mesh 내용을 저장하며 프리팹과 다른 커스터마이즈 설정은 유지한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Rebuild Customize LED Mesh")]
        public static void RebuildLEDMesh()
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            GameObject root = stage != null && stage.assetPath == PREFAB_PATH
                ? stage.prefabContentsRoot
                : AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Mesh generated = CreateLEDMesh(root.transform);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
            existing.Clear();
            existing.vertices = generated.vertices;
            existing.uv = generated.uv;
            existing.triangles = generated.triangles;
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// root 아래 각 스위치 하우징 경계로 축 주변에만 발광 면을 만든다.
        /// 키캡 길이와 무관한 축별 UV와 정점을 구성한 새 Mesh를 반환한다.
        /// </summary>
        private static Mesh CreateLEDMesh(Transform root)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            Transform keys = root.Find("Keys_87_SharedPrefabs");
            foreach (Transform mechanism in keys)
            {
                if (!mechanism.name.StartsWith("Switch_"))
                {
                    continue;
                }

                Bounds switchBounds = GetSwitchBounds(root, mechanism.Find("Switch_BottomHousing"));
                Bounds topBounds = GetSwitchBounds(root, mechanism.Find("Switch_TopHousing"));
                float y = topBounds.max.y + 0.01f;
                float left = switchBounds.min.x - 0.18f;
                float right = switchBounds.max.x + 0.18f;
                if (IsWideKeySwitch(root, keys, mechanism))
                {
                    left = switchBounds.center.x - WIDE_KEY_LED_WIDTH * 0.5f;
                    right = switchBounds.center.x + WIDE_KEY_LED_WIDTH * 0.5f;
                }
                float back = switchBounds.min.z - 0.18f;
                float front = switchBounds.max.z + 0.18f;
                float holeLeft = Mathf.Clamp(switchBounds.min.x - 0.02f, left, right);
                float holeRight = Mathf.Clamp(switchBounds.max.x + 0.02f, left, right);
                float holeBack = Mathf.Clamp(switchBounds.min.z - 0.02f, back, front);
                float holeFront = Mathf.Clamp(switchBounds.max.z + 0.02f, back, front);
                var footprint = Rect.MinMaxRect(left, back, right, front);

                // 스위치가 드러나도 발광 면이 하우징을 덮지 않도록 중앙을 비운다.
                AddLEDQuad(Rect.MinMaxRect(left, back, holeLeft, front), footprint, y, vertices, triangles, uvs);
                AddLEDQuad(Rect.MinMaxRect(holeRight, back, right, front), footprint, y, vertices, triangles, uvs);
                AddLEDQuad(Rect.MinMaxRect(holeLeft, back, holeRight, holeBack), footprint, y, vertices, triangles, uvs);
                AddLEDQuad(Rect.MinMaxRect(holeLeft, holeFront, holeRight, front), footprint, y, vertices, triangles, uvs);
            }

            var mesh = new Mesh { name = "KeyboardCustomizeLED" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// root 좌표계와 keys의 키캡 배치를 사용해 mechanism에 가로로 긴 키캡이 대응하는지 확인한다.
        /// 키캡 가로 폭이 기준을 넘는 경우 true를 반환하며 실제 발광 폭은 고정값을 사용한다.
        /// </summary>
        private static bool IsWideKeySwitch(Transform root, Transform keys, Transform mechanism)
        {
            foreach (Transform key in keys)
            {
                if (!key.name.StartsWith("Key_")) continue;
                Vector3 offset = key.localPosition - mechanism.localPosition;
                if (Mathf.Abs(offset.x) > 0.001f || Mathf.Abs(offset.z) > 0.001f) continue;
                Bounds bounds = GetSwitchBounds(root, key.Find("PBT_SculptedShell"));
                return bounds.size.x > WIDE_KEY_THRESHOLD;
            }

            return false;
        }

        /// <summary>
        /// root 좌표계와 switchRoot 아래의 Mesh를 사용해 스위치 전체 경계를 계산한다.
        /// 하우징과 스템을 포함하는 로컬 Bounds를 반환한다.
        /// </summary>
        private static Bounds GetSwitchBounds(Transform root, Transform switchRoot)
        {
            var result = new Bounds(root.InverseTransformPoint(switchRoot.position), Vector3.zero);
            foreach (MeshFilter filter in switchRoot.GetComponentsInChildren<MeshFilter>())
            {
                Bounds bounds = filter.sharedMesh.bounds;
                Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = new Vector3(
                        (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                        (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                        (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                    result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + offset));
                }
            }

            return result;
        }

        /// <summary>
        /// area와 높이 y로 사각 면을 추가하고 footprint를 기준으로 키별 UV를 유지한다.
        /// 반환값 없이 vertices, triangles, uvs 목록에 면 데이터를 저장한다.
        /// </summary>
        private static void AddLEDQuad(Rect area, Rect footprint, float y,
            List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }

            int start = vertices.Count;
            vertices.AddRange(new[] { new Vector3(area.xMin, y, area.yMin), new Vector3(area.xMin, y, area.yMax),
                new Vector3(area.xMax, y, area.yMax), new Vector3(area.xMax, y, area.yMin) });
            float uMin = Mathf.InverseLerp(footprint.xMin, footprint.xMax, area.xMin);
            float uMax = Mathf.InverseLerp(footprint.xMin, footprint.xMax, area.xMax);
            float vMin = Mathf.InverseLerp(footprint.yMin, footprint.yMax, area.yMin);
            float vMax = Mathf.InverseLerp(footprint.yMin, footprint.yMax, area.yMax);
            uvs.AddRange(new[] { new Vector2(uMin, vMin), new Vector2(uMin, vMax),
                new Vector2(uMax, vMax), new Vector2(uMax, vMin) });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
    }
}
#endif


