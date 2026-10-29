using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Player
{
    public sealed class WorkshopDragPreviewRenderer : MonoBehaviour
    {
        private const int PREVIEW_LAYER = 30;

        [Header("Runtime Preview")]
        private readonly List<(GameObject Object, int Layer)> _layers = new List<(GameObject, int)>();
        private Camera _source;
        private Camera _overlay;
        private UniversalAdditionalCameraData _sourceData;
        private bool _sourceIncludedLayer;

        /// <summary>
        /// source 카메라 뒤에 드래그 파츠 전용 오버레이 카메라를 연결한다.
        /// 드래그가 시작되기 전에는 카메라를 비활성화하며 원본 카메라 설정은 유지한다. 반환값은 없다.
        /// </summary>
        public void Initialize(Camera source)
        {
            _source = source;
            _sourceData = source.GetUniversalAdditionalCameraData();
            _overlay = new GameObject("WorkshopDragPreviewCamera", typeof(Camera)).GetComponent<Camera>();
            _overlay.CopyFrom(source);
            _overlay.cullingMask = 1 << PREVIEW_LAYER;
            _overlay.enabled = false;
            UniversalAdditionalCameraData data = _overlay.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            _sourceData.cameraStack.Add(_overlay);
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        /// <summary>
        /// part의 원래 레이어를 보관하고 드래그 전용 카메라에서만 그리도록 전환한다.
        /// 메시와 재질 및 크기는 변경하지 않고 키보드보다 앞에 표시한다. 반환값은 없다.
        /// </summary>
        public void Show(Transform part)
        {
            Show(new[] { part });
        }

        /// <summary>parts의 기존 실물들을 하나의 드래그 카메라에서 표시한다. 원래 레이어만 보관하며 계층·재질·크기는 변경하지 않는다.</summary>
        public void Show(IReadOnlyList<Transform> parts)
        {
            Hide();
            _sourceIncludedLayer = (_source.cullingMask & (1 << PREVIEW_LAYER)) != 0;
            _source.cullingMask &= ~(1 << PREVIEW_LAYER);
            foreach (Transform part in parts)
            foreach (Transform child in part.GetComponentsInChildren<Transform>(true))
            {
                _layers.Add((child.gameObject, child.gameObject.layer));
                child.gameObject.layer = PREVIEW_LAYER;
            }
            _overlay.enabled = true;
        }

        /// <summary>
        /// 드래그 파츠의 레이어와 원본 카메라의 해당 레이어 표시 여부를 복원한다.
        /// 전용 카메라를 끄며 실제 설치와 물리 상태는 변경하지 않는다. 반환값은 없다.
        /// </summary>
        public void Hide()
        {
            if (_layers.Count == 0) return;
            foreach (var entry in _layers)
                if (entry.Object != null) entry.Object.layer = entry.Layer;
            _layers.Clear();
            if (_source != null && _sourceIncludedLayer) _source.cullingMask |= 1 << PREVIEW_LAYER;
            if (_overlay != null) _overlay.enabled = false;
        }

        /// <summary>
        /// camera가 원본 카메라일 때 최신 시야각과 자세를 전용 카메라에 반영한다.
        /// context는 렌더 파이프라인에서 전달되며 파츠 좌표와 배율은 변경하지 않는다. 반환값은 없다.
        /// </summary>
        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_source == null || _overlay == null || camera != _source || !_overlay.enabled) return;
            _overlay.CopyFrom(_source);
            _overlay.cullingMask = 1 << PREVIEW_LAYER;
            _overlay.transform.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            Hide();
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        void OnDestroy()
        {
            Hide();
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            if (_sourceData != null) _sourceData.cameraStack.Remove(_overlay);
            if (_overlay != null) Destroy(_overlay.gameObject);
        }
    }
}
