using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Logging;
using Game.Session;
using Game.Settings;

public sealed class MonitorSettingsAppView : MonoBehaviour, IPointerClickHandler
{
    [Header("Settings")]
    [SerializeField] private GameSession _gameSession;
    private GameSettings _settings;

    [Header("Popup")]
    [SerializeField] private CanvasGroup _popupCanvasGroup;
    [SerializeField] private CanvasGroup _windowCanvasGroup;
    [SerializeField] private CanvasGroup _quitConfirmationCanvasGroup;
    [SerializeField] private MonitorSettingsButton _closeButton;
    [SerializeField] private MonitorSettingsButton _quitButton;
    [SerializeField] private MonitorSettingsButton _confirmQuitButton;
    [SerializeField] private MonitorSettingsButton _cancelQuitButton;
    [SerializeField] private MonitorSettingsButton _logFolderButton;
    private bool _isInputEnabled;
    private bool _isQuitting;
    public bool IsOpen { get; private set; }
    public bool IsQuitConfirmationOpen { get; private set; }
    public event Action OpenRequested;
    public event Action<bool> OpenStateChanged;

    [Header("Controls")]
    [SerializeField] private MonitorSettingsSlider _sfxSlider;
    [SerializeField] private MonitorSettingsSlider _shakeSlider;
    [SerializeField] private Text _sfxPercentage;
    [SerializeField] private Text _shakePercentage;

    void OnEnable()
    {
        _settings = _gameSession.Settings;
        _settings.ValuesChanged += RefreshValues;
        _sfxSlider.ValueChanged += SetSfxVolume;
        _shakeSlider.ValueChanged += SetShakeStrength;
        _closeButton.Clicked += Close;
        _quitButton.Clicked += RequestQuit;
        _confirmQuitButton.Clicked += ConfirmQuit;
        _cancelQuitButton.Clicked += CancelQuit;
        _logFolderButton.Clicked += KeyboardInputLogger.OpenLogFolder;
        RefreshValues();
        RefreshCanvasState();
    }

    void OnDisable()
    {
        SetOpen(false);
        _settings.Save();
        _settings.ValuesChanged -= RefreshValues;
        _sfxSlider.ValueChanged -= SetSfxVolume;
        _shakeSlider.ValueChanged -= SetShakeStrength;
        _closeButton.Clicked -= Close;
        _quitButton.Clicked -= RequestQuit;
        _confirmQuitButton.Clicked -= ConfirmQuit;
        _cancelQuitButton.Clicked -= CancelQuit;
        _logFolderButton.Clicked -= KeyboardInputLogger.OpenLogFolder;
    }

    void Update()
    {
        if (!IsOpen || !_isInputEnabled || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (IsQuitConfirmationOpen) CancelQuit();
        else Close();
    }

    /// <summary>
    /// eventData의 왼쪽 더블 클릭으로 설정 앱 열기를 요청한다.
    /// 모니터에서 앱 입력을 허용한 경우에만 OpenRequested를 발생시킨다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isInputEnabled && !IsOpen && eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2)
        {
            AudioManager.instance.PlayButtonSound();
            OpenRequested?.Invoke();
        }
    }

    /// <summary>
    /// isOpen에 따라 설정 팝업의 표시 상태를 변경한다.
    /// 닫을 때 설정을 저장하고 종료 확인을 해제하며 OpenStateChanged로 모니터 입력을 갱신한다.
    /// </summary>
    public void SetOpen(bool isOpen)
    {
        if (IsOpen == isOpen || (isOpen && (!_isInputEnabled || _isQuitting))) return;
        IsOpen = isOpen;
        IsQuitConfirmationOpen = false;
        if (isOpen) RefreshValues();
        else _settings.Save();
        RefreshCanvasState();
        OpenStateChanged?.Invoke(isOpen);
    }

