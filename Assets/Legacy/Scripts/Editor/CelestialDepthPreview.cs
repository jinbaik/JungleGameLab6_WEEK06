using System;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class CelestialDepthPreview
    {
        private const string ROOT = "Assets/Resource/";
        private const string PREVIEW = ".local/CelestialDepthPreview";

        /// <summary>
        /// 기존 천공 재질의 내부 깊이와 발광을 조정하고 두 시점의 키캡 모습을 렌더링한다.
        /// 기존 공용 재질만 저장하며 키캡과 키보드 및 텍스트 에셋은 변경하지 않는다.
        /// </summary>
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("별도 복사 프로젝트의 배치 모드에서 실행합니다.");
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory(PREVIEW);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeycapCelestial.mat");
            material.SetFloat("_ParallaxDepth", 0.65f);
            material.SetFloat("_DepthContrast", 1.1f);
            material.SetFloat("_FlowSpeed", 0.035f);
            material.SetFloat("_Glow", 0.85f);
            material.SetColor("_StarColor", new Color(0.82f, 0.92f, 1f));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            string[] sizes = { "1u", "2_25u", "6_25u" };
            Vector3[] positions = { new Vector3(-2f, 0f, 0.9f), new Vector3(0.5f, 0f, 0.9f), new Vector3(0f, 0f, -1.05f) };
            for (int index = 0; index < sizes.Length; index++)
            {
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keycaps/Celestial/Keycap_" + sizes[index] + "_Celestial.prefab"));
                cap.transform.localScale = Vector3.one;
                cap.transform.position = positions[index];
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.gray;
            Light light = new GameObject("PreviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.8f;
            light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Camera camera = new GameObject("SpaceDepthCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.01f, 0.015f, 0.025f);
            camera.transform.position = new Vector3(3.3f, 5f, -6f);
            camera.transform.LookAt(new Vector3(0f, 0.25f, 0f));
            SaveImage(camera, "AngleA.png");
            camera.transform.position = new Vector3(-3.3f, 5f, -6f);
            camera.transform.LookAt(new Vector3(0f, 0.25f, 0f));
            SaveImage(camera, "AngleB.png");
            if (ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("천공 깊이 셰이더 컴파일 오류가 있습니다.");
            Debug.Log("CELESTIAL_DEPTH_PREVIEW_COMPLETE");
        }

        /// <summary>
        /// camera의 현재 시점을 name PNG로 저장한다.
        /// 렌더 텍스처를 읽어 미리보기 파일을 생성하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void SaveImage(Camera camera, string name)
        {
            RenderTexture target = new RenderTexture(1400, 900, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1400, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW + "/" + name, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
