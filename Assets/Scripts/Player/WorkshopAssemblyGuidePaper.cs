using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorkshopAssemblyGuidePaper : MonoBehaviour
    {
        private const float READING_CANVAS_SURFACE_OFFSET = 0.02f;

        [Header("Reading")]
        [SerializeField] private RectTransform _guideCanvas;
        [SerializeField, Min(0.1f)] private float _readingDistance = 0.9f;
        [SerializeField, Min(0.1f)] private float _holdingDistance = 1.2f;
        private Camera _camera;
        private WorkshopKeycapDrawerTransition _toolTray;
        private BoxCollider _hitCollider;
        private Transform _readingParent;
        private Vector3 _readingPosition;
        private Quaternion _readingRotation;
        private Quaternion _canvasRotationBeforeReading;
        private Vector3 _canvasPositionBeforeReading;
        private Vector3 _canvasScaleBeforeReading;
        private Vector3 _holdOriginPosition;
        private Quaternion _holdOriginRotation;
        private Vector3 _grabOffset;
        private bool _held;
        private bool _reading;
        private bool _stored;
        private bool _startedFromTray;
        private bool _awaitPressAfterReading;

        public bool IsHeld => _held;
        public bool IsReading => _reading;
        public bool IsStored => _stored;
        public bool IsReturnHoldActive => _held && !_stored && !_reading && !_awaitPressAfterReading;

        /// <summary>
        /// pointer가 종이 위에 있을 때 읽기를 시작하고 이미 읽는 중이면 이전 자세로 돌아간다.
        /// 화면 좌표와 UI 가림 상태를 사용하며 우클릭을 처리했는지 반환한다.
        /// </summary>
        public bool TryToggleReading(Vector2 pointer, bool pointerOverUi)
        {
            if (_stored || _camera == null) return false;
            if (_reading)
            {
                ExitReading();
                return true;
            }
            if (pointerOverUi || !IsPointerOverPaper(_camera.ScreenPointToRay(pointer), pointer)) return false;
            EnterReading();
            return true;
        }

        void Awake()
        {
            _hitCollider = GetComponent<BoxCollider>();
        }

        /// <summary>
        /// camera와 tray를 종이의 화면 집기 및 도구함 반환 대상으로 연결한다.
        /// 전달된 참조를 보관하며 종이의 위치와 보관 상태는 변경하지 않는다.
        /// </summary>
        public void Initialize(Camera camera, WorkshopKeycapDrawerTransition tray)
        {
            _camera = camera;
            _toolTray = tray;
        }

        /// <summary>
        /// 도구함에 보관된 종이를 ray가 가리키는 위치로 꺼내 집는다.
        /// camera와 ray를 사용하며 꺼내기에 성공하면 활성화된 집기 상태를 반환한다.
        /// </summary>
        public bool BeginFromTray(Camera camera, Ray ray)
        {
            if (!_stored) return false;
            _camera = camera;
            _stored = false;
            gameObject.SetActive(true);
            transform.SetParent(null, true);
            _holdOriginPosition = transform.position;
            _holdOriginRotation = transform.rotation;
            _startedFromTray = true;
            _held = true;
            _awaitPressAfterReading = false;
            _grabOffset = Vector3.zero;
            _hitCollider.enabled = false;
            MoveWithPointer(ray);
            return true;
        }

        /// <summary>
        /// mouse와 pointer로 종이 집기, 이동, 도구함 반환 및 우클릭 읽기를 처리한다.
        /// pointerOverUi이면 새 상호작용을 막고 이번 입력을 처리했는지 반환한다.
        /// </summary>
        public bool ProcessPointer(Mouse mouse, Vector2 pointer, bool pointerOverUi)
        {
            if (_stored || _camera == null) return false;
            if (_reading)
            {
                if (mouse.rightButton.wasPressedThisFrame) TryToggleReading(pointer, pointerOverUi);
                return true;
            }

            Ray ray = _camera.ScreenPointToRay(pointer);
            bool hovered = !pointerOverUi && IsPointerOverPaper(ray, pointer);
            if (hovered && mouse.rightButton.wasPressedThisFrame)
                return TryToggleReading(pointer, pointerOverUi);
            if (_held)
            {
                if (_awaitPressAfterReading)
                {
                    if (mouse.leftButton.wasPressedThisFrame) _awaitPressAfterReading = false;
                    else return true;
                }
                if (!mouse.leftButton.isPressed)
                {
                    if (_toolTray != null && _toolTray.CanReturnHeldPart(transform))
                    {
                        StoreInTray();
                        _toolTray.CompletePartReturn(transform);
                        return true;
                    }
                    if (_toolTray != null && _toolTray.IsCategoryOpen(WorkshopDrawerCategory.ScrewsTools)
                        && _toolTray.ContainsReturnPoint(pointer)) StoreInTray();
                    else PlaceOnSurface();
                    return true;
                }
                MoveWithPointer(ray);
                return true;
            }
            if (!hovered || !mouse.leftButton.wasPressedThisFrame) return false;
            _holdOriginPosition = transform.position;
            _holdOriginRotation = transform.rotation;
            _startedFromTray = false;
            _held = true;
            _grabOffset = transform.position - ray.GetPoint(_holdingDistance);
            _hitCollider.enabled = false;
            return true;
        }

        /// <summary>
        /// 종이 읽기와 집기를 취소하고 시작 위치 또는 도구함 상태로 되돌린다.
        /// 현재 부모와 저장된 위치를 사용하며 확대 상태와 충돌체 상태를 복원한다.
        /// </summary>
        public void CancelInteraction()
        {
            if (_reading) ExitReading();
            if (!_held) return;
            if (_startedFromTray) { StoreInTray(); return; }
            transform.SetPositionAndRotation(_holdOriginPosition, _holdOriginRotation);
            _held = false;
            _awaitPressAfterReading = false;
            _hitCollider.enabled = true;
        }

        /// <summary>
        /// ray와 화면 위치로 종이의 투영 영역 또는 가장 가까운 충돌체를 검사한다.
        /// 종이가 pointer 아래에 있으면 true를 반환하며 위치와 집기 상태는 유지한다.
        /// </summary>
        private bool IsPointerOverPaper(Ray ray, Vector2 pointer)
        {
            if (!_held)
                return Physics.Raycast(ray, out RaycastHit hit, _camera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider == _hitCollider;

            Vector3 center = _hitCollider.center;
            Vector3 extent = _hitCollider.size * 0.5f;
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = center + Vector3.Scale(extent, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                Vector3 screen = _camera.WorldToScreenPoint(transform.TransformPoint(local));
                if (screen.z <= 0f) return false;
                minimum = Vector2.Min(minimum, screen);
                maximum = Vector2.Max(maximum, screen);
            }
            return new Rect(minimum, maximum - minimum).Contains(pointer);
        }

        /// <summary>
        /// 현재 종이와 Canvas의 자세를 저장하고 카메라 앞 읽기 위치에 고정한다.
        /// camera의 방향과 _readingDistance를 사용하며 글자를 종이 앞면으로 띄우고 충돌체를 끈다.
        /// </summary>
        private void EnterReading()
        {
            _readingParent = transform.parent;
            _readingPosition = transform.position;
            _readingRotation = transform.rotation;
            _canvasRotationBeforeReading = _guideCanvas.localRotation;
            _canvasPositionBeforeReading = _guideCanvas.localPosition;
            _canvasScaleBeforeReading = _guideCanvas.localScale;
            _reading = true;
            _hitCollider.enabled = false;
            transform.SetParent(_camera.transform, false);
            transform.localPosition = new Vector3(0f, 0f, _readingDistance);
            transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            _guideCanvas.localPosition = _canvasPositionBeforeReading + Vector3.up * READING_CANVAS_SURFACE_OFFSET;
            _guideCanvas.localRotation = Quaternion.Euler(-90f, 0f, 180f);
            _guideCanvas.localScale = new Vector3(-_canvasScaleBeforeReading.x,
                _canvasScaleBeforeReading.y, _canvasScaleBeforeReading.z);
        }

        /// <summary>
        /// 확대 전 종이와 Canvas의 자세를 복원하고 읽기 상태를 끝낸다.
        /// Canvas 위치와 저장된 집기 상태를 복원하며 마우스 왼쪽 버튼을 놓았다면 다음 누름을 기다린다.
        /// </summary>
        private void ExitReading()
        {
            transform.SetParent(_readingParent, true);
            transform.SetPositionAndRotation(_readingPosition, _readingRotation);
            _guideCanvas.localPosition = _canvasPositionBeforeReading;
            _guideCanvas.localRotation = _canvasRotationBeforeReading;
            _guideCanvas.localScale = _canvasScaleBeforeReading;
            _reading = false;
            _hitCollider.enabled = !_held;
            _awaitPressAfterReading = _held && (Mouse.current == null || !Mouse.current.leftButton.isPressed);
        }

        /// <summary>
        /// ray가 가리키는 카메라 앞 평면으로 종이를 이동한다.
        /// _holdingDistance와 집은 지점의 오프셋을 사용하며 회전과 보관 상태는 유지한다.
        /// </summary>
        private void MoveWithPointer(Ray ray)
        {
            transform.position = ray.GetPoint(_holdingDistance) + _grabOffset;
        }

        /// <summary>
        /// 종이 아래의 가장 가까운 표면에 내려놓고 집기 상태를 끝낸다.
        /// 현재 위치와 하향 레이를 사용하며 표면이 없으면 현 위치를 유지한다.
        /// </summary>
        private void PlaceOnSurface()
        {
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.5f)
                transform.position = new Vector3(transform.position.x, hit.point.y + 0.005f, transform.position.z);
            _held = false;
            _awaitPressAfterReading = false;
            _hitCollider.enabled = true;
        }

        /// <summary>
        /// 놓은 종이를 도구함 재고로 전환하고 실물을 숨긴다.
        /// 현재 종이를 사용하며 이후 카드에서 다시 꺼낼 수 있도록 _stored를 갱신한다.
        /// </summary>
        private void StoreInTray()
        {
            _held = false;
            _stored = true;
            _awaitPressAfterReading = false;
            _hitCollider.enabled = true;
            gameObject.SetActive(false);
        }

    }
}
