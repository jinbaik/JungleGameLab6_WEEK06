using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed partial class KeyboardEditOverlayUI
    {
        private bool _structureTraysReady;
        private KeyboardAssemblyState _structurePreviewSource;
        private readonly Dictionary<AssemblyCatalogEntry, RawImage> _structurePreviews = new Dictionary<AssemblyCatalogEntry, RawImage>();
        private readonly Dictionary<AssemblyStructurePart, RectTransform> _structurePanels = new Dictionary<AssemblyStructurePart, RectTransform>();
        private readonly Dictionary<GridLayoutGroup, RectTransform> _structureGrids = new Dictionary<GridLayoutGroup, RectTransform>();
        private readonly List<Texture2D> _structurePreviewTextures = new List<Texture2D>();

        /// <summary>material과 일치하는 하부 하우징의 기존 카탈로그 항목을 반환한다. 실물의 반환 대상만 검색하며 재고는 변경하지 않는다.</summary>
        public AssemblyCatalogEntry GetLowerHousingEntry(Material material)
        {
            foreach (AssemblyCatalogEntry entry in _structurePreviews.Keys)
                if (entry.StructureKind == AssemblyStructurePart.Lower && entry.StructureMaterial == material) return entry;
            return null;
        }

        /// <summary>kind과 material에 해당하는 기존 구조 재고 항목을 반환한다. 표시명은 비교하지 않으며 등록된 카탈로그가 없으면 null을 반환한다.</summary>
        public AssemblyCatalogEntry GetStructureEntry(AssemblyStructurePart kind, Material material)
        {
            foreach (AssemblyCatalogEntry entry in _structurePreviews.Keys)
                if (entry.StructureKind == kind && entry.StructureMaterial == material) return entry;
            return null;
        }

        /// <summary>기존 구조 부품 처리기에 상부·PCB·하부의 출고 카드를 연결한다. 새 재질 항목만 100개로 생성하며 키캡·스위치 수량과 기존 원본 에셋은 유지한다.</summary>
        private void ConnectStructureTrays()
        {
            ConnectStructureTray(AssemblyStructurePart.Upper, WorkshopDrawerCategory.UpperHousing, _upperCard);
            ConnectStructureTray(AssemblyStructurePart.Pcb, WorkshopDrawerCategory.Pcb, _pcbWorldDrag);
            ConnectStructureTray(AssemblyStructurePart.Lower, WorkshopDrawerCategory.LowerHousing, _lowerCard);
            _structureTraysReady = true;
            ConnectPlateTray();
            RefreshStructureTrayPreviews();
        }

        /// <summary>kind의 기존 drag를 category 트레이에 연결하고 현재 카탈로그의 재질별 항목을 한 번 생성한다. 스크롤·아이콘·수량은 기존 카드 경로를 재사용한다.</summary>
        private void ConnectStructureTray(AssemblyStructurePart kind, WorkshopDrawerCategory category, WorkshopPcbCardDrag drag)
        {
            drag.transform.SetParent(_keycapTrayContent, false);
            drag.ConnectStockTray(_keycapTrayTransition);
            RectTransform panel = CreateScrollViewport(category.ToString(), 0, out RectTransform content);
            panel.SetParent(_keycapTrayContent, false);
            _structurePanels.Add(kind, panel);
            var buttons = new List<KeyboardEditButton>();
            var materials = new List<Material>();
            if (kind == AssemblyStructurePart.Pcb)
            {
                foreach (Material material in _pcbMaterials)
                    if (material != null && !materials.Contains(material)) materials.Add(material);
                if (materials.Count == 0) materials.Add(null);
            }
            else foreach (Material material in _keycapMaterials)
                if (material != null && !materials.Contains(material)) materials.Add(material);
            foreach (Material material in materials)
            {
                AssemblyCatalogEntry entry = AssemblyCatalogEntry.CreateStructure(kind, material, 100);
                KeyboardEditButton button = CreatePickerItemButton(content, kind + "_" + (material != null ? material.name : "PCB"),
                    buttons.Count, material != null ? material.name : "PCB", out Image background, out Text indicator);
                indicator.gameObject.SetActive(false);
                RawImage preview = new GameObject("StructurePreview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                preview.transform.SetParent(button.transform, false);
                preview.texture = Texture2D.whiteTexture;
                preview.raycastTarget = false;
                _structurePreviews.Add(entry, preview);
                _keycapPlacement.BindStructureCard(button, entry, preview.texture, _keycapTrayTransition, drag);
                buttons.Add(button);
            }
            ConnectTrayCatalog(panel, content, buttons, category, out GridLayoutGroup grid, out RectTransform clip);
            panel.offsetMin = new Vector2(0f, 100f);
            clip.offsetMax = new Vector2(-16f, -12f);
            _structureGrids.Add(grid, clip);
        }

        /// <summary>point가 kind의 현재 열린 트레이 안인지 반환한다. 숨긴 다른 카테고리와 이전 카탈로그는 반환 대상으로 취급하지 않는다.</summary>
        public bool IsInStructureTray(Vector2 point, AssemblyStructurePart kind)
        {
            WorkshopDrawerCategory category = kind == AssemblyStructurePart.Upper ? WorkshopDrawerCategory.UpperHousing
                : kind == AssemblyStructurePart.Pcb ? WorkshopDrawerCategory.Pcb : WorkshopDrawerCategory.LowerHousing;
            return _keycapTrayTransition.IsCategoryOpen(category) && _structurePanels.TryGetValue(kind, out RectTransform panel)
                && ContainsPointer(panel, point);
        }

        /// <summary>현재 보강판 목록과 기존 설치·분리 처리기를 새 트레이에 연결한다. 원래 조립 조건과 재질·타건 효과 이벤트는 유지한다.</summary>
        private void ConnectPlateTray()
        {
            if (!_structureTraysReady || _plateViewport == null) return;
            ScrollRect scroll = _plateViewport.GetComponent<ScrollRect>();
            _catalogLayout.EmbedTrayCatalog(scroll, _keycapTrayContent);
            if (_assemblyCatalog)
            {
                _plateCard.transform.SetParent(_keycapTrayContent, false);
                _assemblyActions.SetParent(_keycapTrayContent, false);
                _assemblyActions.anchorMin = Vector2.zero;
                _assemblyActions.anchorMax = Vector2.right;
                _assemblyActions.pivot = Vector2.zero;
                _assemblyActions.offsetMin = new Vector2(12f, 12f);
                _assemblyActions.offsetMax = new Vector2(-12f, 94f);
            }
            _keycapTrayTransition.ConnectCatalogScroll(scroll, _keycapPlacement, WorkshopDrawerCategory.Plate);
        }

        /// <summary>현재 작업대의 실제 구조 메시와 각 항목의 원본 재질로 독립 썸네일을 갱신한다. 원본 부품 재질·재고는 변경하지 않으며 같은 모델의 이미지는 재사용한다.</summary>
        private void RefreshStructureTrayPreviews()
        {
            if (!_structureTraysReady) return;
            KeyboardAssemblyState assembly = _plateCustomization != null ? _plateCustomization.GetComponent<KeyboardAssemblyState>() : null;
            if (assembly == _structurePreviewSource && _structurePreviewTextures.Count > 0) return;
            foreach (RawImage image in _structurePreviews.Values) { image.texture = null; image.enabled = false; }
            DestroyPreviewTextures(_structurePreviewTextures);
            _structurePreviewSource = assembly;
            if (assembly == null || !assembly.Initialized) return;
            foreach (var preview in _structurePreviews)
            {
                Texture2D texture = AssemblyCatalogPreview.RenderParts(assembly.GetStructureParts(preview.Key.StructureKind), preview.Key.StructureMaterial,
                    preview.Key.StructureKind == AssemblyStructurePart.Pcb, true);
                if (texture == null) continue;
                _structurePreviewTextures.Add(texture);
                preview.Value.texture = texture;
                preview.Value.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
                preview.Value.enabled = true;
                _keycapPlacement.SetInventoryTexture(preview.Key, texture);
            }
        }

        /// <summary>트레이 폭에 맞춰 구조 부품 카드 너비만 갱신한다. 항목별 수량·재질과 스크롤 상태는 변경하지 않는다.</summary>
        private void UpdateStructureTrayWidths()
        {
            foreach (var grid in _structureGrids)
                grid.Key.cellSize = new Vector2(Mathf.Max(1f, (grid.Value.rect.width - 16f) * 0.5f), 150f);
        }
    }
}
