using UnityEngine;

namespace KeyboardModeling.Assembly
{
    public interface IKeyboardPullerContext
    {
        bool InputAllowed { get; }
        Camera InputCamera { get; }
        float DragWorldHeight { get; }
        /// <summary>screenPosition이 UI 위인지 반환한다. 입력 상태는 변경하지 않는다.</summary>
        bool IsPointerOverUI(Vector2 screenPosition);
        /// <summary>collider가 연결된 풀러 또는 복귀 표시의 충돌체인지 반환한다. 입력 상태는 변경하지 않는다.</summary>
        bool IsToolCollider(Collider collider);
        /// <summary>현재 도구와 충돌하는 조작을 취소한다. 입력값과 반환값은 없다.</summary>
        void CancelDrag();
        /// <summary>hit의 파츠를 extractSwitches 종류와 속도값으로 분리한다. 성공 여부를 반환한다.</summary>
        bool TryExtractPart(Collider hit, bool extractSwitches, Vector3 velocity, Vector3 angularVelocity);
    }
}
