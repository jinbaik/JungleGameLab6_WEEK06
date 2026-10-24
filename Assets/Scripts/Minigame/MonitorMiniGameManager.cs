using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using Game.Logging;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class MonitorMiniGameManager : MonoBehaviour
    {
        [Header("Mini Games")]
        [SerializeField] private MonitorMiniGameDefinition[] _games = Array.Empty<MonitorMiniGameDefinition>();
        [SerializeField] private Transform _screenRoot;
        private MonitorMiniGame _currentGame;
        private double _gameStartedAt;
        private string _gameRunId;

        public IReadOnlyList<MonitorMiniGameDefinition> Games => _games;
        public MonitorMiniGame CurrentGame => _currentGame;
        public event Action<MonitorMiniGame> GameChanged;

        void Update()
        {
            if (_currentGame != null) _currentGame.Tick(Time.deltaTime);
        }

        void OnDisable()
        {
            StopGame();
        }

        /// <summary>Inspector 목록의 index에 등록된 SO를 사용해 게임 화면을 생성한다. 유효하지 않은 인덱스면 false를 반환하며 기존 실행은 유지한다.</summary>
        public bool StartGame(int index)
        {
            if (!isActiveAndEnabled || index < 0 || index >= _games.Length) return false;
            return StartGame(_games[index]);
        }

        /// <summary>definition의 화면 프리팹을 모니터 화면 영역에 생성하고 SO 데이터로 실행한다. 잘못된 구성은 false를 반환하며 동일 게임은 기존 실행을 유지한다.</summary>
        public bool StartGame(MonitorMiniGameDefinition definition)
        {
            if (!isActiveAndEnabled || definition == null || _screenRoot == null
                || definition.ScreenPrefab == null || !definition.ScreenPrefab.gameObject.activeSelf
                || !definition.ScreenPrefab.enabled || !definition.ScreenPrefab.CanRun(definition)) return false;
            if (_currentGame != null && _currentGame.Definition == definition) return true;

            StopGame();
            MonitorMiniGame game = Instantiate(definition.ScreenPrefab, _screenRoot, false);
            _currentGame = game;
            _gameStartedAt = Time.realtimeSinceStartupAsDouble;
            _gameRunId = Guid.NewGuid().ToString("N");
            game.Finished += HandleFinished;
            game.Begin(definition);
            if (_currentGame == game) GameChanged?.Invoke(game);
            return _currentGame == game;
        }

        /// <summary>현재 게임의 구독을 해제하고 실행 화면을 정리한다. 입력값과 반환값 없이 선택을 비우고 GameChanged에 null을 전달한다.</summary>
        public void StopGame()
        {
            if (_currentGame == null) return;
            MonitorMiniGame game = _currentGame;
            double endedAt = Time.realtimeSinceStartupAsDouble;
            _currentGame = null;
            game.Finished -= HandleFinished;
            game.End();
            CustomizationLogger.RecordMiniGameDuration(gameObject, game.Definition.name, _gameRunId, _gameStartedAt,
                endedAt);
            _gameRunId = null;
            game.gameObject.SetActive(false);
            Destroy(game.gameObject);
            GameChanged?.Invoke(null);
        }

        /// <summary>완료를 알린 game이 현재 게임이면 실행을 종료한다. 다른 게임의 완료 통지는 무시하며 반환값은 없다.</summary>
        private void HandleFinished(MonitorMiniGame game)
        {
            if (_currentGame == game) StopGame();
        }

        /// <summary>외부 입력 연결부의 key 누름을 현재 게임에 전달한다. 실행 중인 게임이 없으면 무시하며 키보드 자체의 타건 동작은 변경하지 않는다.</summary>
        public void SendKeyPressed(Key key)
        {
            if (_currentGame != null) _currentGame.HandleKeyPressed(key);
        }

        /// <summary>외부 입력 연결부의 key 뗌을 현재 게임에 전달한다. 실행 중인 게임이 없으면 무시하며 반환값은 없다.</summary>
        public void SendKeyReleased(Key key)
        {
            if (_currentGame != null) _currentGame.HandleKeyReleased(key);
        }

        /// <summary>외부 입력 연결부의 character를 현재 게임에 전달한다. 실행 중인 게임이 없으면 무시하며 반환값은 없다.</summary>
        public void SendTextInput(char character)
        {
            if (_currentGame != null) _currentGame.HandleTextInput(character);
        }
    }
}
