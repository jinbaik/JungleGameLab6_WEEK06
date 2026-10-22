using System.Collections;

using UnityEngine;

using KeyboardModeling;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class KeyboardWallRack : MonoBehaviour, IKeyboardHolder
    {
        private const float ROTATION_DURATION = 0.35f;

        [Header("Keyboard Display")]
        [SerializeField] private Transform _keyboardMountPoint;
        [SerializeField] private string _persistentId;

        private Rigidbody _keyboardBody;
        private bool _originalIsKinematic;
        private bool _originalUseGravity;

        public string PersistentId => _persistentId;
        public Rigidbody KeyboardBody => _keyboardBody;

        void OnTriggerStay(Collider other)
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

            TryAttachKeyboard(body);
        }

        /// <summary>
        /// body를 빈 진열대에 고정하고 점유 참조와 원래 물리를 보관한다.
        /// immediate가 true인 복원에서는 장착 자세를 즉시 적용하며 꺼낸 뒤 사용할 동적 물리를 지정한다.
        /// </summary>
        public bool TryAttachKeyboard(Rigidbody body, bool immediate = false)
        {
            if (_keyboardBody != null || body == null) return false;
            KeyboardPickupTarget pickupTarget = body.GetComponentInChildren<KeyboardPickupTarget>(true);
            if (pickupTarget == null) return false;
            _keyboardBody = body;
            _originalIsKinematic = !immediate && body.isKinematic;
            _originalUseGravity = immediate || body.useGravity;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;
            body.transform.SetParent(transform, true);
            Vector3 localCenterOffset = pickupTarget.GetVisualCenterLocalPosition(body.transform);
            if (immediate) body.rotation = _keyboardMountPoint.rotation;
            body.position = _keyboardMountPoint.position - body.transform.TransformVector(localCenterOffset);
            if (!immediate)
            {
                StartCoroutine(RotateKeyboardToDisplay(body, localCenterOffset));
                KeyboardProtectionCover cover = body.GetComponentInChildren<KeyboardProtectionCover>();
                if (cover != null) cover.RemoveFrom(body.transform);
            }
            return true;
        }

        /// <summary>
        /// 키보드를 장착 기준점의 회전으로 부드럽게 돌리고 시각적 중앙을 장착 위치에 맞춘다.
        /// body와 localCenterOffset을 사용하며 매 프레임 키보드의 회전과 위치를 갱신한다.
        /// </summary>
        private IEnumerator RotateKeyboardToDisplay(Rigidbody body, Vector3 localCenterOffset)
        {
            Quaternion startRotation = body.rotation;
            Quaternion targetRotation = _keyboardMountPoint.rotation;
            if (Quaternion.Dot(startRotation, targetRotation) < 0f)
            {
                targetRotation = new Quaternion(
                    -targetRotation.x,
                    -targetRotation.y,
                    -targetRotation.z,
                    -targetRotation.w);
            }

            float elapsed = 0f;
            while (_keyboardBody == body && elapsed < ROTATION_DURATION)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / ROTATION_DURATION));
                Quaternion rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
                body.rotation = rotation;
                body.position = _keyboardMountPoint.position - body.transform.TransformVector(localCenterOffset);
                yield return null;
            }

            if (_keyboardBody != body)
                yield break;

            body.rotation = targetRotation;
            body.position = _keyboardMountPoint.position - body.transform.TransformVector(localCenterOffset);
        }

        /// <summary>
        /// body가 현재 걸이에 고정되어 있으면 걸이 상태와 저장된 물리 상태를 해제한다.
        /// body를 사용하며 이후 플레이어가 키보드를 들 수 있도록 물리 설정을 복원한다.
        /// </summary>
        public void ReleaseKeyboard(Rigidbody body)
        {
            if (_keyboardBody != body)
                return;

            _keyboardBody = null;
            body.isKinematic = _originalIsKinematic;
            body.useGravity = _originalUseGravity;
        }
    }
}
