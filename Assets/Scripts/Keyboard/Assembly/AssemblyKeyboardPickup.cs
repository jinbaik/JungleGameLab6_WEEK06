using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

using Game.Player;

namespace KeyboardModeling.Assembly
{
    [DefaultExecutionOrder(-110)]
    [DisallowMultipleComponent]
    public sealed class AssemblyKeyboardPickup : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private AssemblyFocusController _focusController;
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera _inputCamera;
        [SerializeField] private Rigidbody _keyboardBody;
        [SerializeField] private KeyboardPickupTarget _pickupTarget;

        [Header("Workbench Placement")]
        [SerializeField] private Collider _placementArea;
        private Vector3 _placementPosition;
        private Quaternion _placementRotation;
        private bool _isPlaced;
        private int _placementInputFrame = -1;

        [Header("Selection")]
        [SerializeField, Min(0.1f)] private float _interactionDistance = 12f;
        [SerializeField] private LayerMask _interactionLayers = ~0;

        [Header("Hold Pose")]
        [SerializeField, Min(0.1f)] private float _heldDistance = 2f;
        [SerializeField] private Vector3 _heldOffset = new Vector3(0f, 0.12f, 0f);
        [SerializeField] private Vector3 _heldEulerAngles = new Vector3(-75f, 0f, 0f);
        [SerializeField, Min(0.01f), Tooltip("값이 클수록 들기 목표 자세에 빠르게 접근합니다.")] private float _followSpeed = 12f;

        [Header("Throw")]
        [SerializeField, Min(0f)] private float _throwSpeed = 10f;

        [Header("Runtime State")]
        private Vector3 _centerOffset;
        private bool _isHolding;
        private bool _wasKinematic;
        private bool _usedGravity;
        private bool _hasFocus;
        private bool _acceptClick;

        public bool IsHolding => _isHolding;
        public bool IsPlaced => _isPlaced && !_isHolding && _keyboardBody.isKinematic
            && Vector3.Distance(_keyboardBody.position, _placementPosition) < 0.001f
            && Quaternion.Angle(_keyboardBody.rotation, _placementRotation) < 0.1f;
        public bool PlacementInputConsumed => _placementInputFrame == Time.frameCount;

        void Awake()
        {
            _placementPosition = _keyboardBody.position;
            _placementRotation = _keyboardBody.rotation;
            _isPlaced = _keyboardBody.isKinematic;
        }

        void OnEnable()
        {
            _hasFocus = Application.isFocused;
            _acceptClick = false;
        }

        void Update()
        {
            if (!InGameCursor.CanProcessPointer || !_hasFocus || _focusController.IsFocused || _brain.IsBlending
                || _brain.ActiveVirtualCamera != (ICinemachineCamera)_inputCamera
                || Game.Tutorials.FirstEncounterTutorial.IsBlocking)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            if (!_acceptClick)
            {
                if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
                    _acceptClick = true;
                return;
            }
            if (_isHolding && mouse.rightButton.wasPressedThisFrame)
            {
                _placementInputFrame = Time.frameCount;
                if (IsPlacementAreaInCenter())
                    PlaceOnWorkbench();
                return;
            }
            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            if (_isHolding)
                ThrowKeyboard();
            else if (IsTargetInCenter())
                PickUpKeyboard();
        }

        void FixedUpdate()
        {
            if (_isHolding && _hasFocus && !Game.Tutorials.FirstEncounterTutorial.IsBlocking)
                FollowHeldKeyboard();
        }

