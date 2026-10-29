using System;

using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    public enum WorkshopDrawerCategory
    {
        [InspectorName("키캡")] Keycaps,
        [InspectorName("스위치(축)")] Switches,
        [InspectorName("도구")] ScrewsTools,
        [InspectorName("LED·커스텀 이미지")] LedCustomImage,
        [InspectorName("상부 하우징")] UpperHousing,
        [InspectorName("보강판")] Plate,
        [InspectorName("PCB")] Pcb,
        [InspectorName("하부 하우징")] LowerHousing
    }

    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class WorkshopDrawerInput : MonoBehaviour
    {
        [Serializable]
        private sealed class DrawerBinding
        {
            [SerializeField] private WorkshopDrawerCategory _category;
            [SerializeField] private Transform _drawer;
            [SerializeField] private Transform _front;
            [SerializeField] private Transform _handle;
            [SerializeField] private Collider[] _clickAreas;
            [SerializeField] private Collider _openClickArea;

            public WorkshopDrawerCategory Category => _category;
            public Transform Drawer => _drawer;
            public Transform Front => _front;
            public Transform Handle => _handle;
            public Collider[] ClickAreas => _clickAreas;
            public Collider OpenClickArea => _openClickArea;

            /// <summary>열린 수납칸의 명시된 클릭 영역이 없으면 현재 앞판 경계로 고정 트리거를 만든다. parent 아래에 두어 실제 서랍 이동과 독립적으로 닫기 선택을 유지한다.</summary>
            public void PrepareOpenClickArea(Transform parent)
            {
                if (_openClickArea != null) return;
                Bounds bounds = _clickAreas[0].bounds;
                GameObject slot = new GameObject(_category + "OpenClickArea");
                slot.transform.position = bounds.center;
                slot.transform.SetParent(parent, true);
                BoxCollider area = slot.AddComponent<BoxCollider>();
                area.size = bounds.size;
                area.isTrigger = true;
                _openClickArea = area;
            }
        }

        [Header("Workshop")]
        [SerializeField] private WorkshopViewController _workshopView;
        [SerializeField] private WorkshopKeyboardPullerController _pullers;
        [SerializeField] private Camera _outputCamera;

        [Header("Drawer Selection")]
        [SerializeField] private DrawerBinding[] _drawers;
        [SerializeField] private Collider[] _occlusionAreas;

        [Header("Hover Outline")]
        [SerializeField] private Material _hoverOutlineMaterial;
        [SerializeField] private Shader _hoverDepthShader;
        private WorkshopKeyboardPlacementPreview _frontPreview;
        private WorkshopKeyboardPlacementPreview _handlePreview;

        [Header("Pointer State")]
        private DrawerBinding _hovered;
        private bool _pressArmed;
        private bool _transitionLocked;
        private DrawerBinding _openDrawer;

        public event Action<WorkshopDrawerCategory> DrawerClicked;
        public bool IsInputAllowed => isActiveAndEnabled && !_transitionLocked && CanProcessInput();

        /// <summary>locked로 전환 중 입력을 잠그거나 해제하고 기존 호버와 버튼 준비 상태를 비운다.</summary>
        public void SetTransitionLocked(bool locked)
        {
            _transitionLocked = locked;
            _pullers.SetDrawerTransitionLocked(locked);
            ResetPointer();
        }

        /// <summary>category의 열린 상태를 지정한다. opened이면 이동한 서랍 대신 명시된 빈 수납칸을 판정한다.</summary>
        public void SetDrawerOpen(WorkshopDrawerCategory category, bool opened)
        {
            _openDrawer = null;
            if (opened)
                foreach (DrawerBinding drawer in _drawers)
                    if (drawer.Category == category) { _openDrawer = drawer; break; }
            ResetPointer();
        }

        /// <summary>category에 명시적으로 연결된 실제 서랍 Transform을 반환한다. 연결이 없으면 null이며 상태는 변경하지 않는다.</summary>
        public Transform GetDrawer(WorkshopDrawerCategory category)
        {
            foreach (DrawerBinding drawer in _drawers)
                if (drawer.Category == category) return drawer.Drawer;
            return null;
        }

        void Awake()
        {
            foreach (DrawerBinding drawer in _drawers) drawer.PrepareOpenClickArea(transform);
            _frontPreview = new WorkshopKeyboardPlacementPreview(
                _hoverOutlineMaterial, _hoverDepthShader, _outputCamera, Color.white);
            _handlePreview = new WorkshopKeyboardPlacementPreview(
                _hoverOutlineMaterial, _hoverDepthShader, _outputCamera, Color.white);
        }

        void OnEnable()
        {
            ResetPointer();
            InGameCursor.PointerCanceled += ResetPointer;
        }

        void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (!IsInputAllowed || mouse == null)
            {
                ResetPointer();
                return;
            }

            Vector2 pointer = InGameCursor.Position;
            if (!_outputCamera.pixelRect.Contains(pointer) || _pullers.IsPointerOverUI(pointer))
            {
                ResetPointer();
                return;
            }

            bool canClick = _pressArmed;
            if (!_pressArmed)
                _pressArmed = !mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame;

            Physics.SyncTransforms();
            SetHovered(FindDrawer(_outputCamera.ScreenPointToRay(pointer)));
            if (_hovered == null) return;

            if (canClick && mouse.leftButton.wasPressedThisFrame)
            {
                WorkshopDrawerCategory category = _hovered.Category;
                Debug.Log($"[WorkshopDrawerInput] Clicked: {category}", this);
                DrawerClicked?.Invoke(category);
                if (_transitionLocked || _hovered == null) return;
            }
            if (_hovered != _openDrawer)
            {
                _frontPreview.Show(new Pose(_hovered.Front.position, _hovered.Front.rotation));
                _handlePreview.Show(new Pose(_hovered.Handle.position, _hovered.Handle.rotation));
            }
        }

        /// <summary>
        /// 기존 작업대 집중 상태와 공통 입력 및 조립 점유 상태로 서랍 입력 허용 여부를 반환한다.
        /// 진입·복귀 블렌드와 튜토리얼 및 업로드 창과 부품·도구 조작 중에는 허용하지 않는다.
        /// </summary>
        private bool CanProcessInput()
        {
            return _workshopView.IsKeyboardEditing && _workshopView.CanShowControlHints
                && _pullers.isActiveAndEnabled && _pullers.InputAllowed && !_pullers.IsManipulatingPart;
        }

        /// <summary>
        /// 출력 카메라의 ray로 명시된 서랍 클릭 영역 중 가장 가까운 대상을 반환한다.
        /// 기존 물리 Collider와 수납장 차폐 영역보다 뒤에 있거나 비활성인 대상은 제외한다.
        /// </summary>
        private DrawerBinding FindDrawer(Ray ray)
        {
            float nearest = _outputCamera.farClipPlane;
            if (Physics.Raycast(ray, out RaycastHit obstruction, nearest,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                nearest = obstruction.distance;

            foreach (Collider area in _occlusionAreas)
                if (area.enabled && area.gameObject.activeInHierarchy && area.Raycast(ray, out RaycastHit hit, nearest))
                    nearest = hit.distance;

            DrawerBinding selected = null;
            foreach (DrawerBinding drawer in _drawers)
            {
                if (drawer == _openDrawer)
                {
                    Collider slot = drawer.OpenClickArea;
                    if (slot.enabled && slot.gameObject.activeInHierarchy
                        && slot.Raycast(ray, out RaycastHit slotHit, nearest) && slotHit.distance < nearest)
                    {
                        nearest = slotHit.distance;
                        selected = drawer;
                    }
                    continue;
                }
                if (!drawer.Drawer.gameObject.activeInHierarchy) continue;
                foreach (Collider area in drawer.ClickAreas)
                {
                    if (!area.enabled || !area.gameObject.activeInHierarchy
                        || !area.Raycast(ray, out RaycastHit hit, nearest) || hit.distance >= nearest) continue;
                    nearest = hit.distance;
                    selected = drawer;
                }
            }
            return selected;
        }

        /// <summary>
        /// drawer가 이전 호버 대상과 다르면 앞판과 손잡이의 외곽선 메시를 준비한다.
        /// null이면 현재 대상을 해제하며 공유 재질과 실제 서랍 상태는 변경하지 않는다.
        /// </summary>
        private void SetHovered(DrawerBinding drawer)
        {
            if (_hovered == drawer) return;
            _hovered = drawer;
            if (drawer == null || drawer == _openDrawer) return;
            _frontPreview.Prepare(drawer.Front);
            _handlePreview.Prepare(drawer.Handle);
        }

        /// <summary>
        /// 호버 대상과 클릭 준비 상태를 비운다.
        /// 입력 재개 후 버튼을 놓은 프레임을 거쳐야 다음 클릭을 처리한다.
        /// </summary>
        private void ResetPointer()
        {
            _hovered = null;
            _pressArmed = false;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) ResetPointer();
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= ResetPointer;
            ResetPointer();
        }

        void OnDestroy()
        {
            _frontPreview?.Dispose();
            _handlePreview?.Dispose();
        }
    }
}
