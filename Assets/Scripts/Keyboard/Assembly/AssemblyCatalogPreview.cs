using UnityEngine;
using UnityEngine.Rendering;

namespace KeyboardModeling.Assembly
{
    public static class AssemblyCatalogPreview
    {
        private const int PREVIEW_LAYER = 31;
        private const int TEXTURE_SIZE = 128;

        /// <summary>
        /// mesh와 materials를 격리된 미리보기에서 렌더링하여 아이콘 텍스처를 반환한다.
        /// 원본 에셋과 씬 배치는 변경하지 않으며 호출자가 반환 텍스처를 해제한다.
        /// </summary>
        public static Texture2D Render(Mesh mesh, Material[] materials)
        {
            GameObject stage = new GameObject("AssemblyCatalogPreviewStage");
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.transform.position = new Vector3(10000f, 10000f, 10000f);
            GameObject model = new GameObject("PreviewModel", typeof(MeshFilter), typeof(MeshRenderer));
            model.transform.SetParent(stage.transform, false);
            model.layer = PREVIEW_LAYER;
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = model.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            KeyboardToonSurface.ApplyBounds(renderer);

            GameObject lightObject = new GameObject("PreviewLight", typeof(Light));
            lightObject.transform.SetParent(stage.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PREVIEW_LAYER;

            GameObject cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << PREVIEW_LAYER;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            Bounds bounds = renderer.bounds;
            float radius = bounds.extents.magnitude;
            float distance = Mathf.Max(3f, radius * 3f);
            camera.transform.position = bounds.center + new Vector3(0.65f, 0.82f, -1f).normalized * distance;
            camera.transform.LookAt(bounds.center, Vector3.up);
            camera.farClipPlane = distance + radius * 2f + 1f;
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.8f;

            RenderTexture target = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 16, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                Texture2D texture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, TEXTURE_SIZE, TEXTURE_SIZE), 0, 0);
                texture.Apply(false, true);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                stage.SetActive(false);
                Object.Destroy(stage);
            }
        }
    }
}
