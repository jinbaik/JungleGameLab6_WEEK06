using System.Collections;

using UnityEngine;

using Game.Upgrades;

namespace KeyboardModeling
{
    [RequireComponent(typeof(KeyboardAutomaticStation))]
    [DisallowMultipleComponent]
    public sealed class KeyboardAutomaticRefillController : MonoBehaviour
    {
        private const float RETRY_INTERVAL = 1f;

        [Header("Dependencies")]
        private KeyboardAutomaticStation _station;
        private KeyboardInteractionController _interactionController;
        private UpgradeService _upgrades;

        [Header("Transfer")]
        [SerializeField, Min(0.01f)] private float _carryDuration = 0.75f;
        private Coroutine _refillRoutine;
        private KeyboardInteractionController.KeyboardState _reserved;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private bool _placingReserved;

        void Awake()
        {
            _station = GetComponent<KeyboardAutomaticStation>();
            _interactionController = KeyboardAutoAttackManager.Instance.InteractionController;
        }

        void OnEnable()
        {
            if (_upgrades == null)
                return;

            Subscribe();
            BeginRefill();
        }

        void Start()
        {
            _upgrades = KeyboardAutoAttackManager.Instance.GameSession.Upgrades;
            Subscribe();
            BeginRefill();
        }

        void OnDisable()
        {
            if (_upgrades != null)
                Unsubscribe();

            CancelRefill();
        }

        /// <summary>
        /// 작업대의 배치·파괴 이벤트와 Auto Click 레벨 변경 이벤트를 연결한다.
        /// 이벤트를 통해 보충 시작과 수동 배치 취소를 처리한다.
        /// </summary>
        private void Subscribe()
        {
            _station.KeyboardDestroyed += HandleKeyboardDestroyed;
            _station.KeyboardChanged += HandleKeyboardChanged;
            _upgrades.LevelChanged += HandleUpgradeChanged;
        }

        /// <summary>
        /// 작업대와 강화 서비스에 등록한 C# 이벤트를 해제한다.
        /// 비활성화 중인 컨트롤러가 보충을 시작하지 않도록 한다.
        /// </summary>
        private void Unsubscribe()
        {
            _station.KeyboardDestroyed -= HandleKeyboardDestroyed;
            _station.KeyboardChanged -= HandleKeyboardChanged;
            _upgrades.LevelChanged -= HandleUpgradeChanged;
        }

        /// <summary>
        /// station의 키보드 파괴 완료를 받아 다음 보충을 시작한다.
        /// 해금과 슬롯 상태는 BeginRefill에서 확인한다.
        /// </summary>
        private void HandleKeyboardDestroyed(KeyboardAutomaticStation station)
        {
            BeginRefill();
        }

        /// <summary>
        /// keyboard가 수동으로 놓였으면 자동 보충을 취소한다.
        /// 자체 자동 배치가 발생시킨 변경 이벤트는 취소하지 않는다.
        /// </summary>
        private void HandleKeyboardChanged(KeyboardInputController keyboard)
        {
            if (keyboard != null && !_placingReserved)
                CancelRefill();
        }

        /// <summary>
        /// Auto Click 구매 뒤 빈 작업대의 보충 가능 여부를 다시 확인한다.
        /// 이미 진행 중인 대기 시간은 코루틴에서 현재 강화 수치로 갱신한다.
        /// </summary>
        private void HandleUpgradeChanged(UpgradeId id, int level)
        {
            if (id == UpgradeId.AutoClick)
                BeginRefill();
        }

        /// <summary>
        /// 자동 보충이 해금된 빈 작업대에서 보충 대기를 시작한다.
        /// 현재 작업대 상태와 실행 중인 코루틴을 확인하여 중복 실행을 막는다.
        /// </summary>
        private void BeginRefill()
        {
            if (_refillRoutine != null || _station.HasKeyboard || !_upgrades.IsAutoRefillUnlocked())
                return;

            _refillRoutine = StartCoroutine(Refill());
        }

        /// <summary>
        /// 현재 강화 단계의 대기 시간이 지난 뒤 실제 월드 키보드를 가져온다.
        /// 대기 중 강화되면 새 시간을 적용하고 재고가 없으면 다시 검색한다.
        /// </summary>
        private IEnumerator Refill()
        {
            float elapsed = 0f;

            while (elapsed < _upgrades.GetAutoRefillDelay())
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            while (!_station.HasKeyboard && _upgrades.IsAutoRefillUnlocked())
            {
                if (!_interactionController.TryReserveAutomaticKeyboard(_station, out KeyboardInteractionController.KeyboardState source))
                {
                    yield return WaitForRetry();
                    continue;
                }

                _reserved = source;
                _originalPosition = source.Body.position;
                _originalRotation = source.Body.rotation;
                _station.SetAutoRefillInProgress(true);

                yield return CarryReservedKeyboard(source);

                _placingReserved = true;
                bool placed = _interactionController.TryPlaceReservedAutomaticKeyboard(source, _station);
                _placingReserved = false;

                if (placed)
                {
                    _reserved = null;
                    _station.SetAutoRefillInProgress(false);
                    _refillRoutine = null;
                    yield break;
                }

                ReleaseReserved();
                yield return WaitForRetry();
            }

            _refillRoutine = null;
        }

        /// <summary>
        /// source 원본을 현재 위치에서 작업대 배치 위치까지 운반한다.
        /// 운반 시간과 Time.deltaTime을 사용하여 Rigidbody의 위치와 회전을 변경한다.
        /// </summary>
        private IEnumerator CarryReservedKeyboard(
            KeyboardInteractionController.KeyboardState source)
        {
            Vector3 startPosition = source.Body.position;
            Quaternion startRotation = source.Body.rotation;
            float elapsed = 0f;

            while (elapsed < _carryDuration && source.Body != null)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _carryDuration));

                source.Body.position = Vector3.Lerp(startPosition, _station.Placement.position, progress);
                source.Body.rotation = Quaternion.Slerp(startRotation, _station.Placement.rotation, progress);
                yield return null;
            }

            if (source.Body != null)
            {
                source.Body.position = _station.Placement.position;
                source.Body.rotation = _station.Placement.rotation;
            }
        }

        /// <summary>
        /// 다음 월드 키보드 검색까지 잠시 기다린다.
        /// RETRY_INTERVAL과 Time.deltaTime을 사용하며 슬롯 상태는 유지한다.
        /// </summary>
        private IEnumerator WaitForRetry()
        {
            float elapsed = 0f;
            while (elapsed < RETRY_INTERVAL)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// 진행 중인 보충을 멈추고 예약한 원본을 되돌린다.
        /// 수동 배치 또는 컴포넌트 비활성화 시 운반 상태를 해제한다.
        /// </summary>
        private void CancelRefill()
        {
            if (_refillRoutine != null)
            {
                StopCoroutine(_refillRoutine);
                _refillRoutine = null;
            }

            ReleaseReserved();
            _placingReserved = false;
        }

        /// <summary>
        /// 예약한 원본 키보드를 이전 위치로 복원한다.
        /// 예약 참조를 비우고 작업대의 수동 배치 제한을 해제한다.
        /// </summary>
        private void ReleaseReserved()
        {
            if (_reserved != null)
            {
                _interactionController.ReleaseAutomaticKeyboard(_reserved, _originalPosition, _originalRotation);
                _reserved = null;
            }

            _station.SetAutoRefillInProgress(false);
        }
    }
}
