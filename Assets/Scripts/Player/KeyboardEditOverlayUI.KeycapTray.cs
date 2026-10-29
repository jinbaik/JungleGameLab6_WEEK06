using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed partial class KeyboardEditOverlayUI
    {
        [Header("Keycap Tray Display")]
        [SerializeField] private RectTransform _keycapTrayContent;
        [SerializeField] private WorkshopKeycapDrawerTransition _keycapTrayTransition;
        private GridLayoutGroup _keycapTrayGrid;
        private RectTransform _keycapTrayViewport;
        private readonly System.Collections.Generic.List<Texture2D> _toolPreviewTextures = new System.Collections.Generic.List<Texture2D>();
        private GridLayoutGroup _switchTrayGrid;
        private RectTransform _switchTrayViewport;
        private GridLayoutGroup _toolTrayGrid;
        private RectTransform _toolTrayViewport;

        public bool UsesScreenTrays => _keycapTrayContent != null && _keycapTrayTransition != null;

        /// <summary>기존 키캡 ScrollRect와 카드들을 새 ContentArea 안의 세로 그리드로 연결한다. 카드와 썸네일 및 재고는 다시 만들지 않는다.</summary>
        private void ConnectKeycapTray()
        {
            ConnectTrayCatalog(_viewport, _content, _previewButtons, WorkshopDrawerCategory.Keycaps,
                out _keycapTrayGrid, out _keycapTrayViewport);
            ScrollRect scroll = _switchSoundViewport.GetComponent<ScrollRect>();
            _switchSoundViewport.SetParent(_keycapTrayContent, false);
            ConnectTrayCatalog(_switchSoundViewport, scroll.content, _switchSoundButtons, WorkshopDrawerCategory.Switches,
                out _switchTrayGrid, out _switchTrayViewport);
            _keycapPlacement.BindTrayPlacementModes(_keycapTrayContent, _pickerFont);
            ConnectToolTray();
            ConnectEditorTray();
            ConnectStructureTrays();
        }

        /// <summary>기존 트레이 틀에 두 풀러 접근 카드를 만든다. 실제 도구와 입력은 기존 pullerController에 C# 이벤트로 연결하고 나사 기능은 생성하지 않는다.</summary>
        private void ConnectToolTray()
        {
            RectTransform panel = CreateScrollViewport("Tools", 2, out RectTransform content);
            panel.SetParent(_keycapTrayContent, false);
            var buttons = new System.Collections.Generic.List<KeyboardEditButton>();
            var items = new WorkshopPullerTrayItem[2];
            for (int index = 0; index < items.Length; index++)
            {
                KeyboardEditButton button = CreatePickerItemButton(content, index == 0 ? "KeycapPuller" : "SwitchPuller",
                    index, index == 0 ? "키캡 Puller" : "스위치 Puller", out Image background, out Text indicator);
                indicator.gameObject.SetActive(false);
                items[index] = button.gameObject.AddComponent<WorkshopPullerTrayItem>();
                items[index].Initialize(index == 1);
                buttons.Add(button);
                Texture2D texture = _pullerController.CreateToolPreview(index == 1);
                _toolPreviewTextures.Add(texture);
                CreateMaterialPreviewImage(button.transform, "ToolPreview", texture, new Vector2(96f, 88f), 14f);
            }
            ConnectTrayCatalog(panel, content, buttons, WorkshopDrawerCategory.ScrewsTools, out _toolTrayGrid, out _toolTrayViewport);
            _pullerController.ConnectToolTray(_keycapTrayTransition, items);
        }

        /// <summary>viewport와 content의 기존 카드를 category의 독립 세로 목록으로 연결한다. 재고와 썸네일을 재생성하지 않고 grid와 clip을 반환한다.</summary>
        private void ConnectTrayCatalog(RectTransform viewport, RectTransform content,
            System.Collections.Generic.List<KeyboardEditButton> buttons, WorkshopDrawerCategory category,
            out GridLayoutGroup grid, out RectTransform clip)
        {
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            foreach (Image image in viewport.GetComponentsInChildren<Image>())
                if (image.transform.parent == viewport || image.transform == viewport) image.color = Color.clear;

            clip = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image)).GetComponent<RectTransform>();
            clip.SetParent(viewport, false);
            clip.anchorMin = Vector2.zero;
            clip.anchorMax = Vector2.one;
            clip.offsetMin = Vector2.zero;
            clip.offsetMax = new Vector2(-16f, -60f);
            clip.GetComponent<Image>().color = Color.clear;
            content.SetParent(clip, false);
            content.anchorMin = Vector2.up;
            content.anchorMax = Vector2.one;
            content.pivot = Vector2.up;
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.spacing = new Vector2(8f, 8f);
            grid.childAlignment = TextAnchor.UpperCenter;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            grid.cellSize = new Vector2(150f, 150f);

            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            Scrollbar bar = scroll.horizontalScrollbar;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.viewport = clip;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 35f;
            RectTransform track = (RectTransform)bar.transform;
            track.anchorMin = Vector2.right;
            track.anchorMax = Vector2.one;
            track.pivot = Vector2.right;
            track.offsetMin = new Vector2(-10f, 2f);
            track.offsetMax = new Vector2(0f, -62f);
            bar.direction = Scrollbar.Direction.BottomToTop;
            foreach (KeyboardEditButton button in buttons)
            {
                button.enabled = false;
                Text label = button.transform.Find("Label").GetComponent<Text>();
                label.rectTransform.sizeDelta = new Vector2(-12f, 32f);
                label.rectTransform.anchoredPosition = new Vector2(0f, 6f);
                RawImage preview = button.GetComponentInChildren<RawImage>(true);
                if (preview == null) continue;
                RectTransform imageArea = new GameObject("ModelImageArea", typeof(RectTransform)).GetComponent<RectTransform>();
                imageArea.SetParent(button.transform, false);
                imageArea.anchorMin = Vector2.zero;
                imageArea.anchorMax = Vector2.one;
                imageArea.offsetMin = new Vector2(8f, 42f);
                imageArea.offsetMax = new Vector2(-8f, -8f);
                preview.transform.SetParent(imageArea, false);
                AspectRatioFitter aspect = preview.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = preview.texture != null ? (float)preview.texture.width / preview.texture.height : 1f;
            }
            _keycapTrayTransition.ConnectCatalogScroll(scroll, _keycapPlacement, category);
        }

        /// <summary>현재 Viewport 폭으로 두 열 카드의 너비만 맞춘다. 스크롤 위치와 항목 순서 및 트레이 배치는 변경하지 않는다.</summary>
        private void UpdateKeycapTrayWidth()
        {
            UpdateStructureTrayWidths();
            Vector2 size = new Vector2(Mathf.Max(1f, (_keycapTrayViewport.rect.width - 16f) * 0.5f), 150f);
            if (_keycapTrayGrid.cellSize != size) _keycapTrayGrid.cellSize = size;
            if (_switchTrayGrid != null)
                _switchTrayGrid.cellSize = new Vector2(Mathf.Max(1f, (_switchTrayViewport.rect.width - 16f) * 0.5f), 150f);
            if (_toolTrayGrid != null)
                _toolTrayGrid.cellSize = new Vector2(Mathf.Max(1f, (_toolTrayViewport.rect.width - 16f) * 0.5f), 150f);
        }
    }
}
