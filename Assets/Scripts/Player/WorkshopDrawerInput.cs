using System;
using System.Collections;
using System.Collections.Generic;

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

        [Header("Return Candidate")]
        private Transform _returnPart;
        private DrawerBinding _returnDrawer;
        private bool _returnTrayOpen;

        [Header("Return Drawer Presentation")]
        [SerializeField, Min(0f)] private float _returnPullDistance = 0.035f;
        [SerializeField, Min(0.01f)] private float _returnOpenDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float _returnCloseDuration = 0.16f;
        private DrawerBinding _returnTarget;
        private DrawerBinding _returnAnimatedDrawer;
        private Coroutine _returnMotion;
        private Vector3 _returnClosedPosition;
        private Quaternion _returnClosedRotation;
        private Vector3 _returnClosedScale;
        private float _returnAmount;
        private bool ReturnPresentationAllowed => isActiveAndEnabled && !_transitionLocked
            && _workshopView.IsKeyboardEditing && _workshopView.CanShowControlHints
            && _pullers.isActiveAndEnabled && _pullers.EditingPointerAllowed && !InGameCursor.IsWorldAim
            && !Game.Tutorials.FirstEncounterTutorial.IsVisible;

        public Transform ReturnPart => _returnPart;
        public WorkshopDrawerCategory? ReturnCategory => _returnDrawer != null ? _returnDrawer.Category : null;
        public Transform ReturnDrawer => _returnDrawer != null ? _returnDrawer.Drawer : null;
        public bool IsReturnCandidateValid => _returnPart != null && _returnDrawer != null;
        public bool IsReturnTrayOpen => _returnTrayOpen;
        public event Action ReturnCandidateChanged;

        public event Action<WorkshopDrawerCategory> DrawerClicked;
        public bool IsInputAllowed => isActiveAndEnabled && !_transitionLocked && CanProcessInput();

        /// <summary>locked로 전환 중 입력을 잠그거나 해제하고 기존 호버와 버튼 준비 상태를 비운다.</summary>
        public void SetTransitionLocked(bool locked)
        {
            _transitionLocked = locked;
            if (locked) ResetReturnPresentation();
            _pullers.SetDrawerTransitionLocked(locked);
            ResetPointer();
            if (locked) ClearReturnCandidate();
        }

        /// <summary>category의 열린 상태를 지정한다. opened이면 이동한 서랍 대신 명시된 빈 수납칸을 판정한다.</summary>
        public void SetDrawerOpen(WorkshopDrawerCategory category, bool opened)
        {
            ResetReturnPresentation();
            ClearReturnCandidate();
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
            InGameCursor.PointerCanceled += ClearReturnCandidate;
            InGameCursor.PointerCanceled += ResetReturnPresentation;
            ReturnCandidateChanged += HandleReturnCandidateChanged;
            HandleReturnCandidateChanged();
        }

        void LateUpdate()
        {
            if (!ReturnPresentationAllowed) ResetReturnPresentation();
            UpdateReturnCandidate();
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
                && InGameCursor.CanProcessVirtualPointer
                && _pullers.isActiveAndEnabled && !_pullers.IsManipulatingPart;
        }

        /// <summary>
        /// 출력 카메라의 ray로 명시된 서랍 클릭 영역 중 가장 가까운 대상을 반환한다.
        /// heldPart와 heldParts의 Collider만 가림에서 제외하며 기존 물리·수납장 차폐 영역 뒤나 비활성인 대상은 거부한다.
        /// </summary>
        private DrawerBinding FindDrawer(Ray ray, Transform heldPart = null, IReadOnlyList<Transform> heldParts = null)
        {
            float nearest = _outputCamera.farClipPlane;
            if (heldPart == null)
            {
                if (Physics.Raycast(ray, out RaycastHit obstruction, nearest,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    nearest = obstruction.distance;
            }
            else
            {
                foreach (RaycastHit obstruction in Physics.RaycastAll(ray, nearest,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    Transform hitTransform = obstruction.collider.transform;
                    bool held = hitTransform.IsChildOf(heldPart);
                    if (heldParts != null)
                        foreach (Transform part in heldParts)
                            if (part != null && hitTransform.IsChildOf(part)) { held = true; break; }
                    if (!held) nearest = Mathf.Min(nearest, obstruction.distance);
                }
            }

            foreach (Collider area in _occlusionAreas)
                if (area.enabled && area.gameObject.activeInHierarchy && area.Raycast(ray, out RaycastHit hit, nearest))
                    nearest = hit.distance;

            DrawerBinding selected = null;
            foreach (DrawerBinding drawer in _drawers)
            {
                if (drawer == _openDrawer || heldPart != null)
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

        /// <summary>집중 완료 상태의 자유 포인터로 수납장 영역과 기존 집기 실물을 조회한다. 조준한 서랍 종류와 무관하게 실물 카테고리의 후보만 갱신하며 드롭에는 개입하지 않는다.</summary>
        private void UpdateReturnCandidate()
        {
            if (_transitionLocked || !_workshopView.IsKeyboardEditing || !_workshopView.CanShowControlHints
                || !_pullers.isActiveAndEnabled || !_pullers.EditingPointerAllowed || InGameCursor.IsWorldAim
                || Game.Tutorials.FirstEncounterTutorial.IsVisible || Mouse.current == null
                || !_workshopView.TryGetHeldReturnPart(out Transform part, out WorkshopDrawerCategory category, out IReadOnlyList<Transform> parts))
            {
                ClearReturnCandidate();
                return;
            }
            Vector2 pointer = InGameCursor.Position;
            if (!_outputCamera.pixelRect.Contains(pointer) || _pullers.IsPointerOverUI(pointer))
            {
                ClearReturnCandidate();
                return;
            }
            Physics.SyncTransforms();
            if (FindDrawer(_outputCamera.ScreenPointToRay(pointer), part, parts) != null)
                foreach (DrawerBinding drawer in _drawers)
                    if (drawer.Category == category && drawer.Drawer.gameObject.activeInHierarchy)
                    {
                        SetReturnCandidate(part, drawer);
                        return;
                    }
            ClearReturnCandidate();
        }

        /// <summary>part와 drawer 및 기존 열린 수납칸 상태가 바뀔 때만 후보를 저장하고 C# 이벤트와 개발 로그를 발행한다. 서랍·실물·재고는 변경하지 않는다.</summary>
        private void SetReturnCandidate(Transform part, DrawerBinding drawer)
        {
            bool trayOpen = drawer != null && drawer == _openDrawer;
            if (_returnPart == part && _returnDrawer == drawer && _returnTrayOpen == trayOpen) return;
            _returnPart = part;
            _returnDrawer = drawer;
            _returnTrayOpen = trayOpen;
            Debug.Log(drawer != null ? $"[WorkshopDrawerInput] Return candidate: {drawer.Category}, TrayOpen: {trayOpen}"
                : "[WorkshopDrawerInput] Return candidate cleared", this);
            ReturnCandidateChanged?.Invoke();
        }

        /// <summary>현재 반납 후보를 해제한다. 유효 후보가 있었을 때만 변경 이벤트와 해제 로그를 발행하며 집기·회수 상태는 유지한다.</summary>
        private void ClearReturnCandidate()
        {
            SetReturnCandidate(null, null);
        }

        /// <summary>Drop할 part를 기존 후보 판정으로 즉시 재검사하고 일치 여부를 반환한다. 실제 왼쪽 버튼 해제에서만 허용하며 집기·보관 상태는 변경하지 않는다.</summary>
        public bool CanReturnHeldPart(Transform part)
        {
            Mouse mouse = Mouse.current;
            if (!isActiveAndEnabled || mouse == null || mouse.leftButton.isPressed || !mouse.leftButton.wasReleasedThisFrame) return false;
            UpdateReturnCandidate();
            return part != null && IsReturnCandidateValid && _returnPart == part;
        }

        /// <summary>보관 완료된 part가 현재 후보와 같으면 반납 완료를 기록하고 후보를 해제한다. 임시 서랍만 기존 닫기 경로로 넘기며 열린 화면용 트레이는 유지한다.</summary>
        public void CompletePartReturn(Transform part)
        {
            if (_returnPart != part || !IsReturnCandidateValid) return;
            Debug.Log($"[WorkshopDrawerInput] Returned: {_returnDrawer.Category}", this);
            ClearReturnCandidate();
        }

        /// <summary>현재 후보 이벤트에서 임시 인출할 서랍만 갱신한다. 이미 UI로 열린 대상은 생략하며 같은 목표는 다시 시작하지 않고 강제 입력 중단은 즉시 정리한다.</summary>
        private void HandleReturnCandidateChanged()
        {
            if (!ReturnPresentationAllowed)
            {
                ResetReturnPresentation();
                return;
            }
            DrawerBinding target = IsReturnCandidateValid && !IsReturnTrayOpen ? _returnDrawer : null;
            if (_returnTarget == target) return;
            _returnTarget = target;
            if (_returnMotion == null && (_returnAnimatedDrawer != null || target != null))
                _returnMotion = StartCoroutine(AnimateReturnDrawer());
        }

        /// <summary>최신 후보 목표로 임시 인출과 닫기를 진행하고 실제 이동 방향이 바뀔 때만 개폐음을 재생한다. Time.deltaTime과 현재 인출량으로 이동하며 닫힌 뒤 정상 로컬 자세를 복원한다.</summary>
        private IEnumerator AnimateReturnDrawer()
        {
            yield return null;
            bool? lastOpening = null;
            while (_returnAnimatedDrawer != null || _returnTarget != null)
            {
                if (_returnAnimatedDrawer == null)
                {
                    _returnAnimatedDrawer = _returnTarget;
                    Transform drawer = _returnAnimatedDrawer.Drawer;
                    _returnClosedPosition = drawer.localPosition;
                    _returnClosedRotation = drawer.localRotation;
                    _returnClosedScale = drawer.localScale;
                    _returnAmount = 0f;
                    lastOpening = null;
                }
                bool opening = _returnAnimatedDrawer == _returnTarget;
                if (lastOpening != opening && _returnAmount != (opening ? 1f : 0f))
                {
                    AudioManager.PlayDrawerSound(opening);
                    lastOpening = opening;
                }
                float duration = opening ? _returnOpenDuration : _returnCloseDuration;
                _returnAmount = Mathf.MoveTowards(_returnAmount, opening ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, duration));
                _returnAnimatedDrawer.Drawer.localPosition = _returnClosedPosition
                    + _returnClosedRotation * Vector3.back * (_returnPullDistance * Mathf.SmoothStep(0f, 1f, _returnAmount));
                if (!opening && _returnAmount == 0f) RestoreReturnDrawerPose();
                else if (opening && _returnAmount == 1f) break;
                yield return null;
            }
            _returnMotion = null;
        }

        /// <summary>이번 연출이 소유한 서랍만 저장된 닫힌 로컬 위치·회전·스케일로 복원한다. 화면용 트레이 서랍과 실물 부품은 변경하지 않는다.</summary>
        private void RestoreReturnDrawerPose()
        {
            if (_returnAnimatedDrawer != null && _returnAnimatedDrawer.Drawer != null)
            {
                Transform drawer = _returnAnimatedDrawer.Drawer;
                drawer.localPosition = _returnClosedPosition;
                drawer.localRotation = _returnClosedRotation;
                drawer.localScale = _returnClosedScale;
            }
            _returnAnimatedDrawer = null;
            _returnAmount = 0f;
        }

        /// <summary>진행 중 임시 인출 코루틴과 목표를 취소하고 이번 연출의 서랍을 즉시 닫는다. 기존 화면용 전환이 정상 닫힌 자세를 기록하기 전에도 호출하며 다른 서랍은 변경하지 않는다.</summary>
        public void ResetReturnPresentation()
        {
            if (_returnMotion != null) StopCoroutine(_returnMotion);
            _returnMotion = null;
            _returnTarget = null;
            RestoreReturnDrawerPose();
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
            if (!focused)
            {
                ResetPointer();
                ClearReturnCandidate();
                ResetReturnPresentation();
            }
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= ResetPointer;
            InGameCursor.PointerCanceled -= ClearReturnCandidate;
            InGameCursor.PointerCanceled -= ResetReturnPresentation;
            ReturnCandidateChanged -= HandleReturnCandidateChanged;
            ResetPointer();
            ClearReturnCandidate();
            ResetReturnPresentation();
        }

        void OnDestroy()
        {
            _frontPreview?.Dispose();
            _handlePreview?.Dispose();
        }
    }
}
