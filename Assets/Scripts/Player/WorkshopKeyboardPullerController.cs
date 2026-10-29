using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed class WorkshopKeyboardPullerController : MonoBehaviour, IKeyboardPullerContext
    {
        [Header("Workshop")]
        [SerializeField] private PlayerInteractionController _interaction;
        [SerializeField] private Camera _camera;
        [SerializeField] private AssemblyFullerTool _keycapPuller;
        [SerializeField] private AssemblyFullerTool _switchPuller;
        [SerializeField] private WorkshopKeycapPlacement _keycapPlacement;

        [Header("Removal")]
        [SerializeField, Min(0f)] private float _heightOffset = 0.2f;
        [SerializeField, Min(0.01f)] private float _returnDelay = 2f;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private readonly List<GameObject> _fragments = new List<GameObject>();
        private KeyboardInputController _editingKeyboard;
        private float _dragWorldHeight;

        public bool InputAllowed => _interaction.IsKeyboardEditing && _interaction.EditingKeyboard != null
            && !Game.Tutorials.FirstEncounterTutorial.IsBlocking && InGameCursor.CanProcessVirtualPointer;
        public bool IsManipulatingPart => _keycapPuller.IsSelected || _switchPuller.IsSelected
            || (_keycapPlacement != null && _keycapPlacement.IsDragging);
        public Camera InputCamera => _camera;
        public float DragWorldHeight => _dragWorldHeight;

        /// <summary>
        /// collider가 W08의 키캡 풀러나 스위치 풀러 또는 해당 복귀 표시에 속하는지 반환한다.
        /// 도구와 입력 상태는 변경하지 않는다.
        /// </summary>
        public bool IsToolCollider(Collider collider)
        {
            return _keycapPuller.OwnsCollider(collider) || _switchPuller.OwnsCollider(collider);
        }

        void Awake()
        {
            _keycapPuller.SetContext(this, true);
            _switchPuller.SetContext(this, true);
        }

        void OnEnable()
        {
            InGameCursor.PointerCanceled += CancelDrag;
            _keycapPuller.HeldToolChanged += HandleHeldToolChanged;
            _switchPuller.HeldToolChanged += HandleHeldToolChanged;
        }

        /// <summary>
        /// tool의 집기와 복귀 알림으로 기존 파츠 렌더러의 풀러 표시를 갱신한다.
        /// null이면 원래 레이어로 복원하며 실제 도구 위치와 제거 동작은 변경하지 않는다.
        /// </summary>
        private void HandleHeldToolChanged(Transform tool)
        {
            _keycapPlacement.ShowToolPreview(tool);
        }

        void Update()
        {
            KeyboardInputController target = _interaction.IsKeyboardEditing ? _interaction.EditingKeyboard : null;
            if (target != _editingKeyboard)
            {
                CancelDrag();
                _editingKeyboard = target;
                if (target != null) target.GetComponent<KeyboardCustomization>().PrepareSwitchRemovalTargets();
                UpdateDragHeight();
            }
            if (!InputAllowed || Mouse.current == null) { CancelDrag(); return; }
            if (_keycapPlacement != null && _keycapPlacement.IsDragging) return;
            Vector2 pointer = InGameCursor.Position;
            Ray ray = _camera.ScreenPointToRay(pointer);
            if (_keycapPuller.ProcessPointer(ray, pointer, Mouse.current)) return;
            if (_switchPuller.ProcessPointer(ray, pointer, Mouse.current)) return;
            if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI(pointer))
                TryRemoveClickedPart(ray);
        }

        /// <summary>
        /// ray에 닿는 현재 키보드의 제거 가능한 파츠 중 가장 가까운 hit와 종류를 반환한다.
        /// 본체의 넓은 집기 충돌체는 건너뛰며 키캡이 덮고 있는 축과 외부 물체 뒤의 파츠는 제외한다.
        /// </summary>
        private bool TryGetPartHit(Ray ray, out RaycastHit hit, out bool extractSwitches)
        {
            extractSwitches = false;
            hit = default;
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            float nearest = float.PositiveInfinity;
            float obstruction = float.PositiveInfinity;
            foreach (RaycastHit candidate in Physics.RaycastAll(ray, _camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Transform hitTransform = candidate.collider.transform;
                if (!hitTransform.IsChildOf(customization.transform))
                {
                    obstruction = Mathf.Min(obstruction, candidate.distance);
                    continue;
                }
                if (candidate.distance < nearest && customization.TryGetRemovablePart(hitTransform, out _, out bool isSwitch))
                {
                    hit = candidate;
                    nearest = candidate.distance;
                    extractSwitches = isSwitch;
                }
            }
            return nearest < obstruction;
        }

        /// <summary>
        /// ray가 가리키는 설치 파츠 한 개를 집어 인벤토리 반환 또는 원본 복귀 드래그를 시작한다.
        /// 재고 항목을 연결할 수 없으면 원본 설치 상태를 복원한다. 반환값은 없다.
        /// </summary>
        private void TryRemoveClickedPart(Ray ray)
        {
            if (!TryGetPartHit(ray, out RaycastHit hit, out bool extractSwitches)) return;
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (!customization.TryRemovePart(hit.collider.transform, extractSwitches, out Transform removed)) return;
            if (!extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedKeycap(removed, false, Vector3.zero, Vector3.zero))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return;
            }
            if (extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedSwitch(removed, false, Vector3.zero, Vector3.zero))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return;
            }
            removed.gameObject.SetActive(true);
            if (extractSwitches)
                _interaction.EditingKeyboard.SetSwitchSound(customization.GetInstalledSwitchType(removed.GetComponent<KeyboardSwitch>()));
        }

        /// <summary>
        /// 현재 편집 키보드의 메시 범위와 높이 설정으로 카메라 아래의 풀러 이동 평면 높이를 갱신한다.
        /// 입력값과 반환값은 없으며 작업대에 배치한 풀러의 위치와 크기는 유지한다.
        /// </summary>
        private void UpdateDragHeight()
        {
            if (_editingKeyboard == null) return;
            Bounds bounds = new Bounds(_editingKeyboard.transform.position, Vector3.zero);
            bool found = false;
            foreach (MeshRenderer renderer in _editingKeyboard.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            _dragWorldHeight = Mathf.Min(bounds.max.y + bounds.size.magnitude * _heightOffset,
                _camera.transform.position.y - _camera.nearClipPlane - 0.05f);
        }

        /// <summary>
        /// screenPosition의 UI 레이캐스트에서 편집 화면 배경을 제외하고 실제 UI가 있는지 반환한다.
        /// 풀러 상태는 변경하지 않으며 버튼과 패널 위에서 도구 선택을 차단한다.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
            _uiHits.Clear();
            EventSystem.current.RaycastAll(pointer, _uiHits);
            foreach (RaycastResult hit in _uiHits)
            {
                if (hit.gameObject.TryGetComponent<KeyboardEditOverlayUI>(out _)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// screenPosition이 풀러를 가리키거나 이미 풀러를 들고 있는지 반환한다.
        /// 현재 입력 허용 상태와 카메라 레이를 사용하며 UI 선택과 도구 상태는 변경하지 않는다.
        /// </summary>
        public bool IsPullerInteraction(Vector2 screenPosition)
        {
            if (!InputAllowed) return false;
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            return _keycapPuller.IsSelected || _switchPuller.IsSelected
                || _keycapPuller.IsPointerOverTool(ray) || _switchPuller.IsPointerOverTool(ray);
        }

        /// <summary>state에 현재 풀러 사용과 실제 제거 대상의 조작을 추가한다. 기존 도구 가림 검사와 키보드 분리 조건을 읽는다.</summary>
        public bool CollectControlHints(WorkshopControlHintState state)
        {
            if (!InputAllowed || (_keycapPlacement != null && _keycapPlacement.IsDragging)) return false;
            Vector2 pointer = InGameCursor.Position;
            AssemblyFullerTool selected = _keycapPuller.IsSelected ? _keycapPuller : _switchPuller.IsSelected ? _switchPuller : null;
            Ray ray = _camera.ScreenPointToRay(pointer);
            if (selected != null)
            {
                state.Title = selected.ExtractsSwitches ? "스위치 Puller" : "키캡 Puller";
                Collider target = selected.GetExtractionTarget(pointer);
                KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
                if (target != null && customization.TryGetRemovablePart(target.transform, out _, out bool isSwitch)
                    && isSwitch == selected.ExtractsSwitches)
                    state.Add("좌클릭 유지·이동", isSwitch ? "스위치 연속 분리" : "키캡 연속 분리");
                state.Add("좌클릭 놓기", "Puller 복귀");
                return true;
            }
            if (IsPointerOverUI(pointer)) return false;
            AssemblyFullerTool tool = _keycapPuller.IsPointerOverTool(ray) ? _keycapPuller
                : _switchPuller.IsPointerOverTool(ray) ? _switchPuller : null;
            if (tool != null)
            {
                state.Title = tool.ExtractsSwitches ? "스위치 Puller" : "키캡 Puller";
                state.Add("좌클릭 유지", "Puller 집기·사용");
                return true;
            }
            if (!TryGetPartHit(ray, out _, out bool extractSwitches)) return false;
            state.Add("좌클릭 드래그", extractSwitches ? "스위치 분리·집기" : "키캡 분리·집기");
            return true;
        }

        /// <summary>현재 포인터가 풀러 선택이나 사용에 해당하는지 반환한다. 기존 키보드 드래그 회전과 입력을 구분한다.</summary>
        public bool ConsumesPointer()
        {
            if (!InputAllowed || Mouse.current == null) return false;
            if (_keycapPlacement != null && _keycapPlacement.IsDragging) return true;
            Vector2 pointer = InGameCursor.Position;
            if (IsPointerOverUI(pointer)) return false;
            Ray ray = _camera.ScreenPointToRay(pointer);
            return _keycapPuller.IsSelected || _switchPuller.IsSelected
                || _keycapPuller.IsPointerOverTool(ray) || _switchPuller.IsPointerOverTool(ray)
                || (_keycapPlacement != null && _keycapPlacement.IsPointerOverLoosePart(ray))
                || TryGetPartHit(ray, out _, out _);
        }

        /// <summary>선택 중인 두 풀러를 원래 자리로 되돌린다. 이미 제거된 파츠와 키보드 상태는 유지한다.</summary>
        public void CancelDrag()
        {
            if (_keycapPlacement != null) _keycapPlacement.CancelDrag();
            _keycapPuller.ReturnToOrigin();
            _switchPuller.ReturnToOrigin();
        }

        /// <summary>hit가 현재 편집 키보드의 제거 가능한 파츠이면 비활성화하고 속도값을 가진 시각 파츠를 생성한다. 성공 여부를 반환한다.</summary>
        public bool TryExtractPart(Collider hit, bool extractSwitches, Vector3 velocity, Vector3 angularVelocity)
        {
            if (!InputAllowed || hit == null || _editingKeyboard == null) return false;
            KeyboardCustomization customization = _editingKeyboard.GetComponent<KeyboardCustomization>();
            if (!customization.TryRemovePart(hit.transform, extractSwitches, out Transform removed)) return false;
            if (!extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedKeycap(removed, true, velocity, angularVelocity))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return true;
            }
            if (extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedSwitch(removed, true, velocity, angularVelocity))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return true;
            }
            CreateDetachedVisual(removed, velocity, angularVelocity);
            AudioManager.PlayPartSound(removed.position, true);
            return true;
        }

        /// <summary>
        /// removed의 메시와 재질을 복사하고 velocity와 angularVelocity를 적용한 Rigidbody를 반환한다.
        /// scheduleRemoval이 true이면 지연 제거를 예약하며 복사할 메시가 없으면 null을 반환한다.
        /// </summary>
        public Rigidbody CreateDetachedVisual(Transform removed, Vector3 velocity, Vector3 angularVelocity,
            bool scheduleRemoval = true)
        {
            GameObject visual = new GameObject("Removed_" + removed.name);
            visual.transform.position = removed.position;
            Bounds bounds = new Bounds();
            bool found = false;
            foreach (MeshRenderer source in removed.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!source.enabled || !IsVisibleChild(source.transform, removed)) continue;
                MeshFilter filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                GameObject mesh = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                mesh.transform.SetParent(visual.transform, false);
                mesh.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                mesh.transform.localScale = source.transform.lossyScale;
                mesh.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                var properties = new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);
                renderer.SetPropertyBlock(properties);
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            if (!found) { Destroy(visual); return null; }
            BoxCollider collider = visual.AddComponent<BoxCollider>();
            collider.center = visual.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
            foreach (Collider player in _interaction.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, player);
            foreach (Collider keyboardCollider in _interaction.EditingKeyboard.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(collider, keyboardCollider);
            Rigidbody body = visual.AddComponent<Rigidbody>();
            body.mass = 0.02f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            _fragments.RemoveAll(fragment => fragment == null);
            _fragments.Add(visual);
            if (scheduleRemoval) Destroy(visual, _returnDelay);
            return body;
        }

        /// <summary>child와 root 사이에 비활성 장식이 있는지 검사해 실제 표시 대상인지 반환한다. 제거로 비활성화한 root 자체는 제외한다.</summary>
        private bool IsVisibleChild(Transform child, Transform root)
        {
            while (child != root)
            {
                if (!child.gameObject.activeSelf) return false;
                child = child.parent;
            }
            return true;
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= CancelDrag;
            CancelDrag();
            _keycapPuller.HeldToolChanged -= HandleHeldToolChanged;
            _switchPuller.HeldToolChanged -= HandleHeldToolChanged;
            _editingKeyboard = null;
            UpdateDragHeight();
        }

        void OnDestroy()
        {
            foreach (GameObject fragment in _fragments) if (fragment != null) Destroy(fragment);
        }
    }
}

