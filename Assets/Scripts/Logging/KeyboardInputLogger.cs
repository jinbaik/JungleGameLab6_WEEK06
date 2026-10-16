using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

using Game.Tournament;
using KeyboardModeling;

namespace Game.Logging
{
    [DefaultExecutionOrder(20000)]
    [DisallowMultipleComponent]
    public sealed class KeyboardInputLogger : MonoBehaviour
    {
        private const double FLUSH_INTERVAL = 1d;
        private static KeyboardInputLogger _instance;

        [Header("Runtime State")]
        private readonly Dictionary<Key, PressState> _tracked = new Dictionary<Key, PressState>();
        private readonly Dictionary<Key, long> _downCounts = new Dictionary<Key, long>();
        private readonly Dictionary<Key, int> _sampledFrames = new Dictionary<Key, int>();
        private readonly HashSet<Key> _blocked = new HashSet<Key>();
        private readonly Dictionary<KeyboardInputController, KeyboardState> _keyboards = new Dictionary<KeyboardInputController, KeyboardState>();
        private readonly ConditionalWeakTable<KeyboardInputController, KeyboardState> _effectContexts = new ConditionalWeakTable<KeyboardInputController, KeyboardState>();
        private KeyboardInputController _processingKeyboard;
        private GameManager _battleSource;
        private int _playerSpawnIndex;
        private int _enemySpawnIndex;
        private TournamentController _shopSource;
        private string _shopVisitId;
        private string _shopRunId;
        private int _shopRound;
        private string _shopBattleKind;
        private double _shopStart;
        private PlaytestCsvWriter _writer;
        private PlaytestStageRecorder _stages;
        private Keyboard _device;
        private double _nextFlush;
        private bool _ownsSession;
        private bool _closed;
        private bool _focused;
        private bool _resyncFocus;
        private bool _folderErrorReported;
        private int _focusRecoveryFrame = -1;

        /// <summary>
        /// 새 앱 실행의 정적 로거 참조를 초기화한다.
        /// 도메인 재로드가 꺼진 에디터에서도 이전 실행 참조를 남기지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() { _instance = null; }

        /// <summary>
        /// 최초 씬에 로거가 없으면 실행용 PlaytestLogger를 생성한다.
        /// 직접 테스트 씬을 실행한 경우에도 Start 이전에 기록을 준비한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLogger()
        {
            if (_instance == null) new GameObject("PlaytestLogger").AddComponent<KeyboardInputLogger>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _ownsSession = true;
            DontDestroyOnLoad(gameObject);
            string sessionId = Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
            string logFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PlaytestLogs"));
#else
            string logFolder = Path.Combine(Application.persistentDataPath, "PlaytestLogs");
#endif
            _writer = new PlaytestCsvWriter(logFolder, sessionId, Application.version);
            _stages = new PlaytestStageRecorder(_writer);
            _stages.MenuInputSamplingRequested += SampleMenuKeys;
            _writer.Write(new PlaytestRecord("SessionStart", Time.realtimeSinceStartupAsDouble));
            _nextFlush = Time.realtimeSinceStartupAsDouble + FLUSH_INTERVAL;
            _device = Keyboard.current;
            _focused = HasInputFocus();
            BlockHeldKeys();
            GameManager.PlaytestRoundStarted += HandleRoundStarted;
            GameManager.PlaytestRoundEnded += HandleRoundEnded;
            GameManager.PlaytestLogFolderRequested += OpenLogFolder;
            GameManager.PlaytestKeyboardReady += HandleKeyboardReady;
            TournamentController.PlaytestShopStateChanged += HandleShopState;
            TournamentController.PlaytestInventorySnapshotRequested += HandleInventorySnapshot;
            TournamentController.PlaytestShopAction += HandleShopAction;
            TournamentKeyboardEffects.PlaytestEffectObserved += HandleSpecialEffect;
            KeyboardInputController.PlaytestKeyProcessing += HandleKeyProcessing;
            InputSystem.onDeviceChange += HandleDeviceChange;
            _writer.Flush();
        }

