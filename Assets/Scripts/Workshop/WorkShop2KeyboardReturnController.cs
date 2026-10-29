using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Game.Player;

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

        [Header("Movement")]
        [SerializeField, Min(0.01f)] private float _moveDuration = 0.6f;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float _waitBeforeThrow = 1f;
        [SerializeField, Min(0f)] private float _waitAfterThrow = 1f;

        [Header("Throw")]
        [SerializeField, Min(0.1f)] private float _throwFlightDuration = 1.2f;
        [SerializeField, Min(0f)] private float _spinSpeed = 5f;

        [Header("Runtime")]
        private readonly Queue<Rigidbody> _pendingKeyboards = new Queue<Rigidbody>();
        private readonly HashSet<Rigidbody> _queuedKeyboards = new HashSet<Rigidbody>();
        private bool _sequenceRunning;

        void Awake()
        {
            _actor.position = _hiddenPosition;
        }

        void OnDisable()
        {
            StopAllCoroutines();
            foreach (Rigidbody keyboard in _pendingKeyboards)
            {
                if (keyboard == null) continue;
                keyboard.isKinematic = false;
                keyboard.useGravity = true;
                keyboard.WakeUp();
            }
            _pendingKeyboards.Clear();
            _queuedKeyboards.Clear();
            _sequenceRunning = false;
            if (_actor != null)
                _actor.position = _hiddenPosition;
        }

        void OnTriggerEnter(Collider other)
        {
            Rigidbody keyboard = other.attachedRigidbody;
            if (keyboard == null || keyboard.isKinematic || !IsKeyboard(keyboard)) return;
            if (!_queuedKeyboards.Add(keyboard)) return;

            keyboard.linearVelocity = Vector3.zero;
            keyboard.angularVelocity = Vector3.zero;
            keyboard.useGravity = false;
            keyboard.isKinematic = true;
            _pendingKeyboards.Enqueue(keyboard);

            if (_sequenceRunning) return;
            _sequenceRunning = true;
            StartCoroutine(ReturnKeyboardSequence());
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
            }

            keyboard = null;
            return false;
        }

        /// <summary>
        /// 대기 큐에서 가장 오래 기다린 키보드를 꺼내고 중복 등록 목록에서 제거한다.
        /// 유효한 항목이 없으면 false를 반환한다.
        /// </summary>
        private bool TryTakeKeyboard(out Rigidbody keyboard)
        {
            if (!TryPeekKeyboard(out keyboard)) return false;
            _pendingKeyboards.Dequeue();
            _queuedKeyboards.Remove(keyboard);
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
        /// _throwOriginOffset, _throwFlightDuration, _spinSpeed를 사용해 위치와 속도를 갱신한다.
        /// </summary>
        private void LaunchKeyboard(Rigidbody keyboard)
        {
            Vector3 origin = _actor.position + _throwOriginOffset;
            keyboard.position = origin;
            keyboard.useGravity = true;
            keyboard.isKinematic = false;
            keyboard.detectCollisions = true;
            keyboard.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            keyboard.linearVelocity = CalculateLaunchVelocity(origin, _throwTargetPosition);
            keyboard.angularVelocity = Random.insideUnitSphere * _spinSpeed;
            keyboard.WakeUp();
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
