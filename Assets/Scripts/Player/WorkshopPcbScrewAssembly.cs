using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

using KeyboardModeling.Assembly;

namespace Game.Player
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class WorkshopPcbScrewAssembly : MonoBehaviour
    {
        [Header("Screw Position Markers")]
        [SerializeField, Min(0.01f)] private float _markerScale = 1.6f;
        [SerializeField, Min(1f)] private float _markerThickness = 4f;
        [SerializeField, Min(0f)] private float _hitPadding = 0.08f;
        [SerializeField] private Color _looseColor = new Color(1f, 0.65f, 0.2f, 0.8f);
        [SerializeField] private Color _fixedColor = new Color(0.3f, 1f, 0.45f, 0.8f);

        [Header("Screw Interaction")]
        private WorkshopKeyboardPullerController _pullers;
        private KeyboardAssemblyState _assembly;
        private Material _outlineMaterial;
        private Shader _depthShader;
        private Material _markerMaterial;
        private readonly List<WorkshopKeyboardPlacementPreview> _outlines = new List<WorkshopKeyboardPlacementPreview>();
        private MaterialPropertyBlock _markerProperties;
        private readonly List<MeshCollider> _blockers = new List<MeshCollider>();
        private bool _selected;
        private bool _captured;

        /// <summary>pullers와 기존 아웃라인 에셋을 연결한다. 가상 커서 카메라를 재사용하며 나사 진행 상태는 변경하지 않는다.</summary>
        public void Initialize(WorkshopKeyboardPullerController pullers, Material outlineMaterial, Shader depthShader)
        {
            _markerProperties = new MaterialPropertyBlock();
            _pullers = pullers;
            _outlineMaterial = outlineMaterial;
            _depthShader = depthShader;
        }

        /// <summary>shader와 Inspector 설정으로 나사 표시 전용 재질과 크기를 준비한다. 원본 재질과 나사 상태는 유지한다.</summary>
        public void ConfigureMarkers(Shader shader, float scale, float padding, Color looseColor, Color fixedColor, float thickness)
        {
            if (shader == null) return;
            _markerMaterial = new Material(shader);
            _markerScale = scale;
            _markerThickness = thickness;
            _hitPadding = padding;
            _looseColor = looseColor;
            _fixedColor = fixedColor;
        }

        /// <summary>index의 최종 체결 위치와 머리 중심으로 표시 행렬을 반환한다. 중심과 축을 유지하고 표시 모델만 확대한다.</summary>
        private Matrix4x4 GetMarkerMatrix(int index)
        {
            Transform screw = _assembly.GetScrew(index);
            Pose pose = _assembly.GetScrewPose(index);
            Vector3 center = GetHeadBounds(screw).center;
            if (_markerMaterial == null) return Matrix4x4.TRS(pose.position, pose.rotation, screw.lossyScale);
            return Matrix4x4.TRS(pose.position, pose.rotation, screw.lossyScale)
                * Matrix4x4.Translate(center) * Matrix4x4.Scale(new Vector3(1f, Mathf.Max(1f, _markerThickness), 1f) * Mathf.Max(0.01f, _markerScale))
                * Matrix4x4.Translate(-center);
        }

        /// <summary>assembly와 selected로 PCB 탭의 나사 조립 대상을 갱신한다. 대상 변경 시 안내만 다시 준비하며 시작한 체결 연출은 유지한다.</summary>
        public void SetContext(KeyboardAssemblyState assembly, bool selected)
        {
            if (_assembly != assembly)
            {
                ClearGeometry();
                _assembly = assembly;
                if (assembly != null)
                {
                    if (_markerMaterial == null)
                        for (int index = 0; index < 6; index++)
                        {
                            var outline = new WorkshopKeyboardPlacementPreview(_outlineMaterial, _depthShader, _pullers.InputCamera);
                            outline.Prepare(assembly.GetScrew(index), true);
                            _outlines.Add(outline);
                        }
                    foreach (MeshRenderer renderer in assembly.UpperRenderers)
                    {
                        GameObject proxy = new GameObject("ScrewPointerOcclusion");
                        proxy.layer = 2;
                        MeshCollider blocker = proxy.AddComponent<MeshCollider>();
                        blocker.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        blocker.excludeLayers = ~0;
                        blocker.enabled = false;
                        _blockers.Add(blocker);
                    }
                }
            }
            _selected = selected;
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (_captured && (mouse == null || !mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame))
                _captured = false;
            _pullers.SetScrewPointerCaptured(_captured);
            if (_captured || !_selected || _assembly == null || !_assembly.PcbPlaced || _assembly.PlateInstalled || _assembly.UpperInstalled || Time.timeScale <= 0f
                || !_pullers.InputAllowed || _pullers.IsManipulatingPart || mouse == null
                || !mouse.leftButton.wasPressedThisFrame || _pullers.IsPointerOverUI(InGameCursor.Position)) return;
            Ray ray = _pullers.InputCamera.ScreenPointToRay(InGameCursor.Position);
            int hit = FindScrew(ray, out float distance);
            if (hit < 0 || IsBlocked(ray, distance)) return;
            _captured = true;
            _pullers.SetScrewPointerCaptured(true);
            if (_assembly.FixedScrews[hit]) _assembly.ReleaseScrew(hit);
            else _assembly.FixScrew(hit);
        }

        void LateUpdate()
        {
            if (!_selected || _assembly == null || !_assembly.PcbPlaced || _assembly.PlateInstalled || _assembly.UpperInstalled
                || !_pullers.InputAllowed || Time.timeScale <= 0f) return;
            for (int index = 0; index < 6; index++)
            {
                if (!_assembly.CanFixScrew(index) && !_assembly.CanReleaseScrew(index)) continue;
                if (_markerMaterial == null)
                {
                    _outlines[index].SetOutlineColor(_assembly.FixedScrews[index] ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.65f, 0.2f));
                    _outlines[index].Show(_assembly.GetScrewPose(index));
                    continue;
                }
                Transform screw = _assembly.GetScrew(index);
                Matrix4x4 matrix = GetMarkerMatrix(index);
                Vector3 center = matrix.MultiplyPoint3x4(GetHeadBounds(screw).center);
                Vector3 direction = center - _pullers.InputCamera.transform.position;
                Ray ray = new Ray(_pullers.InputCamera.transform.position, direction);
                if (IsBlocked(ray, direction.magnitude)) continue;
                _markerProperties.SetColor("_Color", _assembly.FixedScrews[index] ? _fixedColor : _looseColor);
                Mesh mesh = screw.GetComponent<MeshFilter>().sharedMesh;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    Graphics.DrawMesh(mesh, matrix, _markerMaterial, 2, _pullers.InputCamera, submesh,
                        _markerProperties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }

        /// <summary>ray가 닿는 나사 머리의 가장 가까운 인덱스와 월드 거리를 반환한다. 진행 중·완료 나사도 입력을 소비하도록 판정하며 상태는 변경하지 않는다.</summary>
        private int FindScrew(Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            int nearest = -1;
            for (int index = 0; index < 6; index++)
            {
                if (!_assembly.CanFixScrew(index) && !_assembly.CanReleaseScrew(index)) continue;
                Transform screw = _assembly.GetScrew(index);
                Bounds bounds = GetHeadBounds(screw);
                bounds.Expand(_hitPadding);
                bounds.size *= 1.5f;
                float candidate = GetHitDistance(ray, bounds, GetMarkerMatrix(index));
                if (candidate >= distance) continue;
                distance = candidate;
                nearest = index;
            }
            return nearest;
        }

        /// <summary>screw의 원본 메시 기준으로 나사 머리 영역을 반환한다. 로컬 Y 원점은 머리 아랫면이며 클릭 여유만 더한다.</summary>
        private Bounds GetHeadBounds(Transform screw)
        {
            Bounds bounds = screw.GetComponent<MeshFilter>().sharedMesh.bounds;
            float top = bounds.max.y;
            bounds.center = new Vector3(bounds.center.x, top * 0.5f, bounds.center.z);
            bounds.size = new Vector3(bounds.size.x, top, bounds.size.z);

            return bounds;
        }

        /// <summary>ray와 bounds를 matrix 기준으로 교차 검사한다. 교차점까지의 월드 거리를 반환하며 빗나가면 무한대를 반환한다.</summary>
        private float GetHitDistance(Ray ray, Bounds bounds, Matrix4x4 matrix)
        {
            Matrix4x4 inverse = matrix.inverse;
            Ray local = new Ray(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction));
            if (!bounds.IntersectRay(local, out float distance)) return float.PositiveInfinity;
            return Vector3.Distance(ray.origin, matrix.MultiplyPoint3x4(local.GetPoint(distance)));
        }

        /// <summary>ray의 distance 앞에 상단부 실제 메시나 외부 물체가 있는지 반환한다. 입력 전용 메시 프록시는 물리 충돌과 일반 레이캐스트에서 제외한다.</summary>
        private bool IsBlocked(Ray ray, float distance)
        {
            for (int index = 0; index < _blockers.Count; index++)
            {
                MeshRenderer source = _assembly.UpperRenderers[index];
                MeshCollider blocker = _blockers[index];
                blocker.enabled = source.enabled && source.gameObject.activeInHierarchy;
                if (!blocker.enabled) continue;
                blocker.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                blocker.transform.localScale = source.transform.lossyScale;
            }
            Physics.SyncTransforms();
            foreach (MeshCollider blocker in _blockers)
                if (blocker.enabled && blocker.Raycast(ray, out RaycastHit hit, distance)) return true;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(_assembly.transform)) return true;
            return false;
        }

        /// <summary>표시용 아웃라인 재질과 입력 차단 메시 프록시를 정리한다. 원본 모델과 시작된 나사 체결은 유지한다.</summary>
        private void ClearGeometry()
        {
            foreach (var outline in _outlines) outline.Dispose();
            _outlines.Clear();

            foreach (MeshCollider blocker in _blockers)
                if (blocker != null) Destroy(blocker.gameObject);
            _blockers.Clear();
        }

        void OnDisable()
        {
            _captured = false;
            if (_pullers != null) _pullers.SetScrewPointerCaptured(false);
            foreach (MeshCollider blocker in _blockers)
                if (blocker != null) blocker.enabled = false;
        }

        void OnDestroy() { ClearGeometry(); if (_markerMaterial != null) Destroy(_markerMaterial); }
    }
}
