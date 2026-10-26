using System;
using System.Collections.Generic;

using UnityEngine;

using Game.Logging;

namespace KeyboardModeling.Assembly
{
    public enum AssemblyStructurePart
    {
        Pcb,
        Plate,
        Upper,
        Lower
    }

    [DisallowMultipleComponent]
    public sealed class KeyboardAssemblyState : MonoBehaviour
    {
        [Header("Assembly Parts")]
        [SerializeField] private KeyboardCustomization _customization;
        [SerializeField] private KeyboardPartInstallation _installation;
        [SerializeField] private Transform _pcb;
        [SerializeField] private Transform[] _screws;
        [SerializeField] private MeshRenderer[] _upperRenderers;
        private Transform _lower;
        private Transform _lowerParent;
        private Transform _plate;
        private Transform _pcbParent;
        private readonly Dictionary<Transform, Transform> _pcbPartParents = new Dictionary<Transform, Transform>();
        private readonly Dictionary<Rigidbody, (bool Kinematic, bool Gravity, bool Collisions)> _pcbBodies
            = new Dictionary<Rigidbody, (bool, bool, bool)>();

        [Header("PCB Screw Fastening")]
        [SerializeField, Min(0f)] private float _screwStartHeight = 0.8f;
        [SerializeField, Min(0.01f)] private float _screwDuration = 0.65f;
        [SerializeField] private float _screwRotationDegrees = 720f;
        private readonly Vector3[] _screwPositions = new Vector3[6];
        private readonly Quaternion[] _screwRotations = new Quaternion[6];
        private readonly bool[] _runningScrews = new bool[6];
        private readonly bool[] _releasingScrews = new bool[6];
        private readonly float[] _screwElapsed = new float[6];

        [Header("Assembly Progress")]
        private bool _initialized;
        private bool _pcbInstalled;
        private bool _lowerInstalled;
        private bool _lowerLoose;
        private bool _pcbLoose;
        private bool _plateInstalled;
        private bool _upperInstalled;
        private readonly bool[] _fixedScrews = new bool[6];
        private int _screwCount;
        private Vector3 _pcbLocalPosition;
        private Quaternion _pcbLocalRotation;
        private Vector3 _pcbLocalScale;
        private Bounds _pcbSlotBounds;
        private readonly Transform[][] _structureParts = new Transform[4][];
        private readonly Vector3[][] _structurePositions = new Vector3[4][];
        private readonly Quaternion[][] _structureRotations = new Quaternion[4][];
        private readonly Vector3[][] _structureScales = new Vector3[4][];

        private readonly Dictionary<AssemblyStructurePart, AssemblyCatalogEntry> _structureInventory = new Dictionary<AssemblyStructurePart, AssemblyCatalogEntry>();

        public bool Initialized => _initialized;
        public Transform Pcb => _pcb;
        public IReadOnlyList<MeshRenderer> UpperRenderers => _upperRenderers;
        public Pose PcbPose => new Pose(transform.TransformPoint(_pcbLocalPosition), transform.rotation * _pcbLocalRotation);
        public event Action PcbPlacementCompleted;
        public bool PcbPlaced => _pcbInstalled;
        public bool PcbLoose => _pcbLoose;
        public bool PcbComplete => _pcbInstalled && _screwCount == 6;
        public bool PlateInstalled => _plateInstalled;
        public bool UpperInstalled => _upperInstalled;
        public bool LowerInstalled => _lowerInstalled;
        public int ScrewCount => _screwCount;
        public IReadOnlyList<bool> FixedScrews => Array.AsReadOnly(_fixedScrews);
        public Material UpperMaterial => _upperRenderers[0].sharedMaterial;
        public bool CanInstallPcb => _initialized && _lowerInstalled
            && !_pcbInstalled && !_plateInstalled && !_upperInstalled;
        public bool CanInstallPlate => _initialized && PcbComplete && !_plateInstalled
            && !_upperInstalled && !_customization.HasAnyInstalledKeycaps();
        public bool CanInstallUpper => _initialized && _lowerInstalled && !_upperInstalled;
        public bool CanInstallLower => _initialized && !_lowerInstalled;
        public bool CanRemoveLower => _initialized && _lowerInstalled && !_pcbInstalled
            && !_plateInstalled && !_upperInstalled && _screwCount == 0
            && (_pcbLoose || !_customization.HasAnyInstalledKeycaps() && !_customization.HasAnySwitchForKeycaps());
        public bool LowerLoose => _lowerLoose;
        public bool CanRemovePcb
        {
            get
            {
                if (!_initialized || !_pcbInstalled || _upperInstalled || _plateInstalled || _screwCount != 0) return false;
                foreach (bool running in _runningScrews) if (running) return false;
                return true;
            }
        }
        public bool CanRemoveUpper => _initialized && _upperInstalled;
        public bool CanRemovePlate => _initialized && _plateInstalled && !_upperInstalled
            && !_customization.HasAnyInstalledKeycaps();

