using System.Collections;

using UnityEngine;

using KeyboardModeling;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorkbenchKeyboardHolder : MonoBehaviour, IKeyboardHolder
    {
        private const float ROTATION_DURATION = 0.35f;

        [Header("Keyboard")]
        private Rigidbody _keyboardBody;
        private Quaternion _initialKeyboardRotation;

        /// <summary>
        /// 작업대에 고정된 키보드의 Rigidbody를 반환한다.
        /// _keyboardBody를 외부 상호작용 코드가 조회할 수 있게 제공한다.
        /// </summary>
        public Rigidbody KeyboardBody => _keyboardBody;

        /// <summary>
        /// 작업대에 키보드를 고정할 때 결정한 최초 목표 회전을 반환한다.
        /// 편집 중 회전이나 재진입에 영향을 받지 않는 복원 기준을 제공한다.
        /// </summary>
        public Quaternion InitialKeyboardRotation => _initialKeyboardRotation;

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
            _initialKeyboardRotation = transform.rotation * Quaternion.Euler(-57f, 0f, 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            body.transform.SetParent(transform, true);

            Vector3 localCenterOffset = pickupTarget.GetVisualCenterLocalPosition(body.transform);
            body.position = transform.position - body.transform.TransformVector(localCenterOffset);
            StartCoroutine(RotateKeyboardToHolder(body, localCenterOffset));

            KeyboardProtectionCover cover = body.GetComponentInChildren<KeyboardProtectionCover>();
            if (cover != null)
                cover.RemoveFrom(body.transform);
        }

        /// <summary>
        /// 키보드의 시각적 중앙을 holder 위치에 유지하며 body를 목표 회전까지 보간한다.
        /// body와 localCenterOffset을 사용하며 키보드가 해제되면 회전을 중단한다.
        /// </summary>
        private IEnumerator RotateKeyboardToHolder(Rigidbody body, Vector3 centerOffset)
        {
            Quaternion startRotation = body.rotation;
            Quaternion targetRotation = _initialKeyboardRotation;
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
                body.position = transform.position - body.transform.TransformVector(centerOffset);
                yield return null;
            }

            if (_keyboardBody != body)
                yield break;

            body.rotation = targetRotation;
            body.position = transform.position - body.transform.TransformVector(centerOffset);
        }

        /// <summary>
        /// body가 이 holder에 고정된 키보드인지 확인해 보관 상태를 해제한다.
        /// body를 사용하며, 해제되면 이후 키보드가 holder에 다시 고정될 수 있다.
        /// </summary>
        public void ReleaseKeyboard(Rigidbody body)
        {
            if (_keyboardBody == body)
                _keyboardBody = null;
        }
    }
}
