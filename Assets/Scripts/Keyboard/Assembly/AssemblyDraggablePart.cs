using System.Collections.Generic;

using UnityEngine;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyDraggablePart : MonoBehaviour
    {
        [Header("Physics")]
        [SerializeField] private Rigidbody _body;
        [SerializeField] private Collider _partCollider;

        [Header("Runtime State")]
        private AssemblyPartSlot _slot;
        private AssemblyPartSlot _originSlot;
        private AssemblyPartSlot _returnSlot;
        private GameObject _keycapShape;
        private bool _isSwitch;
        private bool _isAdaptiveKeycap;
        private GameObject _generatedShape;
        private readonly Dictionary<GameObject, GameObject> _shapeVisuals = new Dictionary<GameObject, GameObject>();
        private bool _restoreOnInvalidDrop;
        private bool _wasInstalled;
        private bool _wasKinematic;
        private Vector3 _returnPosition;
        private Vector3 _returnScale;
        private Quaternion _returnRotation;
        private Vector3 _returnVelocity;
        private Vector3 _returnAngularVelocity;
        private AssemblyCatalogEntry _catalogEntry;
        private int _interactionVersion;
        private readonly List<(Collider Collider, bool WasIgnored)> _keyboardCollisionStates
            = new List<(Collider Collider, bool WasIgnored)>();

        public bool IsSwitch => _isSwitch;
        public bool IsAdaptiveKeycap => _isAdaptiveKeycap;
        public GameObject KeycapShape => _keycapShape;
        public Collider PartCollider => _partCollider;
        public AssemblyPartSlot Slot => _slot;
        public AssemblyCatalogEntry CatalogEntry => _catalogEntry;
        public int InteractionVersion => _interactionVersion;

        /// <summary>
        /// entry의 명시적인 카탈로그 연결을 이 실물 부품에 저장한다.
        /// 반환 및 재출고 시 사용할 종류 식별 정보를 유지한다.
        /// </summary>
        public void SetCatalogEntry(AssemblyCatalogEntry entry)
        {
            _catalogEntry = entry;
        }

        /// <summary>
        /// entry의 출고 부품을 작업대의 독립 드래그 대상으로 초기화한다.
        /// preserveAppearance가 true이면 현재 메시와 배율을 유지하고, 아니면 출고 형상으로 초기화한다.
        /// 슬롯 연결 없이 물리를 고정하며 반환값은 없다.
        /// </summary>
        public void InitializeLoosePart(AssemblyCatalogEntry entry, bool preserveAppearance = false)
        {
            _catalogEntry = entry;
            _slot = null;
            _originSlot = null;
            _restoreOnInvalidDrop = false;
            if (_partCollider == null) _partCollider = GetComponent<Collider>();
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_body == null) _body = gameObject.AddComponent<Rigidbody>();
            _body.mass = 0.02f;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (preserveAppearance)
            {
                _isSwitch = entry.IsSwitch;
                _isAdaptiveKeycap = !entry.IsSwitch && !entry.IsArtisan;
            }
            else PrepareInventoryPart(entry, entry.Prefab.gameObject);
            SetBodyFrozen(true);
        }

        /// <summary>
        /// slot과 collider를 연결하고 잘못된 Drop의 복귀 정책을 설정한다.
        /// 필요한 Rigidbody를 준비하며 초기 상태는 중력 없이 고정한다.
        /// </summary>
        public void Initialize(AssemblyPartSlot slot, Collider partCollider, bool restoreOnInvalidDrop)
        {
            _slot = slot;
            _originSlot = slot;
            _isSwitch = slot.IsSwitch;
            _keycapShape = slot.KeycapShape;
            _isAdaptiveKeycap = false;
            _partCollider = partCollider;
            _restoreOnInvalidDrop = restoreOnInvalidDrop;
            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
                if (_body == null)
                    _body = gameObject.AddComponent<Rigidbody>();
            }
            _body.mass = 0.02f;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            SetBodyFrozen(true);
        }

        /// <summary>
        /// 현재 위치, 회전과 슬롯 점유 상태를 저장하고 드래그를 시작한다.
        /// 기존 점유와 부모 결합을 해제하고 월드 자세와 물리 정지 상태를 유지하며 장착 부품의 제거음을 재생한다.
        /// </summary>
        public void BeginDrag()
        {
            if (_slot != null && _slot.InstalledPart == this && !_slot.CanRemove())
                return;
            _interactionVersion++;
            _returnPosition = transform.position;
            _returnScale = transform.localScale;
            _returnRotation = transform.rotation;
            _returnSlot = _slot;
            _wasInstalled = _slot != null && _slot.InstalledPart == this;
            _wasKinematic = _body.isKinematic;
            _returnVelocity = _body.linearVelocity;
            _returnAngularVelocity = _body.angularVelocity;
            if (_slot != null)
                _slot.Release(this);
            SetBodyFrozen(true);
            RestoreKeyboardCollisions();
            transform.SetParent(null, true);
            if (_wasInstalled) AudioManager.PlayPartSound(_returnPosition, true);
        }

        /// <summary>
        /// install이 참이면 지정 슬롯에 장착을 시도하고 거짓이거나 점유로 거부되면 복귀 정책을 적용한다.
        /// 기존 키보드 키캡은 이전 상태로 복귀하고 출고 부품과 스위치는 중력으로 떨어진다.
        /// </summary>
        public void EndDrag(bool install)
        {
            if (install && _slot.Install(this, true))
                return;

            if (_restoreOnInvalidDrop)
                RestoreDragOrigin();
            else
                DropWithPhysics();
        }

        /// <summary>
        /// UI 표시 단계의 취소에 사용할 집기 전 위치와 회전 및 물리 상태를 복구한다.
        /// 저장한 점유 상태에 따라 슬롯에 재장착하거나 기존 운동 상태로 돌려놓는다.
        /// </summary>
        public void RestoreDragOrigin()
        {
            gameObject.SetActive(true);
            if (_wasInstalled)
            {
                if (!_returnSlot.Install(this))
                    DropWithPhysics();
            }
            else
            {
                transform.SetPositionAndRotation(_returnPosition, _returnRotation);
                transform.localScale = _returnScale;
                SetBodyFrozen(_wasKinematic);
                if (!_wasKinematic)
                {
                    _body.linearVelocity = _returnVelocity;
                    _body.angularVelocity = _returnAngularVelocity;
                }
            }
        }

        /// <summary>
        /// 실물 부품을 재고 보관 상태로 전환한다.
        /// 슬롯의 점유 및 원본 참조와 부모 결합을 해제하고 물리를 고정한 뒤 오브젝트를 숨긴다.
        /// </summary>
        public void StoreInInventory()
        {
            _interactionVersion++;
            if (_slot != null)
                _slot.ForgetPart(this);
            if (_originSlot != null && _originSlot != _slot)
                _originSlot.ForgetPart(this);
            _slot = null;
            _originSlot = null;
            SetBodyFrozen(true);
            RestoreKeyboardCollisions();
            transform.SetParent(null, true);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 연결된 슬롯의 자식으로 부품을 결합하고 슬롯 기준 자세로 정렬한다.
        /// 재질형 키캡에는 슬롯의 기존 형상을 적용하고 원본 규격의 크기와 고정 상태를 유지한다.
        /// </summary>
        public void SnapToSlot()
        {
            _interactionVersion++;
            SetBodyFrozen(true);
            if (_isAdaptiveKeycap)
                ApplyKeycapShape(_slot.KeycapShape, _catalogEntry.KeycapMaterial);
            transform.SetParent(_slot.transform, true);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            IgnoreKeyboardCollisions();
        }

        /// <summary>
        /// 현재 부품의 슬롯 점유를 해제하고 중력 낙하를 시작한다.
        /// 부모 결합을 해제하되 월드 자세와 크기는 유지하며 Rigidbody를 동적 상태로 전환한다.
        /// </summary>
        public void DropWithPhysics()
        {
            if (_slot != null)
                _slot.Release(this);
            RestoreKeyboardCollisions();
            transform.SetParent(null, true);
            SetBodyFrozen(false);
        }

        /// <summary>
        /// 장착된 부품의 선행 분리 조건을 검사하고 velocity와 angularVelocity로 튀어 오르게 한다.
        /// 성공하면 점유를 해제하고 제거음을 재생하며 미장착 상태 또는 선행 부품이 남아 있으면 false를 반환한다.
        /// </summary>
        public bool TryPopOff(Vector3 velocity, Vector3 angularVelocity)
        {
            if (_slot == null || _slot.InstalledPart != this || !_slot.CanRemove())
                return false;
            DropWithPhysics();
            _body.linearVelocity = velocity;
            _body.angularVelocity = angularVelocity;
            AudioManager.PlayPartSound(transform.position, true);
            return true;
        }

        /// <summary>
        /// slot으로 장착 대상을 변경하고 이전 슬롯의 점유 참조를 해제한다.
        /// 원본 식별과 복귀 기준은 별도로 유지한다.
        /// </summary>
        public void AttachSlot(AssemblyPartSlot slot)
        {
            if (_slot != null && _slot != slot)
                _slot.Release(this);
            _slot = slot;
        }

        /// <summary>
        /// entry와 previewShape로 출고 부품의 종류와 미리보기 형상을 설정한다.
        /// 보관했던 긴 키도 재질과 항목을 유지하며 기본 프리뷰 형상으로 돌아간다.
        /// </summary>
        public void PrepareInventoryPart(AssemblyCatalogEntry entry, GameObject previewShape)
        {
            _isSwitch = entry.IsSwitch;
            _isAdaptiveKeycap = !entry.IsSwitch && !entry.IsArtisan;
            _keycapShape = previewShape;
            RestoreKeyboardCollisions();
            transform.SetParent(null, true);
            transform.localScale = entry.Prefab.transform.localScale;
            if (_isAdaptiveKeycap)
                ApplyKeycapShape(previewShape, entry.KeycapMaterial);
        }

        /// <summary>
        /// source의 기존 렌더 메시와 상대 배치를 복사하고 shellMaterial을 외피에 적용한다.
        /// 형상별 표시 모델을 재사용하며 부품 수량과 식별 정보는 유지한다.
        /// </summary>
        private void ApplyKeycapShape(GameObject source, Material shellMaterial)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            if (_generatedShape != null)
                _generatedShape.SetActive(false);
            if (!_shapeVisuals.TryGetValue(source, out _generatedShape))
            {
                _generatedShape = new GameObject("InstalledKeycapShape");
                _generatedShape.transform.SetParent(transform, false);
                _shapeVisuals.Add(source, _generatedShape);
                foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    MeshRenderer original = filter.GetComponent<MeshRenderer>();
                    if (original == null || !original.enabled || !original.gameObject.activeSelf)
                        continue;
                    GameObject visual = new GameObject(filter.name);
                    visual.transform.SetParent(_generatedShape.transform, false);
                    visual.transform.localPosition = source.transform.InverseTransformPoint(filter.transform.position);
                    visual.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                    Vector3 scale = filter.transform.lossyScale;
                    Vector3 rootScale = source.transform.lossyScale;
                    visual.transform.localScale = new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z);
                    visual.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = filter.name == "PBT_SculptedShell" ? new[] { shellMaterial } : original.sharedMaterials;
                }
            }
            _generatedShape.SetActive(true);
            foreach (Renderer renderer in _generatedShape.GetComponentsInChildren<Renderer>())
                renderer.enabled = true;
            BoxCollider bounds = source.GetComponent<BoxCollider>();
            BoxCollider collider = (BoxCollider)_partCollider;
            collider.center = bounds.center;
            collider.size = bounds.size;
        }

        /// <summary>
        /// 장착 슬롯의 본체 Rigidbody에 속한 Collider와 이 부품 사이의 기존 충돌 설정을 보관한다.
        /// 부모 본체가 있을 때만 자기 충돌을 제외하며 다른 물체와의 충돌은 유지한다.
        /// </summary>
        private void IgnoreKeyboardCollisions()
        {
            RestoreKeyboardCollisions();
            Rigidbody keyboardBody = _slot.GetComponentInParent<Rigidbody>();
            if (keyboardBody == null)
                return;
            foreach (Collider collider in keyboardBody.GetComponentsInChildren<Collider>(true))
            {
                if (collider.attachedRigidbody != keyboardBody)
                    continue;
                _keyboardCollisionStates.Add((collider, Physics.GetIgnoreCollision(_partCollider, collider)));
                Physics.IgnoreCollision(_partCollider, collider, true);
            }
        }

        /// <summary>
        /// 본체 결합 시 저장한 Collider 쌍의 충돌 설정을 복원하고 기록을 비운다.
        /// 분리 부품은 본체와 다시 충돌하며 이미 파괴된 Collider는 건너뛴다.
        /// </summary>
        private void RestoreKeyboardCollisions()
        {
            foreach (var state in _keyboardCollisionStates)
                if (state.Collider != null)
                    Physics.IgnoreCollision(_partCollider, state.Collider, state.WasIgnored);
            _keyboardCollisionStates.Clear();
        }

        /// <summary>
        /// frozen 값으로 드래그 및 장착 중의 물리 고정 상태를 설정한다.
        /// 기존 속도를 초기화하고 중력과 kinematic 상태를 함께 갱신한다.
        /// </summary>
        private void SetBodyFrozen(bool frozen)
        {
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.isKinematic = frozen;
            _body.useGravity = !frozen;
            if (!frozen)
                _body.WakeUp();
        }
    }
}