        /// <summary>kind의 설치된 상단부·보강판·빈 하단부를 분리한다. 선행 상태를 검사하고 성공 시 해당 점유만 해제한다.</summary>
        public bool TryRemoveStructure(AssemblyStructurePart kind)
        {
            if (kind == AssemblyStructurePart.Upper && CanRemoveUpper) _upperInstalled = false;
            else if (kind == AssemblyStructurePart.Plate && CanRemovePlate) _plateInstalled = false;
            else if (kind == AssemblyStructurePart.Lower && CanRemoveLower)
            {
                _lowerInstalled = false;
                _lowerLoose = true;
                _lower.SetParent(null, true);
            }
            else return false;
            return true;
        }

        /// <summary>취소된 kind의 구조 부품을 원래 설치 상태로 되돌린다. 해당 점유와 원래 자세를 복구한다.</summary>
        public void RestoreRemovedStructure(AssemblyStructurePart kind)
        {
            if (kind == AssemblyStructurePart.Upper) _upperInstalled = true;
            else if (kind == AssemblyStructurePart.Plate) _plateInstalled = true;
            else if (kind == AssemblyStructurePart.Lower) { _lowerInstalled = true; _lowerLoose = false; }
            RestoreStructurePose(kind);
        }

        /// <summary>하단부를 재고에 반환한다. 원본 관리 객체는 유지하고 하단부 실물만 숨긴다.</summary>
        public void ReturnLowerToInventory()
        {
            _lowerLoose = false;
            RestoreStructurePose(AssemblyStructurePart.Lower);
        }

        /// <summary>재고에서 꺼낸 미설치 하단부를 바닥 부품으로 전환한다. 관리 객체를 유지하고 실물의 부모만 분리한다.</summary>
        public void DropLooseLower()
        {
            _lowerLoose = true;
            _lower.SetParent(null, true);
        }

