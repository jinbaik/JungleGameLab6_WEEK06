using System.Globalization;

using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ToastView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Text _label;
        private RectTransform _rect;
        private CanvasGroup _canvasGroup;

        [Header("Runtime State")]
        private ToastRequest _request;
        private Vector2 _offset;
        private float _elapsed;
        private float _duration;
        private float _riseDistance;
        private float _fadeStart;
        private bool _running;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _label.raycastTarget = false;
        }

        /// <summary>
        /// 이전 연출 상태를 초기화하고 request를 표시할 상승 및 페이드 설정을 저장한다.
        /// 수명, 상승 거리, 페이드 시작 비율, 오프셋, 색과 크기를 사용하여 새 Toast를 시작한다.
        /// </summary>
        public void Begin(ToastRequest request, float duration, float riseDistance, float fadeStart, Vector2 offset, Color color, float scale)
        {
            ResetForPool();
            _request = request;
            _duration = duration;
            _riseDistance = riseDistance;
            _fadeStart = fadeStart;
            _offset = offset;
            _label.text = (request.Kind == ToastKind.Reward ? "+" : "-") + request.Amount.ToString(CultureInfo.InvariantCulture);
            _label.color = color;
            _rect.localScale = Vector3.one * scale;
            _running = true;
        }

        /// <summary>
        /// deltaTime으로 수명과 상승을 진행하고 저장한 월드 위치를 camera로 매 프레임 다시 투영한다.
        /// root의 로컬 좌표로 배치하며 화면 밖 피해와 카메라 뒤 알림은 숨기고 수명 종료 여부를 반환한다.
        /// </summary>
        public bool Tick(float deltaTime, Camera camera, RectTransform root)
        {
            if (!_running) return false;

            _elapsed += deltaTime;
            if (_elapsed >= _duration) return false;

            float progress = Mathf.Clamp01(_elapsed / _duration);
            Vector3 screenPosition = camera.WorldToScreenPoint(_request.WorldPosition);
            if (screenPosition.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPosition, null, out Vector2 localPosition))
            {
                _canvasGroup.alpha = 0f;
                return true;
            }

            _rect.anchoredPosition = localPosition + _offset + Vector2.up * (_riseDistance * progress);
            float alpha = 1f - Mathf.Clamp01((progress - _fadeStart) / (1f - _fadeStart));
            if (_request.Kind == ToastKind.Damage)
            {
                Vector2 displayedScreen = RectTransformUtility.WorldToScreenPoint(null, _rect.position);
                if (!camera.pixelRect.Contains(displayedScreen)) alpha = 0f;
            }

            _canvasGroup.alpha = alpha;
            return true;
        }

        /// <summary>
        /// 풀 반환 또는 재사용 전에 이전 표시와 애니메이션 상태를 제거한다.
        /// 저장 데이터, 위치, 회전, 크기, 색, 알파, 경과 시간 및 진행 플래그를 초기화한다.
        /// </summary>
        public void ResetForPool()
        {
            _running = false;
            _request = default;
            _offset = Vector2.zero;
            _elapsed = 0f;
            _duration = 0f;
            _riseDistance = 0f;
            _fadeStart = 0f;
            _rect.anchoredPosition3D = Vector3.zero;
            _rect.localRotation = Quaternion.identity;
            _rect.localScale = Vector3.one;
            _label.text = string.Empty;
            _label.color = Color.white;
            _canvasGroup.alpha = 0f;
        }
    }
}
