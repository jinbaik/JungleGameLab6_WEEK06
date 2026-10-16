using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class KeyboardGoalAppView : MonoBehaviour, IPointerClickHandler
{
    [Header("Progress")]
    [Tooltip("Main 씬의 SmashModeTrigger에 있는 진행 컴포넌트를 연결합니다.")]
    [SerializeField] private KeyboardCompletionProgress _completionProgress;

    [Header("Popup")]
    [SerializeField] private CanvasGroup _popupCanvasGroup;
    [SerializeField] private Text _completedText;
    [SerializeField] private Text _remainingText;
    [SerializeField] private Text _statusText;
    [SerializeField] private Slider _progressFill;
    [SerializeField] private MonitorGoalCloseButton _closeButton;
    private bool _isInputEnabled;
    public bool IsOpen { get; private set; }
    public event Action OpenRequested;
    public event Action<bool> OpenStateChanged;

    void OnEnable()
    {
        if (_completionProgress != null)
            _completionProgress.ProgressChanged += RefreshProgress;
        _closeButton.Clicked += Close;
        RefreshProgress();
    }

    void OnDisable()
    {
        if (_completionProgress != null)
            _completionProgress.ProgressChanged -= RefreshProgress;
        _closeButton.Clicked -= Close;
        SetOpen(false);
    }

    /// <summary>
    /// eventData의 왼쪽 더블 클릭으로 목표 앱 열기를 요청한다.
    /// 입력 허용 상태와 팝업 상태를 확인하고 OpenRequested를 전달한다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isInputEnabled && !IsOpen && eventData.button == PointerEventData.InputButton.Left
            && eventData.clickCount == 2)
            OpenRequested?.Invoke();
    }

    /// <summary>
    /// isOpen으로 단일 목표 팝업의 표시 상태를 변경한다.
    /// 열 때 현재 진행을 읽고 CanvasGroup과 OpenStateChanged를 갱신한다.
    /// </summary>
    public void SetOpen(bool isOpen)
    {
        if (isOpen && _completionProgress == null) return;
        if (IsOpen == isOpen) return;
        IsOpen = isOpen;
        if (isOpen) RefreshProgress();
        _popupCanvasGroup.alpha = isOpen ? 1f : 0f;
        _popupCanvasGroup.interactable = isOpen && _isInputEnabled;
        _popupCanvasGroup.blocksRaycasts = _popupCanvasGroup.interactable;
        OpenStateChanged?.Invoke(isOpen);
    }

    /// <summary>
    /// isEnabled로 모니터 조작 중 목표 앱과 팝업의 입력 권한을 변경한다.
    /// 진행 연결 상태를 확인하고 팝업의 상호작용 및 레이캐스트를 갱신한다.
    /// </summary>
    public void SetInputEnabled(bool isEnabled)
    {
        _isInputEnabled = isEnabled && _completionProgress != null;
        _popupCanvasGroup.interactable = _isInputEnabled && IsOpen;
        _popupCanvasGroup.blocksRaycasts = _popupCanvasGroup.interactable;
    }

    /// <summary>
    /// 닫기 요청을 받아 목표 팝업의 표시 상태를 해제한다.
    /// 입력값 없이 SetOpen을 호출하여 화면과 열림 상태를 갱신한다.
    /// </summary>
    private void Close()
    {
        SetOpen(false);
    }

    /// <summary>
    /// 연결된 진행 컴포넌트의 초기 파괴 수와 Inspector 목표 수를 UI에 표시한다.
    /// 남은 수를 0 이상으로 계산하고 진행 막대 및 목표 달성 문구를 갱신한다.
    /// </summary>
    private void RefreshProgress()
    {
        if (_completionProgress == null) return;
        int completed = _completionProgress.CompletedInitialKeyboardCount;
        int required = _completionProgress.RequiredInitialKeyboardCount;
        int remaining = Mathf.Max(required - completed, 0);
        _completedText.text = $"파괴 완료 {completed} / {required}";
        _remainingText.text = $"남은 목표 {remaining}개";
        _statusText.text = remaining == 0
            ? "목표 달성 — 금 키보드 보상 조건 완료"
            : $"초기 키보드를 파괴하면 보상 키보드 {_completionProgress.RequiredRewardKeyboardCount}개를 받습니다.";
        _progressFill.value = Mathf.Clamp01((float)completed / required);
    }
}
