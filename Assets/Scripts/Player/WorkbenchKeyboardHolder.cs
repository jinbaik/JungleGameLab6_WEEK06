using System.Collections;

using UnityEngine;

using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorkbenchKeyboardHolder : MonoBehaviour, IKeyboardHolder
    {
        private const float ROTATION_DURATION = 0.35f;

        [Header("Placement Pose")]
        [SerializeField] private Vector3 _placementCenterOffset;
        [SerializeField] private Vector3 _placementEulerAngles = new Vector3(-57f, 0f, 0f);
        [SerializeField] private Collider _placementSurface;
        [SerializeField] private Renderer _placementSurfaceVisual;

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

        /// <summary>
        /// 본체의 시각 중심 오프셋과 Inspector의 배치 중심 및 회전으로 최종 자세를 반환한다.
        /// centerOffset과 body의 하단부 형상을 사용하며 조립 키보드는 작업대 표면 위로 보정한 자세를 반환한다.
        /// </summary>
        public Pose GetPlacementPose(Vector3 centerOffset, Rigidbody body = null)
        {
            Quaternion rotation = transform.rotation * Quaternion.Euler(_placementEulerAngles);
            Pose pose = new Pose(transform.TransformPoint(_placementCenterOffset) - rotation * centerOffset, rotation);
            if (body != null && body.TryGetComponent(out KeyboardAssemblyState assembly)
                && assembly.Initialized && assembly.LowerInstalled && _placementSurface != null)
                pose = AlignLowerToSurface(body, pose);
            return pose;
        }

        /// <summary>
        /// body의 하단부 바닥과 작업대 상판 및 매트 윗면을 비교해 pose의 침투를 보정한다.
        /// 하단부 메시 경계와 지지면 Collider 및 Renderer를 사용해 위쪽 보정만 반환 자세에 적용한다.
        /// </summary>
        private Pose AlignLowerToSurface(Rigidbody body, Pose pose)
        {
            float surfaceHeight = _placementSurface.bounds.max.y;
            if (_placementSurfaceVisual != null && _placementSurfaceVisual.enabled
                && _placementSurfaceVisual.gameObject.activeInHierarchy)
                surfaceHeight = Mathf.Max(surfaceHeight, _placementSurfaceVisual.bounds.max.y);
            MeshRenderer lower = body.GetComponent<KeyboardCustomization>().CaseRenderers[0];
            Bounds bounds = lower.localBounds;
            float bottom = float.PositiveInfinity;
            Matrix4x4 targetMatrix = Matrix4x4.TRS(pose.position, pose.rotation, body.transform.lossyScale);
            Matrix4x4 lowerToRoot = body.transform.worldToLocalMatrix * lower.transform.localToWorldMatrix;
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (index & 1) == 0 ? -1f : 1f,
                    (index & 2) == 0 ? -1f : 1f,
                    (index & 4) == 0 ? -1f : 1f));
                bottom = Mathf.Min(bottom, targetMatrix.MultiplyPoint3x4(lowerToRoot.MultiplyPoint3x4(corner)).y);
            }
            pose.position += Vector3.up * Mathf.Max(0f, surfaceHeight + 0.003f - bottom);
            return pose;
        }

        /// <summary>
        /// 빈 Holder에 body를 명시적으로 배치하고 점유 상태와 최초 회전을 저장한다.
        /// centerOffset으로 최종 자세를 정하며 속도와 중력을 정리하고 고정 성공 여부를 반환한다.
        /// </summary>
        public bool TryPlaceKeyboard(Rigidbody body, Vector3 centerOffset)
        {
            if (_keyboardBody != null)
                return false;

            Pose pose = GetPlacementPose(centerOffset, body);
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            _keyboardBody = body;
            _initialKeyboardRotation = pose.rotation;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.transform.SetParent(transform, true);
            body.position = pose.position;
            body.rotation = pose.rotation;
            body.transform.SetPositionAndRotation(pose.position, pose.rotation);
            Game.Logging.CustomizationTelemetry.Emit(body.GetComponent<KeyboardCustomization>(), "WorkbenchPlaced");
            return true;
        }

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

            if (body.TryGetComponent(out KeyboardAssemblyState assembly) && assembly.Initialized)
            {
                Vector3 localCenter = pickupTarget.GetVisualCenterLocalPosition(body.transform);
                Vector3 centerOffset = Quaternion.Inverse(body.rotation) * body.transform.TransformVector(localCenter);
                TryPlaceKeyboard(body, centerOffset);
                KeyboardProtectionCover assemblyCover = body.GetComponentInChildren<KeyboardProtectionCover>();
                if (assemblyCover != null) assemblyCover.RemoveFrom(body.transform);
                return;
            }

            _keyboardBody = body;
            _initialKeyboardRotation = GetPlacementPose(Vector3.zero).rotation;
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
