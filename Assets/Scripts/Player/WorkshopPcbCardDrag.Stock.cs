using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed partial class WorkshopPcbCardDrag
    {
        private WorkshopKeycapDrawerTransition _stockTray;
        private bool _stockEnabled;
        private bool _stockDragging;
        private bool _stockReservation;
        private AssemblyCatalogEntry _stockEntry;
        private Material _stockPreviousMaterial;
        private IReadOnlyList<Transform> _stockParts;
        private AssemblyDraggablePart _stockKeyboard;
        private Transform _stockOriginParent;

        /// <summary>tray를 기존 구조 부품 드래그의 출고 입력으로 연결한다. 별도 드래그 처리기를 만들지 않고 기존 컨트롤러의 카메라·물리·복원 경로를 사용한다.</summary>
        public void ConnectStockTray(WorkshopKeycapDrawerTransition tray)
        {
            _stockTray = tray;
            _stockEnabled = true;
        }

        /// <summary>entry의 수량과 현재 서랍·부품 점유로 출고 가능 여부를 반환한다. 하부는 새 키보드 생성 경로를, 상부·PCB는 기존 설치 조건을 사용한다.</summary>
        public bool CanBeginStockDrag(AssemblyCatalogEntry entry)
        {
            if (!_stockEnabled || !_selected || !_stockTray.CanInteractWithTray || !_pullers.InputAllowed
                || _pullers.IsManipulatingPart || entry.AvailableQuantity <= 0 || entry.StructureKind != _kind) return false;
            if (_kind == AssemblyStructurePart.Lower) return _pullers.KeyboardDispenser.CanIssueLowerHousing
                && (entry.HasStoredPart || _pullers.KeyboardDispenser.CanDispense);
            return _assembly != null && _assembly.CanInstallStructure(_kind)
                && _assembly.GetStructureInventoryEntry(_kind) == null
                && (_kind != AssemblyStructurePart.Pcb || !_assembly.PcbLoose);
        }

        /// <summary>entry 한 개를 기존 재고에 예약하고 point에서 실물 출고를 시작한다. 상부·PCB는 미설치 실물을, 하부는 자판기 원본의 편집 가능한 새 키보드를 사용한다.</summary>
        public void BeginStockDrag(AssemblyCatalogEntry entry, Vector2 point)
        {
            if (!CanBeginStockDrag(entry) || Mouse.current == null || !Mouse.current.leftButton.isPressed || !entry.TryReserve()) return;
            _stockEntry = entry;
            _stockReservation = true;
            try
            {
                if (_kind == AssemblyStructurePart.Lower)
                {
                    _stockOriginParent = null;
                    if (!_pullers.KeyboardDispenser.CanIssueLowerHousing)
                    {
                        entry.FinishReservation(false);
                        _stockEntry = null;
                        _stockReservation = false;
                        return;
                    }
                    _stockKeyboard = entry.TakeStoredPart();
                    if (_stockKeyboard == null)
                    {
                        Transform origin = _assembly != null ? _assembly.transform : _pullers.transform;
                        if (!_pullers.KeyboardDispenser.TryCreateKeyboard(origin.position, origin.rotation, out GameObject root))
                        {
                            entry.FinishReservation(false);
                            _stockEntry = null;
                            _stockReservation = false;
                            return;
                        }
                        _stockKeyboard = root.GetComponent<AssemblyDraggablePart>();
                        if (_stockKeyboard == null) _stockKeyboard = root.AddComponent<AssemblyDraggablePart>();
                    }
                    _stockKeyboard.gameObject.SetActive(true);
                    _stockKeyboard.InitializeLoosePart(entry, true);
                    KeyboardCustomization customization = _stockKeyboard.GetComponent<KeyboardCustomization>();
                    if (customization.CaseMaterial != entry.StructureMaterial) customization.SetCaseMaterial(entry.StructureMaterial);
                    _stockParts = new[] { _stockKeyboard.transform };
                }
                else
                {
                    _stockPreviousMaterial = _assembly.GetStructureMaterial(_kind);
                    _assembly.SetStructureMaterial(_kind, entry.StructureMaterial);
                    _stockParts = _parts;
                }
                StartStockMotion(point);
            }
            catch
            {
                CancelStockDrag();
                throw;
            }
        }

        /// <summary>entry의 PCB 기판 재질을 현재 편집 실물에 적용한다. 입력 점유를 검사하며 설치 상태와 재고는 변경하지 않는다.</summary>
        public void SelectPcbMaterial(AssemblyCatalogEntry entry)
        {
            if (_kind != AssemblyStructurePart.Pcb || entry.StructureKind != _kind || !_selected
                || !_stockTray.CanInteractWithTray || !_pullers.InputAllowed || _pullers.IsManipulatingPart
                || _assembly == null || !_assembly.Initialized) return;
            _assembly.SetStructureMaterial(_kind, entry.StructureMaterial);
        }

        /// <summary>현재 출고 실물의 자세와 충돌 상태를 저장하고 point를 따르는 카메라 평행 평면 드래그를 시작한다. 슬롯 위 이동만으로 설치하지 않는다.</summary>
        private void StartStockMotion(Vector2 point)
        {
            _stockDragging = true;
            _dragging = true;
            _pullers.SetPcbDragging(true);
            Bounds bounds = default;
            bool found = false;
            foreach (Transform part in _stockParts)
            {
                part.gameObject.SetActive(true);
                foreach (MeshRenderer renderer in part.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!renderer.enabled) continue;
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }
            if (!found) throw new InvalidOperationException("출고 부품의 표시 메시가 없습니다.");
            _centerOffset = _stockParts[0].position - bounds.center;
            _partOffsets = new Vector3[_stockParts.Count];
            for (int index = 0; index < _stockParts.Count; index++) _partOffsets[index] = _stockParts[index].position - _stockParts[0].position;
            _dragPlane = new Plane(_pullers.InputCamera.transform.forward, bounds.center);
            _colliders.Clear();
            foreach (Transform part in _stockParts)
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                _colliders.Add((collider, collider.enabled));
                collider.enabled = false;
            }
            _preview.Show(_stockParts);
            MoveParts(point);
        }

        /// <summary>출고 또는 하부 재집기의 진행 여부를 반환한다. 기존 커서·드래그 평면과 설치 판정 및 낙하를 사용하고 취소 시 예약을 복구한다.</summary>
        private bool ProcessStockDrag()
        {
            if (!_stockDragging)
            {
                if (_kind == AssemblyStructurePart.Lower) TryPickIssuedKeyboard();
                return _stockDragging;
            }
            if (!_pullers.InputAllowed || Mouse.current == null || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelStockDrag();
                return true;
            }
            Vector2 point = InGameCursor.Position;
            MoveParts(point);
            if (Mouse.current.leftButton.isPressed) return true;
            if (!Mouse.current.leftButton.wasReleasedThisFrame) { CancelStockDrag(); return true; }
            bool overUI = _pullers.IsPointerOverUI(point);
            if (overUI)
            {
                if (_stockReservation) CancelStockDrag();
                else
                {
                    bool returnedToTray = _stockTray.IsCategoryOpen(WorkshopDrawerCategory.LowerHousing)
                        && _stockTray.TryGetReturnDestination(out _) && _overlay.IsInStructureTray(point, _kind);
                    if (returnedToTray)
                    {
                        AssemblyDraggablePart keyboard = _stockKeyboard;
                        AssemblyCatalogEntry entry = _stockEntry;
                        ReleaseStockKeyboardHolder(keyboard);
                        FinishStockMotion();
                        entry.StorePart(keyboard, true);
                    }
                    else CancelStockDrag();
                }
                return true;
            }
            Ray ray = _pullers.InputCamera.ScreenPointToRay(point);
            if (_kind == AssemblyStructurePart.Lower)
            {
                AssemblyDraggablePart keyboard = _stockKeyboard;
                ReleaseStockKeyboardHolder(keyboard);
                CommitStockReservation();
                FinishStockMotion();
                if (!_pullers.TryPlaceLowerHousing(keyboard.GetComponent<Rigidbody>(), ray))
                    keyboard.DropWithPhysics();
                return true;
            }
            bool installed = !IsObstructed(ray) && _assembly.TryPlaceStructure(_kind, ray);
            if (!installed && _kind == AssemblyStructurePart.Upper)
            {
                CancelStockDrag();
                return true;
            }
            CommitStockReservation();
            FinishStockMotion();
            if (!installed)
            {
                if (_kind == AssemblyStructurePart.Pcb)
                {
                    _assembly.MarkPcbLoose();
                    foreach (Transform part in _parts) part.SetParent(null, true);
                }
                DropWorldParts(ray);
            }
            return true;
        }

        /// <summary>출고 예약을 한 번 소비 확정한다. 상부·PCB는 소유 키보드에 원본 재질 항목을 기록하고 하부는 실제 키보드의 카탈로그 참조를 유지한다.</summary>
        private void CommitStockReservation()
        {
            if (!_stockReservation) return;
            if (_kind != AssemblyStructurePart.Lower) _assembly.SetStructureInventoryEntry(_kind, _stockEntry);
            _stockReservation = false;
            _stockEntry.FinishReservation(true);
        }

        /// <summary>출고 실물의 표시 레이어와 충돌 상태를 복구하고 공통 입력 점유를 해제한다. 수량 확정과 설치·보관은 호출자가 처리한다.</summary>
        private void FinishStockMotion()
        {
            _preview.Hide();
            foreach (var state in _colliders) state.Collider.enabled = state.Enabled;
            _colliders.Clear();
            _stockDragging = false;
            _dragging = false;
            _pullers.SetPcbDragging(false);
            _stockParts = null;
            _stockEntry = null;
            _stockKeyboard = null;
        }

        /// <summary>진행 중 출고를 취소하여 기존 예약·실물 보관을 복구한다. 이미 출고된 하부의 재집기 취소는 원래 월드 자세로 복구하며 수량은 늘리지 않는다.</summary>
        private void CancelStockDrag()
        {
            AssemblyCatalogEntry entry = _stockEntry;
            AssemblyDraggablePart keyboard = _stockKeyboard;
            bool reserved = _stockReservation;
            Transform originParent = _stockOriginParent;
            _stockReservation = false;
            FinishStockMotion();
            if (_kind == AssemblyStructurePart.Lower)
            {
                if (reserved) entry.StorePart(keyboard, false);
                else if (keyboard != null)
                {
                    keyboard.RestoreDragOrigin();
                    keyboard.transform.SetParent(originParent, true);
                }
            }
            else
            {
                _assembly.RestoreStructurePose(_kind);
                if (_stockPreviousMaterial != null) _assembly.SetStructureMaterial(_kind, _stockPreviousMaterial);
            }
            if (reserved) entry.FinishReservation(false);
        }

        /// <summary>열린 하부 서랍에서 작업대·바닥의 빈 하우징을 point 입력으로 집는다. 기존 실물과 재질 항목을 연결하고 조립된 본체는 거부하며 재고는 반환 확정까지 유지한다.</summary>
        private void TryPickIssuedKeyboard()
        {
            if (!_selected || !_stockTray.CanInteractWithTray || !_pullers.InputAllowed || _pullers.IsManipulatingPart || Mouse.current == null
                || !Mouse.current.leftButton.wasPressedThisFrame || _pullers.IsPointerOverUI(InGameCursor.Position)) return;
            Ray ray = _pullers.InputCamera.ScreenPointToRay(InGameCursor.Position);
            if (!Physics.Raycast(ray, out RaycastHit hit, _pullers.InputCamera.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            KeyboardAssemblyState assembly = hit.collider.GetComponentInParent<KeyboardAssemblyState>();
            if (assembly == null || !assembly.CanRemoveLower) return;
            if (assembly.GetComponentInParent<KeyboardWallRack>() != null) return;
            AssemblyDraggablePart keyboard = assembly.GetComponent<AssemblyDraggablePart>();
            AssemblyCatalogEntry entry = keyboard != null ? keyboard.CatalogEntry : null;
            if (entry == null || !entry.IsStructure || entry.StructureKind != AssemblyStructurePart.Lower
                || entry.StructureMaterial != assembly.GetStructureMaterial(AssemblyStructurePart.Lower))
                entry = _overlay.GetLowerHousingEntry(assembly.GetStructureMaterial(AssemblyStructurePart.Lower));
            if (entry == null) return;
            Rigidbody body = assembly.GetComponent<Rigidbody>();
            bool wasKinematic = body.isKinematic;
            bool usedGravity = body.useGravity;
            Vector3 velocity = body.linearVelocity;
            Vector3 angularVelocity = body.angularVelocity;
            _stockOriginParent = assembly.transform.parent;
            if (keyboard == null) keyboard = assembly.gameObject.AddComponent<AssemblyDraggablePart>();
            keyboard.InitializeLoosePart(entry, true);
            body.isKinematic = wasKinematic;
            body.useGravity = usedGravity;
            if (!wasKinematic) { body.linearVelocity = velocity; body.angularVelocity = angularVelocity; }
            _stockEntry = entry;
            _stockKeyboard = keyboard;
            _stockReservation = false;
            keyboard.BeginDrag();
            _stockParts = new[] { keyboard.transform };
            StartStockMotion(InGameCursor.Position);
        }

        /// <summary>keyboard가 집기 전에 속했던 홀더의 점유를 해제한다. 반환·바닥 드롭 확정 때만 호출하며 취소 시 원래 홀더 연결은 유지한다.</summary>
        private void ReleaseStockKeyboardHolder(AssemblyDraggablePart keyboard)
        {
            if (_stockOriginParent != null)
                _stockOriginParent.GetComponent<IKeyboardHolder>()?.ReleaseKeyboard(keyboard.GetComponent<Rigidbody>());
            _stockOriginParent = null;
        }
    }
}
