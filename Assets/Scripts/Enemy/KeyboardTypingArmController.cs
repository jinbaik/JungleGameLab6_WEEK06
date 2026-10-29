using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardTypingArmController : MonoBehaviour
    {
        private const float REACH_MARGIN = 0.0001f;
        private const float REAR_FACING_YAW = 270f;

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

        [Header("Elbow Constraint")]
        [SerializeField] private bool _keepElbowBehind;
        [Tooltip("팔 루트의 로컬 +Z 방향으로 유지할 어깨 기준 팔꿈치 피벗의 최소 거리입니다.")]
        [SerializeField, Min(0f)] private float _minimumElbowBackOffset = 0.005f;

        [Header("Press Motion")]
        [SerializeField] private bool _useUnscaledTime = true;
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
        private int _diagnosticPressCount;
        private bool _diagnosticMotion;
        private bool _diagnosticMotionStarted;
        public bool IsPressing => _isPressing;
        public string LastPressFailure { get; private set; }

        /// <summary>duration의 40%를 누름, 60%를 복귀 시간으로 설정하여 대전별 팔 동작을 변경한다.</summary>
        public void ConfigureTournamentMotion(float duration)
        {
            _pressDuration = Mathf.Max(0.01f, duration * 0.4f);
            _retractDuration = Mathf.Max(0.01f, duration * 0.6f);
        }

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
        }

        void OnDisable()
        {
            StopPress();
        }

        void LateUpdate()
        {
            if (!_isPressing)
                return;
            if (_diagnosticMotion && !_diagnosticMotionStarted)
            {
                _diagnosticMotionStarted = true;
                Debug.Log($"[AutoAttack][MotionTick] Arm={name}, Id={GetInstanceID()}, Delta={Time.unscaledDeltaTime:F4}, Tip={_contactPoint.position}, PressDuration={_pressDuration}, RetractDuration={_retractDuration}", this);
            }
            _elapsed += _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float scale = transform.lossyScale.x;
            Vector3 bottom = _point - _normal * (_pressDepth * scale);
            if (_elapsed < _pressDuration)
            {
                float amount = Mathf.SmoothStep(0, 1, _elapsed / _pressDuration);
                if (!ApplyTipPose(Vector3.Lerp(_point, bottom, amount), _normal))
                {
                    StopPress();
                    return;
                }
            }
            else
            {
                ReleaseKeycap();
                float amount = Mathf.Clamp01((_elapsed - _pressDuration) / _retractDuration);
                Vector3 hover = _point + _normal * (_hoverHeight * scale);
                if (!ApplyTipPose(Vector3.Lerp(bottom, hover, Mathf.SmoothStep(0, 1, amount)), _normal))
                {
                    StopPress();
                    return;
                }
                if (amount >= 1)
                {
                    _isPressing = false;
                    if (_diagnosticMotion)
                        Debug.Log($"[AutoAttack][MotionEnd] Arm={name}, Id={GetInstanceID()}, Tip={_contactPoint.position}, Target={_point}, Elapsed={_elapsed:F4}", this);
                    _diagnosticMotion = false;
                }
            }
        }

        /// <summary>
        /// 전달받은 키캡으로 즉시 이동하고 짧게 누르는 동작을 시작한다.
        /// keycap의 윗면과 방향을 사용해 이전 누름을 교체하며 대상이 없거나 도달 불가능하면 false를 반환한다.
        /// </summary>
        public bool PressKey(Transform keycap)
        {
            return BeginPress(keycap);
        }

        /// <summary>
        /// 자동공격에서 지정한 키캡을 기계팔로 누른다.
        /// keycap을 공통 관절 애니메이션에 전달하며 플레이어 입력 이벤트나 데미지는 발생시키지 않고 요청 성공 여부를 반환한다.
        /// </summary>
        public bool PressKeyAutomatically(Transform keycap)
        {
            return BeginPress(keycap);
        }

        /// <summary>
        /// 진행 중인 타건을 취소하고 키캡 눌림 표시를 해제한다.
        /// 입력값 없이 현재 누름 상태와 대상 참조를 정리한다.
        /// </summary>
        public void CancelPress()
        {
            StopPress();
        }

        /// <summary>
        /// 지정한 키캡의 윗면으로 이동하고 짧은 누름 동작을 시작한다.
        /// keycap의 위치와 Collider를 사용해 관절과 키캡 표시를 변경하며 도달 가능 여부를 반환한다.
        /// </summary>
        private bool BeginPress(Transform keycap)
        {
            if (!isActiveAndEnabled || keycap == null)
            {
                LastPressFailure = !isActiveAndEnabled ? "ArmInactive" : "KeycapMissing";
                return false;
            }
            BoxCollider collider = keycap.GetComponent<BoxCollider>();
            if (collider == null)
            {
                LastPressFailure = "BoxColliderMissing";
                return false;
            }
            Vector3 point = keycap.TransformPoint(collider.center + Vector3.up * collider.size.y * 0.5f);
            Vector3 normal = keycap.up;
            float scale = transform.lossyScale.x;
            if (!TryCalculatePose(point, normal, out Vector3 contactAngles, out string failure) ||
                !TryCalculatePose(point - normal * (_pressDepth * scale), normal, out _, out failure) ||
                !TryCalculatePose(point + normal * (_hoverHeight * scale), normal, out _, out failure))
            {
                LastPressFailure = failure;
                return false;
            }
            LastPressFailure = null;
            StopPress();
            _point = point;
            _normal = normal;
            _elapsed = 0;
            _actuatedKeycap = keycap;
            _actuatedKeyboard = keycap.GetComponentInParent<KeyboardInputController>();
            Vector3 previousTip = _contactPoint.position;
            ApplyPose(contactAngles, _normal);
            if (_actuatedKeyboard != null)
                _actuatedKeyboard.SetKeycapActuated(keycap, true);
            _isPressing = true;
            _diagnosticPressCount++;
            _diagnosticMotion = _diagnosticPressCount <= 3;
            _diagnosticMotionStarted = false;
            if (_diagnosticMotion)
                Debug.Log($"[AutoAttack][MotionBegin] Arm={name}, Id={GetInstanceID()}, Key={keycap.name}, TipBefore={previousTip}, TipAfter={_contactPoint.position}, Target={_point}, Shoulder={_shoulder.localEulerAngles}, Elbow={_elbow.localEulerAngles}", this);
            return true;
        }

        /// <summary>
        /// point와 normal에 도달할 관절 자세를 계산하고 팔 길이와 후방 제약을 확인한다.
        /// jointAngles의 x, y, z에 베이스 Y축과 어깨 및 팔꿈치 Z축 각도를 반환하며 실패 이유를 failure에 저장한다.
        /// </summary>
        private bool TryCalculatePose(Vector3 point, Vector3 normal, out Vector3 jointAngles, out string failure)
        {
            jointAngles = Vector3.zero;
            failure = null;
            Vector3 wristPosition = transform.InverseTransformPoint(point) + transform.InverseTransformDirection(normal).normalized * _toolLength;
            Vector3 offset = wristPosition - _shoulderOrigin;
            float distance = offset.magnitude;
            if (distance >= _upperLength + _lowerLength - REACH_MARGIN || distance <= Mathf.Abs(_upperLength - _lowerLength) + REACH_MARGIN)
            {
                failure = $"OutOfReach Distance={distance:F4}, Min={Mathf.Abs(_upperLength - _lowerLength):F4}, Max={_upperLength + _lowerLength:F4}, Target={point}, Arm={transform.position}, Scale={transform.lossyScale}";
                return false;
            }
            float horizontal = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
            float yaw = horizontal > REACH_MARGIN
                ? Mathf.Atan2(-offset.z, offset.x) * Mathf.Rad2Deg
                : _keepElbowBehind ? REAR_FACING_YAW : _baseYaw.localEulerAngles.y;
            float cosine = (offset.sqrMagnitude - _upperLength * _upperLength - _lowerLength * _lowerLength) / (2 * _upperLength * _lowerLength);
            float elbow = Mathf.Acos(Mathf.Clamp(cosine, -1, 1));
            Vector3 primary = CalculateCandidate(yaw, horizontal, offset.y, elbow);
            Vector3 alternative = CalculateCandidate(yaw + 180f, -horizontal, offset.y, elbow);
            bool primaryAllowed = IsPoseAllowed(primary);
            bool alternativeAllowed = IsPoseAllowed(alternative);
            if (!primaryAllowed && !alternativeAllowed)
            {
                failure = $"ElbowConstraint Target={point}, MinimumBackOffset={_minimumElbowBackOffset:F4}, Arm={transform.position}";
                return false;
            }
            jointAngles = !primaryAllowed || (alternativeAllowed && GetPoseChange(alternative) < GetPoseChange(primary))
                ? alternative
                : primary;
            return true;
        }

        /// <summary>
        /// yaw와 수평 및 수직 거리, elbow 라디안 각도로 한 관절 후보를 계산한다.
        /// 베이스 Y축과 어깨 및 팔꿈치 Z축의 도 단위 각도를 Vector3로 반환한다.
        /// </summary>
        private Vector3 CalculateCandidate(float yaw, float horizontal, float vertical, float elbow)
        {
            float shoulder = Mathf.Atan2(vertical, horizontal) - Mathf.PI * 0.5f
                - Mathf.Atan2(_lowerLength * Mathf.Sin(elbow), _upperLength + _lowerLength * Mathf.Cos(elbow));
            return new Vector3(yaw, shoulder * Mathf.Rad2Deg, elbow * Mathf.Rad2Deg);
        }

        /// <summary>
        /// jointAngles 후보의 팔꿈치 피벗이 어깨 기준 후방 여유 거리를 만족하는지 확인한다.
        /// 팔 루트의 로컬 좌표를 사용하며 후방 제약이 꺼져 있으면 모든 후보를 허용한다.
        /// </summary>
        private bool IsPoseAllowed(Vector3 jointAngles)
        {
            if (!_keepElbowBehind)
                return true;
            Vector3 elbowPosition = _shoulderOrigin
                + Quaternion.Euler(0f, jointAngles.x, 0f)
                * (Quaternion.Euler(0f, 0f, jointAngles.y) * _elbow.localPosition);
            return elbowPosition.z >= _shoulderOrigin.z + _minimumElbowBackOffset;
        }

        /// <summary>
        /// jointAngles로 이동할 때 현재 자세에서 변하는 각도의 합을 계산한다.
        /// 제약이 켜져 있으면 어깨와 팔꿈치도 비교하며 꺼져 있으면 기존 베이스 회전 기준을 유지한다.
        /// </summary>
        private float GetPoseChange(Vector3 jointAngles)
        {
            float change = Mathf.Abs(Mathf.DeltaAngle(_baseYaw.localEulerAngles.y, jointAngles.x));
            if (_keepElbowBehind)
            {
                change += Mathf.Abs(Mathf.DeltaAngle(_shoulder.localEulerAngles.z, jointAngles.y));
                change += Mathf.Abs(Mathf.DeltaAngle(_elbow.localEulerAngles.z, jointAngles.z));
            }
            return change;
        }

        /// <summary>
        /// point와 normal로 유효한 관절 자세를 계산하고 적용한다.
        /// 성공 여부를 반환하며 실패하면 관절을 변경하지 않고 LastPressFailure에 원인을 저장한다.
        /// </summary>
        private bool ApplyTipPose(Vector3 point, Vector3 normal)
        {
            if (!TryCalculatePose(point, normal, out Vector3 jointAngles, out string failure))
            {
                LastPressFailure = failure;
                return false;
            }
            ApplyPose(jointAngles, normal);
            return true;
        }

        /// <summary>
        /// 검증된 jointAngles와 키캡 normal을 관절 Transform에 적용한다.
        /// 베이스, 어깨, 팔꿈치 회전과 키캡을 향하는 손목 자세를 변경한다.
        /// </summary>
        private void ApplyPose(Vector3 jointAngles, Vector3 normal)
        {
            _baseYaw.localRotation = Quaternion.Euler(0f, jointAngles.x, 0f);
            _shoulder.localRotation = Quaternion.Euler(0f, 0f, jointAngles.y);
            _elbow.localRotation = Quaternion.Euler(0f, 0f, jointAngles.z);
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
            if (_isPressing && _diagnosticMotion)
                Debug.Log($"[AutoAttack][MotionStopped] Arm={name}, Id={GetInstanceID()}, Elapsed={_elapsed:F4}, Active={isActiveAndEnabled}", this);
            _isPressing = false;
            ReleaseKeycap();
        }
    }
}