        void Update()
        {
            if (!_ownsSession || _closed) return;
            if (SynchronizeFocus())
            {
                SynchronizeDevice();
                if (_device != null)
                    foreach (KeyControl key in _device.allKeys) SampleKey(key);
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= _nextFlush)
            {
                FlushLog();
                _nextFlush = now + FLUSH_INTERVAL;
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!_ownsSession || _closed) return;
            if (!hasFocus) LoseFocus();
            else _resyncFocus = true;
        }

        void OnApplicationQuit() { if (_ownsSession) FinishSession("ApplicationQuit", true); }

        void OnDestroy()
        {
            if (!_ownsSession) return;
            FinishSession("LoggerDestroyed", false);
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// 실제 게임 창 포커스 여부를 확인하여 입력 수집 가능 여부를 반환한다.
        /// 에디터에서는 실행 중인 GameView와 앱 포커스를 함께 사용한다.
        /// </summary>
        private bool HasInputFocus()
        {
#if UNITY_EDITOR
            UnityEditor.EditorWindow window = UnityEditor.EditorWindow.focusedWindow;
            return Application.isFocused && UnityEditor.EditorApplication.isPlaying
                && !UnityEditor.EditorApplication.isPaused && window != null && window.GetType().Name == "GameView";
#else
            return Application.isFocused;
#endif
        }

        /// <summary>
        /// 현재 포커스와 동기화 요청을 반영하고 수집 가능 여부를 반환한다.
        /// 잃은 포커스의 추적은 마감하고 복귀 시 이미 눌린 키는 차단한다.
        /// </summary>
        private bool SynchronizeFocus()
        {
            bool focused = HasInputFocus();
            if (!focused)
            {
                if (_focused) LoseFocus();
                return false;
            }
            if (!_focused || _resyncFocus)
            {
                _focused = true;
                _resyncFocus = false;
                _focusRecoveryFrame = Time.frameCount;
                BlockHeldKeys();
            }
            return true;
        }

        /// <summary>
        /// 현재 추적 입력을 FocusLost로 마감한다.
        /// 입력 수집을 중단하고 파일을 Flush하여 합성 해제를 일반 Up으로 처리하지 않는다.
        /// </summary>
        private void LoseFocus()
        {
            _focused = false;
            _resyncFocus = true;
            foreach (Key key in _tracked.Keys) _blocked.Add(key);
            ReleaseTrackedKeys("FocusLost");
            FlushLog();
        }

        /// <summary>
        /// 현재 눌린 키를 실제 해제까지 새 Down으로 처리하지 않도록 저장한다.
        /// 포커스 복귀 또는 장치 동기화 시 현재 Input System 상태를 읽는다.
        /// </summary>
        private void BlockHeldKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            foreach (KeyControl key in keyboard.allKeys)
                if (key.isPressed || key.wasPressedThisFrame) _blocked.Add(key.keyCode);
        }

        /// <summary>
        /// 현재 Keyboard 장치가 교체되면 이전 추적을 마감한다.
        /// 새 장치의 눌린 상태를 차단하여 동기화 입력의 중복 집계를 막는다.
        /// </summary>
        private void SynchronizeDevice()
        {
            if (_device == Keyboard.current) return;
            ReleaseTrackedKeys("DeviceChanged");
            _device = Keyboard.current;
            _sampledFrames.Clear();
            _blocked.Clear();
            BlockHeldKeys();
        }

        /// <summary>
        /// device와 change의 연결 해제 및 리셋 통지를 추적 종료에 반영한다.
        /// 포커스 상실 리셋은 FocusLost로 구분하고 일반 합성 Released 기록을 방지한다.
        /// </summary>
        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (_closed || device != _device) return;
            if (change == InputDeviceChange.SoftReset || change == InputDeviceChange.HardReset
                || change == InputDeviceChange.Disconnected || change == InputDeviceChange.Removed)
            {
                string reason = !HasInputFocus() ? "FocusLost"
                    : change == InputDeviceChange.Disconnected || change == InputDeviceChange.Removed ? "DeviceDisconnected" : "DeviceReset";
                foreach (Key key in _tracked.Keys) _blocked.Add(key);
                ReleaseTrackedKeys(reason);
                _resyncFocus = true;
                FlushLog();
            }
        }

