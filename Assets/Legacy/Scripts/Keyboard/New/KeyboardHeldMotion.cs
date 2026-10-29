using UnityEngine;

/// <summary>
/// 손에 든 키보드의 물리 상태와 카메라 앞 목표 자세를 관리한다.
/// 카메라와 들기 설정을 사용하여 벽 충돌을 피하면서 Rigidbody를 이동한다.
/// </summary>
internal sealed class KeyboardHeldMotion
{
    private readonly Camera _camera;

    /// <summary>
    /// camera를 들기 목표 자세의 기준으로 보관한다.
    /// 카메라 참조만 저장하며 키보드 상태는 변경하지 않는다.
    /// </summary>
    public KeyboardHeldMotion(Camera camera)
    {
        _camera = camera;
    }

    /// <summary>
    /// state를 손에 든 물리 상태로 만들고 padCollider와의 겹침을 확인한다.
    /// 속도와 중력을 정리하며 패드에 바로 놓을 수 있는지 반환한다.
    /// </summary>
    public bool BeginHold(KeyboardInteractionController.KeyboardState state, BoxCollider padCollider)
    {
        if (!state.Body.isKinematic)
        {
            state.Body.linearVelocity = Vector3.zero;
            state.Body.angularVelocity = Vector3.zero;
        }

        state.Body.isKinematic = true;
        state.Body.useGravity = false;
        return !Physics.ComputePenetration(state.Collider, state.Body.position, state.Body.rotation,
            padCollider, padCollider.transform.position, padCollider.transform.rotation, out _, out _);
    }

    /// <summary>
    /// state의 Rigidbody를 카메라 앞 목표 자세로 이동한다.
    /// 거리와 회전, 추적 속도 및 충돌 설정을 사용하며 위치와 회전을 변경한다.
    /// </summary>
    public void Move(KeyboardInteractionController.KeyboardState state, float holdDistance,
        float followSpeed, Vector3 holdEulerAngles, LayerMask blockLayers, float wallGap)
    {
        Rigidbody body = state.Body;
        Quaternion rotation = _camera.transform.rotation * Quaternion.Euler(holdEulerAngles);
        Vector3 center = _camera.transform.position + _camera.transform.forward * holdDistance;
        Vector3 position = center - rotation * Vector3.Scale(state.Collider.center, body.transform.lossyScale);
        float amount = 1f - Mathf.Exp(-followSpeed * Time.fixedDeltaTime);

        Vector3 nextPosition = Vector3.Lerp(body.position, position, amount);
        Quaternion nextRotation = Quaternion.Slerp(body.rotation, rotation, amount);
        ConstrainPose(state, blockLayers, wallGap, ref nextPosition, ref nextRotation);

        body.MovePosition(nextPosition);
        body.MoveRotation(nextRotation);
    }

    /// <summary>
    /// state의 손에 든 물리 상태를 해제한다.
    /// Rigidbody의 중력을 다시 켜고 동적 상태로 변경한다.
    /// </summary>
    public void Drop(KeyboardInteractionController.KeyboardState state)
    {
        state.Body.isKinematic = false;
        state.Body.useGravity = true;
    }

    /// <summary>
    /// state의 목표 position과 rotation이 벽을 통과하지 않도록 제한한다.
    /// blockLayers와 wallGap을 적용한 충돌 결과에 따라 두 목표 값을 변경한다.
    /// </summary>
    private void ConstrainPose(KeyboardInteractionController.KeyboardState state,
        LayerMask blockLayers, float wallGap, ref Vector3 position, ref Quaternion rotation)
    {
        Rigidbody body = state.Body;
        BoxCollider collider = state.Collider;
        Vector3 scale = body.transform.lossyScale;
        Vector3 scaledCenter = Vector3.Scale(collider.center, scale);
        Vector3 halfExtents = Vector3.Scale(collider.size,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;

        Vector3 currentCenter = body.position + body.rotation * scaledCenter;
        Vector3 targetCenter = position + rotation * scaledCenter;
        Vector3 movement = targetCenter - currentCenter;
        float distance = movement.magnitude;

        if (distance > 0.0001f && Physics.BoxCast(currentCenter, halfExtents, movement / distance,
            out RaycastHit hit, body.rotation, distance, blockLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 stoppedCenter = currentCenter + movement / distance * Mathf.Max(0f, hit.distance - wallGap);
            position = stoppedCenter - rotation * scaledCenter;
        }

        targetCenter = position + rotation * scaledCenter;
        if (!Physics.CheckBox(targetCenter, halfExtents, rotation, blockLayers, QueryTriggerInteraction.Ignore))
            return;

        Quaternion previousRotation = body.rotation;
        if (!Physics.CheckBox(targetCenter, halfExtents, previousRotation, blockLayers, QueryTriggerInteraction.Ignore))
        {
            position = targetCenter - previousRotation * scaledCenter;
            rotation = previousRotation;
            return;
        }

        position = body.position;
        rotation = previousRotation;
    }
}
