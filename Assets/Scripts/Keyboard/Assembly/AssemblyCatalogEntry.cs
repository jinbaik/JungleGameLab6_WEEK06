using System;
using System.Collections.Generic;

using UnityEngine;

using Game.Tournament;

namespace KeyboardModeling.Assembly
{
    [Serializable]
    public sealed class AssemblyCatalogEntry
    {
        [Header("UI Source")]
        [SerializeField] private Material _keycapMaterial;
        [SerializeField] private KeycapDefinition _keycapDefinition;
        [SerializeField] private SwitchData _switchData;

        [Header("Spawn Prefab")]
        [SerializeField] private AssemblyDraggablePart _prefab;

        [Header("Inventory")]
        [SerializeField, Min(0)] private int _initialQuantity = 1;
        private bool _isStructure;
        private AssemblyStructurePart _structureKind;
        private int _quantity;
        private int _reservedQuantity;
        private readonly Stack<AssemblyDraggablePart> _storedParts = new Stack<AssemblyDraggablePart>();

        public bool IsStructure => _isStructure;
        public AssemblyStructurePart StructureKind => _structureKind;
        public Material StructureMaterial => _keycapMaterial;
        public AssemblyDraggablePart Prefab => _prefab;
        public Material KeycapMaterial => _keycapMaterial;
        public KeycapDefinition KeycapDefinition => _keycapDefinition;
        public SwitchData SwitchData => _switchData;
        public bool IsArtisan => _keycapDefinition != null && _keycapDefinition.IsArtisan;
        public bool IsSwitch => _switchData != null;
        public int AvailableQuantity => _quantity - _reservedQuantity;
        public bool HasStoredPart => _storedParts.Count > 0;

        /// <summary>kind와 material의 새 구조 부품 항목을 quantity로 초기화해 반환한다. 기존 항목의 수량·예약·보관은 건드리지 않는다.</summary>
        public static AssemblyCatalogEntry CreateStructure(AssemblyStructurePart kind, Material material, int quantity)
        {
            var entry = new AssemblyCatalogEntry { _isStructure = true, _structureKind = kind,
                _keycapMaterial = material, _initialQuantity = quantity };
            entry.InitializeInventory();
            return entry;
        }

        /// <summary>
        /// Inspector의 initialQuantity로 이번 실행의 재고와 예약을 초기화한다.
        /// 초기 설정은 변경하지 않고 보관 인스턴스 목록을 비운다.
        /// </summary>
        public void InitializeInventory()
        {
            _quantity = Mathf.Max(0, _initialQuantity);
            _reservedQuantity = 0;
            _storedParts.Clear();
        }

        /// <summary>
        /// 사용 가능한 재고가 있으면 한 개를 드래그용으로 예약하고 성공 여부를 반환한다.
        /// 확정 재고는 유지하고 예약 수량만 증가시킨다.
        /// </summary>
        public bool TryReserve()
        {
            if (AvailableQuantity <= 0)
                return false;
            _reservedQuantity++;
            return true;
        }

        /// <summary>
        /// consumed에 따라 한 개의 예약을 사용 확정하거나 취소한다.
        /// 예약을 해제하고 사용 확정 시에만 보유 수량을 감소시킨다.
        /// </summary>
        public void FinishReservation(bool consumed)
        {
            _reservedQuantity--;
            if (consumed)
                _quantity--;
        }

        /// <summary>
        /// 보관 중인 실제 부품을 한 개 꺼내 반환한다.
        /// 보관 인스턴스가 없으면 null을 반환하며 재고 수량은 변경하지 않는다.
        /// </summary>
        public AssemblyDraggablePart TakeStoredPart()
        {
            while (_storedParts.Count > 0)
            {
                AssemblyDraggablePart part = _storedParts.Pop();
                if (part != null)
                    return part;
            }
            return null;
        }

        /// <summary>
        /// part를 비활성 보관 상태로 넣고 addQuantity가 참이면 재고를 한 개 늘린다.
        /// 예약 취소에서는 보관 인스턴스만 회수하고 수량을 중복 증가시키지 않는다.
        /// </summary>
        public void StorePart(AssemblyDraggablePart part, bool addQuantity)
        {
            if (part != null)
            {
                part.StoreInInventory();
                _storedParts.Push(part);
            }
            if (addQuantity)
                _quantity++;
        }

        /// <summary>
        /// material과 definition의 에셋 참조가 이 키캡 항목과 일치하는지 반환한다.
        /// 표시 이름은 사용하지 않으며 스위치 항목은 제외한다.
        /// </summary>
        public bool MatchesKeycap(Material material, KeycapDefinition definition)
        {
            return _switchData == null && _keycapMaterial == material && _keycapDefinition == definition;
        }

        /// <summary>
        /// data의 에셋 참조가 이 스위치 항목과 일치하는지 반환한다.
        /// 표시 이름이나 문자열 ID를 사용하지 않는다.
        /// </summary>
        public bool MatchesSwitch(SwitchData data)
        {
            return _switchData == data;
        }
    }
}
