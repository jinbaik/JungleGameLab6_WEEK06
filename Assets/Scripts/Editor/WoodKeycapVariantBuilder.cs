using System;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class WoodKeycapVariantBuilder
    {
        private const string KEYCAP_ROOT = "Assets/Resource/Prefabs/Keycaps";
        private const string VARIANT_ROOT = KEYCAP_ROOT + "/Wood";
        private const string MATERIAL_ROOT = "Assets/Resource/Materials";
        private const string MATERIAL_PATH = MATERIAL_ROOT + "/KeycapWoodWalnut.mat";
        private const string TEXTURE_PATH = MATERIAL_ROOT + "/KeycapWoodGrain.asset";

        /// <summary>
        /// 기존 가로형 키캡에서 원목 재질을 사용하는 프리팹 버리언트를 만든다.
        /// 기존 8종 프리팹과 공용 PBT 재질을 사용하여 외부 재질만 덮어쓰고 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Wood Keycap Variants")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("편집 중인 씬을 보호하기 위해 별도 배치 프로젝트에서 실행합니다.");
            Directory.CreateDirectory(VARIANT_ROOT);
            Directory.CreateDirectory(".local/WoodKeycapPreview");
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material material = MakeWoodMaterial();
            string[] sizes = { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u" };
            foreach (string size in sizes)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(KEYCAP_ROOT + "/Keycap_" + size + ".prefab");
                string path = VARIANT_ROOT + "/Keycap_" + size + "_Wood.prefab";
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    MeshRenderer shell = instance.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                    shell.sharedMaterial = material;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(shell);
                    GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, path);
                    if (PrefabUtility.GetPrefabAssetType(variant) != PrefabAssetType.Variant)
                        throw new InvalidOperationException("Prefab Variant가 아닌 에셋이 생성됐습니다: " + path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            AssetDatabase.SaveAssets();
            RenderPreview();
            File.WriteAllText(".local/WoodKeycapPreview/BuildReport.txt", "Prefab variants: 8\nParent: existing horizontal keycaps\nOverride: exterior shell material only\nNew mesh assets: 0\nAdditional vertices: 0\nTexture: 256 x 256 RGB24\n");
            Debug.Log("WOOD_KEYCAP_VARIANTS_COMPLETE variants=8 newMeshes=0 addedVertices=0");
        }

        /// <summary>
        /// 기존 PBT 재질 설정을 기반으로 원목 재질을 저장한다.
        /// 기존 셰이더와 공용 미세 노멀을 재사용하고 새 나뭇결 텍스처를 적용한 재질을 반환한다.
        /// </summary>
        private static Material MakeWoodMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            if (material == null)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_ROOT + "/Porcelain_PBT.mat");
                material = new Material(source) { name = "KeycapWoodWalnut" };
                AssetDatabase.CreateAsset(material, MATERIAL_PATH);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_BaseMap", MakeWoodTexture());
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.34f);
            material.SetFloat("_BumpScale", 0.07f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 작은 반복 나뭇결 색상 텍스처를 생성한다.
        /// 고정 해상도와 반복 가능한 파형 및 난수로 따뜻한 월넛 색상의 Texture2D 에셋을 반환한다.
        /// </summary>
        private static Texture2D MakeWoodTexture()
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TEXTURE_PATH);
            Texture2D texture = existing;
            if (texture == null)
                texture = new Texture2D(256, 256, TextureFormat.RGB24, true)
                {
                    name = "KeycapWoodGrain",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
            var random = new System.Random(87);
            var pixels = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
            {
                float v = y / 256f;
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 256f;
                    float warp = 1.15f * Mathf.Sin(u * Mathf.PI * 2f) + 0.28f * Mathf.Sin(u * Mathf.PI * 6f);
                    float growth = v * 28f + 1.2f * Mathf.Sin(v * Mathf.PI * 4f) + 0.38f * Mathf.Sin(v * Mathf.PI * 14f + u * Mathf.PI * 2f);
                    float grain = 0.5f + 0.5f * Mathf.Sin(growth * Mathf.PI * 2f + warp);
                    float broad = 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 10f + warp * 0.35f);
                    float variation = Mathf.PerlinNoise(4f + Mathf.Cos(u * Mathf.PI * 2f) * 2f + Mathf.Sin(v * Mathf.PI * 2f) * 0.6f, 9f + Mathf.Sin(u * Mathf.PI * 2f) * 2f + Mathf.Cos(v * Mathf.PI * 2f) * 2f);
                    float streak = Mathf.Pow(grain, 7f) * (0.2f + variation * 0.8f);
                    float noise = (float)random.NextDouble();
                    float shade = Mathf.Clamp01(0.48f + broad * 0.23f - streak * 0.22f + (noise - 0.5f) * 0.045f);
                    pixels[y * 256 + x] = Color.Lerp(new Color(0.21f, 0.093f, 0.037f), new Color(0.59f, 0.365f, 0.18f), shade);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            if (existing == null)
                AssetDatabase.CreateAsset(texture, TEXTURE_PATH);
            else
                EditorUtility.SetDirty(texture);
            return texture;
        }

        /// <summary>
        /// 원목 테마의 일반 키캡과 긴 키캡을 한 장으로 렌더링한다.
        /// 기존 버리언트 3종과 임시 카메라 및 조명을 사용해 개인 미리보기를 저장한다.
        /// </summary>
        private static void RenderPreview()
        {
            string[] sizes = { "1u", "2_25u", "6_25u" };
            Vector3[] positions = { new Vector3(-2.3f, 0f, 0.8f), new Vector3(0.3f, 0f, 0.8f), new Vector3(0f, 0f, -1.05f) };
            for (int index = 0; index < sizes.Length; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VARIANT_ROOT + "/Keycap_" + sizes[index] + "_Wood.prefab");
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.localScale = Vector3.one;
                instance.transform.position = positions[index];
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
            GameObject lightObject = new GameObject("Preview Light");
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            GameObject cameraObject = new GameObject("Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(3.3f, 5f, -5.8f);
            camera.transform.LookAt(new Vector3(0f, 0.2f, -0.2f));
            camera.orthographic = true;
            camera.orthographicSize = 2.15f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.065f, 0.08f, 0.09f);
            RenderTexture target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(".local/WoodKeycapPreview/WoodKeycaps.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