        /// <summary>
        /// 게임이 처리할 keyboard와 key의 원본 장치 입력을 판정 전에 수집한다.
        /// Input System의 Down과 Up만 읽어 같은 프레임의 점수 및 콤보 경계 순서를 유지한다.
        /// </summary>
        private void HandleKeyProcessing(KeyboardInputController keyboard, Key key)
        {
            if (_closed || !SynchronizeFocus()) return;
            SynchronizeDevice();
            _processingKeyboard = keyboard;
            try { if (_device != null) SampleKey(_device[key]); }
            finally { _processingKeyboard = null; }
        }

        /// <summary>
        /// 메뉴가 닫히는 프레임의 전체 원본 키 플래그를 수집한다.
        /// 기존 장치 상태만 읽고 아직 기록하지 않은 Down과 Up을 메뉴 집계 제외 상태에서 저장한다.
        /// </summary>
        private void SampleMenuKeys()
        {
            if (_closed || !SynchronizeFocus()) return;
            SynchronizeDevice();
            if (_device != null) foreach (KeyControl key in _device.allKeys) SampleKey(key);
        }

        /// <summary>
        /// key의 프레임 Down과 Up 플래그를 독립적으로 읽는다.
        /// 한 프레임의 중복 감지와 길게 누른 키의 반복 Down을 막고 이벤트를 즉시 기록한다.
        /// </summary>
        private void SampleKey(KeyControl key)
        {
            Key code = key.keyCode;
            if (_sampledFrames.TryGetValue(code, out int frame) && frame == Time.frameCount) return;
            _sampledFrames[code] = Time.frameCount;
            if (_blocked.Contains(code))
            {
                if (Time.frameCount > _focusRecoveryFrame && (key.wasReleasedThisFrame || !key.isPressed)) _blocked.Remove(code);
                return;
            }
            if (key.wasPressedThisFrame && !_tracked.ContainsKey(code)) RecordKeyEvent(code, true, "");
            if (key.wasReleasedThisFrame && _tracked.ContainsKey(code)) RecordKeyEvent(code, false, "Released");
        }