        /// <summary>최초 신규 조립에서 연결된 부품의 연출을 취소하고 하단부만 남긴다. 입력 없이 초기화 표시를 저장하며 재호출은 상태를 유지한다.</summary>
        public void InitializeNewAssembly()
        {
            if (_initialized) return;
            _lower = _customization.CaseRenderers[0].transform;
            _lowerParent = _lower.parent;
            _lowerInstalled = true;
            _plate = _customization.PlateRenderer.transform;
            _pcbLocalPosition = _pcb.localPosition;
            _pcbParent = _pcb.parent;
            _pcbLocalRotation = _pcb.localRotation;
            _pcbLocalScale = _pcb.localScale;
            CacheStructurePose(AssemblyStructurePart.Pcb, new[] { _pcb });
            CacheStructurePose(AssemblyStructurePart.Lower, new[] { _lower });
            BoxCollider lowerCollider = _lower.GetComponent<BoxCollider>();
            if (lowerCollider == null) lowerCollider = _lower.gameObject.AddComponent<BoxCollider>();
            Bounds lowerBounds = _lower.GetComponent<MeshFilter>().sharedMesh.bounds;
            lowerCollider.center = lowerBounds.center;
            lowerCollider.size = new Vector3(lowerBounds.size.x, Mathf.Max(lowerBounds.size.y, 0.02f), lowerBounds.size.z);
            CacheStructurePose(AssemblyStructurePart.Plate, new[] { _plate });
            BoxCollider plateCollider = _plate.GetComponent<BoxCollider>();
            if (plateCollider == null) plateCollider = _plate.gameObject.AddComponent<BoxCollider>();
            Bounds plateBounds = _plate.GetComponent<MeshFilter>().sharedMesh.bounds;
            plateCollider.center = plateBounds.center;
            plateCollider.size = new Vector3(plateBounds.size.x, Mathf.Max(plateBounds.size.y, 0.02f), plateBounds.size.z);
            var upperParts = new Transform[_upperRenderers.Length];
            for (int index = 0; index < upperParts.Length; index++) upperParts[index] = _upperRenderers[index].transform;
            CacheStructurePose(AssemblyStructurePart.Upper, upperParts);
            for (int index = 0; index < _screws.Length; index++)
            {
                _screwPositions[index] = _screws[index].localPosition;
                _screwRotations[index] = _screws[index].localRotation;
            }
            MeshFilter board = _pcb.Find("PCB_Board").GetComponent<MeshFilter>();
            Matrix4x4 matrix = transform.worldToLocalMatrix * board.transform.localToWorldMatrix;
            Bounds bounds = board.sharedMesh.bounds;
            _pcbSlotBounds = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                _pcbSlotBounds.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
            BoxCollider pcbCollider = _pcb.GetComponent<BoxCollider>();
            if (pcbCollider == null) pcbCollider = _pcb.gameObject.AddComponent<BoxCollider>();
            Matrix4x4 pcbMatrix = _pcb.worldToLocalMatrix * board.transform.localToWorldMatrix;
            Bounds pcbBounds = new Bounds(pcbMatrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                pcbBounds.Encapsulate(pcbMatrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
            pcbCollider.center = pcbBounds.center;
            pcbCollider.size = new Vector3(pcbBounds.size.x, Mathf.Max(pcbBounds.size.y, 0.02f), pcbBounds.size.z);
            _installation.CancelAll();
            _customization.ConnectPcbSlots(_pcb);
            _lower.gameObject.SetActive(true);
            ApplyStructureVisibility();
            _customization.DisableKeycaps();
            _customization.DisableSwitches();
            _customization.SetLEDEnabled(false);
            _initialized = true;
        }

        /// <summary>
        /// 신규 조립으로 초기화된 키보드를 완성 상태로 전환한다.
        /// 캐시한 PCB, 나사, 보강판, 상단부와 기본 축 및 키캡을 복원하고 조립 진행 상태를 갱신한다.
        /// </summary>
        public void CompleteInitialAssembly()
        {
            if (!_initialized) return;
            _pcbInstalled = true;
            _lowerInstalled = true;
            _plateInstalled = true;
            _upperInstalled = true;
            _screwCount = _screws.Length;
            for (int index = 0; index < _screws.Length; index++)
            {
                _fixedScrews[index] = true;
                _screws[index].SetLocalPositionAndRotation(_screwPositions[index], _screwRotations[index]);
            }
            RestorePcbPose();
            RestoreStructurePose(AssemblyStructurePart.Lower);
            RestoreStructurePose(AssemblyStructurePart.Plate);
            RestoreStructurePose(AssemblyStructurePart.Upper);
            ApplyStructureVisibility();
            _customization.PrepareCompletedAssembly();
        }

        /// <summary>하단부와 간섭 부품 상태를 재검사해 PCB를 즉시 배치한다. 성공하면 배치 상태를 저장하고 true를 반환하며 연출은 실행하지 않는다.</summary>
        private bool InstallPcb()
        {
            if (!CanInstallPcb) return false;
            _pcbInstalled = true;
            _pcbLoose = false;
            RestorePcbPose();
            PcbPlacementCompleted?.Invoke();
            return true;
        }

        /// <summary>ray가 지정된 하단부의 PCB 설치 영역을 통과하는지 반환한다. 현재 설치 조건과 키보드 로컬 XZ 경계만 검사한다.</summary>
        public bool IsInsidePcbSlot(Ray ray)
        {
            if (!CanInstallPcb) return false;
            Ray local = new Ray(transform.InverseTransformPoint(ray.origin), transform.InverseTransformVector(ray.direction));
            if (!new Plane(Vector3.up, _pcbSlotBounds.center).Raycast(local, out float distance)) return false;
            Vector3 point = local.GetPoint(distance);
            return point.x >= _pcbSlotBounds.min.x && point.x <= _pcbSlotBounds.max.x
                && point.z >= _pcbSlotBounds.min.z && point.z <= _pcbSlotBounds.max.z;
        }

        /// <summary>ray가 설치된 PCB 보드 영역을 통과하는지 검사한다. 맞으면 월드 집기 위치를 반환하고 조립 상태는 변경하지 않는다.</summary>
        public bool TryGetInstalledPcbHit(Ray ray, out Vector3 point)
        {
            point = default;
            if (!CanRemovePcb) return false;
            Ray local = new Ray(transform.InverseTransformPoint(ray.origin), transform.InverseTransformVector(ray.direction));
            if (!new Plane(Vector3.up, _pcbSlotBounds.center).Raycast(local, out float distance)) return false;
            Vector3 hit = local.GetPoint(distance);
            if (hit.x < _pcbSlotBounds.min.x || hit.x > _pcbSlotBounds.max.x
                || hit.z < _pcbSlotBounds.min.z || hit.z > _pcbSlotBounds.max.z) return false;
            point = transform.TransformPoint(hit);
            return true;
        }

        /// <summary>ray의 설치 영역과 현재 조립 조건을 다시 검사해 PCB 배치를 확정한다. 실패 시 상태를 유지하고 false를 반환한다.</summary>
        public bool TryPlacePcb(Ray ray)
        {
            return IsInsidePcbSlot(ray) && InstallPcb();
        }

        /// <summary>입력 없이 원본 PCB를 지정된 로컬 위치와 회전 및 크기로 복원한다. 배치 여부에 맞춰 활성 상태를 갱신한다.</summary>
        public void RestorePcbPose()
        {
            _pcb.SetParent(_pcbParent, true);
            _pcb.SetLocalPositionAndRotation(_pcbLocalPosition, _pcbLocalRotation);
            _pcb.localScale = _pcbLocalScale;
            Rigidbody body = _pcb.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.useGravity = false; }
            _pcb.gameObject.SetActive(_pcbInstalled);
        }

        /// <summary>PCB와 나사 조건을 확인한 뒤 PCB와 현재 장착 축·키캡만 하나의 이동 계층으로 묶는다. 성공 시 PCB 점유를 해제한다.</summary>
        public bool TryRemovePcb()
        {
            if (!CanRemovePcb) return false;
            for (int index = 0; index < _customization.KeycapCount; index++)
            {
                if (_customization.IsPcbSwitchInstalled(index)) AttachPcbPart(_customization.GetSwitchSlotTransform(index));
                if (_customization.IsPcbKeycapInstalled(index)) AttachPcbPart(_customization.GetKeycapSlotTransform(index));
            }
            _pcb.SetParent(null, true);
            _pcbInstalled = false;
            _pcbLoose = true;
            return true;
        }

        /// <summary>part의 기존 부모를 보관하고 PCB에 연결한다. 월드 자세와 개별 커스텀 상태는 유지한다.</summary>
        private void AttachPcbPart(Transform part)
        {
            if (!_pcbPartParents.ContainsKey(part)) _pcbPartParents.Add(part, part.parent);
            part.SetParent(_pcb, true);
            foreach (Rigidbody body in part.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!_pcbBodies.ContainsKey(body))
                    _pcbBodies.Add(body, (body.isKinematic, body.useGravity, body.detectCollisions));
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
            }
        }

