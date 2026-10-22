using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerFootstepController : MonoBehaviour
{
    [Header("Footstep Timing")]
    [SerializeField, Min(0.01f)] private float _stepDistance = 2.2f;
    [SerializeField, Min(0f)] private float _minimumWalkingSpeed = 0.1f;
    [SerializeField, Min(0f)] private float _minimumStepInterval = 0.3f;
    private PlayerMovement _movement;
    private float _distanceSinceStep;
    private bool _isWalking;
    private double _lastStepTime = double.NegativeInfinity;

    void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
    }

    void OnEnable()
    {
        _distanceSinceStep = 0f;
        _isWalking = false;
        _movement.MovementProcessed += HandleMovementProcessed;
    }

    void OnDisable()
    {
        _movement.MovementProcessed -= HandleMovementProcessed;
        _distanceSinceStep = 0f;
        _isWalking = false;
    }

    /// <summary>
    /// displacement와 canStep으로 첫 발과 이동 거리별 발소리를 footPosition에 재생하되 최소 재생 간격을 지킨다.
    /// 정지·공중·이동 차단 시 보행 상태와 누적 거리를 초기화하고 마지막 재생 시각은 유지한다.
    /// </summary>
    private void HandleMovementProcessed(Vector3 displacement, Vector3 footPosition, bool canStep)
    {
        float distance = Vector3.ProjectOnPlane(displacement, Vector3.up).magnitude;
        if (!canStep || Time.deltaTime <= 0f || distance <= Mathf.Epsilon || distance / Time.deltaTime < _minimumWalkingSpeed)
        {
            _distanceSinceStep = 0f;
            _isWalking = false;
            return;
        }

        if (!_isWalking)
        {
            _isWalking = true;
            _distanceSinceStep = 0f;
            if (Time.timeAsDouble - _lastStepTime < _minimumStepInterval) return;

            AudioManager.instance.PlayOneShot(FMODEvents.instance.FootSteps, footPosition);
            _lastStepTime = Time.timeAsDouble;
            return;
        }

        _distanceSinceStep += distance;
        float stepDistance = Mathf.Max(0.01f, _stepDistance);
        if (_distanceSinceStep < stepDistance) return;
        if (Time.timeAsDouble - _lastStepTime < _minimumStepInterval) return;

        _distanceSinceStep %= stepDistance;
        AudioManager.instance.PlayOneShot(FMODEvents.instance.FootSteps, footPosition);
        _lastStepTime = Time.timeAsDouble;
    }
}
