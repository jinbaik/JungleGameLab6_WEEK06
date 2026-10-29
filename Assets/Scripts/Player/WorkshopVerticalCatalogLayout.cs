using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class WorkshopVerticalCatalogLayout : MonoBehaviour
    {
        [Header("Catalog Layout")]
        [SerializeField, Min(320f)] private float _width = 340f;
        [SerializeField, Min(0f)] private float _bottomMargin = 120f;
        private readonly HashSet<ScrollRect> _configured = new HashSet<ScrollRect>();
        private readonly Dictionary<RectTransform, ScrollRect> _editorPanels = new Dictionary<RectTransform, ScrollRect>();
        private readonly List<Transform> _pendingPanels = new List<Transform>();
        private readonly List<ScrollRect> _scrollRects = new List<ScrollRect>();
        private RectTransform _menuRoot;
        private KeyboardEditButton _toggle;
        private Text _toggleLabel;

        void Start()
        {
            _menuRoot = new GameObject("WorkshopCatalogMenu", typeof(RectTransform)).GetComponent<RectTransform>();
            _menuRoot.SetParent(transform, false);
            _menuRoot.anchorMin = Vector2.zero;
            _menuRoot.anchorMax = Vector2.one;
            _menuRoot.sizeDelta = Vector2.zero;
            RectTransform toggleRect = new GameObject("CatalogToggle", typeof(RectTransform), typeof(Image),
                typeof(KeyboardEditButton)).GetComponent<RectTransform>();
            toggleRect.SetParent(transform, false);
            toggleRect.anchorMin = toggleRect.anchorMax = toggleRect.pivot = Vector2.up;
            toggleRect.anchoredPosition = new Vector2(_width + 36f, -24f);
            toggleRect.sizeDelta = new Vector2(180f, 40f);
            toggleRect.GetComponent<Image>().color = new Color32(37, 52, 68, 255);
            _toggle = toggleRect.GetComponent<KeyboardEditButton>();
            _toggle.Clicked += HandleToggleClicked;
            _toggleLabel = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            _toggleLabel.transform.SetParent(toggleRect, false);
            _toggleLabel.rectTransform.anchorMin = Vector2.zero;
            _toggleLabel.rectTransform.anchorMax = Vector2.one;
            _toggleLabel.rectTransform.sizeDelta = Vector2.zero;
            _toggleLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _toggleLabel.fontSize = 16;
            _toggleLabel.alignment = TextAnchor.MiddleCenter;
            _toggleLabel.raycastTarget = false;
            _toggleLabel.text = "UI 접기";
        }

        void LateUpdate()
        {
            _pendingPanels.Clear();
            foreach (Transform child in transform)
            {
                if (child == _menuRoot || child == _toggle.transform || child.name == "ModeBadge"
                    || child.name == "ControlBar" || child.name == "TournamentEntryButton") continue;
                _pendingPanels.Add(child);
            }
            foreach (Transform panel in _pendingPanels)
            {
                panel.SetParent(_menuRoot, false);
                if (panel.name == "LEDCustomizationPanel" || panel.name == "Photo customization")
                    ConfigureEditorPanel((RectTransform)panel);
            }
            foreach (var editor in _editorPanels)
            {
                editor.Value.gameObject.SetActive(editor.Key.gameObject.activeSelf);
                Vector2 size = new Vector2(0f, editor.Key.rect.height * editor.Key.localScale.y);
                if (editor.Value.content.sizeDelta != size) editor.Value.content.sizeDelta = size;
            }
            GetComponentsInChildren(true, _scrollRects);
            foreach (ScrollRect scroll in _scrollRects)
            {
                if (scroll.transform.parent != _menuRoot || !_configured.Add(scroll)) continue;
                ConfigureCatalog(scroll);
            }
        }

        void OnDestroy()
        {
            if (_toggle != null) _toggle.Clicked -= HandleToggleClicked;
        }

        /// <summary>
        /// button의 클릭으로 재료 목록과 파츠 버튼의 표시 상태를 함께 전환한다.
        /// 현재 선택과 재료는 유지하고 토글 문구와 위치를 갱신한다. 반환값은 없다.
        /// </summary>
        private void HandleToggleClicked(KeyboardEditButton button)
        {
            bool expanded = !_menuRoot.gameObject.activeSelf;
            _menuRoot.gameObject.SetActive(expanded);
            _toggleLabel.text = expanded ? "UI 접기" : "UI 펼치기";
            ((RectTransform)button.transform).anchoredPosition = new Vector2(expanded ? _width + 36f : 24f, -24f);
        }

        /// <summary>
        /// panel에서 LED 또는 이미지 조작 항목을 추출해 공통 왼쪽 목록에 배치한다.
        /// 독립 카드 프레임과 중첩 스크롤은 제거하고 항목의 활성 상태를 호스트와 연결한다. 반환값은 없다.
        /// </summary>
        private void ConfigureEditorPanel(RectTransform panel)
        {
            RectTransform host = new GameObject(panel.name + "CatalogHost", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            host.SetParent(_menuRoot, false);
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.up;
            host.pivot = Vector2.up;
            host.anchoredPosition = new Vector2(24f, -24f);
            host.sizeDelta = new Vector2(_width, -24f - _bottomMargin);
            host.GetComponent<Image>().color = new Color32(37, 52, 68, 255);
            RectTransform clip = new GameObject("EditorClip", typeof(RectTransform), typeof(RectMask2D))
                .GetComponent<RectTransform>();
            clip.SetParent(host, false);
            clip.anchorMin = Vector2.zero;
            clip.anchorMax = Vector2.one;
            clip.offsetMin = new Vector2(12f, 12f);
            clip.offsetMax = new Vector2(-12f, -12f);
            RectTransform content = new GameObject("EditorContent", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(clip, false);
            content.anchorMin = Vector2.up;
            content.anchorMax = Vector2.one;
            content.pivot = Vector2.up;
            content.anchoredPosition = Vector2.zero;
            if (panel.TryGetComponent<KeyboardLEDPanelUI>(out var led))
                led.EmbedControls(content, _width - 24f);
            else
                panel = GetComponent<KeyboardCustomizationTestUI>().EmbedControls(content, _width - 24f);
            ScrollRect scroll = host.GetComponent<ScrollRect>();
            scroll.viewport = clip;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            scroll.verticalScrollbar = CreateEditorScrollbar(host);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _configured.Add(scroll);
            _editorPanels.Add(panel, scroll);
        }

        /// <summary>
        /// host 오른쪽에 재료 목록과 같은 색상과 두께의 세로 스크롤바를 만든다.
        /// 생성된 핸들을 연결한 Scrollbar를 반환하며 조작 항목의 값은 변경하지 않는다.
        /// </summary>
        private Scrollbar CreateEditorScrollbar(RectTransform host)
        {
            RectTransform track = new GameObject("EditorScrollbar", typeof(RectTransform), typeof(Image),
                typeof(Scrollbar)).GetComponent<RectTransform>();
            track.SetParent(host, false);
            track.anchorMin = Vector2.right;
            track.anchorMax = Vector2.one;
            track.pivot = Vector2.right;
            track.anchoredPosition = new Vector2(-4f, 12f);
            track.sizeDelta = new Vector2(5f, -24f);
            track.GetComponent<Image>().color = new Color32(28, 40, 53, 220);
            RectTransform handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            handle.SetParent(track, false);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.sizeDelta = Vector2.zero;
            Image image = handle.GetComponent<Image>();
            image.color = new Color32(75, 129, 151, 255);
            Scrollbar bar = track.GetComponent<Scrollbar>();
            bar.handleRect = handle;
            bar.targetGraphic = image;
            bar.direction = Scrollbar.Direction.BottomToTop;
            return bar;
        }

        /// <summary>
        /// scroll의 기존 카드와 색상을 유지하면서 왼쪽 세로 목록과 오른쪽 스크롤바로 재배치한다.
        /// 보강판 탭 아래에 별도 마스크를 만들고 각 콘텐츠를 두 열의 카드 목록으로 변경한다. 반환값은 없다.
        /// </summary>
        private void ConfigureCatalog(ScrollRect scroll)
        {
            RectTransform panel = (RectTransform)scroll.transform;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.up;
            panel.pivot = Vector2.up;
            panel.anchoredPosition = new Vector2(24f, -24f);
            panel.sizeDelta = new Vector2(_width, -24f - _bottomMargin);

            var contents = new List<RectTransform>();
            foreach (RectTransform child in panel)
                if (child.name.EndsWith("Content")) contents.Add(child);

            RectTransform clip = new GameObject("CatalogClip", typeof(RectTransform), typeof(RectMask2D))
                .GetComponent<RectTransform>();
            clip.SetParent(panel, false);
            clip.anchorMin = Vector2.zero;
            clip.anchorMax = Vector2.one;
            clip.offsetMin = new Vector2(12f, 12f);
            float topInset = panel.name == "PlateViewport" ? 48f
                : panel.Find("KeycapPlacementModes") != null ? 60f : 12f;
            clip.offsetMax = new Vector2(-24f, -topInset);
            scroll.viewport = clip;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 35f;

            foreach (RectTransform content in contents)
            {
                content.SetParent(clip, false);
                content.anchorMin = Vector2.up;
                content.anchorMax = Vector2.one;
                content.pivot = Vector2.up;
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = Vector2.zero;
                GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 2;
                grid.cellSize = new Vector2((_width - 48f) * 0.5f, 128f);
                grid.spacing = new Vector2(12f, 12f);
                grid.childAlignment = TextAnchor.UpperCenter;
                ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            Scrollbar bar = scroll.horizontalScrollbar;
            scroll.horizontalScrollbar = null;
            RectTransform track = (RectTransform)bar.transform;
            track.anchorMin = Vector2.right;
            track.anchorMax = Vector2.one;
            track.pivot = Vector2.right;
            track.anchoredPosition = new Vector2(-8f, 12f);
            track.sizeDelta = new Vector2(7f, -24f);
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalNormalizedPosition = 1f;
        }
    }
}
