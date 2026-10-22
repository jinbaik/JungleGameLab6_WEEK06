using System;

using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineBrain _brain;
    [SerializeField] private CinemachineCamera _inputCam;
    [SerializeField] private InputActionReference _moveInput;

    [Header("Camera")]
    [SerializeField, Min(0f)] private float _cameraHeight = 4.9f;
    private CharacterController _controller;
    private Vector3 _cameraLocalPosition;
    private bool _wasInputCamReady;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 4f;
    [SerializeField] private float _gravity = -20f;
    private float _verticalVelocity;

    public event Action<Vector3, Vector3, bool> MovementProcessed;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _cameraLocalPosition = _inputCam.transform.localPosition;
        _cameraLocalPosition.y = _cameraHeight;
        _inputCam.transform.localPosition = _cameraLocalPosition;
    }

    void OnEnable()
    {
        _moveInput.action.Enable();
    }

    void OnDisable()
    {
        _moveInput.action.Disable();
        _wasInputCamReady = false;
        MovementProcessed?.Invoke(Vector3.zero, transform.position, false);
    }

    void Update()
    {
        if (_brain.ActiveVirtualCamera != (ICinemachineCamera)_inputCam || _brain.IsBlending || !_wasInputCamReady || !Application.isFocused)
        {
            _verticalVelocity = 0f;
            MovementProcessed?.Invoke(Vector3.zero, transform.position, false);
            return;
        }

        Vector2 input = Vector2.ClampMagnitude(_moveInput.action.ReadValue<Vector2>(), 1f);
        Vector3 forward = Vector3.ProjectOnPlane(_brain.OutputCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 movement = (right * input.x + forward * input.y) * _moveSpeed;

        if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        else _verticalVelocity += _gravity * Time.deltaTime;

        Vector3 previousPosition = transform.position;
        bool wasGrounded = _controller.isGrounded;
        _controller.Move((movement + Vector3.up * _verticalVelocity) * Time.deltaTime);
        Vector3 displacement = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up);
        Bounds bounds = _controller.bounds;
        Vector3 footPosition = bounds.center - Vector3.up * bounds.extents.y;
        bool canStep = wasGrounded && _controller.isGrounded && _moveInput.action.enabled && input.sqrMagnitude > 0f;
        MovementProcessed?.Invoke(displacement, footPosition, canStep);
    }

    void LateUpdate()
    {
        bool isInputCamReady = _brain.ActiveVirtualCamera == (ICinemachineCamera)_inputCam && !_brain.IsBlending;
        if (!isInputCamReady)
        {
            _wasInputCamReady = false;
            return;
        }

        if (_wasInputCamReady) return;
        AlignPlayerToCamera();
        _wasInputCamReady = true;
    }

    /// <summary>
    /// destination 월드 위치로 플레이어를 즉시 이동하고 누적 낙하 속도를 초기화한다.
    /// CharacterController의 활성 상태와 시선 방향을 유지하며 이동 관찰자에게 정지 상태를 알린다.
    /// </summary>
    public void TeleportTo(Vector3 destination)
    {
        bool controllerEnabled = _controller.enabled;
        _controller.enabled = false;
        transform.position = destination;
        _verticalVelocity = 0f;
        _controller.enabled = controllerEnabled;
        MovementProcessed?.Invoke(Vector3.zero, transform.position, false);
    }

    /// <summary>
    /// InputCam의 현재 월드 위치와 Awake에서 저장한 로컬 위치로 Player 위치를 맞춘다.
    /// 카메라의 월드 위치를 유지하면서 로컬 위치를 복원하고 수직 속도를 초기화한다.
    /// </summary>
    private void AlignPlayerToCamera()
    {
        Vector3 cameraPosition = _inputCam.transform.position;
        Vector3 cameraOffset = transform.TransformVector(_cameraLocalPosition);

        _controller.enabled = false;
        transform.position = cameraPosition - cameraOffset;
        _inputCam.transform.localPosition = _cameraLocalPosition;
        _controller.enabled = true;
        _verticalVelocity = 0f;
        MovementProcessed?.Invoke(Vector3.zero, transform.position, false);
    }
}
