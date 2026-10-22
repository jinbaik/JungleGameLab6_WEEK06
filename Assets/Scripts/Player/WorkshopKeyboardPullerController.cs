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
        private Rigidbody _draggedPart;
        private Plane _partDragPlane;
        private Vector3 _partGrabOffset;

        public bool InputAllowed => _interaction.IsKeyboardEditing && _interaction.EditingKeyboard != null
            && !Game.Tutorials.FirstEncounterTutorial.IsBlocking && InGameCursor.CanProcessVirtualPointer;
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
            if (_draggedPart != null)
            {
                UpdatePartDrag(ray, Mouse.current);
                return;
            }
            if (_keycapPuller.ProcessPointer(ray, pointer, Mouse.current)) return;
            if (_switchPuller.ProcessPointer(ray, pointer, Mouse.current)) return;
            if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI(pointer))
                TryBeginPartDrag(ray);
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
                if (!candidate.transform.IsChildOf(customization.transform))
                {
                    obstruction = Mathf.Min(obstruction, candidate.distance);
                    continue;
                }
                if (candidate.distance < nearest && customization.TryGetRemovablePart(candidate.transform, out _, out bool isSwitch))
                {
                    hit = candidate;
                    nearest = candidate.distance;
                    extractSwitches = isSwitch;
                }
            }
            return nearest < obstruction;
        }

        /// <summary>
        /// ray가 가리키는 설치 파츠를 분리하고 표시용 복제본을 포인터 드래그 상태로 전환한다.
        /// 원본 설치 상태를 갱신하고 이동 평면과 잡기 오프셋을 저장한다. 반환값은 없다.
        /// </summary>
        private void TryBeginPartDrag(Ray ray)
        {
            if (!TryGetPartHit(ray, out RaycastHit hit, out bool extractSwitches)) return;
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (!customization.TryRemovePart(hit.transform, extractSwitches, out Transform removed)) return;
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
            _draggedPart = CreateDetachedVisual(removed, Vector3.zero, Vector3.zero, false);
            if (_draggedPart == null) return;
            _draggedPart.isKinematic = true;
            _draggedPart.useGravity = false;
            _partDragPlane = new Plane(_camera.transform.forward, hit.point);
            _partGrabOffset = _draggedPart.position - hit.point;
            AudioManager.PlayPartSound(removed.position, true);
        }

        /// <summary>
        /// ray와 mouse 입력으로 집은 파츠를 이동시키고 버튼 해제 시 작업 공간에 내려놓는다.
        /// 드래그 중 위치를 갱신하며 놓은 복제본은 중력 적용 후 기존 지연 시간에 제거한다.
        /// </summary>
        private void UpdatePartDrag(Ray ray, Mouse mouse)
        {
            if (_partDragPlane.Raycast(ray, out float distance))
                _draggedPart.position = ray.GetPoint(distance) + _partGrabOffset;
            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                ReleasePart();
        }

        /// <summary>
        /// 현재 집은 표시용 파츠의 물리를 복원하고 지연 제거를 예약한다.
        /// 입력값과 반환값은 없으며 드래그 참조를 비운다. 원본의 제거 상태는 유지한다.
        /// </summary>
        private void ReleasePart()
        {
            if (_draggedPart == null) return;
            _draggedPart.isKinematic = false;
            _draggedPart.useGravity = true;
            Destroy(_draggedPart.gameObject, _returnDelay);
            _draggedPart = null;
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

        /// <summary>현재 포인터가 풀러 선택이나 사용에 해당하는지 반환한다. 기존 키보드 드래그 회전과 입력을 구분한다.</summary>
        public bool ConsumesPointer()
        {
            if (!InputAllowed || Mouse.current == null) return false;
            if (_keycapPlacement != null && _keycapPlacement.IsDragging) return true;
            if (_draggedPart != null) return true;
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
            ReleasePart();
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
            _editingKeyboard = null;
            UpdateDragHeight();
        }

        void OnDestroy()
        {
            foreach (GameObject fragment in _fragments) if (fragment != null) Destroy(fragment);
        }
    }
}

