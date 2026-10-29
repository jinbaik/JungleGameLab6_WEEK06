using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace KeyboardModeling.Assembly
{
    public static class AssemblyCatalogPreview
    {
        private const int PREVIEW_LAYER = 31;
        private const int TEXTURE_SIZE = 128;

        /// <summary>parts와 shellMaterial로 카탈로그 이미지를 반환한다. pcbBoardOnly는 기판만 재질을 대체하고 widePreview는 판류의 여백과 비율을 맞춘다. 원본은 유지하며 메시가 없으면 null을 반환한다.</summary>
        public static Texture2D RenderParts(IReadOnlyList<Transform> parts, Material shellMaterial = null, bool pcbBoardOnly = false, bool widePreview = false)
        {
            return Render(null, null, parts, shellMaterial, pcbBoardOnly, widePreview);
        }

        /// <summary>
        /// mesh와 materials를 격리된 미리보기에서 렌더링하여 아이콘 텍스처를 반환한다.
        /// parts의 메시와 pcbBoardOnly의 재질 범위를 사용하며 widePreview는 투영 경계에 맞춰 판류 이미지를 확대한다. 원본 에셋과 씬 배치는 유지하며 호출자가 반환 텍스처를 해제한다.
        /// </summary>
        public static Texture2D Render(Mesh mesh, Material[] materials, IReadOnlyList<Transform> parts = null, Material shellMaterial = null, bool pcbBoardOnly = false, bool widePreview = false)
        {
            GameObject stage = new GameObject("AssemblyCatalogPreviewStage");
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.transform.position = new Vector3(10000f, 10000f, 10000f);
            var renderers = new List<MeshRenderer>();
            if (parts == null) renderers.Add(CreatePreviewModel(stage.transform, mesh, materials, null, null));
            else
                foreach (Transform part in parts)
                foreach (MeshRenderer source in part.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter filter = source.GetComponent<MeshFilter>();
                    if (!source.enabled || filter == null || filter.sharedMesh == null) continue;
                    Material[] previewMaterials = source.sharedMaterials;
                    if (shellMaterial != null && (pcbBoardOnly ? source.transform == part.Find("PCB_Board") : source.transform == part))
                        for (int index = 0; index < previewMaterials.Length; index++) previewMaterials[index] = shellMaterial;
                    renderers.Add(CreatePreviewModel(stage.transform, filter.sharedMesh, previewMaterials, source.transform, parts[0]));
                }
            if (renderers.Count == 0)
            {
                Object.Destroy(stage);
                return null;
            }

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
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Count; index++) bounds.Encapsulate(renderers[index].bounds);
            float radius = bounds.extents.magnitude;
            float distance = Mathf.Max(3f, radius * 3f);
            camera.transform.position = bounds.center + new Vector3(0.65f, 0.82f, -1f).normalized * distance;
            camera.transform.LookAt(bounds.center, Vector3.up);
            camera.farClipPlane = distance + radius * 2f + 1f;
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.8f;

            int textureWidth = TEXTURE_SIZE;
            if (widePreview)
            {
                Vector2 projected = Vector2.zero;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    projected.x = Mathf.Max(projected.x, Mathf.Abs(Vector3.Dot(offset, camera.transform.right)));
                    projected.y = Mathf.Max(projected.y, Mathf.Abs(Vector3.Dot(offset, camera.transform.up)));
                }
                textureWidth = Mathf.RoundToInt(TEXTURE_SIZE * Mathf.Clamp(projected.x / Mathf.Max(0.001f, projected.y), 1f, 2f));
                camera.aspect = (float)textureWidth / TEXTURE_SIZE;
                camera.orthographicSize = Mathf.Max(0.001f, Mathf.Max(projected.y, projected.x / camera.aspect) * 1.08f);
            }

            RenderTexture target = new RenderTexture(textureWidth, TEXTURE_SIZE, 16, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                Texture2D texture = new Texture2D(textureWidth, TEXTURE_SIZE, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, textureWidth, TEXTURE_SIZE), 0, 0);
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
        /// <summary>parent의 격리된 모델에 mesh와 materials를 직접 연결한다. source와 origin이 있으면 상대 자세를 복사하며 원본 메시의 CPU 읽기 설정은 변경하지 않는다.</summary>
        private static MeshRenderer CreatePreviewModel(Transform parent, Mesh mesh, Material[] materials, Transform source, Transform origin)
        {
            GameObject model = new GameObject("PreviewModel", typeof(MeshFilter), typeof(MeshRenderer));
            model.transform.SetParent(parent, false);
            model.layer = PREVIEW_LAYER;
            if (source != null)
            {
                model.transform.localPosition = origin.InverseTransformPoint(source.position);
                model.transform.localRotation = Quaternion.Inverse(origin.rotation) * source.rotation;
                model.transform.localScale = (origin.worldToLocalMatrix * source.localToWorldMatrix).lossyScale;
            }
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = model.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            KeyboardToonSurface.ApplyBounds(renderer);
            return renderer;
        }
    }
}
