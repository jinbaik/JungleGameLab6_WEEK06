using System.Globalization;

using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class WalletHudToastView : MonoBehaviour
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
        private long _amount;
        private bool _isDoubleReward;
        private float _elapsed;
        private float _duration;
        private float _riseDistance;
        private float _fadeStart;
        public float DisplayHeight => _rect.rect.height;

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
        /// amount를 표시할 HUD Toast의 이전 상태를 초기화하고 새 연출을 시작한다.
        /// 연출 설정과 isDoubleReward를 사용하여 최종 지급액과 강조 상태 및 수명을 저장한다.
        /// </summary>
        public void Begin(long amount, float duration, float riseDistance, float fadeStart, Color color, bool isDoubleReward = false)
        {
            ResetForPool();
            _amount = amount;
            _isDoubleReward = isDoubleReward;
            _duration = duration;
            _riseDistance = riseDistance;
            _fadeStart = fadeStart;
            _label.color = color;
            RefreshLabel();
            _canvasGroup.alpha = 1f;
        }

        /// <summary>
        /// 짧은 시간 안에 추가 획득한 amount를 현재 알림에 합산한다.
        /// 합산된 획득량으로 텍스트를 갱신하고 수명과 알파를 새로 시작한다.
        /// </summary>
        public void Merge(long amount)
        {
            _amount = checked(_amount + amount);
            _elapsed = 0f;
            _canvasGroup.alpha = 1f;
            RefreshLabel();
        }

        /// <summary>
        /// deltaTime으로 HUD 알림의 상승과 페이드를 진행한다.
        /// slot과 rowSpacing으로 우측 상단 스택에 배치하고 수명 종료 여부를 반환한다.
        /// </summary>
        public bool Tick(float deltaTime, int slot, float rowSpacing)
        {
            _elapsed += deltaTime;
            if (_elapsed >= _duration) return false;
            float progress = Mathf.Clamp01(_elapsed / _duration);
            _rect.anchoredPosition = new Vector2(-16f, -slot * rowSpacing + _riseDistance * progress);
            _canvasGroup.alpha = 1f - Mathf.Clamp01((progress - _fadeStart) / (1f - _fadeStart));
            return true;
        }

        /// <summary>
        /// 풀 반환 또는 대여 전에 숫자와 이전 연출을 제거한다.
        /// 획득량, 두 배 여부와 연출 상태를 초기화하고 글자 크기 및 표시 영역을 기본값으로 복원한다.
        /// </summary>
        public void ResetForPool()
        {
            _amount = 0;
            _isDoubleReward = false;
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

        /// <summary>
        /// 저장된 _amount와 두 배 여부를 사용하여 HUD 알림의 문구와 크기를 갱신한다.
        /// 두 배 보상에는 2배 문구와 기본 글자 크기보다 6 큰 폰트를 적용하고 텍스트 영역을 확보한다.
        /// </summary>
        private void RefreshLabel()
        {
            _label.fontSize = _baseFontSize + (_isDoubleReward ? DOUBLE_REWARD_FONT_INCREASE : 0);
            _label.text = (_isDoubleReward ? "+2배! " : "+") + _amount.ToString("N0", CultureInfo.InvariantCulture);
            _rect.sizeDelta = new Vector2(
                Mathf.Max(_baseSize.x, Mathf.Ceil(_label.preferredWidth) + TEXT_PADDING),
                Mathf.Max(_baseSize.y, Mathf.Ceil(_label.preferredHeight) + TEXT_PADDING));
        }
    }
}
