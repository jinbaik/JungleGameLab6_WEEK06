using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeyboardTypingArmController))]
    public sealed class KeyboardTypingArmInputBinding : MonoBehaviour
    {
        [Header("Temporary Smash Input")]
        [SerializeField] private bool _temporarySmashInput = true;
        private KeyboardTypingArmController _arm;
        private KeyboardInteractionController _interactionController;
        private KeyboardInputController _activeKeyboard;

        void Awake()
        {
            _arm = GetComponent<KeyboardTypingArmController>();
        }

        void OnEnable()
        {
            KeyboardAutoAttackManager manager = KeyboardAutoAttackManager.Instance;
            _interactionController = manager != null ? manager.InteractionController : null;
            if (_interactionController != null && _temporarySmashInput)
            {
                _interactionController.ActiveKeyboardChanged += BindKeyboard;
                BindKeyboard(_interactionController.ActiveKeyboard);
            }
        }

        void OnDisable()
        {
            if (_interactionController != null)
                _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            BindKeyboard(null);
        }

        /// <summary>
        /// enabled로 스매쉬 입력 연결 여부를 변경한다.
        /// 기존 이벤트와 타건을 정리하고 활성 상태이면 현재 키보드를 다시 구독한다.
        /// </summary>
        public void SetInputEnabled(bool enabled)
        {
            if (_interactionController != null)
                _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            BindKeyboard(null);
            _temporarySmashInput = enabled;
            if (enabled && isActiveAndEnabled && _interactionController != null)
            {
                _interactionController.ActiveKeyboardChanged += BindKeyboard;
                BindKeyboard(_interactionController.ActiveKeyboard);
            }
        }

        /// <summary>
        /// keyboard를 스매쉬 타건 대상으로 연결한다.
        /// 이전 키보드 이벤트를 해제하고 팔의 누름 상태와 현재 키보드 참조를 변경한다.
        /// </summary>
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            if (_activeKeyboard == keyboard)
                return;
            if (_activeKeyboard != null)
                _activeKeyboard.KeycapPressed -= HandleKeycapPressed;
            _arm.CancelPress();
            _activeKeyboard = keyboard;
            if (_activeKeyboard != null)
                _activeKeyboard.KeycapPressed += HandleKeycapPressed;
        }

        /// <summary>
        /// keycap을 스매쉬 모드의 팔 타건 요청으로 전달한다.
        /// 입력 연결 상태와 스매쉬 모드를 확인하여 팔의 누름 동작을 시작한다.
        /// </summary>
        private void HandleKeycapPressed(Transform keycap)
        {
            if (_temporarySmashInput && _interactionController.IsSmashMode)
                _arm.PressKey(keycap);
        }
    }
}
