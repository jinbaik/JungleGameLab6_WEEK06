using System;

using UnityEngine;

public class RoundDebugHud : MonoBehaviour
{
    private const int HUD_WIDTH = 360;
    private const int HUD_HEIGHT = 185;

    [Header("Dependencies")]
    [SerializeField] private GameManager _gameManager;

    [Header("HUD Display Settings")]
    [SerializeField] private bool _showHud = true;
    [SerializeField] private int _fontSize = 16;
    [SerializeField] private Vector2 _hudPosition = new Vector2(20f, 20f);

    [Header("Runtime Stats")]
    private int _clearedRoundCount;
    private float _lastDefeatTime;
    private bool _isTimeOut;

    void Start()
    {
        if (_gameManager == null)
        {
            _gameManager = GameManager.Instance;
        }

        if (_gameManager != null)
        {
            _gameManager.RoundCleared += HandleRoundCleared;
            _gameManager.RoundTimeOut += HandleRoundTimeOut;
        }
    }

    void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.RoundCleared -= HandleRoundCleared;
            _gameManager.RoundTimeOut -= HandleRoundTimeOut;
        }
    }

    void OnGUI()
    {
        if (!_showHud)
        {
            return;
        }

        if (_gameManager == null)
        {
            _gameManager = GameManager.Instance;
            if (_gameManager == null)
            {
                return;
            }
        }

        DrawDebugHud();
    }

    /// <summary>
    /// 라운드 타이머 및 처치 소요 시간 디버그 정보를 화면에 렌더링한다.
    /// _gameManager의 남은 시간, 경과 시간, 상태 값을 읽어 GUI 박스와 레이블로 화면에 출력한다.
    /// </summary>
    private void DrawDebugHud()
    {
        float posX = _hudPosition.x;
        float posY = Screen.height - HUD_HEIGHT - _hudPosition.y;

        Rect boxRect = new Rect(posX, posY, HUD_WIDTH, HUD_HEIGHT);
        GUI.Box(boxRect, "=== 라운드 & 타이머 디버그 ===");

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = _fontSize,
            fontStyle = FontStyle.Bold
        };

        float startY = posY + 30f;
        float lineHeight = _fontSize + 8f;

        // 현재 라운드 상태 결정 및 색상 설정
        string statusText;
        Color statusColor;

        if (_isTimeOut)
        {
            statusText = "제한 시간 초과";
            statusColor = Color.red;
        }
        else if (_gameManager.IsTimerRunning)
        {
            statusText = "전투 진행 중";
            statusColor = Color.green;
        }
        else
        {
            statusText = "키보드 스폰 / 대기 중";
            statusColor = Color.yellow;
        }

        labelStyle.normal.textColor = statusColor;
        GUI.Label(new Rect(posX + 15f, startY, HUD_WIDTH - 30f, lineHeight), $"상태: {statusText}", labelStyle);

        labelStyle.normal.textColor = Color.white;
        startY += lineHeight;
        GUI.Label(new Rect(posX + 15f, startY, HUD_WIDTH - 30f, lineHeight), $"진행 대상: 키보드 #{_clearedRoundCount + 1}", labelStyle);

        // 남은 제한 시간 표시 (10초 미만 경고 색상)
        startY += lineHeight;
        float remaining = _gameManager.RemainingTime;
        float limit = _gameManager.RoundTimeLimit;
        labelStyle.normal.textColor = remaining <= 10f && _gameManager.IsTimerRunning ? Color.red : Color.cyan;
        GUI.Label(new Rect(posX + 15f, startY, HUD_WIDTH - 30f, lineHeight), $"제한 시간: {remaining:F1}초 / {limit:F1}초", labelStyle);

        // 이번 라운드 소요 시간 표시
        startY += lineHeight;
        labelStyle.normal.textColor = Color.white;
        float elapsed = _gameManager.ElapsedRoundTime;
        GUI.Label(new Rect(posX + 15f, startY, HUD_WIDTH - 30f, lineHeight), $"처치 소요 시간: {elapsed:F2}초", labelStyle);

        // 이전 라운드 기록 표시
        startY += lineHeight;
        labelStyle.normal.textColor = Color.yellow;
        string lastDefeatStr = _lastDefeatTime > 0f ? $"{_lastDefeatTime:F2}초" : "-";
        GUI.Label(new Rect(posX + 15f, startY, HUD_WIDTH - 30f, lineHeight), $"직전 처치 기록: {lastDefeatStr} (누적 처치: {_clearedRoundCount}개)", labelStyle);
    }

    /// <summary>
    /// 라운드 완파 시 쓰러트린 시간을 기록하고 상태를 갱신한다.
    /// defeatTime 소요 시간을 입력받아 _lastDefeatTime과 _clearedRoundCount를 갱신한다.
    /// </summary>
    private void HandleRoundCleared(float defeatTime)
    {
        _lastDefeatTime = defeatTime;
        _clearedRoundCount++;
        _isTimeOut = false;
    }

    /// <summary>
    /// 라운드 제한 시간 초과 시 상태를 갱신한다.
    /// _isTimeOut 플래그를 true로 변경한다.
    /// </summary>
    private void HandleRoundTimeOut()
    {
        _isTimeOut = true;
    }
}
