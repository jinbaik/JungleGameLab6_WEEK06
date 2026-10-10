using System;

using UnityEngine;

using Unity.Cinemachine;

using Game.Economy;

namespace KeyboardModeling
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class KeyboardAutomaticStation : MonoBehaviour
    {
        [Header("Station")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private Transform _placement;
        [SerializeField] private CinemachineCamera _viewCamera;
        [SerializeField] private KeyboardRewardController _rewards;
        [SerializeField] private GameObject _keyboardFragmentsPrefab;
        private KeyboardInputController _keyboard;
        private KeycapHealth[] _keycaps = Array.Empty<KeycapHealth>();
        public KeyboardInputController Keyboard => _keyboard;
        public bool HasKeyboard => _keyboard != null;
        public Transform Placement => _placement;
        public event Action<KeyboardInputController> KeyboardChanged;

        void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            _viewCamera.Priority = 0;
        }

        void OnTriggerStay(Collider other)
        {
            if (!HasKeyboard)
                _interactionController.TryPlaceAutomaticKeyboard(other, this);
        }

        void OnDestroy()
        {
            UnsubscribeRewards();
        }

        /// <summary>
        /// 전달받은 키보드를 회수 불가능한 자동작업대 대상으로 고정한다.
        /// keyboard를 _placement의 로컬 위치 0과 단위 회전에 맞추고 물리 위치도 동기화한다.
        /// 입력과 키캡 Collider를 끄고 파괴·보상 및 교체 이벤트를 연결한다.
        /// </summary>
        public void AcceptKeyboard(KeyboardInputController keyboard)
        {
            Rigidbody body = keyboard.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
            body.useGravity = false;
            Transform keyboardTransform = keyboard.transform;
            keyboardTransform.SetParent(_placement, true);
            keyboardTransform.localPosition = Vector3.zero;
            keyboardTransform.localRotation = Quaternion.identity;
            body.position = keyboardTransform.position;
            body.rotation = keyboardTransform.rotation;
            _keyboard = keyboard;
            _keyboard.SetInputEnabled(false);
            _keyboard.enabled = false;
            foreach (Collider collider in keyboard.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            keyboard.GetComponent<BoxCollider>().enabled = true;
            _keycaps = keyboard.GetComponentsInChildren<KeycapHealth>(true);
            foreach (KeycapHealth keycap in _keycaps)
                keycap.Broken += _rewards.GrantAutomaticKeycapReward;
            KeyboardDestruction destruction = keyboard.gameObject.AddComponent<KeyboardDestruction>();
            destruction.Initialize(keyboard.GetDestructionKeycaps(), _keyboardFragmentsPrefab, HandleBreaking, HandleDestroyed);
            KeyboardChanged?.Invoke(keyboard);
        }

        /// <summary>
        /// 자세히 보기 카메라의 우선순위를 변경한다.
        /// active에 따라 작업대 전용 카메라를 활성 또는 대기 우선순위로 설정한다.
        /// </summary>
        public void SetViewActive(bool active)
        {
            _viewCamera.Priority = active ? 25 : 0;
        }

        /// <summary>
        /// 키보드 전체 파괴가 시작되면 자동공격의 대상을 해제한다.
        /// 파괴 콜백을 사용해 KeyboardChanged에 null을 전달하며 자세히 보기 상태는 유지한다.
        /// </summary>
        private void HandleBreaking()
        {
            KeyboardChanged?.Invoke(null);
        }

        /// <summary>
        /// 파괴 연출이 끝난 키보드를 제거하고 새 키보드를 넣을 수 있게 한다.
        /// 보관된 키보드와 파괴 구독을 정리하고 슬롯 상태를 비운다.
        /// </summary>
        private void HandleDestroyed()
        {
            UnsubscribeRewards();
            GameObject keyboardObject = _keyboard.gameObject;
            _keyboard = null;
            Destroy(keyboardObject);
            KeyboardChanged?.Invoke(null);
        }

        /// <summary>
        /// 보관한 키캡의 자동 보상 구독을 해제한다.
        /// _keycaps의 살아 있는 참조를 사용하여 중복 지급을 막고 목록을 비운다.
        /// </summary>
        private void UnsubscribeRewards()
        {
            foreach (KeycapHealth keycap in _keycaps)
            {
                if (keycap != null)
                    keycap.Broken -= _rewards.GrantAutomaticKeycapReward;
            }
            _keycaps = Array.Empty<KeycapHealth>();
        }
    }
}
