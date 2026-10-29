using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyPartSlot : MonoBehaviour
    {
        [Header("Key And Compatibility")]
        [SerializeField] private Key _key;
        [SerializeField] private bool _isSwitch;
        [SerializeField, Tooltip("키캡 규격과 방향의 원본 형상 Prefab입니다. 장착 회전은 슬롯 Transform을 사용합니다.")] private GameObject _keycapShape;
        [SerializeField, Tooltip("원본 실물의 재질과 데이터로 확인한 인벤토리 항목의 전용 Prefab입니다.")] private AssemblyDraggablePart _inventoryPrefab;

        [Header("Part")]
        [SerializeField] private Transform _part;
        [SerializeField] private Collider _partCollider;

        [Header("Workbench Start Pose")]
        [SerializeField] private Transform _startPose;

        [Header("Installation")]
        [SerializeField, Min(0.001f)] private float _snapRadius = 0.13f;
        [SerializeField] private bool _startsInstalled;
        [SerializeField] private bool _restoreOnInvalidDrop = true;
        [SerializeField, Tooltip("이 슬롯의 부품을 분리하기 전에 비워야 하는 슬롯 목록입니다.")] private AssemblyPartSlot[] _removalBlockers = new AssemblyPartSlot[0];
        private AssemblyPartSlot _installationPrerequisite;
        private AssemblyDraggablePart _originalPart;
        private AssemblyDraggablePart _installedPart;
        private bool _isDragging;

        public Key Key => _key;
        public bool IsSwitch => _isSwitch;
        public GameObject KeycapShape => _keycapShape;
        public AssemblyDraggablePart InventoryPrefab => _inventoryPrefab;
        public Transform Part => _part;
        public Collider PartCollider => _partCollider;
        public bool IsInstalled => _installedPart != null;
        public AssemblyDraggablePart OriginalPart => _originalPart;
        public AssemblyDraggablePart InstalledPart => _installedPart;

        /// <summary>
        /// removalBlockers의 현재 점유를 검사하여 부품 분리 가능 여부를 반환한다.
        /// 선행 슬롯 중 하나라도 장착 상태이면 false를 반환하며 점유 상태는 변경하지 않는다.
        /// </summary>
        public bool CanRemove()
        {
            foreach (AssemblyPartSlot blocker in _removalBlockers)
                if (blocker.IsInstalled)
                    return false;
            return true;
        }

        void Awake()
        {
            _originalPart = _part.gameObject.AddComponent<AssemblyDraggablePart>();
            _originalPart.Initialize(this, _partCollider, _restoreOnInvalidDrop);
            if (_startsInstalled)
            {
                _installedPart = _originalPart;
                _originalPart.SnapToSlot();
            }
            else
            {
                _part.SetPositionAndRotation(_startPose.position, _startPose.rotation);
                _part.localScale = transform.localScale;
                _part.SetParent(null, true);
            }
        }

        /// <summary>
        /// 현재 위치, 회전과 장착 상태를 복귀 기준으로 보관하고 부품을 슬롯에서 분리한다.
        /// 별도 입력 없이 드래그 상태와 미장착 상태를 설정한다.
        /// </summary>
        public void BeginDrag()
        {
            if (!CanRemove())
                return;
            _originalPart.BeginDrag();
            _isDragging = true;
        }

        /// <summary>
        /// 현재 부품과 슬롯의 월드 XZ 거리를 사용하여 장착 범위를 검사한다.
        /// 높이 차이를 제외하고 snapRadius 이내인지 반환한다.
        /// </summary>
        public bool IsInsideSlot()
        {
            return IsInsideSlot(_part.position);
        }

        /// <summary>
        /// worldPosition과 슬롯의 월드 XZ 거리로 장착 가능 여부를 반환한다.
        /// snapRadius를 사용하며 들어 올린 Y 높이는 판정에서 제외한다.
        /// </summary>
        public bool IsInsideSlot(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= _snapRadius * _snapRadius;
        }

        /// <summary>
        /// ray를 슬롯 로컬 공간으로 변환해 실제 규격 모델의 장착 영역과 교차하는지 검사한다.
        /// 임의의 주변 반경 대신 모델 Collider를 사용하며 교차점의 월드 레이 거리를 반환한다.
        /// </summary>
        public bool TryGetPointerHit(Ray ray, out float distance)
        {
            BoxCollider shape = _isSwitch ? _inventoryPrefab.PartCollider as BoxCollider
                : _keycapShape != null ? _keycapShape.GetComponent<BoxCollider>() : null;
            distance = 0f;
            if (shape == null)
                return false;
            Ray localRay = new Ray(transform.InverseTransformPoint(ray.origin), transform.InverseTransformVector(ray.direction));
            if (!new Bounds(shape.center, shape.size).IntersectRay(localRay, out float localDistance))
                return false;
            Vector3 point = transform.TransformPoint(localRay.GetPoint(localDistance));
            distance = Vector3.Dot(point - ray.origin, ray.direction);
            return distance >= 0f;
        }

        /// <summary>
        /// install 값에 따라 슬롯에 장착하거나 집기 전의 유효한 위치로 복귀한다.
        /// 드래그를 종료하고 기존 부품의 위치, 회전과 장착 상태를 갱신한다.
        /// </summary>
        public void EndDrag(bool install)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            _originalPart.EndDrag(install);
        }

        /// <summary>
        /// part의 규격과 선행 장착 및 점유 조건을 검사하고 거부되면 상태 변경 없이 false를 반환한다.
        /// 성공 시 점유와 자세를 갱신하고 true를 반환하며 playSound가 참이면 변경된 장착과 교체의 소리를 재생한다.
        /// </summary>
        public bool Install(AssemblyDraggablePart part, bool playSound = false)
        {
            if (!CanInstall(part))
                return false;

            bool changed = _installedPart != part;
            if (_installedPart != null && changed)
            {
                if (playSound) AudioManager.PlayPartSound(_installedPart.transform.position, true);
                _installedPart.DropWithPhysics();
            }
            part.AttachSlot(this);
            _installedPart = part;
            part.SnapToSlot();
            if (playSound && changed) AudioManager.PlayPartSound(transform.position, false);
            return true;
        }

        /// <summary>
        /// prerequisite를 이 키캡 슬롯의 선행 스위치 슬롯으로 연결한다.
        /// 기존 키 입력 대응으로 연결하며 슬롯 점유와 배치는 변경하지 않는다.
        /// </summary>
        public void SetInstallationPrerequisite(AssemblyPartSlot prerequisite)
        {
            _installationPrerequisite = prerequisite;
        }

        /// <summary>
        /// part의 종류와 규격, 선행 슬롯 점유 및 기존 점유로 장착 가능 여부를 반환한다.
        /// 키캡은 스위치가 필요하고 스위치는 키캡이 분리돼 있어야 하며 검사 중 상태는 변경하지 않는다.
        /// </summary>
        public bool CanInstall(AssemblyDraggablePart part)
        {
            if (!IsCompatible(part))
                return false;
            if (_isSwitch)
                return CanRemove();
            return _installationPrerequisite != null && _installationPrerequisite.IsInstalled
                && (_installedPart == null || _installedPart == part);
        }

        /// <summary>
        /// part를 이 슬롯에 장착할 수 없는 고정 사유 코드를 반환한다.
        /// 현재 호환성, 선행 파츠와 슬롯 점유를 검사하며 장착 상태는 변경하지 않는다.
        /// </summary>
        public string GetInstallFailureReason(AssemblyDraggablePart part)
        {
            if (part == null) return "InvalidPart";
            if (!IsCompatible(part)) return "IncompatiblePart";
            if (_isSwitch && !CanRemove()) return "SlotBlocked";
            if (!_isSwitch && (_installationPrerequisite == null || !_installationPrerequisite.IsInstalled))
                return "PrerequisiteMissing";
            if (!_isSwitch && _installedPart != null && _installedPart != part) return "SlotOccupied";
            return "InstallationRejected";
        }

        /// <summary>
        /// part의 종류와 고정 형상 또는 재질형 키캡 여부로 이 슬롯의 호환성을 반환한다.
        /// 스위치는 스위치 슬롯만 허용하고 고정 키캡은 같은 형상 에셋만 허용한다.
        /// </summary>
        public bool IsCompatible(AssemblyDraggablePart part)
        {
            return _isSwitch == part.IsSwitch
                && (_isSwitch || part.IsAdaptiveKeycap || part.KeycapShape == _keycapShape);
        }

        /// <summary>
        /// part가 현재 점유 부품일 때만 슬롯을 비운다.
        /// 다른 부품의 점유 상태나 Transform은 변경하지 않는다.
        /// </summary>
        public void Release(AssemblyDraggablePart part)
        {
            if (_installedPart == part)
                _installedPart = null;
        }

        /// <summary>
        /// 인벤토리에 보관하는 part의 점유와 원본 실물 참조를 제거한다.
        /// 슬롯 Transform은 유지하고 숨겨진 원본을 다시 집거나 장착하지 않도록 한다.
        /// </summary>
        public void ForgetPart(AssemblyDraggablePart part)
        {
            Release(part);
            if (_originalPart == part)
            {
                _originalPart = null;
                _part = null;
                _partCollider = null;
                _isDragging = false;
            }
        }
    }
}
