using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class KeyboardGuideHudView : MonoBehaviour
    {
        private const string PICKUP_GUIDE = "마우스 좌클릭을 눌러 키보드를 집으시오";
        private const string PLACEMENT_GUIDE = "모니터 앞에 키보드를 놓으시오";

        [Header("References")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private Text _guideText;

        void Awake()
        {
            _guideText.fontSize = 32;
            _guideText.resizeTextForBestFit = false;
            _guideText.raycastTarget = false;
        }

        void OnEnable()
        {
            if (_interactionController == null)
                _interactionController = FindAnyObjectByType<KeyboardInteractionController>();

            if (_interactionController == null)
            {
                RefreshGuide(KeyboardInteractionController.KeyboardGuideState.Hidden);
                return;
            }

            _interactionController.GuideStateChanged += RefreshGuide;
            RefreshGuide(_interactionController.GuideState);
        }

        void OnDisable()
        {
            if (_interactionController != null)
                _interactionController.GuideStateChanged -= RefreshGuide;

            RefreshGuide(KeyboardInteractionController.KeyboardGuideState.Hidden);
        }

        /// <summary>
        /// state에 맞춰 집기 또는 내려놓기 안내 문구를 선택한다.
        /// 가이드 텍스트의 내용과 표시 상태를 변경하며 상호작용 상태는 변경하지 않는다.
        /// </summary>
        private void RefreshGuide(KeyboardInteractionController.KeyboardGuideState state)
        {
            _guideText.text = state switch
            {
                KeyboardInteractionController.KeyboardGuideState.Pickup => PICKUP_GUIDE,
                KeyboardInteractionController.KeyboardGuideState.Placement => PLACEMENT_GUIDE,
                _ => string.Empty
            };
            _guideText.gameObject.SetActive(state != KeyboardInteractionController.KeyboardGuideState.Hidden);
        }
    }
}
