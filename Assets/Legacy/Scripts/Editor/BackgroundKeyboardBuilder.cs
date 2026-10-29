using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

namespace KeyboardModeling.Editor
{
    public static class BackgroundKeyboardBuilder
    {
        private const string ROOT = "Assets/Resource/";
        private static readonly HashSet<string> _bodyParts = new HashSet<string>
        {
            "CNC_LowerCase", "Case_AssemblySeam", "Brass_SwitchPlate",
            "TopBezel_Front", "TopBezel_Rear", "TopBezel_Left", "TopBezel_Right",
            "Indicator_Inset", "StatusLens_0", "StatusLens_1"
        };

        /// <summary>
        /// 조작용 원본에서 외형만 사용하는 배경용 키보드를 생성한다.
        /// 기존 프리팹과 재질을 읽어 경량 메시, 깊이 검사 각인과 스크립트 없는 프리팹을 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Background Keyboard")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("별도 배치 프로젝트에서 실행합니다.");
            GameObject source = PrefabUtility.LoadPrefabContents(ROOT + "Prefabs/Keyboard_TKL_87.prefab");
            GameObject result = new GameObject("Keyboard_TKL_87_Background");
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var reduced = new Dictionary<Mesh, Mesh>();
            int originalVertices = 0;
            int originalRenderers = source.GetComponentsInChildren<MeshRenderer>(true).Length;
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                originalVertices += filter.sharedMesh.vertexCount;
            try
            {
                foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    string part = filter.name;
                    if (part != "PBT_SculptedShell" && !_bodyParts.Contains(part))
                        continue;
                    Mesh original = filter.sharedMesh;
                    if (!reduced.TryGetValue(original, out Mesh mesh))
                    {
                        mesh = ReduceLoft(original);
                        reduced.Add(original, mesh);
                    }
                    AddPart(groups, filter.GetComponent<MeshRenderer>().sharedMaterial, mesh,
                        source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                }
                TextMesh[] labels = source.GetComponentsInChildren<TextMesh>(true);
                Font font = labels[0].font;
                string characters = "";
                foreach (TextMesh label in labels)
                    characters += label.text;
                font.RequestCharactersInTexture(characters, 96, FontStyle.Normal);
                Material ink = MakeInkMaterial(font);
                foreach (TextMesh label in labels)
                    AddPart(groups, ink, MakeLabel(label, font),
                        source.transform.worldToLocalMatrix * label.transform.localToWorldMatrix);
                int vertices = 0;
                int triangles = 0;
                int groupIndex = 0;
                foreach (KeyValuePair<Material, List<CombineInstance>> group in groups)
                {
                    Mesh mesh = new Mesh { name = "KeyboardBackground_" + groupIndex, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(group.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    string path = ROOT + "Meshes/KeyboardBackground_" + groupIndex + ".asset";
                    mesh = (Mesh)SaveGeneratedAsset(mesh, path);
                    GameObject part = new GameObject(group.Key.name);
                    part.transform.SetParent(result.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer renderer = part.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = group.Key;
                    renderer.shadowCastingMode = group.Key == ink ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    vertices += mesh.vertexCount;
                    triangles += mesh.triangles.Length / 3;
                    groupIndex++;
                }
                result.transform.localScale = source.transform.localScale;
                PrefabUtility.SaveAsPrefabAsset(result, ROOT + "Prefabs/Keyboard_TKL_87_Background.prefab");
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(".local/BackgroundKeyboardPreview");
                File.WriteAllText(".local/BackgroundKeyboardPreview/Report.txt",
                    "Original renderers: " + originalRenderers + "\nOriginal mesh vertices: " + originalVertices +
                    "\nBackground renderers: " + groups.Count + "\nBackground vertices: " + vertices +
                    "\nBackground triangles: " + triangles + "\nScripts: 0\nColliders: 0\nUSB parts: 0\n");
                RenderPreview(result);
                Debug.Log("BACKGROUND_KEYBOARD_COMPLETE renderers=" + groups.Count + " vertices=" + vertices + " triangles=" + triangles);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
                UnityEngine.Object.DestroyImmediate(result);
            }
        }

        /// <summary>
        /// 재질별 결합 목록에 메시와 배치 행렬을 추가한다.
        /// groups, material, mesh, matrix를 사용하여 해당 재질의 결합 항목을 변경한다.
        /// </summary>
        private static void AddPart(Dictionary<Material, List<CombineInstance>> groups, Material material, Mesh mesh, Matrix4x4 matrix)
        {
            if (!groups.TryGetValue(material, out List<CombineInstance> parts))
            {
                parts = new List<CombineInstance>();
                groups.Add(material, parts);
            }
            parts.Add(new CombineInstance { mesh = mesh, transform = matrix });
        }

        /// <summary>
        /// 원본 로프트의 단면을 골라 모서리와 오목한 윗면을 유지하며 분할을 줄인다.
        /// source의 버텍스와 UV로 내부가 없는 경량 메시를 반환하고 원본은 변경하지 않는다.
        /// </summary>
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

        /// <summary>
        /// 생성 에셋을 신규 저장하거나 기존 참조를 유지하며 갱신한다.
        /// asset과 path를 사용하여 저장된 에셋을 반환하고 기존 GUID를 보존한다.
        /// </summary>
        private static UnityEngine.Object SaveGeneratedAsset(UnityEngine.Object asset, string path)
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        /// <summary>
        /// 폰트의 현재 아틀라스를 복사하여 깊이 검사를 사용하는 각인 재질을 저장한다.
        /// font의 텍스처를 읽어 배경용 재질을 반환하고 기존 폰트 재질은 변경하지 않는다.
        /// </summary>
        private static Material MakeInkMaterial(Font font)
        {
            Texture atlas = font.material.mainTexture;
            RenderTexture target = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(atlas, target);
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            texture.name = "KeyboardBackgroundLegendAtlas";
            texture = (Texture2D)SaveGeneratedAsset(texture, ROOT + "Materials/KeyboardBackgroundLegendAtlas.asset");
            Material material = new Material(Shader.Find("Keyboard/BackgroundLegend")) { name = "KeyboardBackgroundLegend" };
            material.mainTexture = texture;
            material = (Material)SaveGeneratedAsset(material, ROOT + "Materials/KeyboardBackgroundLegend.mat");
            return material;
        }

        /// <summary>
        /// TextMesh의 문자열과 폰트 문자 정보를 정적인 각인 메시로 변환한다.
        /// label의 크기, 색상, 줄간격과 font의 UV를 사용하여 가운데 정렬된 메시를 반환한다.
        /// </summary>
        private static Mesh MakeLabel(TextMesh label, Font font)
        {
            var points = new List<Vector3>();
            var uv = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            string[] lines = label.text.Split('\n');
            float scale = label.characterSize * 0.1f;
            float lineHeight = 96f * scale * label.lineSpacing;
            for (int line = 0; line < lines.Length; line++)
            {
                float width = 0f;
                foreach (char character in lines[line])
                {
                    font.GetCharacterInfo(character, out CharacterInfo info, 96);
                    width += info.advance * scale;
                }
                float x = -width * 0.5f;
                float y = (lines.Length - 1) * lineHeight * 0.5f - line * lineHeight - 96f * scale * 0.35f;
                foreach (char character in lines[line])
                {
                    font.GetCharacterInfo(character, out CharacterInfo info, 96);
                    int start = points.Count;
                    points.Add(new Vector3(x + info.minX * scale, y + info.minY * scale, 0f));
                    points.Add(new Vector3(x + info.minX * scale, y + info.maxY * scale, 0f));
                    points.Add(new Vector3(x + info.maxX * scale, y + info.maxY * scale, 0f));
                    points.Add(new Vector3(x + info.maxX * scale, y + info.minY * scale, 0f));
                    uv.AddRange(new[] { info.uvBottomLeft, info.uvTopLeft, info.uvTopRight, info.uvBottomRight });
                    colors.AddRange(new[] { label.color, label.color, label.color, label.color });
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                    x += info.advance * scale;
                }
            }
            Mesh mesh = new Mesh { name = "BakedLegend" };
            mesh.SetVertices(points);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 배경용 키보드를 임시 카메라로 렌더링한다.
        /// model의 스케일을 고려한 구도로 개인 미리보기 이미지를 저장한다.
        /// </summary>
        private static void RenderPreview(GameObject model)
        {
            RenderSettings.ambientLight = Color.gray;
            GameObject lightObject = new GameObject("PreviewLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Camera camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.position = model.transform.TransformPoint(new Vector3(17f, 19f, -17f));
            camera.transform.LookAt(model.transform.TransformPoint(new Vector3(9.25f, 0.8f, 2.8f)));
            camera.orthographic = true;
            camera.orthographicSize = 11.5f * model.transform.lossyScale.x;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            RenderTexture target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(".local/BackgroundKeyboardPreview/Keyboard.png", image.EncodeToPNG());
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "OcclusionPreviewWall";
            wall.transform.position = model.transform.TransformPoint(new Vector3(4f, 3f, 2.8f));
            wall.transform.localScale = Vector3.Scale(new Vector3(10f, 8f, 9f), model.transform.lossyScale);
            wall.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Graphite.mat");
            camera.Render();
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(".local/BackgroundKeyboardPreview/Occlusion.png", image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(wall);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
        }
    }
}
