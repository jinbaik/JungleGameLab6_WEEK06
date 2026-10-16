using UnityEngine;
using UnityEngine.UI;

using KeyboardModeling;

namespace Game.UI
{
    public sealed class CrosshairHudView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private Image _crosshair;

        void OnEnable()
        {
            _crosshair.raycastTarget = false;
            _interactionController = FindAnyObjectByType<KeyboardInteractionController>();
            // 모드 컨트롤러가 없는 씬에서도 공용 HUD 프리팹을 사용한다.
            if (_interactionController == null)
            {
                _crosshair.gameObject.SetActive(false);
                return;
            }

            _interactionController.ActiveKeyboardChanged += OnActiveKeyboardChanged;
            RefreshVisibility();
        }

        void OnDisable()
        {
            if (_interactionController != null)
                _interactionController.ActiveKeyboardChanged -= OnActiveKeyboardChanged;
        }

        /// <summary>
        /// keyboard의 활성 키보드 변경 알림을 받아 Crosshair 표시를 갱신한다.
        /// 키보드 참조 대신 모드 컨트롤러의 현재 상태를 표시 기준으로 사용한다.
        /// </summary>
        private void OnActiveKeyboardChanged(KeyboardInputController keyboard)
        {
            RefreshVisibility();
        }

        /// <summary>
        /// 모드 컨트롤러의 IsSmashMode를 조회하여 Crosshair의 활성 상태를 변경한다.
        /// OutSideMode에서만 표시하며 다른 HUD와 게임 상태는 변경하지 않는다.
        /// </summary>
        private void RefreshVisibility()
        {
            _crosshair.gameObject.SetActive(!_interactionController.IsSmashMode);
        }
    }
}
