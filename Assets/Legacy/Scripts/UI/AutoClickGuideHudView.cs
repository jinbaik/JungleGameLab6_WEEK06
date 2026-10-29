using UnityEngine;
using UnityEngine.UI;

using Game.Session;
using Game.Upgrades;

namespace Game.UI
{
    public sealed class AutoClickGuideHudView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private Text _guideText;

        [Header("Guide")]
        [SerializeField, TextArea(2, 3)] private string _message = "이제 창문 옆에 당신을 도와줄 기계 팔이 생겼습니다!\n기계팔 앞에 키보드를 배치해 주세요.";
        [SerializeField, Min(0.1f)] private float _duration = 5f;

        [Header("Runtime State")]
        private UpgradeService _upgrades;
        private float _remainingTime;

        void Awake()
        {
            _guideText.resizeTextForBestFit = false;
            _guideText.raycastTarget = false;
            _guideText.text = _message;
            HideGuide();
        }

        void OnEnable()
        {
            if (_upgrades != null)
                _upgrades.LevelChanged += OnLevelChanged;
        }

        void Start()
        {
            if (_gameSession == null)
                _gameSession = FindAnyObjectByType<GameSession>();

            if (_gameSession == null)
                return;

            _upgrades = _gameSession.Upgrades;
            _upgrades.LevelChanged += OnLevelChanged;
        }

        void Update()
        {
            if (_remainingTime <= 0f)
                return;

            _remainingTime = Mathf.Max(0f, _remainingTime - Time.unscaledDeltaTime);
            if (_remainingTime == 0f)
                HideGuide();
        }

        void OnDisable()
        {
            if (_upgrades != null)
                _upgrades.LevelChanged -= OnLevelChanged;

            HideGuide();
        }

        /// <summary>
        /// 구매 후 id와 level을 확인하여 AutoClick이 1레벨이 되었을 때만 안내를 표시한다.
        /// 설정된 문구와 표시 시간을 적용하고 가이드 텍스트를 활성화한다.
        /// </summary>
        private void OnLevelChanged(UpgradeId id, int level)
        {
            if (id != UpgradeId.AutoClick || level != 1)
                return;

            _guideText.text = _message;
            _remainingTime = _duration;
            _guideText.gameObject.SetActive(true);
        }

        /// <summary>
        /// 현재 가이드 텍스트를 숨기고 남은 표시 시간을 0으로 변경한다.
        /// 입력값 없이 만료 또는 HUD 비활성화 이후의 표시 상태를 정리한다.
        /// </summary>
        private void HideGuide()
        {
            _remainingTime = 0f;
            _guideText.gameObject.SetActive(false);
        }
    }
}
