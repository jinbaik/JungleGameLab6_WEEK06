using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Game.Player;
using Game.Tournament;
using KeyboardModeling;

namespace Game.Workshop
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorkShop2KeyboardReturnController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _actor;
        [SerializeField] private Vector3 _hiddenPosition = new Vector3(-3.19f, -9f, 8.25f);
        [SerializeField] private Vector3 _visitPosition = new Vector3(-3.19f, 1f, 8.25f);
        [SerializeField] private Vector3 _throwOriginOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private Vector3 _throwTargetPosition = new Vector3(-6.7747407f, -1.912f, -1.9889657f);

        [Header("Tournament Return")]
        [SerializeField] private GameObject _workshopKeyboardPrefab;
        [SerializeField] private KeyboardCustomizationStorage _storage;
        [SerializeField] private WorkshopKeyboardStateController _workshopState;

        [Header("Movement")]
        [SerializeField, Min(0.01f)] private float _moveDuration = 0.6f;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float _waitBeforeThrow = 1f;
        [SerializeField, Min(0f)] private float _waitAfterThrow = 1f;

        [Header("Throw")]
        [SerializeField, Min(0.1f)] private float _throwFlightDuration = 1.2f;
        [SerializeField, Min(0f)] private float _spinSpeed = 5f;

        [Header("Player Return")]
        [Tooltip("비워두면 씬 시작 시 플레이어 위치로 복귀합니다.")]
        [SerializeField] private Transform _playerReturnPoint;
        private PlayerMovement _playerMovement;
        private Vector3 _playerStartPosition;

        [Header("Runtime")]
        private readonly Queue<Rigidbody> _pendingKeyboards = new Queue<Rigidbody>();
        private readonly HashSet<Rigidbody> _queuedKeyboards = new HashSet<Rigidbody>();
        private readonly Dictionary<Rigidbody, List<Renderer>> _hiddenRenderers = new Dictionary<Rigidbody, List<Renderer>>();
        private bool _sequenceRunning;

        void Awake()
        {
            if (_workshopState == null) _workshopState = GetComponent<WorkshopKeyboardStateController>();
            _actor.position = _hiddenPosition;
            foreach (GameObject candidate in GameObject.FindGameObjectsWithTag("Player"))
            {
                if (candidate.TryGetComponent(out _playerMovement)) break;
            }
            _playerStartPosition = _playerMovement.transform.position;
        }

        void Start()
        {
            if (CustomizationOnlyWorkshop.IsActive)
            {
                // WorkShop0는 독립 커마 씬이므로 이전 토너먼트 복귀 구성을 가져오지 않는다.
                // _workshopState.RestorePendingKeyboards();
                // TournamentKeyboardSession.TryBeginWorkshopReturn(out KeyboardCustomizationState original);
                return;
            }
            GameObject keyboard = null;
            try
            {
                _workshopState.RestorePendingKeyboards();
                if (!TournamentKeyboardSession.TryBeginWorkshopReturn(out KeyboardCustomizationState original)) return;
                if (_workshopKeyboardPrefab == null || _storage == null)
                    throw new InvalidOperationException("복귀용 키보드 프리팹과 저장소 연결이 필요합니다.");

                // 활성 프리팹의 Awake와 OnEnable이 끝난 뒤 사진을 포함한 원본을 동기 적용한다.
                keyboard = Instantiate(_workshopKeyboardPrefab, _hiddenPosition, _workshopKeyboardPrefab.transform.rotation);
                Rigidbody body = keyboard.GetComponent<Rigidbody>();
                KeyboardCustomization customization = keyboard.GetComponent<KeyboardCustomization>();
                if (body == null || customization == null)
                    throw new InvalidOperationException("복귀용 키보드에 Rigidbody 또는 KeyboardCustomization이 없습니다.");
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
                keyboard.GetComponent<KeyboardInputController>().SetInputEnabled(false);
                KeyboardPickupTarget pickup = keyboard.GetComponent<KeyboardPickupTarget>();
                if (pickup == null) pickup = keyboard.AddComponent<KeyboardPickupTarget>();
                pickup.SetSourcePrefab(_workshopKeyboardPrefab);
                _storage.ApplyCustomization(original, customization);
                if (!RequestReturn(body))
                    throw new InvalidOperationException("복원된 키보드의 반환 대기열 등록에 실패했습니다.");
                TournamentKeyboardSession.CompleteWorkshopReturn();
            }
            catch (Exception exception)
            {
                if (keyboard != null) Destroy(keyboard);
                Debug.LogError($"토너먼트 키보드 복원 실패. 원본을 메모리에 유지하며 자동 재시도하지 않습니다. {exception}", this);
            }
        }

        void OnDisable()
        {
            StopAllCoroutines();
            foreach (Rigidbody keyboard in _pendingKeyboards)
            {
                if (keyboard == null) continue;
                keyboard.isKinematic = false;
                keyboard.useGravity = true;
                keyboard.detectCollisions = true;
                keyboard.WakeUp();
            }
            _pendingKeyboards.Clear();
            foreach (List<Renderer> renderers in _hiddenRenderers.Values)
                foreach (Renderer renderer in renderers)
                    if (renderer != null) renderer.forceRenderingOff = false;
            _hiddenRenderers.Clear();
            _queuedKeyboards.Clear();
            _sequenceRunning = false;
            if (_actor != null)
                _actor.position = _hiddenPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                ReturnPlayer();
                return;
            }

            Rigidbody keyboard = other.attachedRigidbody;
            if (keyboard == null || keyboard.isKinematic || !IsKeyboard(keyboard)) return;
            RequestReturn(keyboard);
        }

        /// <summary>
        /// 준비된 keyboard를 기존 반환 대기열에 등록하고 성공 여부를 반환한다.
        /// 명시적인 요청은 정적 물리 상태도 허용하며 중복 등록과 연출 중복 실행을 막는다.
        /// </summary>
        public bool RequestReturn(Rigidbody keyboard)
        {
            if (!isActiveAndEnabled || keyboard == null || !IsKeyboard(keyboard)) return false;
            if (!_queuedKeyboards.Add(keyboard)) return false;

            if (!keyboard.isKinematic)
            {
                keyboard.linearVelocity = Vector3.zero;
                keyboard.angularVelocity = Vector3.zero;
            }
            keyboard.transform.SetParent(null, true);
            keyboard.useGravity = false;
            keyboard.isKinematic = true;
            keyboard.detectCollisions = false;
            keyboard.position = _hiddenPosition;
            var hiddenRenderers = new List<Renderer>();
            foreach (Renderer renderer in keyboard.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.forceRenderingOff) continue;
                hiddenRenderers.Add(renderer);
                renderer.forceRenderingOff = true;
            }
            _hiddenRenderers.Add(keyboard, hiddenRenderers);
            _pendingKeyboards.Enqueue(keyboard);

            if (_sequenceRunning) return true;
            _sequenceRunning = true;
            StartCoroutine(ReturnKeyboardSequence());
            return true;
        }

        /// <summary>
        /// Player 태그 대상의 복귀 요청으로 플레이어를 방 안의 안전 위치로 이동한다.
        /// 지정된 복귀 지점 또는 시작 위치를 사용하며 낙하 속도를 초기화하고 시선 방향은 유지한다.
        /// </summary>
        private void ReturnPlayer()
        {
            Vector3 destination = _playerReturnPoint != null ? _playerReturnPoint.position : _playerStartPosition;
            _playerMovement.TeleportTo(destination);
        }

        /// <summary>body가 반환 대기 또는 던지기 연출 중인지 반환한다. 작업실 보관에서 반환 중인 키보드를 제외한다.</summary>
        public bool IsReturning(Rigidbody body) { return _queuedKeyboards.Contains(body); }

        /// <summary>
        /// body의 계층에 KeyboardPickupTarget이 있는지 확인해 키보드 여부를 반환한다.
        /// body를 기준으로 부모와 자식 오브젝트를 검색한다.
        /// </summary>
        private bool IsKeyboard(Rigidbody body)
        {
            return body.GetComponentInParent<KeyboardPickupTarget>() != null
                || body.GetComponentInChildren<KeyboardPickupTarget>(true) != null;
        }

        /// <summary>
        /// 대기 중인 키보드를 하나씩 방 안으로 던지고, 큐가 비었을 때 에미넴을 숨김 위치로 이동한다.
        /// 대기 큐와 설정된 이동 및 대기 시간을 사용하며 연속 요청 중에는 에미넴을 위에 유지한다.
        /// </summary>
        private IEnumerator ReturnKeyboardSequence()
        {
            while (true)
            {
                if (!TryPeekKeyboard(out _))
                {
                    yield return MoveActorTo(_hiddenPosition, true);
                    if (TryPeekKeyboard(out _)) continue;
                    _sequenceRunning = false;
                    yield break;
                }

                bool actorAtVisitPosition = (_actor.position - _visitPosition).sqrMagnitude <= 0.0001f;
                if (!actorAtVisitPosition)
                {
                    yield return MoveActorTo(_visitPosition, false);
                    yield return WaitFor(_waitBeforeThrow);
                }
                if (!TryTakeKeyboard(out Rigidbody keyboard)) continue;

                LaunchKeyboard(keyboard);
                yield return WaitFor(_waitAfterThrow);
                _queuedKeyboards.Remove(keyboard);
            }
        }

        /// <summary>
        /// 큐의 첫 키보드가 살아 있는지 확인하고 삭제된 항목을 제거한다.
        /// 유효한 키보드가 있으면 keyboard로 반환한다.
        /// </summary>
        private bool TryPeekKeyboard(out Rigidbody keyboard)
        {
            while (_pendingKeyboards.Count > 0)
            {
                keyboard = _pendingKeyboards.Peek();
                if (keyboard != null && _queuedKeyboards.Contains(keyboard)) return true;
                _pendingKeyboards.Dequeue();
                _queuedKeyboards.Remove(keyboard);
                _hiddenRenderers.Remove(keyboard);
            }

            keyboard = null;
            return false;
        }

        /// <summary>
        /// 대기 큐에서 가장 오래 기다린 키보드를 꺼내 keyboard로 반환한다.
        /// 유효한 항목이 없으면 false를 반환하며 연출 중 중복 등록 방지는 유지한다.
        /// </summary>
        private bool TryTakeKeyboard(out Rigidbody keyboard)
        {
            if (!TryPeekKeyboard(out keyboard)) return false;
            _pendingKeyboards.Dequeue();
            return true;
        }

        /// <summary>
        /// actor를 destination까지 부드럽게 이동시키고, 하강 중 새 키보드가 들어오면 이동을 중단한다.
        /// destination과 _moveDuration을 사용하며 actor의 위치를 갱신한다.
        /// </summary>
        private IEnumerator MoveActorTo(Vector3 destination, bool interruptWhenKeyboardArrives)
        {
            Vector3 startPosition = _actor.position;
            if ((destination - startPosition).sqrMagnitude <= 0.0001f)
            {
                _actor.position = destination;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < _moveDuration)
            {
                if (interruptWhenKeyboardArrives && TryPeekKeyboard(out _)) yield break;
                elapsed += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _moveDuration));
                _actor.position = Vector3.Lerp(startPosition, destination, progress);
                yield return null;
            }

            if (!interruptWhenKeyboardArrives || !TryPeekKeyboard(out _))
                _actor.position = destination;
        }

        /// <summary>
        /// duration 동안 프레임 시간을 누적해 대기한다.
        /// duration을 사용하며 게임 시간에 맞춰 코루틴의 다음 단계를 진행한다.
        /// </summary>
        private IEnumerator WaitFor(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// 키보드를 에미넴 위치에서 throwTarget 방향으로 발사하고 물리 상태를 다시 활성화한다.
        /// _throwOriginOffset, _throwFlightDuration, _spinSpeed로 위치와 속도를 갱신하고 발사 위치에서 Throwing을 재생한다.
        /// </summary>
        private void LaunchKeyboard(Rigidbody keyboard)
        {
            Vector3 origin = _actor.position + _throwOriginOffset;
            keyboard.position = origin;
            foreach (Renderer renderer in _hiddenRenderers[keyboard])
                if (renderer != null) renderer.forceRenderingOff = false;
            _hiddenRenderers.Remove(keyboard);
            keyboard.useGravity = true;
            keyboard.isKinematic = false;
            keyboard.detectCollisions = true;
            keyboard.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            keyboard.linearVelocity = CalculateLaunchVelocity(origin, _throwTargetPosition);
            keyboard.angularVelocity = UnityEngine.Random.insideUnitSphere * _spinSpeed;
            keyboard.WakeUp();
            AudioManager.instance.PlayOneShot(FMODEvents.instance.Throwing, origin);
        }

        /// <summary>
        /// origin에서 throwTargetPosition까지 도달하는 탄도 발사 속도를 계산한다.
        /// _throwFlightDuration과 Physics.gravity를 사용해 키보드 Rigidbody의 초기 속도를 반환한다.
        /// </summary>
        private Vector3 CalculateLaunchVelocity(Vector3 origin, Vector3 throwTargetPosition)
        {
            float flightDuration = Mathf.Max(_throwFlightDuration, 0.1f);
            Vector3 displacement = throwTargetPosition - origin;
            return (displacement - 0.5f * Physics.gravity * flightDuration * flightDuration) / flightDuration;
        }
    }
}
