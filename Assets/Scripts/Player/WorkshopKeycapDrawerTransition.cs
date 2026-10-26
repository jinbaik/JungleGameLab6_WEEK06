using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Player
{
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class WorkshopKeycapDrawerTransition : MonoBehaviour
    {
        [Header("Workshop")]
        [SerializeField] private WorkshopViewController _workshopView;
        [SerializeField] private WorkshopDrawerInput _drawerInput;
        [SerializeField] private Camera _outputCamera;
        [SerializeField] private Transform _keycapDrawer;

        [Header("Screen Tray")]
        [SerializeField] private RectTransform _trayRoot;
        [SerializeField] private CanvasGroup _trayGroup;
        [SerializeField] private KeyboardEditButton _closeButton;
        [SerializeField, Range(0.1f, 2f)] private float _minimumTrayScale = 0.1f;
        [SerializeField, Range(0.1f, 2f)] private float _maximumTrayScale = 1f;

        [Header("Transition")]
        [SerializeField, Min(0.01f)] private float _pullDistance = 0.13f;
        [SerializeField, Min(0.01f)] private float _pullDuration = 0.24f;
        [SerializeField, Min(0.01f)] private float _travelDuration = 0.62f;
        [SerializeField, Min(0.01f)] private float _uiDuration = 0.30f;
        [SerializeField, Range(0.02f, 0.2f)] private float _screenMargin = 0.06f;
        private readonly List<Vector3> _visualCorners = new List<Vector3>();
        private Renderer[] _renderers;
        private Collider[] _colliders;
        private bool[] _rendererStates;
        private bool[] _colliderStates;
        private Transform _closedParent;
        private Vector3 _closedPosition;
        private Quaternion _closedRotation;
        private Vector3 _closedScale;
        private Vector3 _worldScale;
        private Vector2 _trayHome;
        private Canvas _canvas;
        private Coroutine _transition;
        private DrawerState _state;
        private bool _hasClosedPose;
        private int _fittedWidth;
        private int _fittedHeight;
        private ScrollRect _catalogScroll;
        private WorkshopKeycapPlacement _placement;
        private WorkshopDrawerCategory _category;
        private WorkshopDrawerCategory? _nextCategory;
        private readonly Dictionary<WorkshopDrawerCategory, ScrollRect> _catalogs = new Dictionary<WorkshopDrawerCategory, ScrollRect>();
        private readonly List<RaycastResult> _pointerRaycastResults = new List<RaycastResult>();

        public event Action<WorkshopDrawerCategory, bool> TrayStateChanged;

        public bool CanInteractWithTray => _state == DrawerState.Open && _drawerInput.IsInputAllowed;
        public bool IsTrayOpen => _state == DrawerState.Open;
        public WorkshopDrawerCategory Category => _category;
        public bool IsTrayVisible => _trayGroup.alpha > 0f;

        /// <summary>point가 완전히 열린 트레이의 현재 목록 안인지 반환한다. 가상 커서 화면 좌표만 사용하며 서랍 상태는 유지한다.</summary>
        public bool ContainsReturnPoint(Vector2 point)
        {
            return IsTrayOpen && _catalogScroll != null && RectTransformUtility.RectangleContainsScreenPoint(
                _catalogScroll.viewport, point, _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera);
        }

        /// <summary>
        /// 완전히 열린 트레이의 고정 Viewport 중앙을 화면 좌표 destination으로 반환한다.
        /// 카드와 스크롤 위치는 사용하지 않으며 집중 종료·전환·숨김 또는 화면 밖이면 false를 반환한다.
        /// </summary>
        public bool TryGetReturnDestination(out Vector2 destination)
        {
            destination = Vector2.zero;
            if (!IsTrayOpen || !_workshopView.IsKeyboardEditing || _trayGroup.alpha < 1f
                || !_trayRoot.gameObject.activeInHierarchy || _catalogScroll == null
                || !_catalogScroll.gameObject.activeInHierarchy) return false;
            RectTransform viewport = _catalogScroll.viewport;
            if (viewport.rect.width <= 0f || viewport.rect.height <= 0f) return false;
            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            destination = RectTransformUtility.WorldToScreenPoint(camera, viewport.TransformPoint(viewport.rect.center));
            return new Rect(0f, 0f, Screen.width, Screen.height).Contains(destination);
        }

        /// <summary>category의 독립 scroll과 placement 목록 점유를 연결한다. 현재 전환과 출고 상태로 스크롤 및 닫기 허용 여부를 맞춘다.</summary>
        public void ConnectCatalogScroll(ScrollRect scroll, WorkshopKeycapPlacement placement, WorkshopDrawerCategory category = WorkshopDrawerCategory.Keycaps)
        {
            _catalogs[category] = scroll;
            _placement = placement;
            SelectCatalog();
            UpdateTrayInput();
        }

        /// <summary>
        /// category에 등록된 scroll이 교체되거나 파괴되기 전에 연결을 해제한다.
        /// 일치하는 목록만 제거하며 현재 선택된 목록이면 _catalogScroll도 비운다.
        /// </summary>
        public void DisconnectCatalogScroll(ScrollRect scroll, WorkshopDrawerCategory category)
        {
            if (!_catalogs.TryGetValue(category, out ScrollRect registered) || registered != scroll) return;
            _catalogs.Remove(category);
            if (_catalogScroll == scroll) _catalogScroll = null;
        }

        /// <summary>기존 열림 상태와 입력 조건으로 트레이 버튼 및 목록 스크롤 입력만 갱신한다.</summary>
        private void UpdateTrayInput()
        {
            bool canScroll = CanInteractWithTray || (IsTrayOpen && _placement != null
                && _placement.IsInventoryScrolling && _placement.Pullers.EditingPointerAllowed);
            _trayGroup.interactable = canScroll;
            _closeButton.interactable = CanInteractWithTray;
            if (_catalogScroll != null) _catalogScroll.enabled = canScroll;
        }

        private enum DrawerState { Closed, Opening, Open, Closing }

        void Awake()
        {
            _canvas = _trayRoot.GetComponentInParent<Canvas>().rootCanvas;
            _trayHome = _trayRoot.anchoredPosition;
            PrepareDrawer();
            SetTrayVisible(false);
        }

        /// <summary>현재 실제 서랍의 메시 경계와 표시 상태 배열을 준비한다. 원래 모델 크기와 자세는 변경하지 않는다.</summary>
        private void PrepareDrawer()
        {
            _visualCorners.Clear();
            _renderers = _keycapDrawer.GetComponentsInChildren<Renderer>(true);
            _colliders = _keycapDrawer.GetComponentsInChildren<Collider>(true);
            _rendererStates = new bool[_renderers.Length];
            _colliderStates = new bool[_colliders.Length];
            foreach (MeshFilter filter in _keycapDrawer.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    _visualCorners.Add(_keycapDrawer.InverseTransformPoint(filter.transform.TransformPoint(point)));
                }
            }
        }

        /// <summary>현재 category에 해당하는 독립 목록만 표시하고 반환 및 입력 대상 ScrollRect를 변경한다. 스크롤 값은 유지한다.</summary>
        private void SelectCatalog()
        {
            foreach (var catalog in _catalogs)
                catalog.Value.gameObject.SetActive(catalog.Key == _category);
            _catalogs.TryGetValue(_category, out _catalogScroll);
            foreach (Text label in _trayRoot.GetComponentsInChildren<Text>(true))
                if (label.name == "CategoryLabel") label.text = _category switch
                {
                    WorkshopDrawerCategory.Keycaps => "키캡",
                    WorkshopDrawerCategory.Switches => "스위치",
                    WorkshopDrawerCategory.ScrewsTools => "도구",
                    WorkshopDrawerCategory.LedCustomImage => "LED·커스텀 이미지",
                    WorkshopDrawerCategory.UpperHousing => "상부 하우징",
                    WorkshopDrawerCategory.Plate => "보강판",
                    WorkshopDrawerCategory.Pcb => "PCB",
                    _ => "하부 하우징"
                };
        }

        /// <summary>font를 트레이 전체 텍스트에 적용하고 제목·버튼·본문·보조 정보의 크기와 굵기를 통일한다. 중첩 패널 배율을 보정하며 입력값과 색상은 유지한다.</summary>
        public void ApplyTypography(Font font)
        {
            foreach (Text label in _trayRoot.GetComponentsInChildren<Text>(true))
            {
                if (label.name == "AssemblyStatus") continue;
                bool title = label.name == "CategoryLabel" || label.name == "PhotoTitle";
                bool quantity = label.name == "Quantity" || label.name == "SelectedIndicator";
                bool caption = quantity || label.text.Contains(" · ") || label.text.Contains("\n");
                bool button = label.GetComponentInParent<KeyboardEditButton>() != null;
                bool section = label.text == "LED 전원" || label.text == "LED 색상"
                    || label.text == "LED 패턴" || label.text == "밝기 / 강도" || label.text == "애니메이션 속도";
                int size = title ? 22 : caption ? 14 : button || section ? 18 : 16;
                float scale = _trayRoot.InverseTransformVector(label.transform.TransformVector(Vector3.up)).magnitude;
                int localSize = Mathf.Max(1, Mathf.RoundToInt(size / Mathf.Max(0.01f, scale)));
                label.font = font;
                label.fontStyle = title || !caption && (button || section) ? FontStyle.Bold : FontStyle.Normal;
                label.fontSize = localSize;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(localSize * 0.85f));
                label.resizeTextMaxSize = localSize;
                label.lineSpacing = 1f;
            }
        }

        /// <summary>category의 닫힌 실제 서랍과 독립 목록을 연결하고 열기를 시작한다. 이전 서랍은 닫힌 상태여야 한다.</summary>
        private void OpenCategory(WorkshopDrawerCategory category)
        {
            _drawerInput.ResetReturnPresentation();
            _category = category;
            _keycapDrawer = _drawerInput.GetDrawer(category);
            PrepareDrawer();
            SelectCatalog();
            CaptureClosedPose();
            BeginTransition(true);
        }

        /// <summary>category가 완전히 열린 현재 트레이인지 반환한다. 다른 목록과 전환 중 입력은 허용하지 않는다.</summary>
        public bool IsCategoryOpen(WorkshopDrawerCategory category)
        {
            return IsTrayOpen && _category == category;
        }

        /// <summary>Drop할 part를 연결된 수납장 입력의 최종 후보 판정에 전달하고 반납 가능 여부를 반환한다. 트레이와 부품 상태는 변경하지 않는다.</summary>
        public bool CanReturnHeldPart(Transform part)
        {
            return _drawerInput.CanReturnHeldPart(part);
        }

        /// <summary>part의 기존 보관 완료를 수납장 입력에 알린다. 후보와 임시 인출만 정리하며 열린 UI·콘텐츠·스크롤은 유지한다.</summary>
        public void CompletePartReturn(Transform part)
        {
            _drawerInput.CompletePartReturn(part);
        }

        /// <summary>point가 현재 완전히 열린 서랍의 실제 목록 Viewport 안인지 반환한다. 닫힘·전환 중이거나 숨긴 UI는 반환 영역으로 보지 않는다.</summary>
        public bool IsPointerOverOpenTrayViewport(Vector2 point)
        {
            if (!TryGetReturnDestination(out _) || EventSystem.current == null) return false;
            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_catalogScroll.viewport, point, camera)) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = point };
            _pointerRaycastResults.Clear();
            EventSystem.current.RaycastAll(pointer, _pointerRaycastResults);
            if (_pointerRaycastResults.Count == 0) return false;
            Transform hit = _pointerRaycastResults[0].gameObject.transform;
            return hit == _catalogScroll.viewport || hit.IsChildOf(_catalogScroll.viewport);
        }

        void OnEnable()
        {
            _drawerInput.DrawerClicked += HandleDrawerClicked;
            _closeButton.Clicked += HandleCloseClicked;
        }

        void LateUpdate()
        {
            if (_state != DrawerState.Closed && !_workshopView.IsKeyboardEditing)
                RestoreClosed();
            UpdateTrayInput();
            if (_state != DrawerState.Closed && (_fittedWidth != _outputCamera.pixelWidth || _fittedHeight != _outputCamera.pixelHeight))
                FitTray();
        }

        /// <summary>category의 기존 서랍 입력이 허용되면 같은 전환기로 전환한다. 다른 서랍은 현재 닫기를 마친 뒤 열며 추가 클릭은 잠근다.</summary>
        private void HandleDrawerClicked(WorkshopDrawerCategory category)
        {
            if (!_drawerInput.IsInputAllowed) return;
            if (_state == DrawerState.Closed) OpenCategory(category);
            else if (_state == DrawerState.Open)
            {
                _nextCategory = category == _category ? null : category;
                BeginTransition(false);
            }
        }

        /// <summary>category의 기존 클릭 경로로 열기 또는 닫기를 요청한다. 전환·도구 조작·모달 중 요청은 기존 입력 조건으로 무시한다.</summary>
        public void RequestDrawer(WorkshopDrawerCategory category)
        {
            HandleDrawerClicked(category);
        }

        /// <summary>완전히 열린 현재 트레이의 닫기를 요청한다. 기존 입력 조건을 재검사하며 재고와 적용된 편집값은 변경하지 않는다.</summary>
        public void RequestClose()
        {
            if (_state == DrawerState.Open && _drawerInput.IsInputAllowed) BeginTransition(false);
        }

        /// <summary>button의 닫기 요청을 기존 집중 및 조립 입력 조건으로 검사하고 열린 트레이의 복귀를 시작한다.</summary>
        private void HandleCloseClicked(KeyboardEditButton button)
        {
            if (_state == DrawerState.Open && _drawerInput.IsInputAllowed) BeginTransition(false);
        }

        /// <summary>이동 전 부모와 로컬 자세 및 표시·선택 상태를 저장한다. 다음 복귀는 현재 수납장 부모를 기준으로 수행한다.</summary>
        private void CaptureClosedPose()
        {
            _closedParent = _keycapDrawer.parent;
            _closedPosition = _keycapDrawer.localPosition;
            _closedRotation = _keycapDrawer.localRotation;
            _closedScale = _keycapDrawer.localScale;
            _worldScale = _keycapDrawer.lossyScale;
            for (int i = 0; i < _renderers.Length; i++) _rendererStates[i] = _renderers[i].enabled;
            for (int i = 0; i < _colliders.Length; i++) _colliderStates[i] = _colliders[i].enabled;
            _hasClosedPose = true;
        }

        /// <summary>opening에 따라 열기 또는 닫기 코루틴을 시작하고 모든 서랍의 호버와 클릭을 잠근다.</summary>
        private void BeginTransition(bool opening)
        {
            _state = opening ? DrawerState.Opening : DrawerState.Closing;
            TrayStateChanged?.Invoke(_category, false);
            _drawerInput.SetTransitionLocked(true);
            _trayGroup.interactable = false;
            UpdateTrayInput();
            _transition = StartCoroutine(Transition(opening));
        }

        /// <summary>opening에 맞는 개폐음을 한 번 재생하고 실제 인출·화면 밖 이동·트레이 UI를 교대한다. Time.deltaTime으로만 시간을 누적하며 표현 교대에서는 소리를 반복하지 않는다.</summary>
        private IEnumerator Transition(bool opening)
        {
            AudioManager.PlayDrawerSound(opening);
            if (opening)
            {
                yield return MoveDrawer(false, false);
                yield return MoveDrawer(true, false);
                SetDrawerVisible(false);
                _drawerInput.SetDrawerOpen(_category, true);
                FitTray();
                _trayRoot.anchoredPosition = HiddenTrayPosition();
                SetTrayVisible(true);
                yield return MoveTray(true);
            }
            else
            {
                yield return MoveTray(false);
                SetTrayVisible(false);
                Pose pulled = PulledPose();
                _keycapDrawer.SetPositionAndRotation(ExitPosition(pulled), pulled.rotation);
                SetDrawerVisible(true);
                yield return MoveDrawer(true, true);
                yield return MoveDrawer(false, true);
                ApplyClosedPose();
                _drawerInput.SetDrawerOpen(_category, false);
            }
            _state = opening ? DrawerState.Open : DrawerState.Closed;
            _transition = null;
            _drawerInput.SetTransitionLocked(false);
            UpdateTrayInput();
            if (opening) TrayStateChanged?.Invoke(_category, true);
            if (!opening && _nextCategory.HasValue)
            {
                WorkshopDrawerCategory next = _nextCategory.Value;
                _nextCategory = null;
                OpenCategory(next);
            }
        }

        /// <summary>travel이면 화면 밖 구간을, 아니면 로컬 인출 구간을 reverse 방향에 맞춰 보간한다. 모델 스케일은 유지한다.</summary>
        private IEnumerator MoveDrawer(bool travel, bool reverse)
        {
            float duration = travel ? _travelDuration : _pullDuration;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
                float amount = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                if (reverse) amount = 1f - amount;
                if (travel)
                {
                    Pose pulled = PulledPose();
                    _keycapDrawer.SetPositionAndRotation(Vector3.Lerp(pulled.position, ExitPosition(pulled), amount), pulled.rotation);
                }
                else
                {
                    _keycapDrawer.localPosition = _closedPosition + _closedRotation * Vector3.back * (_pullDistance * amount);
                    _keycapDrawer.localRotation = _closedRotation;
                }
                yield return null;
            }
        }

        /// <summary>현재 부모와 저장된 로컬 자세 및 인출 거리로 인출 완료 월드 자세를 반환한다.</summary>
        private Pose PulledPose()
        {
            return new Pose(_closedParent.TransformPoint(_closedPosition + _closedRotation * Vector3.back * _pullDistance),
                _closedParent.rotation * _closedRotation);
        }

        /// <summary>pulled 자세의 모든 메시 경계를 출력 카메라에 투영해 화면 왼쪽 바깥의 위치를 반환한다. 깊이는 유지한다.</summary>
        private Vector3 ExitPosition(Pose pulled)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(pulled.position, pulled.rotation, _worldScale);
            float right = float.NegativeInfinity;
            float top = float.NegativeInfinity;
            float far = 0f;
            foreach (Vector3 corner in _visualCorners)
            {
                Vector3 point = _outputCamera.WorldToViewportPoint(matrix.MultiplyPoint3x4(corner));
                right = Mathf.Max(right, point.x);
                top = Mathf.Max(top, point.y);
                far = Mathf.Max(far, point.z);
            }
            Vector3 origin = _outputCamera.ViewportToWorldPoint(new Vector3(0f, 0f, far));
            Vector3 width = _outputCamera.ViewportToWorldPoint(new Vector3(1f, 0f, far)) - origin;
            Vector3 height = _outputCamera.ViewportToWorldPoint(new Vector3(0f, 1f, far)) - origin;
            return pulled.position - width * Mathf.Max(0f, right + _screenMargin) - height * Mathf.Max(0f, top - 0.25f);
        }

        /// <summary>entering 방향으로 화면용 트레이를 보간한다. 화면 밖 도착 뒤에만 호출자가 표현을 교체한다.</summary>
        private IEnumerator MoveTray(bool entering)
        {
            float elapsed = 0f;
            while (elapsed < _uiDuration)
            {
                elapsed = Mathf.Min(_uiDuration, elapsed + Time.deltaTime);
                float amount = Mathf.SmoothStep(0f, 1f, elapsed / _uiDuration);
                _trayRoot.anchoredPosition = Vector2.Lerp(HiddenTrayPosition(), _trayHome, entering ? amount : 1f - amount);
                yield return null;
            }
        }

        /// <summary>현재 트레이 크기와 배율로 테두리까지 왼쪽 밖에 놓이는 화면 UI 위치를 반환한다.</summary>
        private Vector2 HiddenTrayPosition()
        {
            return new Vector2(-_trayRoot.rect.width * _trayRoot.localScale.x - 32f, -_trayRoot.rect.height * _trayRoot.localScale.y * 0.12f);
        }

        /// <summary>CanvasScaler 크기와 키보드 왼쪽 경계로 트레이 배율을 맞춘다. 최소 표시 배율을 유지하되 화면 안에 들어가는 최대 크기로 제한하며 카드와 입력 상태는 유지한다.</summary>
        private void FitTray()
        {
            _fittedWidth = _outputCamera.pixelWidth;
            _fittedHeight = _outputCamera.pixelHeight;
            RectTransform canvasRect = (RectTransform)_canvas.transform;
            float available = canvasRect.rect.width * 0.25f;
            if (_workshopView.EditingKeyboard != null)
                foreach (Renderer renderer in _workshopView.EditingKeyboard.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    Bounds bounds = renderer.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        Vector3 screen = _outputCamera.WorldToScreenPoint(point);
                        if (screen.z > _outputCamera.nearClipPlane) available = Mathf.Min(available, screen.x / _canvas.scaleFactor - 16f);
                    }
                }
            float maximumScale = Mathf.Max(0.1f, Mathf.Min(_maximumTrayScale,
                (canvasRect.rect.width - _trayHome.x - 16f) / _trayRoot.rect.width,
                (canvasRect.rect.height - _trayHome.y - 100f) / _trayRoot.rect.height));
            float fit = Mathf.Min(maximumScale, (available - _trayHome.x) / _trayRoot.rect.width,
                (canvasRect.rect.height - _trayHome.y - 100f) / _trayRoot.rect.height);
            _trayRoot.localScale = Vector3.one * Mathf.Clamp(fit, Mathf.Min(_minimumTrayScale, maximumScale), maximumScale);
        }

        /// <summary>visible로 트레이의 표시와 해당 사각형 내부의 Raycast만 변경한다. 내부 처리 컴포넌트는 중단하지 않는다.</summary>
        private void SetTrayVisible(bool visible)
        {
            _trayGroup.alpha = visible ? 1f : 0f;
            _trayGroup.blocksRaycasts = visible;
            _trayGroup.interactable = false;
        }

        /// <summary>visible에 따라 실제 서랍의 저장된 Renderer·Collider 상태를 복구하거나 표시와 선택만 숨긴다.</summary>
        private void SetDrawerVisible(bool visible)
        {
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = visible && _rendererStates[i];
            for (int i = 0; i < _colliders.Length; i++) _colliders[i].enabled = visible && _colliderStates[i];
        }

        /// <summary>저장된 부모와 로컬 위치·회전·스케일 및 표시 상태를 정확히 복원한다.</summary>
        private void ApplyClosedPose()
        {
            _keycapDrawer.SetParent(_closedParent, false);
            _keycapDrawer.SetLocalPositionAndRotation(_closedPosition, _closedRotation);
            _keycapDrawer.localScale = _closedScale;
            SetDrawerVisible(true);
        }

        /// <summary>진행 중 전환을 중단하고 3D 서랍과 화면용 UI 및 입력 잠금을 닫힌 기본 상태로 복구한다.</summary>
        private void RestoreClosed()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _nextCategory = null;
            TrayStateChanged?.Invoke(_category, false);
            if (_hasClosedPose && _keycapDrawer != null && _closedParent != null) ApplyClosedPose();
            _state = DrawerState.Closed;
            if (_trayGroup != null) SetTrayVisible(false);
            if (_catalogScroll != null) _catalogScroll.enabled = false;
            if (_drawerInput != null)
            {
                _drawerInput.SetDrawerOpen(_category, false);
                _drawerInput.SetTransitionLocked(false);
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && !InGameCursor.IsSystemDialogOpen) RestoreClosed();
        }

        void OnDisable()
        {
            _drawerInput.DrawerClicked -= HandleDrawerClicked;
            _closeButton.Clicked -= HandleCloseClicked;
            RestoreClosed();
        }
    }
}
