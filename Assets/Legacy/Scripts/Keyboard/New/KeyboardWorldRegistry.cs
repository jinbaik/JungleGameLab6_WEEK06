using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

/// <summary>
/// 월드 키보드의 등록, 보관 및 자동 운반 예약 상태를 관리한다.
/// Rigidbody와 키보드 상태를 연결하며 사용 가능한 원본을 조회한다.
/// </summary>
internal sealed class KeyboardWorldRegistry
{
    private readonly Dictionary<Rigidbody, KeyboardInteractionController.KeyboardState> _keyboards =
        new Dictionary<Rigidbody, KeyboardInteractionController.KeyboardState>();
    private readonly HashSet<KeyboardInteractionController.KeyboardState> _reserved =
        new HashSet<KeyboardInteractionController.KeyboardState>();
    private readonly HashSet<KeyboardInteractionController.KeyboardState> _stored =
        new HashSet<KeyboardInteractionController.KeyboardState>();

    public IEnumerable<KeyboardInteractionController.KeyboardState> Keyboards => _keyboards.Values;

    /// <summary>
    /// instance의 입력과 Collider를 들기 전 상태로 만들고 pickupBounds를 적용한다.
    /// Rigidbody로 조회 가능한 키보드 상태를 등록하여 반환한다.
    /// </summary>
    public KeyboardInteractionController.KeyboardState Register(GameObject instance, Bounds pickupBounds)
    {
        KeyboardInputController keyboard = instance.GetComponent<KeyboardInputController>();
        if (keyboard != null)
        {
            keyboard.SetInputEnabled(false);
            keyboard.enabled = false;
        }

        Collider[] colliders = instance.GetComponentsInChildren<Collider>();
        bool[] colliderEnabled = new bool[colliders.Length];
        for (int index = 0; index < colliders.Length; index++)
        {
            colliderEnabled[index] = colliders[index].enabled;
            colliders[index].enabled = false;
        }

        Rigidbody body = instance.GetComponent<Rigidbody>();
        if (body == null) body = instance.AddComponent<Rigidbody>();
        BoxCollider collider = instance.GetComponent<BoxCollider>();
        if (collider == null) collider = instance.AddComponent<BoxCollider>();
        collider.center = pickupBounds.center;
        collider.size = pickupBounds.size;
        collider.isTrigger = false;
        collider.enabled = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        KeyboardInteractionController.KeyboardState state = new KeyboardInteractionController.KeyboardState(
            instance, keyboard, body, collider, colliders, colliderEnabled);
        _keyboards.Add(state.Body, state);
        return state;
    }

    /// <summary>
    /// body에 연결된 등록 상태를 찾아 state로 전달한다.
    /// 등록 여부를 반환하며 보관과 예약 상태는 변경하지 않는다.
    /// </summary>
    public bool TryGet(Rigidbody body, out KeyboardInteractionController.KeyboardState state)
    {
        return _keyboards.TryGetValue(body, out state);
    }

    /// <summary>
    /// body에 연결된 등록 상태를 반환한다.
    /// 등록되지 않은 경우 Dictionary의 기존 오류를 유지한다.
    /// </summary>
    public KeyboardInteractionController.KeyboardState Get(Rigidbody body)
    {
        return _keyboards[body];
    }

    /// <summary>
    /// body를 월드 조회 대상에서 제거한다.
    /// 해당 Rigidbody의 기존 등록 여부를 반환한다.
    /// </summary>
    public bool Remove(Rigidbody body)
    {
        return _keyboards.Remove(body);
    }

    /// <summary>
    /// state의 상자 보관 여부를 반환한다.
    /// 등록 및 예약 상태는 변경하지 않는다.
    /// </summary>
    public bool IsStored(KeyboardInteractionController.KeyboardState state)
    {
        return _stored.Contains(state);
    }

    /// <summary>
    /// state를 보관 목록에 추가한다.
    /// 기존 보관 여부를 반환한다.
    /// </summary>
    public bool Store(KeyboardInteractionController.KeyboardState state)
    {
        return _stored.Add(state);
    }

    /// <summary>
    /// state를 보관 목록에서 제거한다.
    /// 기존 보관 여부를 반환한다.
    /// </summary>
    public bool Unstore(KeyboardInteractionController.KeyboardState state)
    {
        return _stored.Remove(state);
    }

    /// <summary>
    /// state의 자동 운반 예약 여부를 반환한다.
    /// 등록 및 보관 상태는 변경하지 않는다.
    /// </summary>
    public bool IsReserved(KeyboardInteractionController.KeyboardState state)
    {
        return _reserved.Contains(state);
    }

    /// <summary>
    /// state를 자동 운반 대상으로 예약한다.
    /// 기존 예약 여부를 반환한다.
    /// </summary>
    public bool Reserve(KeyboardInteractionController.KeyboardState state)
    {
        return _reserved.Add(state);
    }

    /// <summary>
    /// state의 자동 운반 예약을 해제한다.
    /// 기존 예약 여부를 반환한다.
    /// </summary>
    public bool Unreserve(KeyboardInteractionController.KeyboardState state)
    {
        return _reserved.Remove(state);
    }

    /// <summary>
    /// placementPosition에 가장 가까운 사용 가능한 월드 키보드를 찾는다.
    /// 손·패드 대상과 fallReturnHeight 아래 대상을 제외하고 reserved로 전달한다.
    /// </summary>
    public bool TryReserveNearest(Vector3 placementPosition,
        KeyboardInteractionController.KeyboardState held,
        KeyboardInteractionController.KeyboardState placed,
        float fallReturnHeight,
        out KeyboardInteractionController.KeyboardState reserved)
    {
        reserved = null;
        float closestDistance = float.PositiveInfinity;

        foreach (KeyboardInteractionController.KeyboardState state in _keyboards.Values)
        {
            if (state.Root == null || !state.Root.activeInHierarchy ||
                state.Keyboard != null || state == held || state == placed ||
                state.PlacementFailed || _stored.Contains(state) || _reserved.Contains(state) ||
                !state.Collider.enabled || state.Body.isKinematic || !state.Body.useGravity ||
                state.Body.position.y <= fallReturnHeight || state.Body.linearVelocity.sqrMagnitude > 1f)
                continue;

            float distance = (state.Body.position - placementPosition).sqrMagnitude;
            if (distance >= closestDistance) continue;

            closestDistance = distance;
            reserved = state;
        }

        if (reserved == null) return false;

        _reserved.Add(reserved);
        reserved.Body.linearVelocity = Vector3.zero;
        reserved.Body.angularVelocity = Vector3.zero;
        reserved.Body.isKinematic = true;
        reserved.Body.useGravity = false;
        reserved.Collider.enabled = false;
        return true;
    }

    /// <summary>
    /// source의 예약을 해제하고 originalPosition과 originalRotation으로 되돌린다.
    /// 물리와 Collider를 다시 켜며 실제 예약이 없으면 상태를 변경하지 않는다.
    /// </summary>
    public void ReleaseReserved(KeyboardInteractionController.KeyboardState source,
        Vector3 originalPosition, Quaternion originalRotation)
    {
        if (source == null || !_reserved.Remove(source) ||
            source.Root == null || source.Body == null || source.Collider == null)
            return;

        source.Body.position = originalPosition;
        source.Body.rotation = originalRotation;
        source.Body.linearVelocity = Vector3.zero;
        source.Body.angularVelocity = Vector3.zero;
        source.Body.isKinematic = false;
        source.Body.useGravity = true;
        source.Collider.enabled = true;
    }
}
