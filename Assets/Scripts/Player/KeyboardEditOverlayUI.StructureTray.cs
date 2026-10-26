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
        private GameObject _structurePreviewPrefab;
        private readonly Dictionary<AssemblyCatalogEntry, RawImage> _structurePreviews = new Dictionary<AssemblyCatalogEntry, RawImage>();
        private readonly Dictionary<AssemblyStructurePart, RectTransform> _structurePanels = new Dictionary<AssemblyStructurePart, RectTransform>();
        private readonly Dictionary<GridLayoutGroup, RectTransform> _structureGrids = new Dictionary<GridLayoutGroup, RectTransform>();
        private readonly List<Texture2D> _structurePreviewTextures = new List<Texture2D>();
        private readonly Dictionary<Material, AssemblyCatalogEntry> _plateInventory = new Dictionary<Material, AssemblyCatalogEntry>();
        private RectTransform _plateTrayTabs;

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

        /// <summary>기존 구조 부품 처리기에 상부·PCB·하부의 출고 카드를 연결한다. 재질 항목과 실물 추적만 연결하며 초기 수량과 기존 원본 에셋은 출고를 제한하지 않는다.</summary>
        private void ConnectStructureTrays()
        {
            ConnectStructureTray(AssemblyStructurePart.Upper, WorkshopDrawerCategory.UpperHousing, _upperCard);
            ConnectStructureTray(AssemblyStructurePart.Pcb, WorkshopDrawerCategory.Pcb, _pcbWorldDrag);
            ConnectStructureTray(AssemblyStructurePart.Lower, WorkshopDrawerCategory.LowerHousing, _lowerCard);
            _structureTraysReady = true;
            ConnectPlateTray();
            RefreshStructureTrayPreviews();
        }

        /// <summary>kind의 기존 drag를 category 트레이에 연결하고 현재 카탈로그의 재질별 항목을 한 번 생성한다. 스크롤·아이콘은 기존 카드 경로를 재사용하며 수량 표시는 만들지 않는다.</summary>
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
                AssemblyCatalogEntry entry = AssemblyCatalogEntry.CreateStructure(kind, material, 0);
                KeyboardEditButton button = CreatePickerItemButton(content, kind + "_" + (material != null ? material.name : "PCB"),
                    buttons.Count, material != null ? KeyboardPartDisplayNames.MaterialName(material) : "기판", out Image background, out Text indicator);
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
            if (kind == AssemblyStructurePart.Plate)
                return _keycapTrayTransition.IsCategoryOpen(WorkshopDrawerCategory.Plate)
                    && _plateViewport != null && ContainsPointer(_plateViewport, point);
            WorkshopDrawerCategory category = kind == AssemblyStructurePart.Upper ? WorkshopDrawerCategory.UpperHousing
                : kind == AssemblyStructurePart.Pcb ? WorkshopDrawerCategory.Pcb : WorkshopDrawerCategory.LowerHousing;
            return _keycapTrayTransition.IsCategoryOpen(category) && _structurePanels.TryGetValue(kind, out RectTransform panel)
                && ContainsPointer(panel, point);
        }

        /// <summary>현재 보강판 카드에 재질 항목과 기존 출고·반환 처리기를 연결한다. 수량 제한 없이 실물 보관 참조와 타건 효과 메뉴를 유지한다.</summary>
        private void ConnectPlateTray()
        {
            if (!_structureTraysReady || _plateViewport == null) return;
            _plateCard.ConnectStockTray(_keycapTrayTransition);
            for (int index = 0; index < _plateMaterialButtons.Count; index++)
            {
                KeyboardEditButton button = _plateMaterialButtons[index];
                if (button.GetComponent<WorkshopKeycapCardDrag>() != null) continue;
                Material material = _plateMaterialButtonMaterials[index];
                if (!_plateInventory.TryGetValue(material, out AssemblyCatalogEntry entry))
                {
                    entry = AssemblyCatalogEntry.CreateStructure(AssemblyStructurePart.Plate, material, 0);
                    _plateInventory.Add(material, entry);
                }
                _keycapPlacement.BindStructureCard(button, entry, button.GetComponentInChildren<RawImage>().texture,
                    _keycapTrayTransition, _plateCard);
            }
            ScrollRect scroll = _plateViewport.GetComponent<ScrollRect>();
            _catalogLayout.EmbedTrayCatalog(scroll, _keycapTrayContent);
            _plateViewport.GetComponent<Image>().color = Color.clear;
            _plateViewport.Find("PlatePanelBackground").GetComponent<Image>().color = Color.clear;
            foreach (Shadow decoration in _plateViewport.GetComponents<Shadow>()) decoration.enabled = false;
            _plateViewport.offsetMax = new Vector2(0f, -60f);
            scroll.viewport.offsetMax = new Vector2(-16f, -12f);
            Scrollbar bar = scroll.verticalScrollbar;
            bar.GetComponent<Image>().color = Color.clear;
            RectTransform track = (RectTransform)bar.transform;
            track.anchorMin = Vector2.right;
            track.anchorMax = Vector2.one;
            track.pivot = Vector2.right;
            track.offsetMin = new Vector2(-10f, 2f);
            track.offsetMax = new Vector2(0f, -12f);
            scroll.verticalNormalizedPosition = 1f;
            if (_plateTrayTabs == null)
            {
                RectTransform row = new GameObject("PlatePickerTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
                _plateTrayTabs = row;
                row.SetParent(_keycapTrayContent, false);
                row.anchorMin = Vector2.up;
                row.anchorMax = Vector2.one;
                row.pivot = Vector2.up;
                row.anchoredPosition = new Vector2(12f, -12f);
                row.sizeDelta = new Vector2(-36f, 36f);
                HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 8f;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = true;
                _plateMaterialTabButton.transform.SetParent(row, false);
                _plateEffectTabButton.transform.SetParent(row, false);
                row.gameObject.SetActive(_pickerMode == PickerMode.Plate);
            }
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
            ResetPlateTrayScroll();
        }

        /// <summary>현재 보강판 목록의 레이아웃을 갱신하고 첫 줄이 테두리까지 보이도록 상단에 정렬한다. 카드 여백과 스크롤 위치만 변경하며 재고와 선택 섹션은 유지한다.</summary>
        private void ResetPlateTrayScroll()
        {
            if (!UsesScreenTrays || _plateViewport == null) return;
            ScrollRect scroll = _plateViewport.GetComponent<ScrollRect>();
            GridLayoutGroup grid = scroll.content.GetComponent<GridLayoutGroup>();
            if (grid == null) return;
            grid.padding = new RectOffset(8, 8, 12, 12);
            Canvas.ForceUpdateCanvases();
            grid.cellSize = new Vector2(Mathf.Max(1f, (scroll.viewport.rect.width
                - grid.padding.horizontal - grid.spacing.x) * 0.5f), grid.cellSize.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f;
            scroll.content.anchoredPosition = Vector2.zero;
            SetPlatePickerTabSelection(_plateMaterialTabButton, _platePickerSection == PlatePickerSection.Material);
            SetPlatePickerTabSelection(_plateEffectTabButton, _platePickerSection == PlatePickerSection.Effect);
        }

        /// <summary>작업대 실물 또는 작업 UI의 조립 원본과 각 항목 재질로 썸네일을 갱신한다. 빈 작업대에서도 원본 메시를 읽으며 실물 생성과 재고·조립 변경 없이 같은 모델의 이미지를 재사용한다.</summary>
        private void RefreshStructureTrayPreviews()
        {
            if (!_structureTraysReady) return;
            KeyboardAssemblyState assembly = _plateCustomization != null ? _plateCustomization.GetComponent<KeyboardAssemblyState>() : null;
            GameObject sourcePrefab = assembly != null && assembly.TryGetComponent(out KeyboardPickupTarget pickup)
                ? pickup.SourcePrefab : _pullerController.LowerHousingPreviewPrefab;
            if (assembly == _structurePreviewSource && sourcePrefab == _structurePreviewPrefab && _structurePreviewTextures.Count > 0) return;
            if (sourcePrefab != null && sourcePrefab == _structurePreviewPrefab && _structurePreviewTextures.Count > 0)
            {
                _structurePreviewSource = assembly;
                return;
            }
            foreach (RawImage image in _structurePreviews.Values) { image.texture = null; image.enabled = false; }
            DestroyPreviewTextures(_structurePreviewTextures);
            _structurePreviewSource = assembly;
            _structurePreviewPrefab = sourcePrefab;
            KeyboardAssemblyState previewAssembly = assembly != null ? assembly
                : sourcePrefab != null ? sourcePrefab.GetComponent<KeyboardAssemblyState>() : null;
            if (previewAssembly == null) return;
            foreach (var preview in _structurePreviews)
            {
                Texture2D texture = AssemblyCatalogPreview.RenderParts(previewAssembly.GetStructurePreviewParts(preview.Key.StructureKind), preview.Key.StructureMaterial,
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
