using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Tournament
{
    public sealed class SpecialKeycapPresenter : MonoBehaviour
    {
        [Header("Prefab References")]
        [SerializeField] private DecalProjector _projector;
        [SerializeField] private KeycapHealth _health;

        /// <summary>미리 배치한 데칼에 material과 type을 적용하고 health의 파괴 알림을 구독한다.</summary>
        public void Initialize(KeycapHealth health, SpecialKeycapType type, Material material)
        {
            _health = health;
            _projector.material = material;
            _projector.drawDistance = 100f;
            _projector.fadeFactor = 1f;
            _projector.enabled = true;
            Vector3 size = _projector.size;
            if (type == SpecialKeycapType.Row) size.y /= 3f;
            if (type == SpecialKeycapType.Column) size.x /= 3f;
            _projector.size = size;
            health.Broken += HandleBroken;
        }

        /// <summary>health의 파괴 알림으로 이 키캡의 특수 표시를 숨기고 구독을 해제한다.</summary>
        private void HandleBroken(KeycapHealth health)
        {
            _projector.enabled = false;
            health.Broken -= HandleBroken;
        }

        void OnDestroy()
        {
            if (_health != null) _health.Broken -= HandleBroken;
        }
    }
}
