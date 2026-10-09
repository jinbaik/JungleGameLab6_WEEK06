using System.Globalization;

using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class WalletHudToastView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Text _label;
        private RectTransform _rect;
        private CanvasGroup _canvasGroup;

        [Header("Runtime State")]
        private long _amount;
        private float _elapsed;
        private float _duration;
        private float _riseDistance;
        private float _fadeStart;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _label.raycastTarget = false;
        }

        /// <summary>
        /// amount를 표시할 HUD Toast의 이전 상태를 초기화하고 새 연출을 시작한다.
        /// duration, riseDistance, fadeStart와 color를 적용하여 획득량과 수명을 저장한다.
        /// </summary>
        public void Begin(long amount, float duration, float riseDistance, float fadeStart, Color color)
        {
            ResetForPool();
            _amount = amount;
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
        /// 획득량, 시간, 위치, 회전, 크기, 텍스트, 색과 알파를 초기화한다.
        /// </summary>
        public void ResetForPool()
        {
            _amount = 0;
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

        /// <summary>
        /// 저장된 획득량을 더하기 기호와 천 단위 구분자가 있는 텍스트로 표시한다.
        /// 현재 _amount를 사용하여 HUD 알림의 숫자를 갱신한다.
        /// </summary>
        private void RefreshLabel()
        {
            _label.text = "+" + _amount.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
