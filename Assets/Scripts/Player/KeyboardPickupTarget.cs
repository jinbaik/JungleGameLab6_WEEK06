using UnityEngine;

using KeyboardModeling;

namespace Game.Player
{
    /// <summary>
    /// 필드에서 마우스로 집을 수 있는 키보드 오브젝트를 표시한다.
    /// 별도 입력값이나 상태를 보관하지 않는다.
    /// </summary>
    public sealed class KeyboardPickupTarget : MonoBehaviour
    {
        /// <summary>
        /// 키보드 렌더러의 합쳐진 시각 bounds에서 keyboardRoot 기준 중앙을 계산한다.
        /// keyboardRoot를 사용하며 커버 렌더러를 제외한 중앙의 로컬 위치를 반환한다.
        /// </summary>
        public Vector3 GetVisualCenterLocalPosition(Transform keyboardRoot)
        {
            Renderer[] renderers = keyboardRoot.GetComponentsInChildren<Renderer>();
            Bounds visualBounds = default;
            bool hasBounds = false;

            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || renderer.GetComponentInParent<KeyboardProtectionCover>() != null)
                    continue;

                Bounds rendererBounds = renderer.localBounds;
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 rendererLocalCorner = new Vector3(
                        (cornerIndex & 1) == 0 ? rendererBounds.min.x : rendererBounds.max.x,
                        (cornerIndex & 2) == 0 ? rendererBounds.min.y : rendererBounds.max.y,
                        (cornerIndex & 4) == 0 ? rendererBounds.min.z : rendererBounds.max.z);
                    Vector3 keyboardLocalCorner = keyboardRoot.InverseTransformPoint(
                        renderer.transform.TransformPoint(rendererLocalCorner));

                    if (!hasBounds)
                    {
                        visualBounds = new Bounds(keyboardLocalCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        visualBounds.Encapsulate(keyboardLocalCorner);
                    }
                }
            }

            return hasBounds ? visualBounds.center : keyboardRoot.InverseTransformPoint(transform.position);
        }
    }
}
