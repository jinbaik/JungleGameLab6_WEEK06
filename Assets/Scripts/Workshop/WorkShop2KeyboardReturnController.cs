using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Game.Player;
using Game.Session;
using Game.Tournament;
using KeyboardModeling;
using KeyboardModeling.Assembly;

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

        [Header("Request Submission")]
        [SerializeField] private GameSession _gameSession;
        private readonly HashSet<Rigidbody> _submissionBodies = new HashSet<Rigidbody>();
        private Rigidbody _deliveryKeyboard;
        private KeyboardRequestState _deliveryRequest;
        private Action<bool> _deliveryFinished;
        private int _returnsBeforeDelivery;
        private KeyboardAssemblyState _deliveryAssembly;
        private bool _deliveryAssemblyWasEnabled;
        private readonly List<Collider> _deliveryColliders = new List<Collider>();
        private readonly List<Rigidbody> _deliveryBodies = new List<Rigidbody>();
        private readonly List<bool> _deliveryKinematic = new List<bool>();
        private readonly List<bool> _deliveryGravity = new List<bool>();
        private readonly List<bool> _deliveryCollisions = new List<bool>();

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
            FinishDelivery(false);
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
            if (_submissionBodies.Contains(keyboard) || KeyboardRequestRuntime.IsSubmittedOrProcessing(keyboard)) return false;
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
        /// 트레이의 keyboard와 requestId를 검증하여 수거를 예약하고 성공 여부를 반환한다.
        /// 진행 의뢰를 고정하고 실물 조작을 차단하며 기존 에미넴 연출에서 finished로 결과를 알린다.
        /// </summary>
        public bool RequestDelivery(Rigidbody keyboard, string requestId, Action<bool> finished)
        {
            KeyboardPickupTarget pickup = keyboard != null ? keyboard.GetComponent<KeyboardPickupTarget>() : null;
            if (!isActiveAndEnabled || _deliveryRequest != null || keyboard == null
                || pickup == null || _queuedKeyboards.Contains(keyboard) || pickup.IsExcludedFromRequests
                || KeyboardRequestRuntime.ActiveRequest?.Definition.RequestId != requestId) return false;
            if (_gameSession == null || _gameSession.Wallet == null)
            {
                Debug.LogError("배송 작업실 지갑 연결이 없습니다. 키보드와 의뢰를 유지합니다.", this);
                return false;
            }
            if (!KeyboardRequestRuntime.TryBeginSubmission(keyboard, out KeyboardRequestState request)) return false;
            _deliveryKeyboard = keyboard;
            _deliveryRequest = request;
            _deliveryFinished = finished;
            _returnsBeforeDelivery = _pendingKeyboards.Count;
            try
            {
                _deliveryAssembly = keyboard.GetComponent<KeyboardAssemblyState>();
                if (_deliveryAssembly != null)
                {
                    _deliveryAssemblyWasEnabled = _deliveryAssembly.enabled;
                    _deliveryAssembly.enabled = false;
                }
                foreach (Collider collider in keyboard.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled) continue;
                    _deliveryColliders.Add(collider);
                    collider.enabled = false;
                }
                foreach (Rigidbody body in keyboard.GetComponentsInChildren<Rigidbody>(true))
                {
                    _deliveryBodies.Add(body);
                    _deliveryKinematic.Add(body.isKinematic);
                    _deliveryGravity.Add(body.useGravity);
                    _deliveryCollisions.Add(body.detectCollisions);
                    _submissionBodies.Add(body);
                    if (!body.isKinematic)
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.detectCollisions = false;
                }
                if (!_sequenceRunning)
                {
                    _sequenceRunning = true;
                    StartCoroutine(ReturnKeyboardSequence());
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                FinishDelivery(false);
                return false;
            }
        }

        /// <summary>
        /// 예약된 키보드의 실제 구성으로 기존 검수와 보상을 확정한 뒤 실물을 수거한다.
        /// 검수 전에 삭제하지 않으며 설정 오류는 고객 실패로 기록하지 않고 조작 상태를 복원한다.
        /// </summary>
        private void CollectDelivery()
        {
            bool collected = false;
            try
            {
                if (_deliveryKeyboard == null) throw new InvalidOperationException("수거할 키보드가 없습니다.");
                KeyboardAssemblyState assembly = _deliveryKeyboard.GetComponent<KeyboardAssemblyState>();
                KeyboardRequestInspection inspection = KeyboardRequestInspection.Inspect(
                    _deliveryKeyboard, assembly, _deliveryRequest.Definition);
                List<GameObject> objects = CollectSubmissionObjects(assembly, _deliveryKeyboard);
                try { KeyboardRequestRuntime.CompleteSubmission(_deliveryRequest, inspection, _gameSession.Wallet); }
                catch (Exception exception)
                {
                    if (_deliveryRequest.Phase != KeyboardRequestPhase.AwaitingConfirmation) throw;
                    // 결과와 지급이 확정된 뒤 알림에서 발생한 오류는 재지급하지 않는다.
                    Debug.LogException(exception, this);
                }
                foreach (GameObject keyboard in objects)
                {
                    keyboard.SetActive(false);
                    Destroy(keyboard);
                }
                collected = true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"배송 내부 오류: 키보드와 의뢰를 보존합니다. {exception}", this);
            }
            FinishDelivery(collected);
        }

        /// <summary>
        /// collected에 따라 수거 잠금을 해제하고 실패 시 저장한 물리와 Collider를 복원한다.
        /// 예약과 임시 참조를 정리한 뒤 콜백을 한 번 알리며 보상과 검수 결과는 변경하지 않는다.
        /// </summary>
        private void FinishDelivery(bool collected)
        {
            if (_deliveryRequest == null) return;
            KeyboardRequestRuntime.CancelSubmission(_deliveryRequest);
            if (!collected && _deliveryAssembly != null) _deliveryAssembly.enabled = _deliveryAssemblyWasEnabled;
            _deliveryAssembly = null;
            for (int index = 0; index < _deliveryBodies.Count; index++)
            {
                Rigidbody body = _deliveryBodies[index];
                _submissionBodies.Remove(body);
                if (collected || body == null) continue;
                body.isKinematic = _deliveryKinematic[index];
                body.useGravity = _deliveryGravity[index];
                body.detectCollisions = _deliveryCollisions[index];
                if (!body.isKinematic) body.WakeUp();
            }
            if (!collected)
                foreach (Collider collider in _deliveryColliders)
                    if (collider != null) collider.enabled = true;
            Action<bool> finished = _deliveryFinished;
            _deliveryRequest = null;
            _deliveryKeyboard = null;
            _deliveryFinished = null;
            _returnsBeforeDelivery = 0;
            _deliveryBodies.Clear();
            _deliveryKinematic.Clear();
            _deliveryGravity.Clear();
            _deliveryCollisions.Clear();
            _deliveryColliders.Clear();
            try { finished?.Invoke(collected); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        /// <summary>
        /// assembly 소유 루트와 arrival 및 루트에서 분리된 원본 구조 부품을 회수 목록으로 반환한다.
        /// 명시적인 소유 참조만 사용하며 별도로 분리된 플레이어 자유 키캡과 축은 목록에 추가하지 않는다.
        /// </summary>
        private List<GameObject> CollectSubmissionObjects(KeyboardAssemblyState assembly, Rigidbody arrival)
        {
            var objects = new HashSet<GameObject> { arrival.gameObject };
            if (assembly == null) return new List<GameObject>(objects);
            objects.Add(assembly.gameObject);
            foreach (AssemblyStructurePart kind in Enum.GetValues(typeof(AssemblyStructurePart)))
                foreach (Transform part in assembly.GetStructureParts(kind))
                    if (!part.IsChildOf(assembly.transform)) objects.Add(part.gameObject);
            return new List<GameObject>(objects);
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

        /// <summary>body가 반환 대기, 던지기 연출 또는 의뢰 회수로 고정된 상태인지 반환한다. 해당 키보드는 작업실 보관 목록에서 제외한다.</summary>
        public bool IsReturning(Rigidbody body)
        {
            return _queuedKeyboards.Contains(body) || _submissionBodies.Contains(body)
                || KeyboardRequestRuntime.IsSubmittedOrProcessing(body);
        }

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
                if (!TryPeekKeyboard(out _) && _deliveryRequest == null)
                {
                    yield return MoveActorTo(_hiddenPosition, true);
                    if (TryPeekKeyboard(out _) || _deliveryRequest != null) continue;
                    _sequenceRunning = false;
                    yield break;
                }

                bool actorAtVisitPosition = (_actor.position - _visitPosition).sqrMagnitude <= 0.0001f;
                if (!actorAtVisitPosition)
                {
                    yield return MoveActorTo(_visitPosition, false);
                    yield return WaitFor(_waitBeforeThrow);
                }
                if (_deliveryRequest != null && (_returnsBeforeDelivery <= 0 || !TryPeekKeyboard(out _)))
                {
                    if (actorAtVisitPosition) yield return WaitFor(_waitBeforeThrow);
                    CollectDelivery();
                    yield return WaitFor(_waitAfterThrow);
                    yield return MoveActorTo(_hiddenPosition, true);
                    continue;
                }
                if (!TryTakeKeyboard(out Rigidbody keyboard)) continue;
                if (_deliveryRequest != null) _returnsBeforeDelivery--;

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