        /// <summary>재고 반환 전에 PCB에 묶인 부품의 원래 부모를 복원한다. 실물 활성 상태와 재고는 변경하지 않는다.</summary>
        public void ReleasePcbParts()
        {
            foreach (KeyValuePair<Transform, Transform> pair in _pcbPartParents)
                pair.Key.SetParent(pair.Value, true);
            _pcbPartParents.Clear();
            foreach (var pair in _pcbBodies)
            {
                pair.Key.isKinematic = pair.Value.Kinematic;
                pair.Key.useGravity = pair.Value.Gravity;
                pair.Key.detectCollisions = pair.Value.Collisions;
            }
            _pcbBodies.Clear();
        }

        /// <summary>취소된 PCB 집기를 원래 조립 자세로 복구한다. 묶음의 키캡·축과 나사 상태는 유지한다.</summary>
        public void RestoreRemovedPcb()
        {
            _pcbInstalled = true;
            _pcbLoose = false;
            RestorePcbPose();
        }

        /// <summary>반환된 PCB의 바닥 점유를 해제한다. 재고 카드에서 다시 꺼낼 수 있도록 상태를 갱신한다.</summary>
        public void ReturnPcbToInventory()
        {
            _pcbLoose = false;
            RestorePcbPose();
        }

        /// <summary>index의 나사가 PCB 배치 후 아직 고정되지 않았는지 반환한다. 조립 상태는 변경하지 않는다.</summary>
        public bool CanFixScrew(int index)
        {
            return _initialized && _pcbInstalled && index >= 0 && index < _fixedScrews.Length
                && !_plateInstalled && !_upperInstalled && !_fixedScrews[index] && !_runningScrews[index];
        }

