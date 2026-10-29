using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Tournament
{
    public static class TournamentPartThumbnailRenderer
    {
        private const int PREVIEW_LAYER = 31;
        private const int TEXTURE_SIZE = 128;

        /// <summary>part의 실제 키보드 메시와 재질을 keyboard에서 찾아 썸네일 텍스처로 반환한다. 호출자가 반환 텍스처를 해제한다.</summary>
        public static Texture2D Render(KeyboardPartDefinition part, KeyboardCustomization keyboard)
        {
            Mesh mesh = GetMesh(part, keyboard);
            Material[] materials = GetMaterials(part);
            if (mesh == null || materials.Length == 0 || materials[0] == null) return null;

            GameObject stage = new GameObject("TournamentPartPreviewStage");
            stage.hideFlags = HideFlags.HideAndDontSave;
            GameObject model = new GameObject("PartModel", typeof(MeshFilter), typeof(MeshRenderer));
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
            lightObject.layer = PREVIEW_LAYER;
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PREVIEW_LAYER;

            GameObject cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);
            cameraObject.layer = PREVIEW_LAYER;
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
            camera.farClipPlane = Mathf.Max(10f, distance + radius * 2f);
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
                texture.name = part.Id + "Thumbnail";
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
                Object.Destroy(stage);
            }
        }

        /// <summary>part의 종류에 맞는 실제 키보드 모델 메시를 반환한다. keyboard에 해당 모델이 없으면 null을 반환한다.</summary>
        private static Mesh GetMesh(KeyboardPartDefinition part, KeyboardCustomization keyboard)
        {
            if (part is KeycapDefinition keycap && keycap.ShellMesh != null) return keycap.ShellMesh;
            if (part is SwitchDefinition)
            {
                KeyboardSwitch keyboardSwitch = keyboard.GetComponentInChildren<KeyboardSwitch>(true);
                if (keyboardSwitch == null) return null;
                MeshFilter[] filters = keyboardSwitch.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter filter in filters)
                    if (filter.name.Contains("TopHousing")) return filter.sharedMesh;
                return filters.Length > 0 ? filters[0].sharedMesh : null;
            }
            if (part is PlateDefinition) return keyboard.PlateRenderer != null
                ? keyboard.PlateRenderer.GetComponent<MeshFilter>().sharedMesh : null;
            if (part is HousingDefinition)
                foreach (MeshRenderer renderer in keyboard.CaseRenderers)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) return filter.sharedMesh;
                }
            KeycapHealth keycapHealth = keyboard.GetComponentInChildren<KeycapHealth>(true);
            MeshFilter keycapFilter = keycapHealth != null ? keycapHealth.GetComponentInChildren<MeshFilter>(true) : null;
            return keycapFilter != null ? keycapFilter.sharedMesh : null;
        }

        /// <summary>part의 모델에 지정된 재질 목록을 반환한다. 부품 정의에 재질이 없으면 빈 배열을 반환한다.</summary>
        private static Material[] GetMaterials(KeyboardPartDefinition part)
        {
            if (part is KeycapDefinition keycap)
                return keycap.ShellMesh != null && keycap.ModelMaterials.Length > 0
                    ? keycap.ModelMaterials : new[] { keycap.Material };
            if (part is SwitchDefinition axis) return new[] { axis.Data.TopHousingMaterial };
            if (part is PlateDefinition plate) return new[] { plate.Data.Material };
            if (part is HousingDefinition housing) return new[] { housing.Material };
            return new Material[0];
        }
    }
}
