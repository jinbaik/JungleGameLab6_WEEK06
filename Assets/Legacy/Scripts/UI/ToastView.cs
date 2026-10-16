using System.Globalization;

using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ToastView : MonoBehaviour
    {
        private const int DOUBLE_REWARD_FONT_INCREASE = 6;
        private const float TEXT_PADDING = 8f;

        [Header("References")]
        [SerializeField] private Text _label;
        private RectTransform _rect;
        private CanvasGroup _canvasGroup;

        [Header("Runtime State")]
        private int _baseFontSize;
        private Vector2 _baseSize;
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
            _baseFontSize = _label.fontSize;
            _baseSize = _rect.sizeDelta;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _label.raycastTarget = false;
            _label.resizeTextForBestFit = false;
        }

        /// <summary>
        /// 이전 연출 상태를 초기화하고 request를 표시할 상승 및 페이드 설정을 저장한다.
        /// 연출 설정과 request의 두 배 판정을 사용하며 보상 강조 시 기본 글자 크기에 6을 더하고 문구를 확장한다.
        /// </summary>
        public void Begin(ToastRequest request, float duration, float riseDistance, float fadeStart, Vector2 offset, Color color, float scale)
        {
            ResetForPool();
            _request = request;
            _duration = duration;
            _riseDistance = riseDistance;
            _fadeStart = fadeStart;
            _offset = offset;
            _label.fontSize = _baseFontSize + (request.IsDoubleReward ? DOUBLE_REWARD_FONT_INCREASE : 0);
            string prefix = request.Kind == ToastKind.Reward ? request.IsDoubleReward ? "+2배! " : "+" : "-";
            _label.text = prefix + request.Amount.ToString(CultureInfo.InvariantCulture);
            if (request.Kind == ToastKind.Reward)
            {
                _rect.sizeDelta = new Vector2(
                    Mathf.Max(_baseSize.x, Mathf.Ceil(_label.preferredWidth) + TEXT_PADDING),
                    Mathf.Max(_baseSize.y, Mathf.Ceil(_label.preferredHeight) + TEXT_PADDING));
            }
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
        /// 저장 데이터와 연출 상태를 초기화하며 글자 크기와 표시 영역도 생성 시 기본값으로 복원한다.
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
            _rect.sizeDelta = _baseSize;
            _label.fontSize = _baseFontSize;
            _label.text = string.Empty;
            _label.color = Color.white;
            _canvasGroup.alpha = 0f;
        }
    }
}