        /// <summary>index의 체결 나사가 분리 가능한지 검사한다. PCB와 덮개 상태를 사용하며 조립 상태는 변경하지 않는다.</summary>
        public bool CanReleaseScrew(int index)
        {
            return _initialized && _pcbInstalled && index >= 0 && index < _fixedScrews.Length
                && !_plateInstalled && !_upperInstalled && _fixedScrews[index] && !_runningScrews[index];
        }

        /// <summary>index의 체결 나사를 역방향으로 풀기 시작한다. 완료 상태는 연출 종료까지 유지하며 성공 여부를 반환한다.</summary>
        public bool ReleaseScrew(int index)
        {
            if (!CanReleaseScrew(index) || Time.timeScale <= 0f) return false;
            _runningScrews[index] = true;
            _releasingScrews[index] = true;
            _screwElapsed[index] = 0f;
            return true;
        }

        /// <summary>index의 미고정 나사 체결을 시작한다. 진행 중 또는 완료된 나사는 거부하고 완료 수량은 연출이 끝날 때만 변경한다.</summary>
        public bool FixScrew(int index)
        {
            if (!CanFixScrew(index) || Time.timeScale <= 0f) return false;
            _runningScrews[index] = true;
            _releasingScrews[index] = false;
            _screwElapsed[index] = 0f;
            _screws[index].gameObject.SetActive(true);
            ApplyScrewPose(index, 0f);
            return true;
        }

        /// <summary>index의 기존 나사 Transform을 반환한다. 원본 메시와 조립 진행 상태는 변경하지 않는다.</summary>
        public Transform GetScrew(int index) => _screws[index];

        /// <summary>index의 나사가 체결 중인지 반환한다. 입력 상태와 완료 수량은 변경하지 않는다.</summary>
        public bool IsScrewRunning(int index) => _runningScrews[index];

        /// <summary>index의 모델 기준점으로 최종 월드 자세를 반환한다. 현재 연출 위치 대신 최초 캐시한 로컬 위치와 회전을 사용한다.</summary>
        public Pose GetScrewPose(int index)
        {
            Transform parent = _screws[index].parent;
            return new Pose(parent.TransformPoint(_screwPositions[index]), parent.rotation * _screwRotations[index]);
        }

