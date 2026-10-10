using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardTypingArmController : MonoBehaviour
    {
        private const float REACH_MARGIN = 0.0001f;

        [Header("Joints")]
        [SerializeField] private Transform _baseYaw;
        [SerializeField] private Transform _shoulder;
        [SerializeField] private Transform _elbow;
        [SerializeField] private Transform _wrist;
        [SerializeField] private Transform _contactPoint;
        private float _upperLength;
        private float _lowerLength;
        private float _toolLength;
        private Vector3 _shoulderOrigin;

        [Header("Temporary Smash Input")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private bool _temporarySmashInput = true;
        private KeyboardInputController _activeKeyboard;

        [Header("Press Motion")]
        [SerializeField, Min(0.01f)] private float _pressDuration = 0.035f;
        [SerializeField, Min(0.01f)] private float _retractDuration = 0.055f;
        [SerializeField, Min(0f)] private float _hoverHeight = 0.01f;
        [SerializeField, Min(0f)] private float _pressDepth = 0.003f;
        private float _elapsed;
        private bool _isPressing;
        private Vector3 _point;
        private Vector3 _normal;
        private Transform _actuatedKeycap;
        private KeyboardInputController _actuatedKeyboard;
        public bool IsPressing => _isPressing;

        void Awake()
        {
            _upperLength = _elbow.localPosition.magnitude;
            _lowerLength = _wrist.localPosition.magnitude;
            _toolLength = _wrist.InverseTransformPoint(_contactPoint.position).magnitude;
            _shoulderOrigin = transform.InverseTransformPoint(_shoulder.position);
        }

        void OnEnable()
        {
            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
                renderer.enabled = true;
            if (_interactionController == null)
                _interactionController = FindFirstObjectByType<KeyboardInteractionController>();
            if (_interactionController != null)
            {
                _interactionController.ActiveKeyboardChanged += BindKeyboard;
                BindKeyboard(_interactionController.ActiveKeyboard);
            }
        }

        void OnDisable()
        {
            if (_interactionController != null)
                _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            BindKeyboard(null);
            StopPress();
        }

        void LateUpdate()
        {
            if (!_isPressing)
                return;
            _elapsed += Time.unscaledDeltaTime;
            float scale = transform.lossyScale.x;
            Vector3 bottom = _point - _normal * (_pressDepth * scale);
            if (_elapsed < _pressDuration)
            {
                float amount = Mathf.SmoothStep(0, 1, _elapsed / _pressDuration);
                ApplyTipPose(Vector3.Lerp(_point, bottom, amount), _normal);
            }
            else
            {
                ReleaseKeycap();
                float amount = Mathf.Clamp01((_elapsed - _pressDuration) / _retractDuration);
                Vector3 hover = _point + _normal * (_hoverHeight * scale);
                ApplyTipPose(Vector3.Lerp(bottom, hover, Mathf.SmoothStep(0, 1, amount)), _normal);
                if (amount >= 1)
                    _isPressing = false;
            }
        }

        /// <summary>
        /// 전달받은 키캡으로 즉시 이동하고 짧게 누르는 동작을 시작한다.
        /// keycap의 윗면과 방향을 사용해 이전 누름을 교체하며 대상이 없거나 도달 불가능하면 false를 반환한다.
        /// </summary>
        public bool PressKey(Transform keycap)
        {
            if (!isActiveAndEnabled || keycap == null)
                return false;
            BoxCollider collider = keycap.GetComponent<BoxCollider>();
            if (collider == null)
                return false;
            Vector3 point = keycap.TransformPoint(collider.center + Vector3.up * collider.size.y * 0.5f);
            Vector3 normal = keycap.up;
            if (!CanReach(point + normal * (_hoverHeight * transform.lossyScale.x), normal))
                return false;
            StopPress();
            _point = point;
            _normal = normal;
            _elapsed = 0;
            _actuatedKeycap = keycap;
            _actuatedKeyboard = keycap.GetComponentInParent<KeyboardInputController>();
            ApplyTipPose(_point, _normal);
            if (_actuatedKeyboard != null)
                _actuatedKeyboard.SetKeycapActuated(keycap, true);
            _isPressing = true;
            return true;
        }

        /// <summary>
        /// 스매쉬 입력 대상으로 사용할 키보드를 교체한다.
        /// keyboard를 저장하고 이전 누름과 입력 구독을 해제한 뒤 새 키캡 입력에 구독한다.
        /// </summary>
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            if (_activeKeyboard == keyboard)
                return;
            if (_activeKeyboard != null)
                _activeKeyboard.KeycapPressed -= HandleKeycapPressed;
            StopPress();
            _activeKeyboard = keyboard;
            if (_activeKeyboard != null)
            {
                _activeKeyboard.KeycapPressed += HandleKeycapPressed;
            }
        }

        /// <summary>
        /// 스매쉬 입력의 모든 키캡을 즉시 타건 함수에 전달한다.
        /// keycap과 임시 연결 설정만 확인하여 미니게임 여부나 키 종류로 요청을 제한하지 않는다.
        /// </summary>
        private void HandleKeycapPressed(Transform keycap)
        {
            if (_temporarySmashInput && _interactionController.IsSmashMode)
                PressKey(keycap);
        }
        /// <summary>
        /// 지정한 막대 끝 목표가 팔의 길이 안에 있는지 확인한다.
        /// point와 normal로 필요한 손목 위치를 계산하여 도달 가능 여부를 반환한다.
        /// </summary>
        private bool CanReach(Vector3 point, Vector3 normal)
        {
            Vector3 wrist = transform.InverseTransformPoint(point) + transform.InverseTransformDirection(normal).normalized * _toolLength;
            float distance = (wrist - _shoulderOrigin).magnitude;
            return distance < _upperLength + _lowerLength - REACH_MARGIN && distance > Mathf.Abs(_upperLength - _lowerLength) + REACH_MARGIN;
        }

        /// <summary>
        /// 막대 끝 좌표에 맞춰 베이스와 어깨·팔꿈치·손목 자세를 계산한다.
        /// point와 normal을 사용하고 현재 회전과 가까운 평면 방향을 선택하여 관절 Transform을 변경한다.
        /// </summary>
        private void ApplyTipPose(Vector3 point, Vector3 normal)
        {
            Vector3 wristPosition = transform.InverseTransformPoint(point) + transform.InverseTransformDirection(normal).normalized * _toolLength;
            Vector3 offset = wristPosition - _shoulderOrigin;
            float horizontal = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
            float yaw = horizontal > REACH_MARGIN ? Mathf.Atan2(-offset.z, offset.x) * Mathf.Rad2Deg : _baseYaw.localEulerAngles.y;
            float alternativeYaw = yaw + 180;
            if (Mathf.Abs(Mathf.DeltaAngle(_baseYaw.localEulerAngles.y, alternativeYaw)) < Mathf.Abs(Mathf.DeltaAngle(_baseYaw.localEulerAngles.y, yaw)))
            {
                yaw = alternativeYaw;
                horizontal = -horizontal;
            }
            float cosine = (offset.sqrMagnitude - _upperLength * _upperLength - _lowerLength * _lowerLength) / (2 * _upperLength * _lowerLength);
            float elbow = Mathf.Acos(Mathf.Clamp(cosine, -1, 1));
            float shoulder = Mathf.Atan2(offset.y, horizontal) - Mathf.PI * 0.5f - Mathf.Atan2(_lowerLength * Mathf.Sin(elbow), _upperLength + _lowerLength * Mathf.Cos(elbow));
            _baseYaw.localRotation = Quaternion.Euler(0, yaw, 0);
            _shoulder.localRotation = Quaternion.Euler(0, 0, shoulder * Mathf.Rad2Deg);
            _elbow.localRotation = Quaternion.Euler(0, 0, elbow * Mathf.Rad2Deg);
            Vector3 forward = Vector3.ProjectOnPlane(_baseYaw.forward, normal).normalized;
            _wrist.rotation = Quaternion.LookRotation(forward, -normal);
        }

        /// <summary>
        /// 기계팔이 누르던 키캡의 표시를 해제한다.
        /// 보관한 키보드와 키캡을 사용해 추가 표시 누름을 끄고 참조를 비운다.
        /// </summary>
        private void ReleaseKeycap()
        {
            if (_actuatedKeyboard != null && _actuatedKeycap != null)
                _actuatedKeyboard.SetKeycapActuated(_actuatedKeycap, false);
            _actuatedKeyboard = null;
            _actuatedKeycap = null;
        }

        /// <summary>
        /// 현재 누름만 중단하고 키캡의 추가 표시를 해제한다.
        /// 진행 상태와 대상 참조를 정리하며 기계팔 위치와 표시 여부는 변경하지 않는다.
        /// </summary>
        private void StopPress()
        {
            _isPressing = false;
            ReleaseKeycap();
        }
    }
}
