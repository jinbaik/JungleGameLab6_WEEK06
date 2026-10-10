using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineBrain _brain;
    [SerializeField] private CinemachineCamera _inputCam;
    [SerializeField] private InputActionReference _moveInput;
    private CharacterController _controller;

    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 4f;
    [SerializeField] private float _gravity = -20f;
    private float _verticalVelocity;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        _moveInput.action.Enable();
    }

    void OnDisable()
    {
        _moveInput.action.Disable();
    }

    void Update()
    {
        if (_brain.ActiveVirtualCamera != (ICinemachineCamera)_inputCam || _brain.IsBlending || !Application.isFocused)
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
}
