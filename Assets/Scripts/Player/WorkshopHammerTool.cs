using System;

using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorkshopHammerTool : MonoBehaviour
    {
        [Header("Hammer")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField, Min(0.1f)] private float _holdingDistance = 1.2f;
        private BoxCollider _hitCollider;
        private Camera _camera;
        private bool _stored = true;
        private bool _held;
        private bool _fromTray;
        private Vector3 _originPosition;
        private Quaternion _originRotation;

        public bool IsHeld => _held;
        public bool IsStored => _stored;
        public Sprite Icon => _spriteRenderer.sprite;

        /// <summary>collider가 이 망치의 집기 충돌체인지 확인하여 결과를 반환한다. 망치 상태는 변경하지 않는다.</summary>
        public bool OwnsCollider(Collider collider) => collider == _hitCollider;

        void Awake()
        {
            _hitCollider = GetComponent<BoxCollider>();
        }

        /// <summary>camera를 망치의 화면 이동 기준으로 저장하고 도구함 보관 상태로 시작한다. 로드된 망치를 숨기며 반환값은 없다.</summary>
        public void Initialize(Camera camera)
        {
            _camera = camera;
            _stored = true;
            gameObject.SetActive(false);
        }

        /// <summary>도구함의 망치를 ray 앞에 꺼내 왼쪽 버튼으로 잡는다. 보관 중인 망치만 활성화하며 성공 여부를 반환한다.</summary>
        public bool BeginTrayHold(Ray ray)
        {
            if (!_stored) return false;
            _stored = false;
            _fromTray = true;
            _held = true;
            gameObject.SetActive(true);
            _hitCollider.enabled = false;
            MoveWithPointer(ray);
            return true;
        }

        /// <summary>mouse와 ray로 망치 집기·이동·놓기를 처리한다. tray의 수납장 반납을 타격보다 먼저 검사하고 기존 tryStrike·표면 배치를 유지하며 입력 점유 여부를 반환한다.</summary>
        public bool ProcessPointer(Mouse mouse, Ray ray, Vector2 pointer, bool pointerOverUi,
            WorkshopKeycapDrawerTransition tray, Func<Ray, bool> tryStrike)
        {
            if (_stored) return false;
            if (_held)
            {
                if (mouse.leftButton.isPressed)
                {
                    MoveWithPointer(ray);
                    return true;
                }
                if (tray != null && tray.CanReturnHeldPart(transform))
                {
                    StoreInTray();
                    tray.CompletePartReturn(transform);
                    return true;
                }
                if (tray != null && tray.IsCategoryOpen(WorkshopDrawerCategory.ScrewsTools)
                    && tray.ContainsReturnPoint(pointer)) StoreInTray();
                else if (!pointerOverUi && tryStrike(ray)) StoreInTray();
                else PlaceOnSurface(ray);
                return true;
            }

            if (pointerOverUi || !mouse.leftButton.wasPressedThisFrame
                || !_hitCollider.Raycast(ray, out _, _camera.farClipPlane)) return false;
            _originPosition = transform.position;
            _originRotation = transform.rotation;
            _fromTray = false;
            _held = true;
            _hitCollider.enabled = false;
            MoveWithPointer(ray);
            return true;
        }

        /// <summary>진행 중인 망치 집기를 취소하고 도구함 또는 집기 전 위치로 되돌린다. 보관 상태와 충돌체를 복구하며 반환값은 없다.</summary>
        public void CancelInteraction()
        {
            if (!_held) return;
            if (_fromTray) { StoreInTray(); return; }
            transform.SetPositionAndRotation(_originPosition, _originRotation);
            _held = false;
            _hitCollider.enabled = true;
        }

        /// <summary>ray가 가리키는 카메라 앞 위치로 망치를 이동하고 카메라 회전에 맞춘다. 망치의 위치와 회전을 변경하며 반환값은 없다.</summary>
        private void MoveWithPointer(Ray ray)
        {
            transform.SetPositionAndRotation(ray.GetPoint(_holdingDistance), _camera.transform.rotation);
        }

        /// <summary>ray 아래 표면에 망치를 내려놓고 다시 집을 수 있게 한다. 표면이 없으면 현재 위치를 유지하고 충돌체를 켠다.</summary>
        private void PlaceOnSurface(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, _camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                transform.position = hit.point + Vector3.up * 0.15f;
            _held = false;
            _hitCollider.enabled = true;
        }

        /// <summary>망치를 도구함 재고로 숨기고 다음 출고를 준비한다. 집기와 충돌 상태를 초기화하며 반환값은 없다.</summary>
        private void StoreInTray()
        {
            _held = false;
            _stored = true;
            _hitCollider.enabled = true;
            gameObject.SetActive(false);
        }
    }
}
