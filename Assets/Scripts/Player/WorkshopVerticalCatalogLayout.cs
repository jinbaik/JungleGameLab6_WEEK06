using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class WorkshopVerticalCatalogLayout : MonoBehaviour
    {
        [Header("Drawer Layout")]
        [SerializeField, Min(320f)] private float _width = 340f;
        [SerializeField, Min(0f)] private float _bottomMargin = 24f;
        [SerializeField, Min(120f)] private float _panelHeight = 320f;
        [SerializeField, Min(24f)] private float _labelSpacing = 32f;
        [SerializeField, Min(24f)] private float _tabWidth = 132f;
        [SerializeField, Min(16f)] private float _tabHeight = 22f;
        [SerializeField, Min(0.01f)] private float _slideDuration = 0.22f;
        [SerializeField, Min(1f)] private float _slideDistance = 340f;
        [SerializeField] private bool _hideDrawerFaces;
        private readonly HashSet<ScrollRect> _configured = new HashSet<ScrollRect>();
        private readonly Dictionary<RectTransform, ScrollRect> _editorPanels = new Dictionary<RectTransform, ScrollRect>();
        private readonly List<Transform> _pendingPanels = new List<Transform>();
        private readonly List<ScrollRect> _scrollRects = new List<ScrollRect>();
        private readonly List<KeyboardEditButton> _labels = new List<KeyboardEditButton>();
        private readonly List<Image> _categoryTabs = new List<Image>();
        private KeyboardEditButton[] _modeButtons;
        private RectTransform _menuRoot;
        private RectTransform _drawerRoot;
        private RectTransform _clip;
        private CanvasGroup _listInput;
        private KeyboardEditOverlayUI _overlay;
        private WorkshopKeyboardPullerController _pullers;
        private GameObject _placeholder;
        private Coroutine _transition;
        private bool _workshopVisible;
        private bool _transitioning;
        private int _selected = -1;
        private float _openAmount;
        public bool IsTransitioning => _transitioning;

        void Start()
        {
            _overlay = GetComponent<KeyboardEditOverlayUI>();
            _pullers = GetComponent<WorkshopKeycapPlacement>().Pullers;
            if (_overlay.UsesScreenTrays)
            {
                _overlay.HideLegacyCatalogDisplay();
                return;
            }
            _drawerRoot = CreateRect("WorkshopDrawerCatalog", transform);
            _drawerRoot.anchorMin = _drawerRoot.anchorMax = _drawerRoot.pivot = Vector2.zero;
            _clip = CreateRect("DrawerRevealMask", _drawerRoot);
            _clip.gameObject.AddComponent<RectMask2D>();
            _menuRoot = CreateRect("SlidingCatalogContents", _clip);
            _menuRoot.anchorMin = _menuRoot.anchorMax = _menuRoot.pivot = Vector2.zero;
            _menuRoot.gameObject.AddComponent<Image>().color = new Color32(225, 204, 176, 255);
            Outline trayRim = _menuRoot.gameObject.AddComponent<Outline>();
            trayRim.effectColor = new Color32(113, 76, 44, 255);
            trayRim.effectDistance = new Vector2(3f, -3f);
            _listInput = _menuRoot.gameObject.AddComponent<CanvasGroup>();
            _placeholder = CreateRect("DisplayOnlyCategory", _menuRoot).gameObject;
            Text note = CreateLabel(_placeholder.transform, "표시 전용 · 교체 기능 준비 전");
            note.alignment = TextAnchor.MiddleCenter;
            string[] labels = { "키캡", "축", "상부 하우징", "LED", "보강판", "PCB", "하부 하우징", "이미지", "케이스 재질" };
            if (_overlay.AssemblyCatalogEnabled)
                labels = new[] { "하단부", "PCB", "보강판", "스위치", "상단부", "키캡", "LED", "이미지" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (i >= 7)
                {
                    KeyboardEditButton utility = _overlay.GetDrawerUtilityButton(i);
                    utility.transform.SetParent(_drawerRoot, false);
                    _labels.Add(utility);
                    continue;
                }
                RectTransform face = CreateRect("DrawerFront_" + labels[i], _drawerRoot);
                Image image = face.gameObject.AddComponent<Image>();
                image.color = new Color32(202, 168, 129, 255);
                Shadow shadow = face.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0.16f, 0.09f, 0.04f, 0.35f);
                shadow.effectDistance = new Vector2(3f, -3f);
                KeyboardEditButton button = face.gameObject.AddComponent<KeyboardEditButton>();
                button.targetGraphic = image;
                button.Clicked += HandleDrawerClicked;
                _labels.Add(button);
                CreateTopViewTray(face, labels[i]);
                if (_hideDrawerFaces)
                {
                    CanvasGroup faceGroup = face.gameObject.AddComponent<CanvasGroup>();
                    faceGroup.alpha = 0f;
                    faceGroup.interactable = false;
                    faceGroup.blocksRaycasts = false;
                }
            }
            _modeButtons = _overlay.MoveDrawerModeButtons(_drawerRoot);
            ResetDrawers(_workshopVisible);
        }

        void LateUpdate()
        {
            if (_overlay.UsesScreenTrays)
            {
                _overlay.HideLegacyCatalogDisplay();
                return;
            }
            if (_menuRoot == null) return;
            _pendingPanels.Clear();
            foreach (Transform child in transform)
            {
                if (child == _drawerRoot || child.name == "ModeBadge" || child.name == "ControlBar"
                    || child.name == "TournamentEntryButton" || child.name == "KeycapCustomizationPanel") continue;
                if (child.name == "KeyboardEditPanel") { child.gameObject.SetActive(false); continue; }
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
            foreach (RectTransform panel in _menuRoot)
            {
                if (_editorPanels.ContainsKey(panel)) continue;
                if (_overlay.AssemblyCatalogEnabled && panel.name == "AssemblyActions")
                {
                    panel.anchorMin = Vector2.zero;
                    panel.anchorMax = Vector2.right;
                    panel.pivot = Vector2.zero;
                    panel.anchoredPosition = Vector2.zero;
                    panel.sizeDelta = new Vector2(0f, 64f);
                    Text status = panel.GetComponentInChildren<Text>(true);
                    status.rectTransform.anchorMin = Vector2.zero;
                    status.rectTransform.anchorMax = Vector2.one;
                    status.rectTransform.offsetMin = new Vector2(8f, 4f);
                    status.rectTransform.offsetMax = new Vector2(-8f, -4f);
                    status.fontSize = 12;
                    status.color = new Color32(48, 32, 22, 255);
                    continue;
                }
                if (_overlay.AssemblyCatalogEnabled && panel.TryGetComponent<WorkshopPcbCardDrag>(out _)) continue;
                panel.anchorMin = Vector2.zero;
                panel.anchorMax = Vector2.one;
                panel.offsetMin = _overlay.AssemblyCatalogEnabled && panel.TryGetComponent<ScrollRect>(out _)
                    ? new Vector2(0f, 64f) : Vector2.zero;
                panel.offsetMax = Vector2.zero;
                if (panel.TryGetComponent(out Image image)) image.color = new Color32(221, 197, 165, 255);
            }
            _drawerRoot.sizeDelta = new Vector2(_width, _panelHeight + 7f * _labelSpacing + 76f);
            _drawerRoot.anchoredPosition = new Vector2(24f, _bottomMargin);
            RectTransform canvas = (RectTransform)GetComponentInParent<Canvas>().transform;
            float fit = Mathf.Min(1f, (canvas.rect.width - 48f) / _drawerRoot.sizeDelta.x,
                (canvas.rect.height - _bottomMargin - 24f) / _drawerRoot.sizeDelta.y);
            _drawerRoot.localScale = Vector3.one * Mathf.Max(0.1f, fit);
            _clip.anchorMin = _clip.anchorMax = _clip.pivot = Vector2.zero;
            float revealedHeight = _panelHeight * _openAmount;
            float openingBase = _selected >= 0 && _selected < 7 ? (6 - _selected) * _labelSpacing : 7f * _labelSpacing;
            _clip.anchoredPosition = new Vector2(0f, openingBase);
            _clip.sizeDelta = new Vector2(_width, revealedHeight);
            _menuRoot.sizeDelta = new Vector2(_width, _panelHeight);
            _menuRoot.anchoredPosition = new Vector2(0f, -(1f - _openAmount) * Mathf.Max(_panelHeight, _slideDistance));
            _clip.gameObject.SetActive(_selected >= 0);
            _placeholder.SetActive(!_overlay.AssemblyCatalogEnabled && (_selected == 2 || _selected == 5 || _selected == 6));
            for (int i = 0; i < _labels.Count; i++)
            {
                RectTransform face = (RectTransform)_labels[i].transform;
                face.anchorMin = face.anchorMax = face.pivot = Vector2.zero;
                face.anchoredPosition = i < 7
                    ? new Vector2(0f, (6 - i) * _labelSpacing + (_selected >= 0 && _selected < 7 && i <= _selected ? revealedHeight : 0f))
                    : new Vector2((i - 7) * _width * 0.5f, _hideDrawerFaces
                        ? canvas.rect.height - _bottomMargin - 62f : 7f * _labelSpacing + revealedHeight + 4f);
                face.sizeDelta = new Vector2(i < 7 ? _width : _width * 0.5f - 4f, i < 7 ? _labelSpacing - 2f : 30f);
                _labels[i].targetGraphic.color = i == _selected ? new Color32(165, 112, 65, 255) : new Color32(211, 180, 143, 255);
                if (i < _categoryTabs.Count)
                {
                    _categoryTabs[i].rectTransform.sizeDelta = new Vector2(
                        Mathf.Min(_tabWidth, _width - 100f), Mathf.Min(_tabHeight, _labelSpacing - 6f));
                    _categoryTabs[i].color = i == _selected ? new Color32(240, 199, 130, 255) : new Color32(235, 220, 195, 255);
                }
            }
            for (int i = 0; i < _modeButtons.Length; i++)
            {
                RectTransform mode = (RectTransform)_modeButtons[i].transform;
                mode.anchorMin = mode.anchorMax = mode.pivot = Vector2.zero;
                mode.anchoredPosition = new Vector2(i * _width * 0.5f, _hideDrawerFaces
                    ? canvas.rect.height - _bottomMargin - 98f : 7f * _labelSpacing + revealedHeight + 40f);
                mode.sizeDelta = new Vector2(_width * 0.5f - 4f, 30f);
            }
            _listInput.interactable = !_transitioning && _selected >= 0;
            _listInput.blocksRaycasts = _selected >= 0;
        }

        /// <summary>
        /// name과 parent로 입력을 가리지 않는 기본 UI 사각형을 생성해 반환한다.
        /// </summary>
        private RectTransform CreateRect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// face에 탑 뷰 서랍의 바닥과 손잡이 및 중앙의 내부 인식표를 구성한다.
        /// text를 표시한 인식표를 포함한 장식은 입력을 받지 않으며 기존 앞면 버튼으로 선택을 처리한다.
        /// </summary>
        private void CreateTopViewTray(RectTransform face, string text)
        {
            Image floor = CreateTrayDecoration("RecessedTrayFloor", face, new Color32(186, 148, 108, 255));
            floor.rectTransform.offsetMin = new Vector2(4f, 8f);
            floor.rectTransform.offsetMax = new Vector2(-4f, -3f);
            Image lip = CreateTrayDecoration("FrontWoodLip", face, new Color32(151, 105, 62, 255));
            lip.rectTransform.anchorMax = Vector2.right;
            lip.rectTransform.offsetMin = Vector2.zero;
            lip.rectTransform.offsetMax = new Vector2(0f, 7f);
            for (int i = 0; i < 2; i++)
            {
                Image handle = CreateTrayDecoration("InsetHandle", face, new Color32(87, 70, 52, 255));
                RectTransform rect = handle.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(i == 0 ? 0.12f : 0.88f, 0.5f);
                rect.sizeDelta = new Vector2(34f, 5f);
                rect.anchoredPosition = new Vector2(0f, -1f);
                Outline edge = handle.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color32(220, 208, 185, 255);
                edge.effectDistance = new Vector2(1f, -1f);
            }
            Image plate = CreateTrayDecoration("CategoryNameplate", face, new Color32(235, 220, 195, 255));
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            plate.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            plate.rectTransform.sizeDelta = new Vector2(_tabWidth, _tabHeight);
            plate.rectTransform.anchoredPosition = new Vector2(0f, 1f);
            _categoryTabs.Add(plate);
            Outline rim = plate.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color32(127, 111, 87, 255);
            rim.effectDistance = new Vector2(1f, -1f);
            Text label = CreateLabel(plate.transform, text);
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 14;
            label.lineSpacing = 0.95f;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>
        /// name과 color로 parent 전체를 채우는 장식 Image를 만들어 반환한다.
        /// 장식은 입력을 받지 않고 기존 서랍 버튼의 클릭 영역을 유지한다.
        /// </summary>
        private Image CreateTrayDecoration(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// parent 안에 text를 표시하는 비입력 인식표를 만들고 Text를 반환한다.
        /// </summary>
        private Text CreateLabel(Transform parent, string text)
        {
            RectTransform rect = CreateRect("CategoryLabel", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.color = new Color32(48, 32, 22, 255);
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        /// <summary>
        /// button에 대응하는 서랍을 요청하며 진행 중 전환과 부품 및 도구 조작 중에는 입력을 무시한다.
        /// </summary>
        private void HandleDrawerClicked(KeyboardEditButton button)
        {
            RequestDrawer(_labels.IndexOf(button));
        }

        /// <summary>
        /// index의 기존 카테고리를 닫기 후 열기 순서로 선택한다.
        /// 동일 항목이면 닫고 진행 중 조작과 애니메이션의 추가 요청은 무시한다.
        /// </summary>
        public void RequestDrawer(int index)
        {
            if (_menuRoot == null || !_workshopVisible || _transitioning || !_pullers.InputAllowed
                || _pullers.IsManipulatingPart) return;
            _transitioning = true;
            _listInput.interactable = false;
            _transition = StartCoroutine(SlideDrawers(index == _selected ? -1 : index));
        }

        /// <summary>
        /// index로 전환하면서 기존 목록을 아래로 숨긴 뒤 새 목록을 위로 이동한다.
        /// Time.deltaTime으로 진행하며 재고와 출고 및 반환 예약은 변경하지 않는다.
        /// </summary>
        private System.Collections.IEnumerator SlideDrawers(int index)
        {
            while (_openAmount > 0f)
            {
                _openAmount = Mathf.MoveTowards(_openAmount, 0f, Time.deltaTime / _slideDuration);
                yield return null;
            }
            _overlay.SetDrawerCategory(-1);
            _selected = index;
            if (index >= 0)
            {
                _overlay.SetDrawerCategory(index);
                yield return null;
                while (_openAmount < 1f)
                {
                    _openAmount = Mathf.MoveTowards(_openAmount, 1f, Time.deltaTime / _slideDuration);
                    yield return null;
                }
            }
            _transitioning = false;
            _transition = null;
            if (index >= 0) _overlay.NotifyDrawerOpened();
        }

        /// <summary>
        /// visible에 따라 서랍을 즉시 접고 UI 표시 상태를 변경한다.
        /// 조작 취소는 기존 호출자가 먼저 처리하며 재고와 회수 코루틴은 유지한다.
        /// </summary>
        public void ResetDrawers(bool visible)
        {
            _workshopVisible = visible;
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _transitioning = false;
            _selected = -1;
            _openAmount = 0f;
            if (_drawerRoot != null)
            {
                _drawerRoot.gameObject.SetActive(visible);
                _clip.gameObject.SetActive(false);
                _listInput.interactable = false;
            }
        }

        /// <summary>
        /// 현재 펼침 중인 목록의 화면상 중심을 반환한다.
        /// 모두 접혔거나 숨겨졌으면 화면 왼쪽 안쪽 세로 중앙을 반환한다.
        /// </summary>
        public Vector2 GetReturnDestination()
        {
            return _workshopVisible && _selected >= 0 && _openAmount > 0f
                ? RectTransformUtility.WorldToScreenPoint(null, _clip.TransformPoint(_clip.rect.center))
                : new Vector2(24f, Screen.height * 0.5f);
        }

        void OnDisable()
        {
            ResetDrawers(false);
        }

        void OnDestroy()
        {
            for (int i = 0; i < Mathf.Min(7, _labels.Count); i++) _labels[i].Clicked -= HandleDrawerClicked;
        }

        /// <summary>기존 panel의 LED 또는 이미지 컨트롤을 trayParent 안의 width 폭 목록으로 이전한다. 편집 기능을 재생성하지 않고 독립 ScrollRect를 반환한다.</summary>
        public ScrollRect EmbedTrayEditor(RectTransform panel, RectTransform trayParent, float width)
        {
            return ConfigureEditorPanel(panel, trayParent, width);
        }

        /// <summary>
        /// panel에서 LED 또는 이미지 조작 항목을 추출해 공통 왼쪽 목록에 배치한다.
        /// trayParent와 trayWidth가 있으면 새 트레이에 배치하고, 없으면 기존 목록의 활성 상태를 연결한다. 생성한 ScrollRect를 반환한다.
        /// </summary>
        private ScrollRect ConfigureEditorPanel(RectTransform panel, RectTransform trayParent = null, float trayWidth = 0f)
        {
            float width = trayParent != null ? trayWidth : _width;
            RectTransform host = new GameObject(panel.name + "CatalogHost", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            host.SetParent(trayParent != null ? trayParent : _menuRoot, false);
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.up;
            host.pivot = Vector2.up;
            host.anchoredPosition = new Vector2(24f, -24f);
            host.sizeDelta = new Vector2(width, -24f - _bottomMargin);
            host.GetComponent<Image>().color = trayParent != null && panel.GetComponent<KeyboardLEDPanelUI>() != null
                ? GetComponent<KeyboardEditOverlayUI>().CatalogCardColor : new Color32(37, 52, 68, 255);
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
                led.EmbedControls(content, width - 24f);
            else
                panel = GetComponent<KeyboardCustomizationTestUI>().EmbedControls(content, width - 24f);
            content.sizeDelta = new Vector2(0f, panel.rect.height * panel.localScale.y);
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
            if (trayParent == null) _editorPanels.Add(panel, scroll);
            else
            {
                host.anchorMin = Vector2.zero;
                host.anchorMax = Vector2.one;
                host.offsetMin = Vector2.zero;
                host.offsetMax = new Vector2(0f, -60f);
            }
            return scroll;
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

        /// <summary>scroll의 기존 보강판 탭·카드·드래그를 parent 트레이의 세로 목록으로 이전한다. 기존 컨트롤과 이벤트를 유지하고 재고나 조립 상태는 변경하지 않는다.</summary>
        public void EmbedTrayCatalog(ScrollRect scroll, RectTransform parent)
        {
            _overlay = GetComponent<KeyboardEditOverlayUI>();
            scroll.transform.SetParent(parent, false);
            ConfigureCatalog(scroll);
            _configured.Add(scroll);
            RectTransform panel = (RectTransform)scroll.transform;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(0f, 100f);
            panel.offsetMax = new Vector2(0f, -12f);
            foreach (GridLayoutGroup grid in panel.GetComponentsInChildren<GridLayoutGroup>(true))
                grid.cellSize = new Vector2(Mathf.Max(1f, (parent.rect.width - 48f) * 0.5f), grid.cellSize.y);
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
                grid.cellSize = new Vector2((_width - 48f) * 0.5f,
                    _overlay.AssemblyCatalogEnabled && content.GetComponentInChildren<WorkshopPcbCardDrag>(true) != null ? 148f : 128f);
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