        /// <summary>
        /// key의 Down 또는 Up을 reason과 실행 경과 시간으로 기록한다.
        /// Down 순번과 누적 횟수 또는 대응 Up의 유지 시간을 저장하고 추적 상태를 변경한다.
        /// </summary>
        private void RecordKeyEvent(Key key, bool down, string reason)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            PlaytestRecord row = new PlaytestRecord("KeyInput", now);
            _stages.AddStageContext(row);
            GameManager source = GameManager.Instance;
            if (source != null) PlaytestStageRecorder.AddTournamentContext(row, source.PlaytestTournament);
            row.Set(PlaytestColumn.Key, key.ToString());
            row.Set(PlaytestColumn.Event, down ? "Down" : "Up");
            if (down)
            {
                long count = _downCounts.TryGetValue(key, out long previous) ? previous + 1 : 1;
                _downCounts[key] = count;
                row.Set(PlaytestColumn.KeyDownCount, count);
                AddTargetContext(row, key);
                long sequence = _writer.Write(row);
                _tracked.Add(key, new PressState(sequence, now));
                _stages.AddDown();
            }
            else
            {
                PressState press = _tracked[key];
                row.Set(PlaytestColumn.PressSequence, press.Sequence);
                row.Set(PlaytestColumn.HeldSeconds, now - press.Start);
                row.Set(PlaytestColumn.EndReason, reason);
                _writer.Write(row);
                _tracked.Remove(key);
            }
        }

        /// <summary>
        /// 추적 중인 모든 키를 reason의 Up으로 한 번씩 마감한다.
        /// 저장한 키 목록을 사용하여 순회 중 추적 사전 변경을 안전하게 처리한다.
        /// </summary>
        private void ReleaseTrackedKeys(string reason)
        {
            List<Key> keys = new List<Key>(_tracked.Keys);
            keys.Sort();
            foreach (Key key in keys) RecordKeyEvent(key, false, reason);
        }

        /// <summary>
        /// source에서 시작한 실제 대결을 CSV 집계기에 전달한다.
        /// 새 실행 식별자와 시작 스냅샷을 준비한다.
        /// </summary>
        private void HandleRoundStarted(GameManager source)
        {
            if (_closed) return;
            _keyboards.Clear();
            _battleSource = source;
            _playerSpawnIndex = 0;
            _enemySpawnIndex = 0;
            _stages.Begin(source);
        }

        /// <summary>
        /// source에서 등장 완료된 keyboard를 isEnemy 소유자의 새 키보드로 기록한다.
        /// 실제 대전 ID는 유지하고 소유자별 생성 순번 및 체력 매핑만 저장한다.
        /// </summary>
        private void HandleKeyboardReady(GameManager source, GameObject keyboard, bool isEnemy)
        {
            if (_closed || source != _battleSource || !_stages.IsActive) return;
            KeyboardInputController input = keyboard.GetComponent<KeyboardInputController>();
            if (_keyboards.ContainsKey(input)) return;
            int index = isEnemy ? ++_enemySpawnIndex : ++_playerSpawnIndex;
            PlaytestRecord row = new PlaytestRecord("KeyboardSpawn", Time.realtimeSinceStartupAsDouble);
            _stages.AddStageContext(row);
            row.Set(PlaytestColumn.Phase, "Battle");
            row.Set(PlaytestColumn.Owner, isEnemy ? "Enemy" : "Player");
            KeyboardState state = new KeyboardState(input, index, row);
            _keyboards.Add(input, state);
            _effectContexts.Remove(input);
            _effectContexts.Add(input, state);
            state.AddContext(row);
            _writer.Write(row);
            TournamentKeyboardEffects effects = keyboard.GetComponent<TournamentKeyboardEffects>();
            if (effects != null) WriteKeyboardLayout(state, effects);
            _writer.Flush();
        }

        /// <summary>
        /// state가 식별하는 실제 키보드의 effects 배치를 키별 상세 행으로 기록한다.
        /// 기본 키와 기믹 유형 및 행열 좌표를 읽으며 배치 난수나 게임 상태는 변경하지 않는다.
        /// </summary>
        private void WriteKeyboardLayout(KeyboardState state, TournamentKeyboardEffects effects)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            string snapshotId = Guid.NewGuid().ToString("N");
            foreach (KeyValuePair<Key, KeycapHealth> binding in state.Layout)
            {
                PlaytestRecord row = new PlaytestRecord("KeyboardLayout", now);
                state.AddBattleContext(row);
                Vector2Int coordinate = effects.GetPlaytestCoordinates(binding.Value);
                row.Set(PlaytestColumn.SnapshotId, snapshotId);
                row.Set(PlaytestColumn.Key, binding.Key.ToString());
                row.Set(PlaytestColumn.SpecialType, effects.GetPlaytestType(binding.Value).ToString());
                row.Set(PlaytestColumn.LayoutRow, coordinate.x);
                row.Set(PlaytestColumn.LayoutColumn, coordinate.y);
                _writer.Write(row);
            }
        }

        /// <summary>
        /// source의 보유 구성을 reason 시점의 양측 스냅샷으로 기록한다.
        /// 대전 시작에만 활성 대전 식별자를 붙이고 초기화 전 실제 수량과 재화를 읽는다.
        /// </summary>
        private void HandleInventorySnapshot(TournamentController source, string reason)
        {
            if (_closed) return;
            WriteInventory(source, source.Inventory, "Player", reason, Guid.NewGuid().ToString("N"));
            WriteInventory(source, source.PlaytestEnemyInventory, "Enemy", reason, Guid.NewGuid().ToString("N"));
            _writer.Flush();
        }

        /// <summary>
        /// inventory의 기본 및 기믹별 수량을 owner와 reason 및 snapshotId로 묶어 기록한다.
        /// source의 실제 재화와 용량을 복사하며 보유 상태를 변경하지 않는다.
        /// </summary>
        private void WriteInventory(TournamentController source, TournamentInventory inventory, string owner,
            string reason, string snapshotId)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            for (int type = 0; type <= (int)SpecialKeycapType.All; type++)
            {
                PlaytestRecord row = new PlaytestRecord("InventorySnapshot", now);
                if (reason == "BattleStart") _stages.AddStageContext(row);
                PlaytestStageRecorder.AddTournamentContext(row, source);
                if (reason == "TournamentStart")
                {
                    row.Set(PlaytestColumn.Phase, "Preparing");
                    row.Set(PlaytestColumn.BattleKind, "TimedRound");
                }
                row.Set(PlaytestColumn.SnapshotId, snapshotId);
                row.Set(PlaytestColumn.Action, reason);
                row.Set(PlaytestColumn.Owner, owner);
                row.Set(PlaytestColumn.SpecialType, ((SpecialKeycapType)type).ToString());
                row.Set(PlaytestColumn.Quantity, type == 0 ? Mathf.Max(0, inventory.Capacity - inventory.Total)
                    : inventory.GetCount((SpecialKeycapType)type));
                row.Set(PlaytestColumn.InventoryCapacity, inventory.Capacity);
                row.Set(PlaytestColumn.InventoryTotal, inventory.Total);
                row.Set(PlaytestColumn.CurrencyAfter, source.Currency);
                if (ReferenceEquals(source, _shopSource)) row.Set(PlaytestColumn.ShopVisitId, _shopVisitId);
                _writer.Write(row);
            }
        }

        /// <summary>
        /// source의 확정된 상점 관찰값 observation을 슬롯과 거래 식별자로 기록한다.
        /// 구매 확정에만 최종 보유 스냅샷을 연결하고 선택과 취소는 재화 지출로 계산하지 않는다.
        /// </summary>
        private void HandleShopAction(TournamentController source, TournamentShopObservation observation)
        {
            if (_closed) return;
            PlaytestRecord row = new PlaytestRecord("ShopAction", Time.realtimeSinceStartupAsDouble);
            PlaytestStageRecorder.AddTournamentContext(row, source);
            if (ReferenceEquals(source, _shopSource)) row.Set(PlaytestColumn.ShopVisitId, _shopVisitId);
            row.Set(PlaytestColumn.ShopTransactionId, observation.TransactionId);
            row.Set(PlaytestColumn.Action, observation.Action);
            row.Set(PlaytestColumn.Owner, "Player");
            if (observation.Slot >= 0) row.Set(PlaytestColumn.OfferSlot, observation.Slot);
            row.Set(PlaytestColumn.OfferGeneration, observation.Generation);
            if (observation.Action != "Reroll") row.Set(PlaytestColumn.SpecialType, observation.Type.ToString());
            row.Set(PlaytestColumn.Quantity, observation.Quantity);
            row.Set(PlaytestColumn.Price, observation.Price);
            row.Set(PlaytestColumn.CurrencyBefore, observation.CurrencyBefore);
            row.Set(PlaytestColumn.CurrencyAfter, observation.CurrencyAfter);
            row.Set(PlaytestColumn.IsConfirmed, observation.Confirmed);
            string snapshotId = observation.Action == "PurchaseConfirmed" ? Guid.NewGuid().ToString("N") : null;
            if (snapshotId != null) row.Set(PlaytestColumn.SnapshotId, snapshotId);
            _writer.Write(row);
            if (snapshotId != null) WriteInventory(source, source.Inventory, "Player", "PurchaseConfirmed", snapshotId);
            _writer.Flush();
        }

        /// <summary>
        /// source의 key에서 관찰한 type 기믹 상태를 action과 reason 및 effectId로 기록한다.
        /// 원래 키보드의 대전 식별자를 유지하며 파괴, 대기, 발동과 취소를 구분한다.
        /// </summary>
        private void HandleSpecialEffect(TournamentKeyboardEffects source, Key key, SpecialKeycapType type,
            string action, string reason, string effectId)
        {
            if (_closed || !_effectContexts.TryGetValue(source.PlaytestKeyboard, out KeyboardState state)) return;
            PlaytestRecord row = new PlaytestRecord("SpecialKeycapEvent", Time.realtimeSinceStartupAsDouble);
            state.AddBattleContext(row);
            if (source.PlaytestTournament != null)
                row.Set(PlaytestColumn.Phase, source.PlaytestTournament.Phase.ToString());
            row.Set(PlaytestColumn.Key, key.ToString());
            row.Set(PlaytestColumn.SpecialType, type.ToString());
            row.Set(PlaytestColumn.Action, action);
            row.Set(PlaytestColumn.EndReason, reason);
            row.Set(PlaytestColumn.EffectId, effectId);
            _writer.Write(row);
        }

        /// <summary>
        /// key의 Down 직전 실제 키캡 상태와 입력 가능 여부를 row에 저장한다.
        /// 처음 파괴한 입력과 메뉴 및 교체 중 입력은 재입력 횟수에서 제외한다.
        /// </summary>
        private void AddTargetContext(PlaytestRecord row, Key key)
        {
            GameManager source = GameManager.Instance;
            if (source == null || source != _battleSource || !_stages.IsActive || !source.IsTimerRunning)
            {
                row.Set(PlaytestColumn.InputState, "NotInBattle");
                return;
            }
            GameObject active = source.ActiveKeyboard;
            KeyboardInputController input = active != null ? active.GetComponent<KeyboardInputController>() : null;
            if (input == null || !_keyboards.TryGetValue(input, out KeyboardState state))
            {
                row.Set(PlaytestColumn.InputState, "KeyboardReplacing");
                return;
            }
            state.AddContext(row);
            row.Set(PlaytestColumn.Owner, "Player");
            if (!_stages.CanCountDown())
                row.Set(PlaytestColumn.InputState, "Menu");
            else if (!input.isActiveAndEnabled || !input.InputEnabled)
                row.Set(PlaytestColumn.InputState, "InputDisabled");
            else if (_processingKeyboard != null && _processingKeyboard != input)
                row.Set(PlaytestColumn.InputState, "OtherKeyboard");
            else if (!state.Layout.TryGetValue(key, out KeycapHealth health) || health == null)
                row.Set(PlaytestColumn.InputState, "Unmapped");
            else
            {
                bool broken = health.CurrentHP <= 0;
                row.Set(PlaytestColumn.InputState, broken ? "Broken" : "Alive");
                row.Set(PlaytestColumn.WasBroken, broken);
                if (!broken) return;
                long count = state.BrokenCounts.TryGetValue(key, out long previous) ? previous + 1 : 1;
                state.BrokenCounts[key] = count;
                row.Set(PlaytestColumn.BrokenKeyDownCount, count);
                _stages.AddBrokenDown();
            }
        }

        /// <summary>
        /// source의 상점 열림과 닫힘 통지를 reason으로 구분하여 기록한다.
        /// 폐기 화면 전환은 포함하며 다음 대전 준비 또는 중단 시 체류 시간을 마감한다.
        /// </summary>
        private void HandleShopState(TournamentController source, bool isOpen, string reason)
        {
            if (_closed) return;
            if (!isOpen)
            {
                if (ReferenceEquals(source, _shopSource)) CloseShop(reason, reason == "NextBattle");
                return;
            }
            if (ReferenceEquals(source, _shopSource)) return;
            CloseShop("Replaced", false);
            _shopSource = source;
            _shopVisitId = Guid.NewGuid().ToString("N");
            _shopRunId = source.PlaytestRunId;
            _shopRound = source.Round;
            _shopBattleKind = source.PlaytestBattleKind;
            _shopStart = Time.realtimeSinceStartupAsDouble;
            PlaytestRecord row = new PlaytestRecord("ShopStart", _shopStart);
            PlaytestStageRecorder.AddTournamentContext(row, source);
            row.Set(PlaytestColumn.ShopVisitId, _shopVisitId);
            row.Set(PlaytestColumn.StartElapsedSeconds, _shopStart);
            _writer.Write(row);
            _writer.Flush();
        }

        /// <summary>
        /// 열려 있는 상점의 실제 경과 시간을 reason과 complete로 한 번 마감한다.
        /// 입력값으로 종료 사유와 완료 여부를 저장하고 상점 추적 상태를 비운다.
        /// </summary>
        private void CloseShop(string reason, bool complete)
        {
            if (_shopVisitId == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            PlaytestRecord row = new PlaytestRecord("ShopEnd", now);
            PlaytestStageRecorder.AddTournamentContext(row, _shopSource);
            row.Set(PlaytestColumn.TournamentRunId, _shopRunId);
            row.Set(PlaytestColumn.RoundIndex, _shopRound);
            row.Set(PlaytestColumn.BattleKind, _shopBattleKind);
            row.Set(PlaytestColumn.ShopVisitId, _shopVisitId);
            row.Set(PlaytestColumn.StartElapsedSeconds, _shopStart);
            row.Set(PlaytestColumn.EndElapsedSeconds, now);
            row.Set(PlaytestColumn.DurationSeconds, now - _shopStart);
            row.Set(PlaytestColumn.EndReason, reason);
            row.Set(PlaytestColumn.IsComplete, complete);
            _writer.Write(row);
            _shopSource = null;
            _shopVisitId = null;
            _writer.Flush();
        }

        /// <summary>
        /// source의 reason과 complete 종료 통지를 집계기에 전달한다.
        /// 최종 점수와 마지막 구간을 한 번 마감한다.
        /// </summary>
        private void HandleRoundEnded(GameManager source, string reason, bool complete)
        {
            if (!_closed) _stages.End(source, reason, complete);
        }

        /// <summary>
        /// 현재 실행의 파일 버퍼를 디스크에 전달한다.
        /// 닫힌 세션은 무시하고 게임 상태는 변경하지 않는다.
        /// </summary>
        public void FlushLog() { if (!_closed) _writer.Flush(); }

        /// <summary>
        /// 현재 기록을 Flush하고 실행의 로그 폴더를 기본 파일 탐색기로 연다.
        /// 폴더 준비나 열기 실패는 한 번만 경고하고 게임 진행을 유지한다.
        /// </summary>
        public static void OpenLogFolder()
        {
            if (_instance == null) return;
            _instance.FlushLog();
            try
            {
                Directory.CreateDirectory(_instance._writer.FolderPath);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _instance._writer.FolderPath,
                    UseShellExecute = true
                })?.Dispose();
#else
                Application.OpenURL(new Uri(_instance._writer.FolderPath + Path.DirectorySeparatorChar).AbsoluteUri);
#endif
            }
            catch (Exception exception)
            {
                if (_instance._folderErrorReported) return;
                _instance._folderErrorReported = true;
                Debug.LogWarning($"[PlaytestLogger] 로그 폴더 열기 실패: {exception.Message}");
            }
        }

        /// <summary>
        /// reason과 complete로 원본 키와 진행 구간 및 세션을 한 번 마감한다.
        /// 이벤트를 해제하고 파일을 닫아 정상 종료와 파괴 콜백의 중복 기록을 방지한다.
        /// </summary>
        private void FinishSession(string reason, bool complete)
        {
            if (_closed) return;
            ReleaseTrackedKeys(reason);
            _stages.Abort(reason);
            CloseShop(reason, false);
            GameManager source = GameManager.Instance;
            TournamentController tournament = source != null ? source.PlaytestTournament : null;
            if (tournament != null && tournament.Inventory != null && tournament.Result == null)
                HandleInventorySnapshot(tournament, reason);
            PlaytestRecord row = new PlaytestRecord("SessionEnd", Time.realtimeSinceStartupAsDouble);
            row.Set(PlaytestColumn.EndReason, reason);
            row.Set(PlaytestColumn.IsComplete, complete);
            _writer.Write(row);
            _closed = true;
            GameManager.PlaytestRoundStarted -= HandleRoundStarted;
            GameManager.PlaytestRoundEnded -= HandleRoundEnded;
            GameManager.PlaytestLogFolderRequested -= OpenLogFolder;
            GameManager.PlaytestKeyboardReady -= HandleKeyboardReady;
            TournamentController.PlaytestShopStateChanged -= HandleShopState;
            TournamentController.PlaytestInventorySnapshotRequested -= HandleInventorySnapshot;
            TournamentController.PlaytestShopAction -= HandleShopAction;
            TournamentKeyboardEffects.PlaytestEffectObserved -= HandleSpecialEffect;
            KeyboardInputController.PlaytestKeyProcessing -= HandleKeyProcessing;
            InputSystem.onDeviceChange -= HandleDeviceChange;
            _stages.MenuInputSamplingRequested -= SampleMenuKeys;
            _writer.Close();
        }

        private sealed class KeyboardState
        {
            private readonly string _id = Guid.NewGuid().ToString("N");
            private readonly int _index;
            private readonly PlaytestRecord _battleContext;
            private readonly IReadOnlyDictionary<Key, KeycapHealth> _layout;
            private readonly Dictionary<Key, long> _brokenCounts = new Dictionary<Key, long>();
            public IReadOnlyDictionary<Key, KeycapHealth> Layout => _layout;
            public Dictionary<Key, long> BrokenCounts => _brokenCounts;

            /// <summary>
            /// input의 실제 키캡 매핑과 index 생성 순번 및 battleContext 입장 문맥을 보관한다.
            /// 새 키보드의 식별자와 키별 재입력 집계 상태를 생성한다.
            /// </summary>
            public KeyboardState(KeyboardInputController input, int index, PlaytestRecord battleContext)
            {
                _index = index;
                _battleContext = battleContext;
                _layout = input.GetKeycapLayout();
            }

            /// <summary>
            /// row에 키보드 식별자와 생성 순번 및 보충 여부를 저장한다.
            /// 입력 행과 입장 이벤트를 같은 키보드로 연결하며 게임 상태는 변경하지 않는다.
            /// </summary>
            public void AddContext(PlaytestRecord row)
            {
                row.Set(PlaytestColumn.KeyboardId, _id);
                row.Set(PlaytestColumn.KeyboardSpawnIndex, _index);
                row.Set(PlaytestColumn.SpawnKind, _index == 1 ? "Initial" : "Replacement");
            }

            /// <summary>
            /// row에 입장 당시 대전과 토너먼트 및 소유자 문맥을 복사한다.
            /// 지연된 효과가 다음 대전으로 섞이지 않도록 저장한 식별자를 사용한다.
            /// </summary>
            public void AddBattleContext(PlaytestRecord row)
            {
                AddContext(row);
                foreach (PlaytestColumn column in new[] { PlaytestColumn.StageId, PlaytestColumn.StageRunId,
                    PlaytestColumn.TournamentRunId, PlaytestColumn.RoundIndex, PlaytestColumn.BattleKind,
                    PlaytestColumn.Phase, PlaytestColumn.Owner })
                    row.Set(column, _battleContext.Values[(int)column]);
            }
        }

        private readonly struct PressState
        {
            public long Sequence { get; }
            public double Start { get; }

            /// <summary>
            /// sequence와 start로 원본 Down의 연결 정보를 보관한다.
            /// Up 유지 시간 계산에 사용할 불변 상태를 생성한다.
            /// </summary>
            public PressState(long sequence, double start) { Sequence = sequence; Start = start; }
        }
    }
}
