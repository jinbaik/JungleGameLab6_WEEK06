using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed partial class KeyboardEditOverlayUI
    {
        private ScrollRect _ledTrayScroll;
        private ScrollRect _photoTrayScroll;
        private RectTransform _editorTabs;
        private KeyboardEditButton _ledTrayTab;
        private KeyboardEditButton _photoTrayTab;
        private bool _photoTraySelected;

        /// <summary>현재 LED·사진 패널을 기존 목록 이전 함수로 트레이 안에 배치한다. 원래 컨트롤과 편집 이벤트를 재사용하며 닫힌 상태로 연결한다.</summary>
        private void ConnectEditorTray()
        {
            float width = Mathf.Max(240f, _keycapTrayContent.rect.width - 24f);
            _ledTrayScroll = _catalogLayout.EmbedTrayEditor((RectTransform)_ledUI.transform, _keycapTrayContent, width);
            _photoTrayScroll = _catalogLayout.EmbedTrayEditor((RectTransform)transform.Find("Photo customization"), _keycapTrayContent, width);
            _ledTrayScroll.gameObject.SetActive(false);
            _photoTrayScroll.gameObject.SetActive(false);
            _editorTabs = new GameObject("EditorTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            _editorTabs.SetParent(_keycapTrayContent, false);
            _editorTabs.anchorMin = Vector2.up;
            _editorTabs.anchorMax = Vector2.one;
            _editorTabs.pivot = Vector2.up;
            _editorTabs.anchoredPosition = new Vector2(12f, -12f);
            _editorTabs.sizeDelta = new Vector2(-36f, 36f);
            HorizontalLayoutGroup layout = _editorTabs.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            _ledTrayTab = CreateEditorTrayTab("LED");
            _photoTrayTab = CreateEditorTrayTab("커스텀 이미지");
            _editorTabs.gameObject.SetActive(false);
            _keycapTrayTransition.ConnectCatalogScroll(_ledTrayScroll, _keycapPlacement, WorkshopDrawerCategory.LedCustomImage);
            SetEditorTrayBindings(true);
        }

        /// <summary>text를 표시하는 기존 스타일 버튼을 트레이 탭 행에 생성해 반환한다. 편집값과 대상 키보드는 변경하지 않는다.</summary>
        private KeyboardEditButton CreateEditorTrayTab(string text)
        {
            KeyboardEditButton button = CreatePickerItemButton(_editorTabs, text, 0, text, out Image background, out Text indicator);
            indicator.gameObject.SetActive(false);
            Text label = button.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        /// <summary>subscribe로 트레이 상태 및 두 편집 탭의 C# 이벤트를 연결 또는 해제한다. 앱 재활성화 시 중복 구독을 막는다.</summary>
        private void SetEditorTrayBindings(bool subscribe)
        {
            if (_keycapTrayTransition != null) _keycapTrayTransition.TrayStateChanged -= HandleEditorTrayStateChanged;
            if (_ledTrayTab != null) _ledTrayTab.Clicked -= HandleEditorTrayTabClicked;
            if (_photoTrayTab != null) _photoTrayTab.Clicked -= HandleEditorTrayTabClicked;
            if (_editorTabs == null) return;
            if (!subscribe) return;
            _keycapTrayTransition.TrayStateChanged += HandleEditorTrayStateChanged;
            _ledTrayTab.Clicked += HandleEditorTrayTabClicked;
            _photoTrayTab.Clicked += HandleEditorTrayTabClicked;
        }

        /// <summary>category와 opened에 따라 완전히 열린 편집 트레이만 활성화한다. 닫을 때 기존 미리보기 취소와 입력 복구를 호출하며 적용값은 유지한다.</summary>
        private void HandleEditorTrayStateChanged(WorkshopDrawerCategory category, bool opened)
        {
            if (_editorTabs == null || _ledTrayScroll == null || _photoTrayScroll == null) return;
            if (opened) _keycapTrayTransition.ApplyTypography(_pickerFont);
            if (opened && (category == WorkshopDrawerCategory.Pcb || category == WorkshopDrawerCategory.UpperHousing
                || category == WorkshopDrawerCategory.LowerHousing)) RefreshStructureTrayPreviews();
            bool visible = opened && category == WorkshopDrawerCategory.LedCustomImage;
            _editorTabs.gameObject.SetActive(visible);
            if (visible) ShowEditorTraySelection();
            else
            {
                if (opened && category == WorkshopDrawerCategory.Plate) SetPickerMode(PickerMode.Plate);
                else if (opened && category == WorkshopDrawerCategory.UpperHousing) SetPickerMode(PickerMode.Upper);
                else if (opened && category == WorkshopDrawerCategory.Pcb) SetPickerMode(PickerMode.Pcb);
                else if (opened && category == WorkshopDrawerCategory.LowerHousing) SetPickerMode(PickerMode.CaseMaterial);
                else if (_pickerMode == PickerMode.Photo || _pickerMode == PickerMode.Led || _pickerMode == PickerMode.Plate
                    || _pickerMode == PickerMode.Upper || _pickerMode == PickerMode.Pcb || _pickerMode == PickerMode.CaseMaterial)
                    SetPickerMode(PickerMode.None);
                _ledTrayScroll.gameObject.SetActive(false);
                _photoTrayScroll.gameObject.SetActive(false);
            }
        }

        /// <summary>button에 해당하는 편집 탭을 입력 가능한 열린 트레이에서 선택한다. 원래 적용·취소와 대상 키보드 연결은 유지한다.</summary>
        private void HandleEditorTrayTabClicked(KeyboardEditButton button)
        {
            if (!_keycapTrayTransition.CanInteractWithTray || !_keycapTrayTransition.IsCategoryOpen(WorkshopDrawerCategory.LedCustomImage)) return;
            _photoTraySelected = button == _photoTrayTab;
            ShowEditorTraySelection();
        }

        /// <summary>선택된 탭의 기존 편집기를 현재 작업대 대상에 연다. 목록과 입력 대상만 교체하며 다른 탭을 숨긴다.</summary>
        private void ShowEditorTraySelection()
        {
            SetPickerMode(_photoTraySelected ? PickerMode.Photo : PickerMode.Led);
            _ledTrayScroll.gameObject.SetActive(!_photoTraySelected);
            _photoTrayScroll.gameObject.SetActive(_photoTraySelected);
            _keycapTrayTransition.ConnectCatalogScroll(_photoTraySelected ? _photoTrayScroll : _ledTrayScroll,
                _keycapPlacement, WorkshopDrawerCategory.LedCustomImage);
            _keycapTrayTransition.ApplyTypography(_pickerFont);
        }
    }
}
