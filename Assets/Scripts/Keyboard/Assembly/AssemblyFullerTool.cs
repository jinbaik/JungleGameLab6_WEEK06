using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyFullerTool : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private AssemblyDragInput _dragInput;
        [SerializeField] private Transform _fuller;
        [SerializeField, Tooltip("도구 로컬 공간의 집게 끝 중심입니다.")] private Transform _gripPoint;
        [SerializeField] private Vector3 _heldEulerAngles;
        [SerializeField, Tooltip("활성화하면 키캡 대신 스위치만 분리합니다.")] private bool _extractSwitches;
        private IKeyboardPullerContext _context;
        private bool _followCameraPlane;
        private IKeyboardPullerContext Context => _context ?? _dragInput;
        public bool IsSelected => _selected;
        public bool ExtractsSwitches => _extractSwitches;
        public Transform ToolTransform => _fuller;
        public event System.Action<Transform> HeldToolChanged;

        /// <summary>
        /// context를 도구 입력과 분리 동작의 대상으로 연결한다.
        /// followCameraPlane이 true이면 표면이 없는 곳에서 카메라 평행 평면을 사용한다. 반환값은 없다.
        /// </summary>
        public void SetContext(IKeyboardPullerContext context, bool followCameraPlane = false)
        {
            _context = context;
            _followCameraPlane = followCameraPlane;
        }

        /// <summary>ray가 현재 도구에 가장 먼저 닿는지 반환한다. 선택 및 물리 상태는 변경하지 않는다.</summary>
        public bool IsPointerOverTool(Ray ray)
        {
            Collider hit = FindVisibleCollider(ray);
            return hit != null && hit.transform.IsChildOf(_fuller);
        }

        /// <summary>screenPosition에서 실제 분리 경로와 같은 UI, 복귀 표시 및 가림 검사를 한 대상을 반환한다. 분리와 도구 상태는 변경하지 않는다.</summary>
        public Collider GetExtractionTarget(Vector2 screenPosition)
        {
            if (Context.IsPointerOverUI(screenPosition)) return null;
            Ray ray = Context.InputCamera.ScreenPointToRay(screenPosition);
            if (_returnCollider != null && _returnCollider.Raycast(ray, out _, Context.InputCamera.farClipPlane)) return null;
            return FindVisibleCollider(ray);
        }

        [Header("Extraction")]
        [SerializeField, Min(1f)] private float _sweepStepPixels = 4f;
        [SerializeField, Min(0f)] private float _popUpSpeed = 2f;
        [SerializeField, Min(0f)] private float _popSideSpeed = 0.35f;
        [SerializeField, Min(0f)] private float _spinSpeed = 3f;

        [Header("Return Outline")]
        [SerializeField] private Color _outlineColor = new Color(0.34f, 0.84f, 0.95f, 1f);
        [SerializeField, Min(0.001f)] private float _outlineWidth = 0.008f;
        [SerializeField, Range(0.05f, 1f)] private float _returnOpacity = 0.25f;
        private GameObject _returnMarker;
        private BoxCollider _returnCollider;
        private Material _outlineMaterial;

        [Header("Runtime State")]
        private readonly HashSet<int> _attemptedTargets = new HashSet<int>();
        private string _interactionId;
        private bool _selected;
        private bool _extracting;
        private Vector3 _originPosition;
        private Quaternion _originRotation;
        private Vector3 _localGripPosition;
        private Plane _followPlane;
        private Vector2 _previousPointer;
        private bool _hasPreviousPointer;
        private Collider[] _colliders;
        private bool[] _colliderStates;
        private Rigidbody[] _bodies;
        private bool[] _kinematicStates;
        private bool[] _gravityStates;
        private Vector3[] _velocities;
        private Vector3[] _angularVelocities;

        /// <summary>
        /// ray와 screenPosition 및 mouse로 도구 선택, 복귀와 누르는 동안의 즉시 분리를 처리한다.
        /// Fuller가 선택되거나 이번 입력으로 선택되면 true를 반환하여 개별 집기와 배치를 차단한다.
        /// </summary>
        public bool ProcessPointer(Ray ray, Vector2 screenPosition, Mouse mouse)
        {
            if (!Context.InputAllowed || !isActiveAndEnabled || _fuller == null)
                return false;

            Physics.SyncTransforms();
            bool overUI = Context.IsPointerOverUI(screenPosition);
            if (!_selected)
            {
                if (!overUI && mouse.leftButton.wasPressedThisFrame)
                {
                    Collider hit = FindVisibleCollider(ray);
                    if (hit != null && hit.transform.IsChildOf(_fuller))
                        return SelectFuller(ray);
                }
                return false;
            }

            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
            {
                RecordUserPutDown();
                return true;
            }

            UpdateToolPose(ray);
            bool overReturn = _returnCollider.Raycast(ray, out RaycastHit returnHit, Context.InputCamera.farClipPlane);
            if (!overUI && overReturn && mouse.leftButton.wasPressedThisFrame)
            {
                RecordUserPutDown();
                return true;
            }

            if (mouse.leftButton.wasPressedThisFrame && !overUI && !overReturn)
            {
                _extracting = true;
                _hasPreviousPointer = false;
            }

            if (_extracting)
            {
                if (overUI || overReturn)
                    _hasPreviousPointer = false;
                else
                {
                    ExtractAlongPointer(_hasPreviousPointer ? _previousPointer : screenPosition, screenPosition);
                    _previousPointer = screenPosition;
                    _hasPreviousPointer = true;
                }
            }
            return true;
        }

        /// <summary>
        /// ray와 이동 평면 설정으로 Fuller의 원래 자세와 로컬 집기 지점을 보관하고 즉시 일괄 분리를 활성화한다.
        /// 진행 중 배치를 기존 정책으로 취소하고 물리 충돌을 해제하며 선택 클릭으로 뒤쪽 키캡은 분리하지 않는다.
        /// </summary>
        private bool SelectFuller(Ray ray)
        {
            Bounds bounds = new Bounds(_fuller.position, Vector3.zero);
            if (_followCameraPlane)
                foreach (MeshRenderer renderer in _fuller.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
            _followPlane = _followCameraPlane
                ? new Plane(Context.InputCamera.transform.forward, bounds.center)
                : new Plane(Vector3.up, new Vector3(0f, Context.DragWorldHeight, 0f));
            _localGripPosition = _gripPoint != null
                ? _fuller.InverseTransformPoint(_gripPoint.position)
                : _fuller.InverseTransformPoint(bounds.center);
            Context.CancelDrag();
            _originPosition = _fuller.position;
            _originRotation = _fuller.rotation;
            CreateReturnMarker();
            _colliders = _fuller.GetComponentsInChildren<Collider>(true);
            _colliderStates = new bool[_colliders.Length];
            for (int index = 0; index < _colliders.Length; index++)
            {
                _colliderStates[index] = _colliders[index].enabled;
                _colliders[index].enabled = false;
            }
            _bodies = _fuller.GetComponentsInChildren<Rigidbody>(true);
            _kinematicStates = new bool[_bodies.Length];
            _gravityStates = new bool[_bodies.Length];
            _velocities = new Vector3[_bodies.Length];
            _angularVelocities = new Vector3[_bodies.Length];
            for (int index = 0; index < _bodies.Length; index++)
            {
                _kinematicStates[index] = _bodies[index].isKinematic;
                _gravityStates[index] = _bodies[index].useGravity;
                _velocities[index] = _bodies[index].linearVelocity;
                _angularVelocities[index] = _bodies[index].angularVelocity;
                _bodies[index].isKinematic = true;
                _bodies[index].useGravity = false;
            }
            _selected = true;
            _interactionId = Guid.NewGuid().ToString("N");
            _attemptedTargets.Clear();
            Context.RecordToolEvent("ToolPickedUp", _extractSwitches ? "SwitchPuller" : "KeycapRemover", _interactionId);
            UpdateToolPose(ray);
            HeldToolChanged?.Invoke(_fuller);
            ResetHold();
            _extracting = true;
            return true;
        }

        /// <summary>
        /// ray와 보관한 집기 지점으로 도구 자세를 갱신하고 카메라와 heldEulerAngles를 기준으로 회전한다.
        /// 카메라 평행 모드에서는 고정 평면으로 화면 크기를 유지하며 기존 모드에서는 실제 표면을 따른다. 반환값은 없다.
        /// </summary>
        private void UpdateToolPose(Ray ray)
        {
            Vector3 position;
            Collider surface = _followCameraPlane ? null : FindVisibleCollider(ray);
            if (surface != null && surface.Raycast(ray, out RaycastHit hit, Context.InputCamera.farClipPlane))
                position = hit.point;
            else
            {
                if (!_followCameraPlane)
                    _followPlane = new Plane(Vector3.up, new Vector3(0f, Context.DragWorldHeight, 0f));
                if (!_followPlane.Raycast(ray, out float distance))
                    return;
                position = ray.GetPoint(distance);
            }
            _fuller.rotation = Context.InputCamera.transform.rotation * Quaternion.Euler(_heldEulerAngles);
            _fuller.position += position - _fuller.TransformPoint(_localGripPosition);
        }

        /// <summary>
        /// collider가 이 도구 또는 동적 복귀 표시의 자식인지 반환한다.
        /// 다른 조립 입력이 도구 외형에 가로막히지 않도록 한다.
        /// </summary>
        public bool OwnsCollider(Collider collider)
        {
            return collider.transform.IsChildOf(_fuller)
                || (_returnMarker != null && collider.transform.IsChildOf(_returnMarker.transform));
        }

        /// <summary>
        /// 풀러의 메시와 원래 자세를 사용해 반투명 복귀 표시와 클릭 범위를 생성한다.
        /// 원본 메시를 공유하며 스크립트와 물리 동작은 복제하지 않는다.
        /// </summary>
        private void CreateReturnMarker()
        {
            _returnMarker = new GameObject("FullerReturnOutline");
            _returnMarker.transform.SetPositionAndRotation(_originPosition, _originRotation);
            _returnMarker.transform.localScale = _fuller.lossyScale;
            _outlineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Color color = _outlineColor;
            color.a *= _returnOpacity;
            _outlineMaterial.SetColor("_BaseColor", color);
            _outlineMaterial.SetFloat("_Surface", 1f);
            _outlineMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _outlineMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _outlineMaterial.SetFloat("_ZWrite", 0f);
            _outlineMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _outlineMaterial.SetOverrideTag("RenderType", "Transparent");
            _outlineMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            var transforms = new Dictionary<Transform, Transform> { { _fuller, _returnMarker.transform } };
            Bounds total = new Bounds();
            bool hasBounds = false;
            foreach (MeshRenderer renderer in _fuller.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled)
                    continue;
                CreateReturnMesh(renderer, transforms);
                Bounds bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = _fuller.InverseTransformPoint(renderer.transform.TransformPoint(
                        bounds.center + Vector3.Scale(bounds.extents, sign)));
                    if (hasBounds)
                        total.Encapsulate(point);
                    else
                    {
                        total = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }
            _returnCollider = _returnMarker.AddComponent<BoxCollider>();
            _returnCollider.center = total.center;
            _returnCollider.size = Vector3.Max(total.size, Vector3.one * _outlineWidth);
            _returnCollider.isTrigger = true;
        }

        /// <summary>
        /// renderer의 메시와 transforms의 복제 계층을 사용해 복귀 위치의 외형을 생성한다.
        /// 모든 서브메시에 표시용 재질을 적용하고 그림자와 광원 프로브를 비활성화한다.
        /// </summary>
        private void CreateReturnMesh(MeshRenderer renderer, Dictionary<Transform, Transform> transforms)
        {
            MeshFilter source = renderer.GetComponent<MeshFilter>();
            if (source == null || source.sharedMesh == null)
                return;
            Transform target = CreateReturnTransform(renderer.transform, transforms);
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            MeshRenderer ghost = target.gameObject.AddComponent<MeshRenderer>();
            Material[] materials = new Material[source.sharedMesh.subMeshCount];
            for (int index = 0; index < materials.Length; index++)
                materials[index] = _outlineMaterial;
            ghost.sharedMaterials = materials;
            ghost.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.receiveShadows = false;
            ghost.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            ghost.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// original의 부모 계층과 로컬 자세를 복사하고 transforms에 등록한 Transform을 반환한다.
        /// 이미 생성한 계층은 재사용하며 원본 컴포넌트는 복제하지 않는다.
        /// </summary>
        private Transform CreateReturnTransform(Transform original, Dictionary<Transform, Transform> transforms)
        {
            if (transforms.TryGetValue(original, out Transform target))
                return target;
            Transform parent = CreateReturnTransform(original.parent, transforms);
            target = new GameObject(original.name).transform;
            target.SetParent(parent, false);
            target.localPosition = original.localPosition;
            target.localRotation = original.localRotation;
            target.localScale = original.localScale;
            transforms.Add(original, target);
            return target;
        }

        /// <summary>
        /// ray의 가장 가까운 실제 충돌체를 반환하며 들고 있는 Fuller와 복귀 표시는 제외한다.
        /// 다른 물체 뒤에 가려진 키캡을 접촉 대상으로 선택하지 않는다.
        /// </summary>
        private Collider FindVisibleCollider(Ray ray)
        {
            Collider nearest = null;
            float distance = Context.InputCamera.farClipPlane;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (_followCameraPlane && hit.rigidbody != null
                    && hit.rigidbody.GetComponent<KeyboardInputController>() != null
                    && hit.collider.GetComponentInParent<KeycapHealth>() == null
                    && hit.collider.GetComponentInParent<KeyboardSwitch>() == null) continue;
                if ((_selected && Context.IsToolCollider(hit.collider))
                    || (_returnMarker != null && hit.transform.IsChildOf(_returnMarker.transform)))
                    continue;
                if (hit.distance < distance)
                {
                    nearest = hit.collider;
                    distance = hit.distance;
                }
            }
            return nearest;
        }

        /// <summary>
        /// 화면의 start부터 end까지 레이캐스트하여 extractSwitches와 일치하는 장착 부품만 분리한다.
        /// UI와 복귀 표시 뒤는 건너뛰며 성공한 분리만 지연 회수하고 같은 부품의 반복 분리를 방지한다.
        /// </summary>
        private void ExtractAlongPointer(Vector2 start, Vector2 end)
        {
            Physics.SyncTransforms();
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / _sweepStepPixels));
            for (int index = 0; index <= steps; index++)
            {
                Vector2 position = Vector2.Lerp(start, end, (float)index / steps);
                if (Context.IsPointerOverUI(position))
                    continue;
                Ray ray = Context.InputCamera.ScreenPointToRay(position);
                if (_returnCollider.Raycast(ray, out RaycastHit returnHit, Context.InputCamera.farClipPlane))
                    continue;
                Collider hit = FindVisibleCollider(ray);
                if (hit == null)
                    continue;
                AssemblyDraggablePart assemblyPart = hit.GetComponentInParent<AssemblyDraggablePart>();
                Component target = assemblyPart != null && assemblyPart.IsSwitch == _extractSwitches
                    ? assemblyPart
                    : _extractSwitches ? hit.GetComponentInParent<KeyboardSwitch>() : hit.GetComponentInParent<KeycapHealth>();
                if (target == null || !_attemptedTargets.Add(target.GetInstanceID()))
                    continue;
                Vector2 sideways = UnityEngine.Random.insideUnitCircle * _popSideSpeed;
                Context.TryExtractPart(hit, _extractSwitches, new Vector3(sideways.x, _popUpSpeed, sideways.y),
                    UnityEngine.Random.onUnitSphere * _spinSpeed, _interactionId);
            }
        }

        /// <summary>
        /// 사용자가 놓은 도구의 ToolPutDown 이벤트를 기록한 뒤 원래 위치로 돌려놓는다.
        /// 현재 도구 종류와 집을 때 만든 interactionId를 사용해 같은 작업으로 묶는다.
        /// </summary>
        private void RecordUserPutDown()
        {
            Context.RecordToolEvent("ToolPutDown", _extractSwitches ? "SwitchPuller" : "KeycapRemover", _interactionId);
            ReturnToOrigin();
        }

        /// <summary>
        /// 버튼 해제 또는 입력 취소 시 연속 분리 활성 상태와 경로를 초기화한다.
        /// Fuller의 준비 상태와 이미 분리된 키캡 및 재고는 변경하지 않는다.
        /// </summary>
        public void ResetHold()
        {
            _extracting = false;
            _hasPreviousPointer = false;
        }

        /// <summary>
        /// 선택한 Fuller를 저장한 위치와 회전으로 돌려놓고 원래 물리 및 충돌 상태를 복원한다.
        /// 복귀 표시와 준비 상태를 제거하며 설치 결과와 재고는 변경하지 않는다.
        /// </summary>
        public void ReturnToOrigin()
        {
            ResetHold();
            if (!_selected)
                return;
            _selected = false;
            HeldToolChanged?.Invoke(null);
            _fuller.SetPositionAndRotation(_originPosition, _originRotation);
            for (int index = 0; index < _bodies.Length; index++)
            {
                _bodies[index].isKinematic = _kinematicStates[index];
                _bodies[index].useGravity = _gravityStates[index];
                if (!_kinematicStates[index])
                {
                    _bodies[index].linearVelocity = _velocities[index];
                    _bodies[index].angularVelocity = _angularVelocities[index];
                }
            }
            for (int index = 0; index < _colliders.Length; index++)
                _colliders[index].enabled = _colliderStates[index];
            _returnMarker.SetActive(false);
            Destroy(_returnMarker);
            Destroy(_outlineMaterial);
            _returnMarker = null;
            _returnCollider = null;
            _outlineMaterial = null;
            _interactionId = null;
            _attemptedTargets.Clear();
        }

        void OnDisable()
        {
            ReturnToOrigin();
        }
    }
}
