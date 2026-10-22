using UnityEngine;

using KeyboardModeling;

/// <summary>
/// 카메라 중앙에서 월드 키보드와 보관 상자의 상호작용 대상을 찾는다.
/// 등록 상태와 물리 충돌 결과를 사용하여 현재 조준한 대상을 반환한다.
/// </summary>
internal sealed class KeyboardWorldTargeting
{
    private readonly Camera _camera;
    private readonly KeyboardWorldRegistry _world;

    /// <summary>
    /// camera와 world를 조준 판정에 사용할 참조로 저장한다.
    /// 저장된 키보드 상태나 물리 상태는 변경하지 않는다.
    /// </summary>
    public KeyboardWorldTargeting(Camera camera, KeyboardWorldRegistry world)
    {
        _camera = camera;
        _world = world;
    }

    /// <summary>
    /// 카메라 중앙 Ray의 가장 가까운 유효 충돌을 찾는다.
    /// rayDistance, pickupLayers와 손에 든 상자·키보드를 사용하여 hit과 성공 여부를 반환한다.
    /// </summary>
    public bool TryGetHit(float rayDistance, LayerMask pickupLayers, KeyboardStorageBox heldBox,
        KeyboardInteractionController.KeyboardState heldKeyboard, out RaycastHit hit)
    {
        hit = default;
        float closest = float.PositiveInfinity;
        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        foreach (RaycastHit candidate in Physics.RaycastAll(ray, rayDistance, pickupLayers, QueryTriggerInteraction.Collide))
        {
            KeyboardStorageBox box = candidate.collider.GetComponentInParent<KeyboardStorageBox>();
            if (box != null && box == heldBox) continue;
            if (candidate.collider is CharacterController && candidate.collider.GetComponent<PlayerMovement>() != null) continue;
            if (candidate.collider.isTrigger && (box == null || !box.IsTopInteraction(candidate.collider)
                || Vector3.Dot(candidate.normal, box.transform.up) < 0.5f)) continue;
            if (candidate.distance >= closest) continue;
            closest = candidate.distance;
            hit = candidate;
        }

        foreach (KeyboardInteractionController.KeyboardState keyboard in _world.Keyboards)
        {
            if (!keyboard.Root.activeInHierarchy || !keyboard.Collider.enabled || _world.IsStored(keyboard)
                || _world.IsReserved(keyboard) || keyboard == heldKeyboard) continue;
            if ((pickupLayers.value & (1 << keyboard.Root.layer)) == 0) continue;
            if ((keyboard.Collider.ClosestPoint(ray.origin) - ray.origin).sqrMagnitude > 0.000001f) continue;
            float distance = keyboard.Collider.bounds.size.magnitude + 1f;
            Ray reverse = new Ray(ray.origin + ray.direction * distance, -ray.direction);
            if (!keyboard.Collider.Raycast(reverse, out RaycastHit insideHit, distance)) continue;
            insideHit.distance = 0f;
            hit = insideHit;
            closest = 0f;
            break;
        }

        return closest < float.PositiveInfinity;
    }

    /// <summary>
    /// 카메라가 조준한 등록 키보드를 찾는다.
    /// rayDistance와 pickupLayers 및 손에 든 상태를 검사하여 state와 성공 여부를 반환한다.
    /// </summary>
    public bool TryGetKeyboard(float rayDistance, LayerMask pickupLayers, KeyboardStorageBox heldBox,
        KeyboardInteractionController.KeyboardState heldKeyboard,
        out KeyboardInteractionController.KeyboardState state)
    {
        state = null;
        if (!TryGetHit(rayDistance, pickupLayers, heldBox, heldKeyboard, out RaycastHit hit))
            return false;

        KeyboardAutomaticStation station = hit.collider.GetComponentInParent<KeyboardAutomaticStation>();
        if (station != null) return false;

        return hit.rigidbody != null && _world.TryGet(hit.rigidbody, out state)
            && !_world.IsStored(state) && !_world.IsReserved(state)
            && state.Root.activeInHierarchy && state.Collider.enabled;
    }
}
