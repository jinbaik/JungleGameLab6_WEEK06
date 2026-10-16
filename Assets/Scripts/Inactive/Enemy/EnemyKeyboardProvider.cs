using System;

using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class EnemyKeyboardProvider : MonoBehaviour
    {
        [Header("Keyboard Supply")]
        [SerializeField] private GameObject _keyboardPrefab;
        [SerializeField] private GameObject _fragmentsPrefab;
        [SerializeField] private Transform _placement;
        [SerializeField, Min(0f)] private float _refillDelay = 0.25f;
        private Transform _generatedRoot;
        private KeyboardInputController _keyboard;
        private KeyboardDestruction _destruction;
        private bool _isSupplying;
        private bool _waitingForKeyboard;
        private float _refillRemaining;
        public KeyboardInputController Keyboard => _keyboard;
        public bool IsSupplying => _isSupplying;
        public event Action<KeyboardInputController> KeyboardChanged;

        void OnDisable()
        {
            StopSupply();
        }

        void OnDestroy()
        {
            ClearGeneratedObjects();
        }

        /// <summary>
        /// 새 라운드의 키보드 공급을 시작한다.
        /// 기존 생성물과 예약을 정리하고 Inspector의 프리팹과 배치 위치로 첫 키보드를 생성한다.
        /// </summary>
        public void StartSupply()
        {
            StopSupply();
            ClearGeneratedObjects();
            _generatedRoot = new GameObject("EnemyGeneratedKeyboards").transform;
            _generatedRoot.SetParent(transform, false);
            _isSupplying = true;
            SpawnKeyboard();
        }

        /// <summary>
        /// 추가 공급을 중단하고 현재 키보드의 파괴 알림을 해제한다.
        /// 입력값 없이 예약 상태와 현재 대상 참조를 비우며 생성된 모델은 결과 확인을 위해 유지한다.
        /// </summary>
        public void StopSupply()
        {
            _isSupplying = false;
            _waitingForKeyboard = false;
            UnbindKeyboard();
        }

        /// <summary>
        /// 라운드에서 전달한 deltaTime만큼 다음 키보드 공급 대기를 진행한다.
        /// 공급 중인 빈 슬롯의 지연이 끝나면 새 키보드를 생성하고 예약을 해제한다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isSupplying || !isActiveAndEnabled || !_waitingForKeyboard || deltaTime <= 0f)
                return;
            _refillRemaining -= deltaTime;
            if (_refillRemaining <= 0f)
            {
                _waitingForKeyboard = false;
                SpawnKeyboard();
            }
        }

        /// <summary>
        /// 설정된 프리팹으로 입력이 차단된 키보드를 배치한다.
        /// 배치 Transform과 완파 대상 목록을 사용하여 물리 상태, 파괴 구독과 현재 키보드를 초기화한다.
        /// </summary>
        private void SpawnKeyboard()
        {
            GameObject instance = Instantiate(_keyboardPrefab, _placement.position, _placement.rotation, _generatedRoot);
            _keyboard = instance.GetComponent<KeyboardInputController>();
            _keyboard.SetInputEnabled(false);
            _keyboard.enabled = false;
            Rigidbody body = instance.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            if (!instance.TryGetComponent(out _destruction))
                _destruction = instance.AddComponent<KeyboardDestruction>();
            instance.GetComponent<KeyboardFragmentPresenter>().Initialize(_fragmentsPrefab, _generatedRoot);
            _destruction.Initialize(_keyboard.GetDestructionKeycaps(), _fragmentsPrefab, null, null, null, null);
            _destruction.Breaking += HandleKeyboardBreaking;
            KeyboardChanged?.Invoke(_keyboard);
        }

        /// <summary>
        /// 현재 키보드의 완파 판정을 받아 공격 대상을 해제한다.
        /// 현재 대상 참조를 비우고 공급이 실행 중이면 다음 키보드의 공급 지연을 예약한다.
        /// </summary>
        private void HandleKeyboardBreaking()
        {
            UnbindKeyboard();
            if (!_isSupplying)
                return;
            _waitingForKeyboard = true;
            _refillRemaining = _refillDelay;
        }

        /// <summary>
        /// 현재 키보드와 완파 컴포넌트의 구독을 해제한다.
        /// 저장된 대상 참조를 사용해 완파 이벤트를 정리하고 빈 슬롯 변경을 알린다.
        /// </summary>
        private void UnbindKeyboard()
        {
            if (_keyboard == null)
                return;
            _destruction.Breaking -= HandleKeyboardBreaking;
            _keyboard = null;
            _destruction = null;
            KeyboardChanged?.Invoke(null);
        }

        /// <summary>
        /// 이 공급기가 만든 키보드와 키보드 파편을 제거한다.
        /// 생성물 루트를 비활성화하고 제거하여 이전 라운드 연출과 물리 상태를 정리한다.
        /// </summary>
        private void ClearGeneratedObjects()
        {
            if (_generatedRoot == null)
                return;
            _generatedRoot.gameObject.SetActive(false);
            Destroy(_generatedRoot.gameObject);
            _generatedRoot = null;
        }
    }
}