        void Update()
        {
            if (Time.timeScale <= 0f) return;
            for (int index = 0; index < _screws.Length; index++)
            {
                if (!_runningScrews[index]) continue;
                _screwElapsed[index] += Time.deltaTime;
                float progress = Mathf.Clamp01(_screwElapsed[index] / Mathf.Max(0.01f, _screwDuration));
                ApplyScrewPose(index, _releasingScrews[index] ? 1f - progress : progress);
                if (progress < 1f) continue;
                _runningScrews[index] = false;
                _fixedScrews[index] = !_releasingScrews[index];
                _screwCount += _fixedScrews[index] ? 1 : -1;
                if (_releasingScrews[index]) _screws[index].gameObject.SetActive(false);
                CustomizationTelemetry.Emit(_customization, "ScrewChanged", "Pcb",
                    _fixedScrews[index] ? "Fixed" : "Released", changed: true,
                    targetId: "Screw:" + index, actionSource: "ScrewPointer", screwCount: _screwCount);
            }
        }

        /// <summary>index와 progress로 나사를 모델 로컬 Y축을 따라 하강·회전시킨다. 완료 시 캐시한 최종 자세로 정확히 정렬한다.</summary>
        private void ApplyScrewPose(int index, float progress)
        {
            Vector3 axis = _screwRotations[index] * Vector3.up;
            _screws[index].localPosition = _screwPositions[index] + axis * (_screwStartHeight * (1f - progress));
            _screws[index].localRotation = _screwRotations[index]
                * Quaternion.AngleAxis(-_screwRotationDegrees * (1f - progress), Vector3.up);
        }

        /// <summary>PCB 완료와 상단부 및 키캡 상태를 재검사해 보강판을 즉시 설치한다. 성공 시 설치 상태를 저장하고 true를 반환한다.</summary>
        private bool InstallPlate()
        {
            if (!CanInstallPlate) return false;
            _plateInstalled = true;
            RestoreStructurePose(AssemblyStructurePart.Plate);
            return true;
        }

        /// <summary>하단부와 중복 설치 조건을 재검사해 상단부를 즉시 설치한다. 다른 부품은 유지하며 성공 시 true를 반환한다.</summary>
        private bool InstallUpper()
        {
            if (!CanInstallUpper) return false;
            _upperInstalled = true;
            RestoreStructurePose(AssemblyStructurePart.Upper);
            return true;
        }

        /// <summary>kind에 해당하는 기존 구조 부품 실물을 반환한다. 참조만 제공하며 설치 상태는 변경하지 않는다.</summary>
        public IReadOnlyList<Transform> GetStructureParts(AssemblyStructurePart kind) => _structureParts[(int)kind];

        /// <summary>kind의 썸네일용 부품 참조를 반환한다. 초기화된 실물은 기존 목록을 사용하고 원본 Prefab은 직렬화된 참조만 읽어 조립·활성 상태를 변경하지 않는다.</summary>
        public IReadOnlyList<Transform> GetStructurePreviewParts(AssemblyStructurePart kind)
        {
            if (_initialized) return GetStructureParts(kind);
            return kind switch
            {
                AssemblyStructurePart.Pcb => new[] { _pcb },
                AssemblyStructurePart.Plate => new[] { _customization.PlateRenderer.transform },
                AssemblyStructurePart.Lower => new[] { _customization.CaseRenderers[0].transform },
                _ => Array.ConvertAll(_upperRenderers, renderer => renderer.transform)
            };
        }

        /// <summary>kind의 현재 공통 설치 조건을 반환한다. 스위치와 키캡의 기존 설치 상태는 검사에만 사용한다.</summary>
        public bool CanInstallStructure(AssemblyStructurePart kind)
        {
            return kind == AssemblyStructurePart.Pcb ? CanInstallPcb
                : kind == AssemblyStructurePart.Plate ? CanInstallPlate
                : kind == AssemblyStructurePart.Lower ? CanInstallLower : CanInstallUpper;
        }

        /// <summary>kind와 index의 실물에 저장된 최종 월드 자세를 반환한다. 드래그 중 임시 자세는 사용하지 않는다.</summary>
        public Pose GetStructurePose(AssemblyStructurePart kind, int index)
        {
            int part = (int)kind;
            Transform parent = kind == AssemblyStructurePart.Lower ? _lowerParent
                : kind == AssemblyStructurePart.Pcb ? _pcbParent : _structureParts[part][index].parent;
            if (parent == null) parent = transform;
            return new Pose(parent.TransformPoint(_structurePositions[part][index]), parent.rotation * _structureRotations[part][index]);
        }