        /// <summary>
        /// 일반 모드 출력 화면의 중앙 Ray로 가장 가까운 실체 Collider를 검사한다.
        /// 거리와 레이어를 사용하며 대상 본체와 장착 자식만 선택하고 가림막과 분리 부품은 제외한다.
        /// </summary>
        private bool IsTargetInCenter()
        {
            Physics.SyncTransforms();
            Ray ray = _brain.OutputCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return Physics.Raycast(ray, out RaycastHit hit, _interactionDistance, _interactionLayers,
                QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(_keyboardBody.transform);
        }

        /// <summary>
        /// 화면 중앙 Ray에서 들고 있는 본체와 장착 부품을 제외한 가장 가까운 실체를 검사한다.
        /// 선택 거리와 레이어 및 명시적인 배치 영역 참조를 사용하여 가려지지 않은 작업대인지 반환한다.
        /// </summary>
        private bool IsPlacementAreaInCenter()
        {
            Physics.SyncTransforms();
            Ray ray = _brain.OutputCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, _interactionDistance, _interactionLayers,
                QueryTriggerInteraction.Collide);
            Collider nearest = null;
            float distance = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(_keyboardBody.transform)
                    || (hit.collider.isTrigger && hit.collider != _placementArea))
                    continue;
                if (hit.distance < distance)
                {
                    nearest = hit.collider;
                    distance = hit.distance;
                }
            }
            return nearest == _placementArea;
        }

        /// <summary>
        /// 씬 시작 시 보관한 본체의 월드 위치와 회전으로 키보드를 내려놓는다.
        /// 현재 속도를 정리하고 물리를 고정하며 추종을 종료하고 배치 상태를 갱신한다.
        /// </summary>
        private void PlaceOnWorkbench()
        {
            _isHolding = false;
            _keyboardBody.isKinematic = false;
            _keyboardBody.linearVelocity = Vector3.zero;
            _keyboardBody.angularVelocity = Vector3.zero;
            _keyboardBody.isKinematic = true;
            _keyboardBody.useGravity = false;
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _keyboardBody.position = _placementPosition;
            _keyboardBody.rotation = _placementRotation;
            _keyboardBody.transform.SetPositionAndRotation(_placementPosition, _placementRotation);
            _isPlaced = true;
        }

        /// <summary>
        /// 기존 PickupTarget의 렌더러 중심 계산으로 들기 기준점을 저장한다.
        /// 본체의 원래 물리 상태를 보관하고 속도와 중력을 정지하여 추종 상태로 전환한다.
        /// </summary>
        private void PickUpKeyboard()
        {
            _isPlaced = false;
            _wasKinematic = _keyboardBody.isKinematic;
            _usedGravity = _keyboardBody.useGravity;
            Vector3 localCenter = _pickupTarget.GetVisualCenterLocalPosition(_keyboardBody.transform);
            _centerOffset = Vector3.Scale(localCenter, _keyboardBody.transform.lossyScale);
            if (!_keyboardBody.isKinematic)
            {
                _keyboardBody.linearVelocity = Vector3.zero;
                _keyboardBody.angularVelocity = Vector3.zero;
            }
            _keyboardBody.isKinematic = true;
            _keyboardBody.useGravity = false;
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _isHolding = true;
        }

        /// <summary>
        /// 기존 들기 계산의 카메라 앞 목표 자세를 시각적 중심 오프셋으로 보정한다.
        /// heldDistance, heldOffset, heldEulerAngles와 followSpeed로 본체 위치와 회전을 부드럽게 추종한다.
        /// </summary>
        private void FollowHeldKeyboard()
        {
            Transform cameraTransform = _brain.OutputCamera.transform;
            Quaternion rotation = cameraTransform.rotation * Quaternion.Euler(_heldEulerAngles);
            Vector3 center = cameraTransform.position + cameraTransform.forward * _heldDistance
                + cameraTransform.TransformDirection(_heldOffset);
            Vector3 position = center - rotation * _centerOffset;
            float progress = 1f - Mathf.Exp(-_followSpeed * Time.fixedDeltaTime);
            _keyboardBody.MovePosition(Vector3.Lerp(_keyboardBody.position, position, progress));
            _keyboardBody.MoveRotation(Quaternion.Slerp(_keyboardBody.rotation, rotation, progress));
        }

        /// <summary>
        /// 기존 투척 계산대로 본체에 중력과 카메라 전방 속도를 적용한다.
        /// throwSpeed를 사용하며 보유 상태만 해제하고 장착 부품과 슬롯 및 재고는 유지한다.
        /// </summary>
        private void ThrowKeyboard()
        {
            _isPlaced = false;
            _isHolding = false;
            _keyboardBody.isKinematic = false;
            _keyboardBody.useGravity = true;
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _keyboardBody.linearVelocity = _brain.OutputCamera.transform.forward * _throwSpeed;
            _keyboardBody.angularVelocity = Vector3.zero;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            _acceptClick = false;
        }

        void OnDisable()
        {
            if (!_isHolding || _keyboardBody == null)
                return;
            _isHolding = false;
            _keyboardBody.isKinematic = _wasKinematic;
            _keyboardBody.useGravity = _usedGravity;
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (!_wasKinematic)
            {
                _keyboardBody.linearVelocity = Vector3.zero;
                _keyboardBody.angularVelocity = Vector3.zero;
            }
        }
    }
}
