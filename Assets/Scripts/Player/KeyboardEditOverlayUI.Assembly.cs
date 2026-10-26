using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed partial class KeyboardEditOverlayUI
    {
        [Header("Assembly Scene Catalog")]
        [SerializeField] private bool _assemblyCatalog;
        [SerializeField] private Material _pcbOutlineMaterial;
        [SerializeField] private Shader _pcbDepthShader;
        [Header("Screw Position Markers")]
        [SerializeField] private Shader _screwMarkerShader;
        [SerializeField, Min(0.01f)] private float _screwMarkerScale = 1.6f;
        [SerializeField, Min(1f), Tooltip("표시 나사의 축 방향 두께 배율입니다. 실제 나사는 변경하지 않습니다.")] private float _screwMarkerThickness = 4f;
        [SerializeField, Min(0f)] private float _screwHitPadding = 0.08f;
        [SerializeField] private Color _screwLooseColor = new Color(1f, 0.65f, 0.2f, 0.8f);
        [SerializeField] private Color _screwFixedColor = new Color(0.3f, 1f, 0.45f, 0.8f);
        private KeyboardEditButton _pcbButton;
        private KeyboardEditButton _upperButton;
        private RectTransform _assemblyActions;
        private WorkshopPcbCardDrag _plateCard;
        private WorkshopPcbCardDrag _upperCard;
        private WorkshopPcbCardDrag _lowerCard;
        private Text _assemblyStatus;
        private RectTransform _pcbViewport;
        private WorkshopPcbCardDrag _pcbCard;
        private WorkshopPcbCardDrag _pcbWorldDrag;
        private WorkshopPcbScrewAssembly _screwAssembly;
        private KeyboardAssemblyState ActiveAssembly => _caseCustomization != null
            ? _caseCustomization.GetComponent<KeyboardAssemblyState>() : null;
        public bool AssemblyCatalogEnabled => _assemblyCatalog;

        /// <summary>기존 종류별 처리기에서 집은 part와 category 및 복합 실물 parts를 반환한다. 도구·전체 키보드·목록 아이콘은 조회하지 않으며 병렬 실물 목록을 만들지 않는다.</summary>
        public bool TryGetHeldReturnPart(out Transform part, out WorkshopDrawerCategory category, out IReadOnlyList<Transform> parts)
        {
            parts = null;
            if (_keycapPlacement.TryGetHeldReturnPart(out part, out category)) return true;
            if ((_lowerCard != null && _lowerCard.TryGetHeldReturnParts(out parts, out category))
                || (_pcbWorldDrag != null && _pcbWorldDrag.TryGetHeldReturnParts(out parts, out category))
                || (_plateCard != null && _plateCard.TryGetHeldReturnParts(out parts, out category))
                || (_upperCard != null && _upperCard.TryGetHeldReturnParts(out parts, out category)))
            {
                part = parts[0];
                return true;
            }
            return false;
        }

        /// <summary>point가 현재 열린 부품 목록 영역인지 반환한다. 탭과 무관하게 목록 패널만 검사하며 상태를 변경하지 않는다.</summary>
        public bool IsInAssemblyReturnArea(Vector2 point)
        {
            RectTransform[] areas = { _viewport, _switchSoundViewport, _caseMaterialViewport,
                _plateViewport, _pcbViewport };
            foreach (RectTransform area in areas)
                if (area != null && area.gameObject.activeInHierarchy
                    && (_keycapTrayContent == null || (area != _viewport && area != _switchSoundViewport) || _keycapTrayTransition.IsTrayVisible)
                    && RectTransformUtility.RectangleContainsScreenPoint(area, point, null)) return true;
            return false;
        }

        /// <summary>assembly의 장착 축·키캡을 모두 재고에 반환한다. 연결이 누락되면 묶음을 보존하고 실패를 반환한다.</summary>
        public bool TryReturnPcbBundle(KeyboardAssemblyState assembly)
        {
            return _keycapPlacement != null && _keycapPlacement.TryReturnPcbParts(
                assembly.GetComponent<KeyboardCustomization>());
        }

        /// <summary>
        /// 기존 부품 버튼을 재사용해 조립 씬의 순서와 설치 조작을 구성한다.
        /// Inspector의 조립 카탈로그 설정이 켜진 UI에만 버튼과 C# 이벤트를 추가한다.
        /// </summary>
        private void BuildAssemblyControls()
        {
            _screwAssembly = gameObject.AddComponent<WorkshopPcbScrewAssembly>();
            _screwAssembly.Initialize(_pullerController, _pcbOutlineMaterial, _pcbDepthShader);
            _screwAssembly.ConfigureMarkers(_screwMarkerShader, _screwMarkerScale, _screwHitPadding,
                _screwLooseColor, _screwFixedColor, _screwMarkerThickness);
            _pcbButton = Instantiate(_caseButton, _caseButton.transform.parent);
            _pcbButton.name = "PcbButton";
            _upperButton = Instantiate(_caseButton, _caseButton.transform.parent);
            _upperButton.name = "UpperButton";
            _pcbButton.Clicked += HandleCategoryClicked;
            _upperButton.Clicked += HandleCategoryClicked;
            KeyboardEditButton[] buttons = { _caseButton, _pcbButton, _plateButton, _switchAxisButton,
                _upperButton, _keycapButton, _ledButton, _customButton };
            string[] labels = { "하단부", "PCB", "보강판", "스위치", "상단부", "키캡", "LED", "이미지" };
            for (int index = 0; index < buttons.Length; index++)
            {
                RectTransform rect = (RectTransform)buttons[index].transform;
                rect.anchoredPosition = new Vector2(12f, -12f - index * 44f);
                rect.SetSiblingIndex(index);
                buttons[index].GetComponentInChildren<Text>(true).text = labels[index];
            }
            RectTransform sidebar = (RectTransform)_caseButton.transform.parent;
            sidebar.sizeDelta = new Vector2(sidebar.sizeDelta.x, Mathf.Max(sidebar.sizeDelta.y, 372f));
            RectTransform background = (RectTransform)sidebar.Find("SidebarBackground");
            background.SetAsFirstSibling();
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = background.offsetMax = Vector2.zero;

            _assemblyActions = new GameObject("AssemblyActions", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            _assemblyActions.SetParent(transform, false);
            _assemblyActions.anchorMin = _assemblyActions.anchorMax = _assemblyActions.pivot = Vector2.up;
            _assemblyActions.anchoredPosition = new Vector2(568f, -80f);
            _assemblyActions.sizeDelta = new Vector2(250f, 368f);
            _assemblyActions.GetComponent<Image>().color = _pickerPanelColor;
            _assemblyActions.GetComponent<Image>().raycastTarget = false;
            _assemblyStatus = CreateAssemblyLabel(_assemblyActions, "", 12f, 74f);
            _assemblyStatus.name = "AssemblyStatus";
            _assemblyStatus.rectTransform.anchorMin = Vector2.zero;
            _assemblyStatus.rectTransform.anchorMax = Vector2.one;
            _assemblyStatus.rectTransform.offsetMin = new Vector2(14f, 8f);
            _assemblyStatus.rectTransform.offsetMax = new Vector2(-14f, -8f);
            _assemblyStatus.color = new Color32(62, 39, 22, 255);
            _assemblyStatus.fontSize = 18;
            _assemblyStatus.fontStyle = FontStyle.Bold;
            _assemblyStatus.alignment = TextAnchor.MiddleLeft;
            _assemblyStatus.lineSpacing = 1.1f;
            _assemblyStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
            _assemblyStatus.resizeTextForBestFit = true;
            _assemblyStatus.resizeTextMinSize = 14;
            _assemblyStatus.resizeTextMaxSize = 18;
            Outline statusBorder = _assemblyActions.gameObject.AddComponent<Outline>();
            statusBorder.effectColor = new Color32(148, 115, 82, 255);
            statusBorder.effectDistance = new Vector2(1.5f, -1.5f);
            _plateCard = CreateMaterialDragController(AssemblyStructurePart.Plate);
            _upperCard = CreateMaterialDragController(AssemblyStructurePart.Upper);
            _lowerCard = CreateMaterialDragController(AssemblyStructurePart.Lower);
            _assemblyActions.gameObject.SetActive(false);
            _pcbViewport = CreateScrollViewport("Pcb", 1, out RectTransform content);
            _pcbCard = CreateStructureCard(content, "PCB · 드래그 배치", AssemblyStructurePart.Pcb, Vector2.zero);
            _pcbWorldDrag = CreateMaterialDragController(AssemblyStructurePart.Pcb);
            _pcbViewport.gameObject.SetActive(false);
        }

        /// <summary>parent에 text와 top, height를 사용한 조립 라벨을 만든다. 생성한 Text를 반환한다.</summary>
        private Text CreateAssemblyLabel(Transform parent, string text, float top, float height)
        {
            Text label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = Vector2.up;
            label.rectTransform.anchoredPosition = new Vector2(10f, -top);
            label.rectTransform.sizeDelta = new Vector2(230f, height);
            label.font = _pickerFont;
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        /// <summary>parent의 position에 label과 kind의 실제 구조 부품 드래그 카드를 만든다. 기존 색상·폰트·드래그 및 반환 경로를 연결하고 생성한 컴포넌트를 반환한다.</summary>
        private WorkshopPcbCardDrag CreateStructureCard(Transform parent, string label, AssemblyStructurePart kind, Vector2 position)
        {
            RectTransform card = new GameObject(kind + "DragCard", typeof(RectTransform), typeof(Image), typeof(KeyboardEditButton))
                .GetComponent<RectTransform>();
            card.SetParent(parent, false);
            card.anchorMin = card.anchorMax = card.pivot = Vector2.up;
            card.anchoredPosition = position;
            card.sizeDelta = new Vector2(131f, 148f);
            Image image = card.GetComponent<Image>();
            image.sprite = _pickerSprite;
            image.color = _pickerCardColor;
            KeyboardEditButton button = card.GetComponent<KeyboardEditButton>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Text text = CreateAssemblyLabel(card, label, 112f, 32f);
            text.rectTransform.anchoredPosition = new Vector2(6f, -112f);
            text.rectTransform.sizeDelta = new Vector2(119f, 32f);
            WorkshopPcbCardDrag drag = card.gameObject.AddComponent<WorkshopPcbCardDrag>();
            drag.Initialize(_pullerController, _pcbOutlineMaterial, _pcbDepthShader, kind,
                worldPickupEnabled: kind != AssemblyStructurePart.Pcb);
            return drag;
        }

        /// <summary>kind의 기존 드래그 처리를 표시 없는 컴포넌트로 준비한다. 별도 배치 카드는 만들지 않고 재질 카드에서만 입력을 받는다.</summary>
        private WorkshopPcbCardDrag CreateMaterialDragController(AssemblyStructurePart kind)
        {
            GameObject host = new GameObject(kind + "MaterialDrag", typeof(RectTransform), typeof(KeyboardEditButton));
            host.transform.SetParent(transform, false);
            ((RectTransform)host.transform).sizeDelta = Vector2.zero;
            WorkshopPcbCardDrag drag = host.AddComponent<WorkshopPcbCardDrag>();
            drag.Initialize(_pullerController, _pcbOutlineMaterial, _pcbDepthShader, kind, false);
            return drag;
        }

        /// <summary>material과 plate 및 origin 카드로 해당 구조 부품의 드래그를 시작한다. eventData는 시작 입력에만 사용하며 선택 표시는 기존 재질 UI로 갱신한다.</summary>
        public void BeginStructureMaterialDrag(Material material, AssemblyStructurePart kind, PointerEventData eventData, RectTransform origin)
        {
            if (!_assemblyCatalog) return;
            if (kind == AssemblyStructurePart.Plate && _pickerMode == PickerMode.Plate)
            {
                if (_plateCard.BeginMaterialDrag(eventData, material, origin)) RefreshPlateSelection();
            }
            else if (kind == AssemblyStructurePart.Upper && _pickerMode == PickerMode.Upper)
            {
                if (_upperCard.BeginMaterialDrag(eventData, material, origin)) RefreshCaseMaterialSelection();
            }
            else if (kind == AssemblyStructurePart.Lower && _pickerMode == PickerMode.CaseMaterial)
            {
                if (_lowerCard.BeginMaterialDrag(eventData, material, origin)) RefreshCaseMaterialSelection();
            }
        }

        /// <summary>현재 편집 부품과 조립 상태로 드래그 카드와 제한 안내를 갱신한다. 설치 상태와 독립 재질 선택값은 변경하지 않는다.</summary>
        private void RefreshAssemblyControls()
        {
            KeyboardAssemblyState assembly = ActiveAssembly;
            bool pcb = _pickerMode == PickerMode.Pcb;
            bool plate = _pickerMode == PickerMode.Plate;
            bool upper = _pickerMode == PickerMode.Upper;
            foreach (KeyboardEditButton button in _casePreviewButtons)
            {
                WorkshopStructureMaterialDrag drag = button.GetComponent<WorkshopStructureMaterialDrag>();
                drag.Initialize(this, _keycapMaterials[_caseMaterialIndices[_casePreviewButtons.IndexOf(button)]],
                    upper ? AssemblyStructurePart.Upper : AssemblyStructurePart.Lower);
                drag.enabled = assembly != null && (upper && assembly.CanInstallUpper
                    || _pickerMode == PickerMode.CaseMaterial && assembly.CanInstallLower && !assembly.LowerLoose);
            }
            foreach (KeyboardEditButton button in _plateMaterialButtons)
                button.GetComponent<WorkshopStructureMaterialDrag>().enabled = !UsesScreenTrays && plate && assembly != null && assembly.CanInstallPlate;
            _screwAssembly.SetContext(assembly, _pullerController.IsEditing);
            _pcbCard.SetContext(assembly, pcb);
            _pcbWorldDrag.SetContext(assembly, UsesScreenTrays && pcb);
            _plateCard.SetContext(assembly, plate);
            _upperCard.SetContext(assembly, upper);
            _lowerCard.SetContext(assembly, _pickerMode == PickerMode.CaseMaterial);
            _plateCard.gameObject.SetActive(assembly != null);
            _upperCard.gameObject.SetActive(assembly != null);
            _lowerCard.gameObject.SetActive(assembly != null || UsesScreenTrays);
            _pcbWorldDrag.gameObject.SetActive(assembly != null);
            _pcbViewport.gameObject.SetActive(pcb && assembly != null);
            _pcbButton.interactable = _upperButton.interactable = assembly != null;
            _pcbButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(pcb);
            _upperButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(upper);
            _assemblyActions.gameObject.SetActive(assembly != null
                && (pcb || plate || upper || _pickerMode == PickerMode.CaseMaterial));
            if (assembly == null) return;
            if (_catalogLayout == null) _assemblyActions.sizeDelta = new Vector2(250f, 100f);
            _assemblyStatus.text = _pickerMode == PickerMode.CaseMaterial
                ? assembly.LowerInstalled && assembly.gameObject.activeInHierarchy ? "하부 하우징 설치됨"
                    : assembly.LowerLoose && assembly.gameObject.activeInHierarchy ? "하부 하우징 미설치 · 분리됨"
                    : "하부 하우징 미설치"
                : pcb
                ? $"{(assembly.PcbComplete ? "PCB 조립 완료" : assembly.PcbPlaced ? "PCB 배치됨" : "PCB 미설치")}\n나사 고정 {assembly.ScrewCount}/6"
                : plate ? $"보강판 {(assembly.PlateInstalled ? "설치됨" : "미설치")}"
                : $"상단부 {(assembly.UpperInstalled ? "설치됨" : "미설치")}";
            if (upper || _pickerMode == PickerMode.CaseMaterial) RefreshCaseMaterialSelection();
        }

        /// <summary>조립 전용 버튼의 C# 이벤트 구독을 해제한다. UI가 파괴될 때 연결만 정리하며 조립 상태는 유지한다.</summary>
        private void ReleaseAssemblyControls()
        {
            _pcbButton.Clicked -= HandleCategoryClicked;
            _upperButton.Clicked -= HandleCategoryClicked;
        }
    }
}
