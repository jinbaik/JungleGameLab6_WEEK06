using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class KeyboardGuideHudView : MonoBehaviour
    {
        private const string PICKUP_GUIDE = "마우스 우클릭으로 키보드를 들어 올리기";
        private const string PLACEMENT_GUIDE = "모니터 앞에 키보드를 놓으시오";
        private const string DISPENSER_GUIDE = "좌클릭으로 키보드 받기";

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
        /// state에 맞춰 키보드 집기, 배치, 보관 또는 자판기 사용 안내 문구를 선택한다.
        /// _guideText의 내용과 표시 상태를 변경하며 상호작용 상태는 변경하지 않는다.
        /// </summary>
        private void RefreshGuide(KeyboardInteractionController.KeyboardGuideState state)
        {
            _guideText.text = state switch
            {
                KeyboardInteractionController.KeyboardGuideState.Pickup => PICKUP_GUIDE,
                KeyboardInteractionController.KeyboardGuideState.Placement => PLACEMENT_GUIDE,
                KeyboardInteractionController.KeyboardGuideState.Dispenser => DISPENSER_GUIDE,
                KeyboardInteractionController.KeyboardGuideState.BoxPickup => "좌클릭으로 상자 집기",
                KeyboardInteractionController.KeyboardGuideState.BoxHolding => "좌클릭으로 상자 내려놓기",
                KeyboardInteractionController.KeyboardGuideState.BoxStore => $"좌클릭으로 키보드 넣기 ({_interactionController.GuideCount}/{_interactionController.GuideCapacity})",
                KeyboardInteractionController.KeyboardGuideState.BoxFull => $"상자가 가득 찼습니다 ({_interactionController.GuideCount}/{_interactionController.GuideCapacity})",
                KeyboardInteractionController.KeyboardGuideState.BoxTake => $"상자 위쪽을 좌클릭하여 키보드 꺼내기 ({_interactionController.GuideCount}/{_interactionController.GuideCapacity})",
                _ => string.Empty
            };
            _guideText.gameObject.SetActive(state != KeyboardInteractionController.KeyboardGuideState.Hidden);
        }
    }
}