        /// <summary>kind 부품의 출고 원본 항목을 반환한다. 기록이 없으면 null이며 수량과 설치 상태는 변경하지 않는다.</summary>
        public AssemblyCatalogEntry GetStructureInventoryEntry(AssemblyStructurePart kind)
        {
            _structureInventory.TryGetValue(kind, out AssemblyCatalogEntry entry);
            return entry;
        }

        /// <summary>kind 실물에 사용된 entry를 기록한다. 재출고 중복을 막고 반환 시 같은 재질 항목을 복구하는 데 사용한다.</summary>
        public void SetStructureInventoryEntry(AssemblyStructurePart kind, AssemblyCatalogEntry entry)
        {
            _structureInventory[kind] = entry;
        }

        /// <summary>kind 부품의 작업실 출고 기록을 해제한다. 반복 반환으로 실물 소유가 남지 않도록 기록만 제거하며 수량은 변경하지 않는다.</summary>
        public void ReturnStructureInventory(AssemblyStructurePart kind)
        {
            _structureInventory.Remove(kind);
        }

        /// <summary>출고한 미설치 PCB를 바닥 실물로 기록한다. 같은 키보드에 추가 PCB를 출고하지 않도록 점유를 유지한다.</summary>
        public void MarkPcbLoose()
        {
            _pcbLoose = true;
        }

        /// <summary>kind 부품의 현재 독립 재질을 반환한다. PCB는 배선과 LED를 제외한 기판 재질을 사용한다.</summary>
        public Material GetStructureMaterial(AssemblyStructurePart kind)
        {
            return kind == AssemblyStructurePart.Plate ? _customization.PlateMaterial
                : kind == AssemblyStructurePart.Upper ? UpperMaterial
                : kind == AssemblyStructurePart.Lower ? _customization.CaseMaterial
                : _pcb.Find("PCB_Board").GetComponent<MeshRenderer>().sharedMaterial;
        }

        /// <summary>kind 부품에 material을 선택한다. PCB는 기판 메시만 변경하며 원본 재질 에셋과 설치 상태는 유지한다.</summary>
        public bool SetStructureMaterial(AssemblyStructurePart kind, Material material)
        {
            if (material == null) return false;
            if (kind == AssemblyStructurePart.Pcb)
            {
                MeshRenderer board = _pcb.Find("PCB_Board").GetComponent<MeshRenderer>();
                board.sharedMaterial = material;
                KeyboardToonSurface.ApplyBounds(board);
                return true;
            }
            if (kind == AssemblyStructurePart.Lower)
            {
                if (_customization.CaseMaterial != material) _customization.SetCaseMaterial(material);
                return true;
            }
            if (kind == AssemblyStructurePart.Plate) return _customization.SetPlateMaterial(material);
            if (kind != AssemblyStructurePart.Upper) return false;
            SetUpperMaterial(material);
            return true;
        }

        /// <summary>kind의 설치 조건과 ray의 하단부 영역을 드롭 직전에 검사한다. 성공한 부품만 정위치에 장착하며 다른 부품은 유지한다.</summary>
        public bool TryPlaceStructure(AssemblyStructurePart kind, Ray ray)
        {
            if (kind == AssemblyStructurePart.Pcb) return TryPlacePcb(ray);
            if (!CanInstallStructure(kind)) return false;
            Ray local = new Ray(transform.InverseTransformPoint(ray.origin), transform.InverseTransformVector(ray.direction));
            Vector3 planePoint = transform.InverseTransformPoint(GetStructurePose(kind, 0).position);
            if (!new Plane(Vector3.up, planePoint).Raycast(local, out float distance)) return false;
            Vector3 point = local.GetPoint(distance);
            if (point.x < _pcbSlotBounds.min.x || point.x > _pcbSlotBounds.max.x
                || point.z < _pcbSlotBounds.min.z || point.z > _pcbSlotBounds.max.z) return false;
            if (kind == AssemblyStructurePart.Lower)
            {
                _lowerInstalled = true;
                _lowerLoose = false;
                RestoreStructurePose(kind);
                return true;
            }
            return kind == AssemblyStructurePart.Plate ? InstallPlate() : InstallUpper();
        }

