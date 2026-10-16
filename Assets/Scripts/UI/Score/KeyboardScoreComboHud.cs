using System.Globalization;

using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class KeyboardScoreComboHud : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private KeyboardScoreCombo _scoreCombo;
        [SerializeField] private Text _scoreText;
        [SerializeField] private string _scoreLabel = "score";
        [SerializeField] private Text _comboText;
        [SerializeField] private Text _multiplierText;
        [SerializeField] private Image _timerFill;

        [Header("Gauge Layout")]
        private RectTransform _timerRect;
        private Vector2 _timerAnchorMin;
        private Vector2 _timerAnchorMax;
        private Vector2 _timerOffsetMin;
        private Vector2 _timerOffsetMax;

        void Awake()
        {
            _timerRect = _timerFill.rectTransform;
            _timerAnchorMin = _timerRect.anchorMin;
            _timerAnchorMax = _timerRect.anchorMax;
            _timerOffsetMin = _timerRect.offsetMin;
            _timerOffsetMax = _timerRect.offsetMax;
            _timerFill.type = Image.Type.Simple;
        }

        void OnEnable()
        {
            _scoreCombo.StateChanged += RefreshState;
            _scoreCombo.TimerChanged += RefreshTimer;
            RefreshState();
            RefreshTimer(_scoreCombo.TimeRatio);
        }

        void OnDisable()
        {
            _scoreCombo.StateChanged -= RefreshState;
            _scoreCombo.TimerChanged -= RefreshTimer;
        }

        /// <summary>
        /// 연결된 스코어와 콤보 상태를 읽어 점수, 콤보, 배율 문구를 갱신한다.
        /// 현재 단계 색상을 콤보와 배율 및 유지시간 게이지에 적용한다.
        /// </summary>
        private void RefreshState()
        {
            _scoreText.text = $"{_scoreLabel}: {_scoreCombo.Score}";
            _comboText.text = $"{_scoreCombo.Combo}콤보";
            _multiplierText.text = $"x{_scoreCombo.Multiplier.ToString("0.#", CultureInfo.InvariantCulture)}배";
            _comboText.color = _scoreCombo.StageColor;
            _multiplierText.color = _scoreCombo.StageColor;
            _timerFill.color = _scoreCombo.StageColor;
        }

        /// <summary>
        /// 남은 유지시간 비율 ratio로 게이지의 양 끝을 기존 중앙을 향해 대칭적으로 이동한다.
        /// 원래 앵커와 여백을 기준으로 너비만 변경하며 중앙 위치와 높이는 유지한다.
        /// </summary>
        private void RefreshTimer(float ratio)
        {
            float amount = Mathf.Clamp01(ratio);
            float anchorCenter = (_timerAnchorMin.x + _timerAnchorMax.x) * 0.5f;
            float offsetCenter = (_timerOffsetMin.x + _timerOffsetMax.x) * 0.5f;
            _timerRect.anchorMin = new Vector2(
                Mathf.Lerp(anchorCenter, _timerAnchorMin.x, amount), _timerAnchorMin.y);
            _timerRect.anchorMax = new Vector2(
                Mathf.Lerp(anchorCenter, _timerAnchorMax.x, amount), _timerAnchorMax.y);
            _timerRect.offsetMin = new Vector2(
                Mathf.Lerp(offsetCenter, _timerOffsetMin.x, amount), _timerOffsetMin.y);
            _timerRect.offsetMax = new Vector2(
                Mathf.Lerp(offsetCenter, _timerOffsetMax.x, amount), _timerOffsetMax.y);
        }
    }
}
