using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

namespace Game.Player
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [CameraPipeline(CinemachineCore.Stage.Body)]
    public sealed class WorkshopLookCamera : CinemachineComponentBase, IInputAxisOwner
    {
        [Serializable]
        private sealed class ReferenceView
        {
            [SerializeField] private CinemachineCamera _camera;
            [SerializeField] private float _angle;
            public CinemachineCamera Camera => _camera;
            public float Angle => _angle;
        }

        [Header("Cinemachine Input")]
        [SerializeField] private CinemachineInputAxisController _inputController;
        [SerializeField] private WorkshopViewController _workshopView;
        [SerializeField] private InputAxis _horizontalAxis = new InputAxis
            { Range = new Vector2(0f, 360f), Wrap = true, Restrictions = InputAxis.RestrictionFlags.NoRecentering };
        [SerializeField] private InputAxis _verticalAxis = new InputAxis
            { Range = new Vector2(-20f, 20f), Restrictions = InputAxis.RestrictionFlags.NoRecentering };
        private int _blockedFrame;

        [Header("Reference Views In Circular Order")]
        [SerializeField] private ReferenceView[] _references = new ReferenceView[0];

        public CinemachineCamera Camera => (CinemachineCamera)VirtualCamera;
        public override bool IsValid => enabled && _references.Length >= 4;
        public override CinemachineCore.Stage Stage => CinemachineCore.Stage.Body;
        public CinemachineCamera CurrentReference
        {
            get
            {
                ReferenceView nearest = _references[0];
                foreach (ReferenceView view in _references)
                    if (Mathf.Abs(Mathf.DeltaAngle(_horizontalAxis.Value, view.Angle))
                        < Mathf.Abs(Mathf.DeltaAngle(_horizontalAxis.Value, nearest.Angle))) nearest = view;
                return nearest.Camera;
            }
        }

        void Start()
        {
            if (Application.isPlaying) _inputController.ReadControlValueOverride = ReadLookInput;
        }

        void Update()
        {
            if (Application.isPlaying && !_workshopView.CanLookAround)
            {
                _blockedFrame = Time.frameCount;
                CancelInput();
            }
        }

        void OnValidate()
        {
            _horizontalAxis.Range = new Vector2(0f, 360f);
            _horizontalAxis.Wrap = true;
            _verticalAxis.Wrap = false;
            _horizontalAxis.Validate();
            _verticalAxis.Validate();
        }

        /// <summary>axes에 기본 Cinemachine 드라이버가 구동할 가로·세로 축을 등록한다. 축 값과 구도는 변경하지 않는다.</summary>
        public void GetInputAxes(List<IInputAxisOwner.AxisDescriptor> axes)
        {
            axes.Add(new IInputAxisOwner.AxisDescriptor { Name = "Look X (Pan)", DrivenAxis = () => ref _horizontalAxis, Hint = IInputAxisOwner.AxisDescriptor.Hints.X });
            axes.Add(new IInputAxisOwner.AxisDescriptor { Name = "Look Y (Tilt)", DrivenAxis = () => ref _verticalAxis, Hint = IInputAxisOwner.AxisDescriptor.Hints.Y });
        }

        /// <summary>action의 현재 입력을 Cinemachine 기본 reader로 읽는다. 공통 차단 중과 해제 직후 한 프레임은 0을 반환한다.</summary>
        private float ReadLookInput(InputAction action, IInputAxisOwner.AxisDescriptor.Hints hint,
            UnityEngine.Object context, CinemachineInputAxisController.Reader.ControlValueReader reader)
        {
            if (!_workshopView.CanLookAround || Time.frameCount <= _blockedFrame + 1) return 0f;
            return reader(action, hint, context, null);
        }

        /// <summary>두 Cinemachine 축 드라이버의 잔여 속도를 지운다. 현재 축 값과 사용자 감도·반응 설정은 유지한다.</summary>
        private void CancelInput()
        {
            foreach (var controller in _inputController.Controllers)
            {
                if (controller.Owner != this) continue;
                controller.InputValue = 0f;
                if (controller.Name == "Look X (Pan)") controller.Driver.CancelCurrentInput(ref _horizontalAxis);
                else controller.Driver.CancelCurrentInput(ref _verticalAxis);
            }
        }

        /// <summary>reference의 명시된 기준 각도와 세로 중심으로 복귀한다. 잔여 입력을 버리고 다음 프레임까지 회전을 차단한다.</summary>
        public void ResetToReference(CinemachineCamera reference)
        {
            foreach (ReferenceView view in _references)
                if (view.Camera == reference) { _horizontalAxis.Value = view.Angle; break; }
            _verticalAxis.Value = _verticalAxis.Center;
            _blockedFrame = Time.frameCount;
            CancelInput();
        }

        /// <summary>curState에 순환 축 구간의 위치·기준 회전·Lens와 세로 오프셋을 적용한다. Main Camera와 기준 카메라 Transform은 변경하지 않는다.</summary>
        public override void MutateCameraState(ref CameraState curState, float deltaTime)
        {
            if (!IsValid) return;
            float angle = Mathf.Repeat(_horizontalAxis.Value, 360f);
            int index = _references.Length - 1;
            for (int i = 0; i < _references.Length - 1; i++)
                if (angle >= _references[i].Angle && angle < _references[i + 1].Angle) { index = i; break; }
            int next = (index + 1) % _references.Length;
            int previous = (index + _references.Length - 1) % _references.Length;
            int after = (next + 1) % _references.Length;
            ReferenceView a = _references[index];
            ReferenceView b = _references[next];
            float span = Mathf.Repeat(b.Angle - a.Angle, 360f);
            float t = Mathf.Repeat(angle - a.Angle, 360f) / span;
            Vector3 start = a.Camera.transform.position;
            Vector3 end = b.Camera.transform.position;
            Vector3 tangentA = (end - _references[previous].Camera.transform.position)
                * (span / (Mathf.Repeat(a.Angle - _references[previous].Angle, 360f) + span));
            Vector3 tangentB = (_references[after].Camera.transform.position - start)
                * (span / (span + Mathf.Repeat(_references[after].Angle - b.Angle, 360f)));
            float squared = t * t;
            float cubed = squared * t;
            curState.RawPosition = (2f * cubed - 3f * squared + 1f) * start
                + (cubed - 2f * squared + t) * tangentA
                + (-2f * cubed + 3f * squared) * end + (cubed - squared) * tangentB;
            curState.RawOrientation = Quaternion.Slerp(a.Camera.transform.rotation, b.Camera.transform.rotation, t)
                * Quaternion.Euler(_verticalAxis.Value - _verticalAxis.Center, 0f, 0f);
            curState.Lens = LensSettings.Lerp(a.Camera.Lens, b.Camera.Lens, t);
        }
    }
}