        /// <summary>kind와 parts의 원래 로컬 위치·회전·크기를 최초 초기화에서 저장한다. 실물의 계층과 설치 상태는 변경하지 않는다.</summary>
        private void CacheStructurePose(AssemblyStructurePart kind, Transform[] parts)
        {
            int part = (int)kind;
            _structureParts[part] = parts;
            _structurePositions[part] = new Vector3[parts.Length];
            _structureRotations[part] = new Quaternion[parts.Length];
            _structureScales[part] = new Vector3[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                _structurePositions[part][index] = parts[index].localPosition;
                _structureRotations[part][index] = parts[index].localRotation;
                _structureScales[part][index] = parts[index].localScale;
            }
        }

        /// <summary>kind의 원본 실물만 저장된 자세로 되돌린다. 설치 여부에 맞춰 표시하며 재질과 다른 부품 및 조립 진행은 유지한다.</summary>
        public void RestoreStructurePose(AssemblyStructurePart kind)
        {
            if (kind == AssemblyStructurePart.Pcb) { RestorePcbPose(); return; }
            if (kind == AssemblyStructurePart.Lower) _lower.SetParent(_lowerParent, true);
            int part = (int)kind;
            bool installed = kind == AssemblyStructurePart.Pcb ? _pcbInstalled
                : kind == AssemblyStructurePart.Plate ? _plateInstalled
                : kind == AssemblyStructurePart.Lower ? _lowerInstalled : _upperInstalled;
            for (int index = 0; index < _structureParts[part].Length; index++)
            {
                Transform target = _structureParts[part][index];
                target.SetLocalPositionAndRotation(_structurePositions[part][index], _structureRotations[part][index]);
                target.localScale = _structureScales[part][index];
                Rigidbody body = target.GetComponent<Rigidbody>();
                if (body != null && kind != AssemblyStructurePart.Pcb)
                {
                    body.isKinematic = true;
                    body.useGravity = false;
                }
                target.gameObject.SetActive(installed);
            }
        }

        /// <summary>축과 키캡의 점유 상태로 신규 축 설치 가능 여부를 반환한다. PCB 나사 6개 완료를 요구하며 보강판과 상단부는 제한하지 않는다.</summary>
        public bool CanInstallSwitch(bool occupied, bool hasKeycap)
        {
            return _initialized && PcbComplete && !occupied && !hasKeycap;
        }

        /// <summary>키캡 점유와 해당 축 설치 상태로 신규 키캡 설치 가능 여부를 반환한다. 보강판과 상단부는 제한하지 않는다.</summary>
        public bool CanInstallKeycap(bool occupied, bool hasSwitch)
        {
            return _initialized && !occupied && hasSwitch;
        }

        /// <summary>material을 상단부 렌더러에만 적용한다. 하단부와 보강판 및 설치 상태는 유지한다.</summary>
        public void SetUpperMaterial(Material material)
        {
            foreach (MeshRenderer renderer in _upperRenderers)
            {
                renderer.sharedMaterial = material;
                KeyboardToonSurface.ApplyBounds(renderer);
            }
        }

        /// <summary>현재 구조 부품의 상태를 연결된 실물에 반영한다. 입력 없이 Collider를 포함한 GameObject 활성 상태만 변경한다.</summary>
        private void ApplyStructureVisibility()
        {
            _pcb.gameObject.SetActive(_pcbInstalled);
            _plate.gameObject.SetActive(_plateInstalled);
            for (int index = 0; index < _screws.Length; index++)
                _screws[index].gameObject.SetActive(_fixedScrews[index]);
            foreach (MeshRenderer upper in _upperRenderers) upper.gameObject.SetActive(_upperInstalled);
        }
    }
}
