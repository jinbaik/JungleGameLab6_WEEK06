using System;

using UnityEngine;

using KeyboardModeling;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class MonitorKeyboardHolder : MonoBehaviour, IKeyboardHolder
    {
        [Header("Monitor Focus")]
        [SerializeField] private Transform _monitorFocusTarget;

        [Header("Keyboard")]
        private Rigidbody _keyboardBody;

        /// <summary>
        /// 키보드와 함께 볼 모니터 화면의 중심 위치를 반환한다.
        /// _monitorFocusTarget을 사용하며 플레이어의 카메라 포커스 목표를 제공한다.
        /// </summary>
        public Transform MonitorFocusTarget => _monitorFocusTarget;

        /// <summary>
        /// 모니터 책상에 고정된 키보드의 Rigidbody를 반환한다.
        /// _keyboardBody를 사용하며 외부 상호작용 코드가 현재 보관 상태를 확인할 수 있게 한다.
        /// </summary>
        public Rigidbody KeyboardBody => _keyboardBody;

        public event Action<bool> KeyboardChanged;

        /// <summary>
        /// 본체 시각 중심 오프셋으로 기존 모니터 Holder의 최종 배치 자세를 반환한다.
        /// centerOffset을 Holder 방향에 맞춰 중심 정렬하며 실제 위치와 점유는 변경하지 않는다.
        /// </summary>
        public Pose GetPlacementPose(Vector3 centerOffset)
        {
            return new Pose(transform.position - transform.rotation * centerOffset, transform.rotation);
        }

        /// <summary>
        /// 빈 Holder에 body를 명시적으로 배치하고 기존 화면 전원 이벤트를 알린다.
        /// centerOffset으로 자세를 정하며 속도를 정리하고 중력을 끈 뒤 배치 성공 여부를 반환한다.
        /// </summary>
        public bool TryPlaceKeyboard(Rigidbody body, Vector3 centerOffset)
        {
            if (_keyboardBody != null) return false;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            Pose pose = GetPlacementPose(centerOffset);
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.transform.SetParent(transform, true);
            body.position = pose.position;
            body.rotation = pose.rotation;
            body.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _keyboardBody = body;
            KeyboardChanged?.Invoke(true);
            return true;
        }

        void OnTriggerEnter(Collider other)
        {
            TryStoreKeyboard(other);
        }

        void OnTriggerStay(Collider other)
        {
            TryStoreKeyboard(other);
        }

        /// <summary>
        /// 감지 영역의 other에서 손을 놓은 키보드를 찾아 모니터 책상에 고정한다.
        /// Collider의 Rigidbody와 KeyboardPickupTarget을 사용하며 거치 상태와 키보드 위치 및 물리 상태를 변경한다.
        /// </summary>
        private void TryStoreKeyboard(Collider other)
        {
            if (_keyboardBody != null)
                return;

            Rigidbody body = other.attachedRigidbody;
            if (body == null || body.isKinematic)
                return;

            KeyboardPickupTarget pickupTarget = body.GetComponentInParent<KeyboardPickupTarget>();
            if (pickupTarget == null)
                pickupTarget = body.GetComponentInChildren<KeyboardPickupTarget>();
            if (pickupTarget == null)
                return;

            _keyboardBody = body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            body.transform.SetParent(transform, true);
            body.transform.localRotation = Quaternion.identity;
            Vector3 visualCenterLocalPosition = pickupTarget.GetVisualCenterLocalPosition(body.transform);
            body.transform.localPosition = -Vector3.Scale(
                visualCenterLocalPosition,
                body.transform.localScale);

            KeyboardProtectionCover cover = body.GetComponentInChildren<KeyboardProtectionCover>();
            if (cover != null)
                cover.RemoveFrom(body.transform);

            KeyboardChanged?.Invoke(true);
        }

        /// <summary>
        /// body가 모니터 책상에 고정된 키보드이면 보관 상태를 해제한다.
        /// body를 사용하며 이후 다른 키보드를 잡을 수 있도록 _keyboardBody를 비운다.
        /// </summary>
        public void ReleaseKeyboard(Rigidbody body)
        {
            if (_keyboardBody == body)
            {
                _keyboardBody = null;
                KeyboardChanged?.Invoke(false);
            }
        }
    }
}
