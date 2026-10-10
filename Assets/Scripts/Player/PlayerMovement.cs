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
    private CharacterController _controller;
    private Vector3 _cameraLocalPosition;
    private bool _wasInputCamReady;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 4f;
    [SerializeField] private float _gravity = -20f;
    private float _verticalVelocity;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _cameraLocalPosition = _inputCam.transform.localPosition;
    }

    void OnEnable()
    {
        _moveInput.action.Enable();
    }

    void OnDisable()
    {
        _moveInput.action.Disable();
        _wasInputCamReady = false;
    }

    void Update()
    {
        if (_brain.ActiveVirtualCamera != (ICinemachineCamera)_inputCam || _brain.IsBlending || !_wasInputCamReady || !Application.isFocused)
        {
            _verticalVelocity = 0f;
            return;
        }

        Vector2 input = Vector2.ClampMagnitude(_moveInput.action.ReadValue<Vector2>(), 1f);
        Vector3 forward = Vector3.ProjectOnPlane(_brain.OutputCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 movement = (right * input.x + forward * input.y) * _moveSpeed;

        if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        else _verticalVelocity += _gravity * Time.deltaTime;

        _controller.Move((movement + Vector3.up * _verticalVelocity) * Time.deltaTime);
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
    }
}
