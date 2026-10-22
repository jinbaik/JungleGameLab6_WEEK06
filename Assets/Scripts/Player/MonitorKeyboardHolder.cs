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

        void OnTriggerEnter(Collider other)
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
        }

        /// <summary>
        /// body가 모니터 책상에 고정된 키보드이면 보관 상태를 해제한다.
        /// body를 사용하며 이후 다른 키보드를 잡을 수 있도록 _keyboardBody를 비운다.
        /// </summary>
        public void ReleaseKeyboard(Rigidbody body)
        {
            if (_keyboardBody == body)
                _keyboardBody = null;
        }
    }
}