    /// <summary>
    /// enabled로 앱과 팝업의 입력 허용 상태를 변경한다.
    /// 팝업 및 종료 확인의 CanvasGroup 입력 권한을 갱신한다.
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        _isInputEnabled = enabled;
        RefreshCanvasState();
    }

    /// <summary>
    /// 현재 설정 팝업을 닫는다.
    /// 저장과 표시 해제를 공통 열림 상태 변경에 전달한다.
    /// </summary>
    private void Close()
    {
        SetOpen(false);
    }

    /// <summary>
    /// value의 백분율을 효과음 설정 비율로 변환한다.
    /// 변경값을 즉시 음량과 표시 상태에 반영한다.
    /// </summary>
    private void SetSfxVolume(float value)
    {
        _settings.SetSfxVolume(value / 100f);
    }

    /// <summary>
    /// value의 백분율을 카메라 흔들림 설정 비율로 변환한다.
    /// 변경값을 즉시 카메라 강도와 표시 상태에 반영한다.
    /// </summary>
    private void SetShakeStrength(float value)
    {
        _settings.SetCameraShakeStrength(value / 100f);
    }

    /// <summary>
    /// 현재 설정 비율을 두 슬라이더와 백분율 텍스트로 표시한다.
    /// 알림 없는 값 갱신으로 슬라이더 이벤트가 반복 발생하지 않도록 한다.
    /// </summary>
    private void RefreshValues()
    {
        _sfxSlider.SetValueWithoutNotify(_settings.SfxVolume * 100f);
        _shakeSlider.SetValueWithoutNotify(_settings.CameraShakeStrength * 100f);
        _sfxPercentage.text = $"{Mathf.RoundToInt(_settings.SfxVolume * 100f)}%";
        _shakePercentage.text = $"{Mathf.RoundToInt(_settings.CameraShakeStrength * 100f)}%";
    }

    /// <summary>
    /// 현재 열림과 입력 상태로 설정 창과 종료 확인 창의 표시를 갱신한다.
    /// 종료 확인 중에는 설정 조작을 잠그고 확인 창만 입력을 받도록 한다.
    /// </summary>
    private void RefreshCanvasState()
    {
        bool input = IsOpen && _isInputEnabled && !_isQuitting;
        _popupCanvasGroup.alpha = IsOpen ? 1f : 0f;
        _popupCanvasGroup.interactable = input;
        _popupCanvasGroup.blocksRaycasts = IsOpen;
        _windowCanvasGroup.interactable = input && !IsQuitConfirmationOpen;
        _quitConfirmationCanvasGroup.alpha = IsQuitConfirmationOpen ? 1f : 0f;
        _quitConfirmationCanvasGroup.interactable = input && IsQuitConfirmationOpen;
        _quitConfirmationCanvasGroup.blocksRaycasts = IsQuitConfirmationOpen;
    }

    /// <summary>
    /// 열린 설정 창에서 종료 확인 창을 표시한다.
    /// 설정 창의 조작을 잠그고 종료 확인 버튼의 입력을 허용한다.
    /// </summary>
    private void RequestQuit()
    {
        if (!IsOpen || !_isInputEnabled || _isQuitting) return;
        IsQuitConfirmationOpen = true;
        RefreshCanvasState();
    }

    /// <summary>
    /// 종료 확인을 취소하고 설정 창의 조작을 복원한다.
    /// 설정 창의 표시와 현재 설정 값을 유지한다.
    /// </summary>
    private void CancelQuit()
    {
        IsQuitConfirmationOpen = false;
        RefreshCanvasState();
    }

    /// <summary>
    /// 종료 확인 상태에서 설정을 저장하고 게임 실행을 종료한다.
    /// 에디터에서는 Play Mode만 종료하며 빌드에서는 Application.Quit를 호출한다.
    /// </summary>
    private void ConfirmQuit()
    {
        if (!IsQuitConfirmationOpen || !_isInputEnabled || _isQuitting) return;
        _isQuitting = true;
        _settings.Save();
        SetOpen(false);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
