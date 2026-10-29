using UnityEngine;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyLEDSettings : MonoBehaviour, IKeyboardLEDSettings
    {
        [Header("LED Layer")]
        [SerializeField] private KeyboardLED _ledLayer;

        [Header("LED Settings")]
        [SerializeField] private bool _ledEnabled;
        [SerializeField, ColorUsage(false, true)] private Color _ledColor = new Color(0f, 0.8f, 1f, 1f);
        [SerializeField, Range(0f, 10f)] private float _ledIntensity = 3f;
        [SerializeField] private LEDPattern _ledPattern;
        [SerializeField, Range(0f, 10f)] private float _ledSpeed = 1f;

        public bool LEDEnabled => _ledEnabled;
        public Color LEDColor => _ledColor;
        public float LEDIntensity => _ledIntensity;
        public LEDPattern CurrentLEDPattern => _ledPattern;
        public float LEDSpeed => _ledSpeed;

        void OnEnable()
        {
            ApplyLED();
        }

        /// <summary>enabled를 전원 상태로 저장하고 연결된 LED 레이어를 갱신한다. 반환값은 없다.</summary>
        public void SetLEDEnabled(bool enabled)
        {
            _ledEnabled = enabled;
            ApplyLED();
        }

        /// <summary>color를 조명 색상으로 저장하고 연결된 LED 레이어를 갱신한다. 반환값은 없다.</summary>
        public void SetLEDColor(Color color)
        {
            _ledColor = color;
            ApplyLED();
        }

        /// <summary>intensity를 지원 범위의 밝기로 저장하고 LED 레이어를 갱신한다. 반환값은 없다.</summary>
        public void SetLEDIntensity(float intensity)
        {
            _ledIntensity = Mathf.Clamp(intensity, 0f, KeyboardCustomization.MaxLEDIntensity);
            ApplyLED();
        }

        /// <summary>pattern을 조명 패턴으로 저장하고 LED 레이어를 갱신한다. 반환값은 없다.</summary>
        public void SetLEDPattern(LEDPattern pattern)
        {
            _ledPattern = pattern;
            ApplyLED();
        }

        /// <summary>speed를 지원 범위의 패턴 속도로 저장하고 LED 레이어를 갱신한다. 반환값은 없다.</summary>
        public void SetLEDSpeed(float speed)
        {
            _ledSpeed = Mathf.Clamp(speed, 0f, KeyboardCustomization.MaxLEDSpeed);
            ApplyLED();
        }

        /// <summary>현재 전원과 색상 및 패턴 설정을 읽어 LED 레이어에 적용한다. 입력값과 반환값은 없다.</summary>
        private void ApplyLED()
        {
            _ledLayer.ApplySettings(_ledEnabled, _ledPattern, _ledColor, _ledIntensity, _ledSpeed);
        }
    }
}
