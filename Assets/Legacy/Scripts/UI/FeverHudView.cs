using UnityEngine;
using UnityEngine.UI;

using KeyboardModeling;

namespace Game.UI
{
    public sealed class FeverHudView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private FeverController _controller;
        private bool _wasFeverActive;

        [Header("Vertical Gauge")]
        [SerializeField] private Slider _gauge;
        [SerializeField] private Text _status;
        [SerializeField] private Text _multiplier;
        [SerializeField] private Color _chargeColor = new Color(0.25f, 0.95f, 0.72f);
        [SerializeField] private Color _activeColor = new Color(1f, 0.56f, 0.16f);

        [Header("Active Fever HUD")]
        [SerializeField] private GameObject _activeFeverRoot;
        [SerializeField] private Slider _remainingGauge;
        [SerializeField] private Text _activeMultiplier;

        [Header("Damage Pulse")]
        [SerializeField, Min(0.01f)] private float _minimumDamageScale = 1f;
        [SerializeField, Min(0.01f)] private float _maximumDamageScale = 1.35f;
        [SerializeField, Min(0.01f)] private float _damagePulseDuration = 0.5f;
        private float _pulseElapsed;

        void OnEnable()
        {
            // 범용 HUD 프리팹은 피버가 없는 씬에서도 사용한다.
            if (_controller == null)
                _controller = FindFirstObjectByType<FeverController>();
            if (_controller == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _controller.StateChanged += RefreshView;
            RefreshView();
        }

        void OnDisable()
        {
            if (_controller != null)
                _controller.StateChanged -= RefreshView;
            _activeFeverRoot.SetActive(false);
            _activeMultiplier.rectTransform.localScale = Vector3.one;
            _wasFeverActive = false;
            _pulseElapsed = 0f;
        }

        void Update()
        {
            RefreshGauges();
            UpdateDamagePulse();
        }

        void OnValidate()
        {
            _minimumDamageScale = Mathf.Max(0.01f, _minimumDamageScale);
            _maximumDamageScale = Mathf.Max(_minimumDamageScale, _maximumDamageScale);
            _damagePulseDuration = Mathf.Max(0.01f, _damagePulseDuration);
        }

        /// <summary>
        /// 연결된 피버 상태로 세로 게이지, 배율 숫자 및 상단 활성 HUD를 갱신한다.
        /// 충전량과 확정된 남은 시간·배율을 사용하며 피버 전환 시 강조 연출을 초기화한다.
        /// </summary>
        private void RefreshView()
        {
            bool active = _controller.IsFeverActive;
            Color accent = active ? _activeColor : _chargeColor;
            RefreshGauges();
            _gauge.targetGraphic.color = accent;
            _status.text = "FEVER";
            _status.color = accent;
            _multiplier.text = $"{_controller.DamageMultiplier:0.0#}";
            _activeMultiplier.text = $"DAMAGE X {_controller.DamageMultiplier:0.0#}";

            if (_wasFeverActive != active)
            {
                _wasFeverActive = active;
                _pulseElapsed = 0f;
                _activeMultiplier.rectTransform.localScale = active ? Vector3.one * _minimumDamageScale : Vector3.one;
            }

            _activeFeverRoot.SetActive(active);
        }

        /// <summary>
        /// 연결된 피버의 충전량 또는 남은 시간 비율로 두 Slider를 갱신한다.
        /// 현재 활성 상태와 Remaining·Duration을 사용하며 표시값만 변경하고 이벤트는 발생시키지 않는다.
        /// </summary>
        private void RefreshGauges()
        {
            bool active = _controller.IsFeverActive;
            float remainingRatio = Mathf.Clamp01(_controller.Remaining / _controller.Duration);
            _gauge.SetValueWithoutNotify(active ? remainingRatio : _controller.Charge);
            _remainingGauge.SetValueWithoutNotify(active ? remainingRatio : 0f);
        }

        /// <summary>
        /// 피버가 진행 중이면 설정된 주기와 크기 범위로 상단 배율 텍스트를 확대·축소한다.
        /// Time.deltaTime으로 연출 시간을 누적하며 피버 일시정지 중에는 현재 크기를 유지한다.
        /// </summary>
        private void UpdateDamagePulse()
        {
            if (!_controller.IsFeverActive || _controller.IsSuspended)
                return;

            _pulseElapsed = Mathf.Repeat(_pulseElapsed + Time.deltaTime, _damagePulseDuration);
            float phase = _pulseElapsed / _damagePulseDuration;
            float weight = (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;
            float scale = Mathf.Lerp(_minimumDamageScale, _maximumDamageScale, weight);
            _activeMultiplier.rectTransform.localScale = Vector3.one * scale;
        }
    }
}
