using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using Game.Logging;
using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed partial class WorkshopViewController
    {
        private Canvas _requestChecklistCanvas;
        private RectTransform _requestChecklistPanel;
        private Text _requestChecklistTitle;
        private Text _requestChecklistBody;
        private Text _requestChecklistStatus;
        private Image _requestChecklistProgress;
        private RectTransform _requestChecklistProgressTrack;
        private Rigidbody _requestChecklistKeyboard;
        private string _requestChecklistId;

        [Header("Request Checklist Layout")]
        [SerializeField, Min(0f)] private float _requestChecklistLeftOffset = 56.6667f;
        [SerializeField, Min(0f)] private float _requestChecklistTopOffset = 51.6667f;
        [SerializeField, Min(220f)] private float _requestChecklistPanelWidth = 320f;
        [SerializeField, Min(12)] private int _requestChecklistTitleFontSize = 18;
        [SerializeField, Min(12)] private int _requestChecklistBodyFontSize = 16;

        [Header("Request Checklist Fade")]
        [SerializeField, Min(0f)] private float _requestChecklistFadeDistance = 140f;
        [SerializeField, Range(0f, 1f)] private float _requestChecklistMinimumOpacity = 0.25f;
        [SerializeField, Min(0f)] private float _requestChecklistFadeDuration = 0.2f;
        private CanvasGroup _requestChecklistGroup;
        private readonly Vector3[] _requestChecklistCorners = new Vector3[4];
        private Vector2 _requestChecklistCanvasSize;
        private float _requestChecklistCanvasScale;

        /// <summary>
        /// 같은 씬의 기존 WorkshopHUD Canvas 아래에 표시용 체크리스트를 만든다.
        /// 화면 캔버스와 기존 글꼴을 재사용하며 생성한 패널은 입력을 가로채지 않는다.
        /// </summary>
        private void InitializeRequestChecklist()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas.gameObject.scene != gameObject.scene || canvas.name != "WorkshopHUD") continue;
                _requestChecklistCanvas = canvas;
                break;
            }
            if (_requestChecklistCanvas == null) return;

            Text template = _requestChecklistCanvas.GetComponentInChildren<Text>(true);
            Font font = template != null ? template.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            GameObject panel = new GameObject("ActiveRequestChecklist", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(_requestChecklistCanvas.transform, false);
            _requestChecklistPanel = (RectTransform)panel.transform;
            _requestChecklistPanel.anchorMin = new Vector2(0f, 1f);
            _requestChecklistPanel.anchorMax = new Vector2(0f, 1f);
            _requestChecklistPanel.pivot = new Vector2(0f, 1f);
            _requestChecklistPanel.anchoredPosition = new Vector2(_requestChecklistLeftOffset, -_requestChecklistTopOffset);
            _requestChecklistPanel.sizeDelta = new Vector2(_requestChecklistPanelWidth, 170f);

            Image background = panel.GetComponent<Image>();
            _requestChecklistGroup = panel.GetComponent<CanvasGroup>();
            _requestChecklistGroup.interactable = false;
            _requestChecklistGroup.blocksRaycasts = false;
            background.color = new Color32(22, 30, 39, 245);
            background.raycastTarget = false;
            Outline border = panel.AddComponent<Outline>();
            border.effectColor = new Color32(135, 170, 184, 50);
            border.effectDistance = new Vector2(1f, -1f);
            _requestChecklistStatus = CreateRequestChecklistText(_requestChecklistPanel, "Status", font,
                13, FontStyle.Bold, new Color32(145, 201, 209, 255));
            Image track = CreateRequestChecklistImage(_requestChecklistPanel, "ProgressTrack", new Color32(49, 62, 73, 255));
            _requestChecklistProgressTrack = track.rectTransform;
            _requestChecklistProgress = CreateRequestChecklistImage(track.transform, "Progress", new Color32(118, 211, 181, 255));
            _requestChecklistProgress.rectTransform.anchorMax = Vector2.one;
            _requestChecklistProgress.rectTransform.offsetMin = Vector2.zero;
            _requestChecklistProgress.rectTransform.offsetMax = Vector2.zero;
            _requestChecklistTitle = CreateRequestChecklistText(_requestChecklistPanel, "Title", font,
                _requestChecklistTitleFontSize, FontStyle.Bold, new Color32(235, 240, 243, 255));
            _requestChecklistTitle.rectTransform.anchoredPosition = new Vector2(20f, -16f);
            _requestChecklistTitle.rectTransform.sizeDelta = new Vector2(_requestChecklistPanelWidth - 40f, 60f);
            _requestChecklistBody = CreateRequestChecklistText(_requestChecklistPanel, "Requirements", font,
                _requestChecklistBodyFontSize, FontStyle.Normal, Color.white);
            _requestChecklistBody.lineSpacing = 1.15f;
            _requestChecklistBody.resizeTextForBestFit = false;
            _requestChecklistBody.rectTransform.anchoredPosition = new Vector2(20f, -90f);
            _requestChecklistBody.rectTransform.sizeDelta = new Vector2(_requestChecklistPanelWidth - 40f, 120f);
            panel.SetActive(false);
            FitRequestChecklistToCanvas();
        }

        /// <summary>
        /// 집중 편집 상태에서 체크리스트와 가상 커서의 화면 가장자리 거리를 불투명도로 반영한다.
        /// 일반 뷰나 기존 규칙으로 숨겨진 패널은 원래 불투명도로 복구하며 표시 상태는 변경하지 않는다.
        /// </summary>
        private void UpdateRequestChecklistOpacity()
        {
            if (_requestChecklistGroup == null) return;
            Vector2 canvasSize = ((RectTransform)_requestChecklistCanvas.transform).rect.size;
            if (canvasSize != _requestChecklistCanvasSize
                || !Mathf.Approximately(_requestChecklistCanvas.scaleFactor, _requestChecklistCanvasScale))
            {
                FitRequestChecklistToCanvas();
                RefreshRequestChecklist();
            }
            if (!_requestChecklistPanel.gameObject.activeInHierarchy)
            {
                _requestChecklistGroup.alpha = 1f;
                return;
            }

            float target = IsKeyboardEditing && CanShowControlHints
                ? WorkshopControlHints.GetProximityOpacity(_requestChecklistPanel, InGameCursor.Position,
                    _requestChecklistFadeDistance, _requestChecklistMinimumOpacity, _requestChecklistCorners)
                : 1f;
            float step = _requestChecklistFadeDuration <= 0f ? 1f
                : Time.unscaledDeltaTime / _requestChecklistFadeDuration;
            _requestChecklistGroup.alpha = Mathf.MoveTowards(_requestChecklistGroup.alpha, target, step);
        }

        /// <summary>
        /// Canvas 좌표로 체크리스트 위치를 적용하고 scaleFactor에 맞춰 패널 폭과 글자 크기를 조정한다.
        /// 화면 크기 변경에도 왼쪽 위 앵커와 텍스트 레이아웃을 유지한다.
        /// </summary>
        private void FitRequestChecklistToCanvas()
        {
            RectTransform canvasRect = (RectTransform)_requestChecklistCanvas.transform;
            float scaleFactor = Mathf.Max(0.001f, _requestChecklistCanvas.scaleFactor);
            float unitPerPixel = 1f / scaleFactor;
            float margin = 20f * unitPerPixel;
            float canvasWidth = canvasRect.rect.width;
            float panelWidth = Mathf.Min(_requestChecklistPanelWidth * unitPerPixel,
                Mathf.Max(1f, canvasWidth - margin * 2f));
            float maxLeftOffset = Mathf.Max(0f, canvasWidth - panelWidth - margin);
            float leftOffset = Mathf.Clamp(_requestChecklistLeftOffset, 0f, maxLeftOffset);
            _requestChecklistPanel.anchoredPosition = new Vector2(leftOffset, -_requestChecklistTopOffset);
            _requestChecklistPanel.sizeDelta = new Vector2(panelWidth, _requestChecklistPanel.sizeDelta.y);

            float horizontalPadding = 16f * unitPerPixel;
            _requestChecklistStatus.fontSize = Mathf.RoundToInt(13f * unitPerPixel);
            _requestChecklistStatus.rectTransform.anchoredPosition = new Vector2(horizontalPadding, -12f * unitPerPixel);
            _requestChecklistStatus.rectTransform.sizeDelta = new Vector2(panelWidth - horizontalPadding * 2f, 22f * unitPerPixel);
            _requestChecklistProgressTrack.anchoredPosition = new Vector2(horizontalPadding, -32f * unitPerPixel);
            _requestChecklistProgressTrack.sizeDelta = new Vector2(panelWidth - horizontalPadding * 2f, 3f * unitPerPixel);
            _requestChecklistTitle.fontSize = Mathf.RoundToInt(_requestChecklistTitleFontSize * unitPerPixel);
            _requestChecklistTitle.rectTransform.anchoredPosition = new Vector2(horizontalPadding, -44f * unitPerPixel);
            _requestChecklistTitle.rectTransform.sizeDelta = new Vector2(panelWidth - horizontalPadding * 2f,
                60f * unitPerPixel);
            _requestChecklistBody.fontSize = Mathf.RoundToInt(_requestChecklistBodyFontSize * unitPerPixel);
            _requestChecklistBody.rectTransform.anchoredPosition = new Vector2(horizontalPadding, -90f * unitPerPixel);
            _requestChecklistBody.rectTransform.sizeDelta = new Vector2(panelWidth - horizontalPadding * 2f,
                _requestChecklistBody.rectTransform.sizeDelta.y);
            _requestChecklistCanvasSize = canvasRect.rect.size;
            _requestChecklistCanvasScale = _requestChecklistCanvas.scaleFactor;
        }

        /// <summary>
        /// parent 아래에 name과 color를 적용한 장식 이미지를 생성해 반환한다.
        /// 왼쪽 위 기준으로 배치하며 입력을 가로채지 않는다.
        /// </summary>
        private static Image CreateRequestChecklistImage(Transform parent, string name, Color color)
        {
            Image image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.up;
            image.rectTransform.pivot = Vector2.up;
            return image;
        }

        /// <summary>
        /// parent에 표시용 Text를 생성하고 font, size, style, color와 이름을 적용한다.
        /// 생성된 텍스트는 입력 Raycast를 받지 않으며 반환된 Text의 배치는 호출자가 정한다.
        /// </summary>
        private static Text CreateRequestChecklistText(Transform parent, string name, Font font, int size,
            FontStyle style, Color color)
        {
            GameObject label = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(parent, false);
            Text text = label.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            return text;
        }

        /// <summary>
        /// 현재 의뢰의 모든 활성 실제 키보드를 기존 검수 함수로 비교해 체크리스트를 갱신한다.
        /// 전체 충족 키보드를 우선하고, 없으면 가장 많은 조건을 충족한 키보드 하나만 표시한다.
        /// </summary>
        private void RefreshRequestChecklist()
        {
            if (_requestChecklistPanel == null) return;
            KeyboardRequestState request = KeyboardRequestRuntime.ActiveRequest;
            _requestChecklistPanel.gameObject.SetActive(request != null);
            if (request == null)
            {
                _requestChecklistKeyboard = null;
                _requestChecklistId = null;
                return;
            }
            if (_requestChecklistId != request.Definition.RequestId)
            {
                _requestChecklistId = request.Definition.RequestId;
                _requestChecklistKeyboard = null;
            }

            MonitorMailMessage mail = request.Definition.InitialMail;
            string identity = mail != null && !string.IsNullOrWhiteSpace(mail.Sender)
                ? mail.Sender : request.Definition.DisplayName;
            string subject = mail != null && !string.IsNullOrWhiteSpace(mail.Subject)
                ? $" · {mail.Subject}" : string.Empty;
            _requestChecklistTitle.text = $"{identity}{subject}";

            Rigidbody selectedBody = null;
            KeyboardRequestInspection selectedInspection = null;
            int bestScore = -1;
            bool selectedComplete = false;
            IReadOnlyList<KeyboardPickupTarget> targets = KeyboardPickupTarget.ActiveTargets;
            for (int index = 0; index < targets.Count; index++)
            {
                KeyboardPickupTarget pickup = targets[index];
                if (pickup == null || !pickup.isActiveAndEnabled || !pickup.gameObject.activeInHierarchy
                    || pickup.gameObject.scene != gameObject.scene || pickup.IsExcludedFromRequests)
                    continue;
                Rigidbody body = pickup.GetComponent<Rigidbody>();
                if (body == null) continue;

                KeyboardRequestInspection inspection = null;
                try
                {
                    inspection = KeyboardRequestInspection.Inspect(body,
                        body.GetComponent<KeyboardAssemblyState>(), request.Definition);
                }
                catch (InvalidOperationException)
                {
                    // 설정 오류는 요구 조건 미충족으로 확정하지 않고 미완료 표시로 남긴다.
                }

                int score = CountSatisfiedRequestRows(inspection, request.Definition);
                bool complete = inspection != null && inspection.Passed;
                if (complete)
                {
                    if (!selectedComplete || body == _requestChecklistKeyboard)
                    {
                        selectedBody = body;
                        selectedInspection = inspection;
                    }
                    selectedComplete = true;
                    continue;
                }
                if (selectedComplete) continue;
                if (score > bestScore || (score == bestScore && body == _requestChecklistKeyboard))
                {
                    selectedBody = body;
                    selectedInspection = inspection;
                    bestScore = score;
                }
            }
            _requestChecklistKeyboard = selectedBody;

            var lines = new List<string>();
            if (selectedBody == null)
                lines.Add(FormatRequestChecklistLine("검사할 키보드가 없습니다", false));
            else if (selectedInspection == null)
                lines.Add("<color=#FFD27A>요구 조건 연결을 확인할 수 없습니다</color>");

            KeyboardRequestDefinition definition = request.Definition;
            if (definition.RequiresCompleteAssembly)
            {
                bool glass = definition.RequiresGlassSurfaces;
                lines.Add(FormatRequestChecklistLine(glass ? "하부 하우징: 유리 재질"
                    : "하부 하우징: 설치", selectedInspection != null && !selectedInspection.MissingLower
                        && (!glass || !selectedInspection.IncorrectLowerMaterial)));
                lines.Add(FormatRequestChecklistLine("PCB: 설치", selectedInspection != null && !selectedInspection.MissingPcb));
                lines.Add(FormatRequestChecklistLine("보강판: 설치", selectedInspection != null && !selectedInspection.MissingPlate));
            }
            if (definition.RequiresAllRequiredSwitchSlots)
            {
                string switchName = definition.RequiredSwitchType switch
                {
                    SwitchType.Red => "적축",
                    SwitchType.Sans => "샌즈 축",
                    _ => definition.RequiredSwitchType.ToString()
                };
                bool complete = selectedInspection != null && selectedInspection.MissingSwitches == 0
                    && selectedInspection.IncorrectSwitches == 0;
                lines.Add(FormatRequestChecklistLine($"스위치: 전체 {switchName}", complete));
            }
            if (definition.RequiresCompleteAssembly)
            {
                bool glass = definition.RequiresGlassSurfaces;
                lines.Add(FormatRequestChecklistLine(glass ? "상부 하우징: 유리 재질"
                    : "상부 하우징: 설치", selectedInspection != null && !selectedInspection.MissingUpper
                        && (!glass || !selectedInspection.IncorrectUpperMaterial)));
                lines.Add(FormatRequestChecklistLine(glass ? "키캡: 전체 유리 재질"
                    : "키캡: 전체 필수 슬롯 설치",
                    selectedInspection != null && selectedInspection.MissingKeycaps == 0
                        && (!glass || selectedInspection.IncorrectGlassKeycaps == 0)));
            }
            if (!definition.RequiresAllRequiredSwitchSlots && !definition.RequiresCompleteAssembly
                && !definition.RequiresGlassSurfaces)
                lines.Add(FormatRequestChecklistLine("키보드 본체",
                    selectedInspection != null && selectedInspection.Passed));

            _requestChecklistBody.text = string.Join("\n", lines);
            float unitPerPixel = 1f / Mathf.Max(0.001f, _requestChecklistCanvas.scaleFactor);
            float panelWidth = _requestChecklistPanel.sizeDelta.x;
            float contentWidth = panelWidth - 32f * unitPerPixel;
            _requestChecklistTitle.rectTransform.sizeDelta = new Vector2(contentWidth, 60f * unitPerPixel);
            float titleHeight = _requestChecklistTitle.preferredHeight;
            _requestChecklistTitle.rectTransform.sizeDelta = new Vector2(contentWidth, titleHeight);
            float bodyTop = 44f * unitPerPixel + titleHeight + 12f * unitPerPixel;
            _requestChecklistBody.rectTransform.anchoredPosition = new Vector2(16f * unitPerPixel, -bodyTop);
            _requestChecklistBody.rectTransform.sizeDelta = new Vector2(contentWidth, 0f);
            float bodyHeight = _requestChecklistBody.preferredHeight;
            _requestChecklistBody.rectTransform.sizeDelta = new Vector2(contentWidth, bodyHeight);
            _requestChecklistPanel.sizeDelta = new Vector2(panelWidth, bodyTop + bodyHeight + 16f * unitPerPixel);
            int total = (definition.RequiresCompleteAssembly ? 5 : 0)
                + (definition.RequiresAllRequiredSwitchSlots ? 1 : 0);
            if (total == 0) total = 1;
            int satisfied = CountSatisfiedRequestRows(selectedInspection, definition);
            _requestChecklistStatus.text = selectedComplete
                ? $"의뢰 제작  /  제출 준비 완료    {satisfied:00} / {total:00}"
                : $"의뢰 제작  /  진행 중    {satisfied:00} / {total:00}";
            _requestChecklistProgress.rectTransform.anchorMax = new Vector2((float)satisfied / total, 1f);
        }

        /// <summary>
        /// inspection에서 현재 의뢰 체크리스트 항목 중 충족한 수를 계산한다.
        /// 표시할 조건 수를 기준으로 후보 키보드를 비교하며 요청과 검수 결과는 변경하지 않는다.
        /// </summary>
        private static int CountSatisfiedRequestRows(KeyboardRequestInspection inspection,
            KeyboardRequestDefinition definition)
        {
            if (inspection == null) return 0;
            if (!definition.RequiresAllRequiredSwitchSlots && !definition.RequiresCompleteAssembly
                && !definition.RequiresGlassSurfaces)
                return inspection.Passed ? 1 : 0;

            int count = 0;
            if (definition.RequiresAllRequiredSwitchSlots
                && inspection.MissingSwitches == 0 && inspection.IncorrectSwitches == 0)
                count++;
            if (!definition.RequiresCompleteAssembly) return count;

            bool glass = definition.RequiresGlassSurfaces;
            if (inspection.MissingKeycaps == 0 && (!glass || inspection.IncorrectGlassKeycaps == 0)) count++;
            if (!inspection.MissingUpper && (!glass || !inspection.IncorrectUpperMaterial)) count++;
            if (!inspection.MissingLower && (!glass || !inspection.IncorrectLowerMaterial)) count++;
            if (!inspection.MissingPcb) count++;
            if (!inspection.MissingPlate) count++;
            return count;
        }

        /// <summary>
        /// label과 complete 상태로 조건 한 줄을 색상 및 체크 표시와 함께 반환한다.
        /// 완료는 민트색 체크, 미완료는 붉은색으로 표현하며 조건 진행 상태는 변경하지 않는다.
        /// </summary>
        private static string FormatRequestChecklistLine(string label, bool complete)
        {
            string color = complete ? "#8AD9BA" : "#FF7777";
            string check = complete ? "✓" : "·";
            return $"<color={color}>{check}   {label}</color>";
        }

        /// <summary>
        /// 관찰된 customization이 실제 키보드에 속할 때 체크리스트를 갱신한다.
        /// observation.Target의 pickup 표시를 확인하며 UI 미리보기 이벤트는 무시한다.
        /// </summary>
        private void HandleChecklistCustomizationObserved(CustomizationObservation observation)
        {
            if (observation.Target != null && observation.Target.GetComponent<KeyboardPickupTarget>() != null)
                RefreshRequestChecklist();
        }
    }
}

