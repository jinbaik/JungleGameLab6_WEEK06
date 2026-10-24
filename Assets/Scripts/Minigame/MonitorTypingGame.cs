using System;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class MonitorTypingGame : MonitorMiniGame
    {
        [Header("Screen UI")]
        [SerializeField] private Text _sentenceLabel;
        [SerializeField] private Text _inputLabel;
        [SerializeField] private Text _statsLabel;
        [SerializeField] private Text _statusLabel;

        [Header("Enhanced UI")]
        [SerializeField] private Text _accuracyValueText;
        [SerializeField] private Text _speedValueText;
        [SerializeField] private Text _timeValueText;
        [SerializeField] private Image _inputBoxBg;
        [SerializeField] private Image _statusBannerBg;

        [Header("Sentences")]
        private TypingSentenceSet _sentenceSet;
        private int _sentenceIndex = -1;
        private string _sentence;
        private string _typed = "";
        private int _attempts;
        private int _correctPresses;
        private int _submittedCharacters;
        private int _submittedStrokes;
        private float _elapsed;
        private bool _started;
        private bool _completed;

        public string Sentence => _sentence;
        public string TypedText => _typed;
        public bool IsCompleted => _completed;
        public float Accuracy => _attempts == 0 ? 100f : 100f * _correctPresses / _attempts;
        public float WordsPerMinute => _elapsed > 0f ? (_submittedCharacters + CountMatchingCharacters()) * 12f / _elapsed : 0f;
        public float StrokesPerMinute => _elapsed > 0f ? (_submittedStrokes + CountMatchingStrokes()) * 60f / _elapsed : 0f;

        /// <summary>
        /// 연결된 문장 SO로 새 연습을 시작한다.
        /// 누적 통계와 문장 순서를 초기화하고 첫 유효 문장을 표시한다.
        /// </summary>
        protected override void OnStarted()
        {
            _sentenceSet = ((MonitorTypingGameDefinition)Definition).SentenceSet;
            _sentenceIndex = -1;
            _attempts = 0;
            _correctPresses = 0;
            _submittedCharacters = 0;
            _submittedStrokes = 0;
            _elapsed = 0f;
            BeginNextSentence();
        }

        /// <summary>
        /// definition이 문장 SO를 가진 타자연습 정의인지 반환한다.
        /// definition을 검사하며 내부 상태는 변경하지 않는다.
        /// </summary>
        public override bool CanRun(MonitorMiniGameDefinition definition)
        {
            return definition is MonitorTypingGameDefinition typing && typing.SentenceSet != null;
        }

        /// <summary>
        /// 현재 연습을 종료하고 대기 안내를 표시한다.
        /// 입력 처리를 멈추며 마지막 결과는 화면에 유지한다.
        /// </summary>
        protected override void OnStopped()
        {
            if (_statusLabel != null)
                _statusLabel.text = "키보드를 거치하고 우클릭하면 타자연습을 시작합니다.";
        }

        /// <summary>
        /// 타자 연습 진행 중 경과 시간을 누적하고 통계 화면을 갱신한다.
        /// deltaTime을 사용하여 _elapsed를 증가시킨다.
        /// </summary>
        public override void Tick(float deltaTime)
        {
            if (!IsRunning || !_started) return;
            _elapsed += deltaTime;
            RefreshStats();
        }

        /// <summary>
        /// 입력된 문자를 제시 문장과 비교하여 입력 문자열 및 누적 정확도를 갱신한다.
        /// character를 검사하여 _typed, _attempts, _correctPresses를 변경한다.
        /// </summary>
        public override void HandleTextInput(char character)
        {
            if (!IsRunning || char.IsControl(character) || _typed.Length >= _sentence.Length) return;
            _started = true;
            _attempts++;
            if (_typed.Length < _sentence.Length && character == _sentence[_typed.Length]) _correctPresses++;
            _typed += character;
            _completed = _typed == _sentence;
            RefreshScreen();
        }

        /// <summary>
        /// Enter 입력 시 다음 문장으로 넘어가고 Backspace 입력 시 마지막 글자를 삭제한다.
        /// key를 사용하여 제출 처리 또는 문자열 수정을 수행한다.
        /// </summary>
        public override void HandleKeyPressed(Key key)
        {
            if (!IsRunning) return;
            if (key == Key.Enter || key == Key.NumpadEnter)
            {
                if (_typed.Length > 0)
                {
                    _submittedCharacters += CountMatchingCharacters();
                    _submittedStrokes += CountMatchingStrokes();
                    BeginNextSentence();
                }
                return;
            }
            if (key == Key.Backspace && _typed.Length > 0)
            {
                _typed = _typed.Substring(0, _typed.Length - 1);
                _completed = _typed == _sentence;
                RefreshScreen();
            }
        }

        /// <summary>
        /// SO 목록에서 다음 유효 문장을 순환 선택하고 현재 입력란을 초기화한다.
        /// _sentenceSet을 참조하여 _sentence를 설정하고 새 문장 입력을 준비한다.
        /// </summary>
        private void BeginNextSentence()
        {
            if (_sentenceSet != null)
            {
                for (int offset = 0; offset < _sentenceSet.Sentences.Count; offset++)
                {
                    _sentenceIndex = (_sentenceIndex + 1) % _sentenceSet.Sentences.Count;
                    string candidate = _sentenceSet.Sentences[_sentenceIndex];
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    _sentence = candidate.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                    _typed = "";
                    _started = false;
                    _completed = false;
                    RefreshScreen();
                    return;
                }
            }
            Debug.LogError("타자연습에 사용할 문장 SO와 문장을 등록하세요.", this);
            Complete();
        }

        /// <summary>
        /// 현재 입력과 제시 문장의 동일한 위치에서 일치하는 글자 수를 계산하여 반환한다.
        /// _typed와 _sentence를 비교하며 상태는 변경하지 않는다.
        /// </summary>
        private int CountMatchingCharacters()
        {
            int count = 0;
            for (int index = 0; index < _typed.Length; index++)
                if (index < _sentence.Length && _typed[index] == _sentence[index]) count++;
            return count;
        }

        /// <summary>
        /// 현재 입력 중 올바르게 입력된 글자의 타수를 한글 자모 및 영문 기준으로 합산하여 반환한다.
        /// _typed와 _sentence를 사용하여 타수를 계산한다.
        /// </summary>
        private int CountMatchingStrokes()
        {
            int count = 0;
            for (int index = 0; index < _typed.Length && index < _sentence.Length; index++)
            {
                if (_typed[index] != _sentence[index]) continue;
                char character = _typed[index];
                if (character < '\uAC00' || character > '\uD7A3')
                {
                    count++;
                    continue;
                }

                int syllable = character - '\uAC00';
                int vowel = syllable / 28 % 21;
                int final = syllable % 28;
                count += 2 + (final == 0 ? 0 : 1);

                // 겹모음과 겹받침은 구성 자모를 각각 세어 타수에 반영한다.
                if (vowel == 9 || vowel == 10 || vowel == 11 || vowel == 14 || vowel == 15 || vowel == 16 || vowel == 19) count++;
                if (final == 3 || final == 5 || final == 6 || (final >= 9 && final <= 15) || final == 18) count++;
            }
            return count;
        }

        /// <summary>
        /// 현재 제시 문장과 사용자 입력을 색상 태그와 함께 화면에 갱신한다.
        /// _sentence와 _typed를 바탕으로 라벨 및 패널 색상을 변경한다.
        /// </summary>
        private void RefreshScreen()
        {
            if (_sentenceLabel != null)
                _sentenceLabel.text = _sentence;

            if (_inputLabel != null)
            {
                var text = new StringBuilder();
                for (int index = 0; index < _typed.Length; index++)
                {
                    char character = _typed[index];
                    text.Append(index < _sentence.Length && character == _sentence[index] ? "<color=#78E5BB>" : "<color=#FF8484>");
                    text.Append(character == '<' ? "＜" : character == '>' ? "＞" : character == ' ' ? '·' : character);
                    text.Append("</color>");
                }
                if (!_completed) text.Append("▏");
                _inputLabel.text = text.ToString();
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = _completed
                    ? "<color=#39FF14>✓ 문장 완성!</color>  [Enter] 다음 문장  ·  [우클릭] 나가기"
                    : "[Enter] 제출 후 다음 문장  ·  [Backspace] 수정  ·  [우클릭] 나가기";
            }

            if (_inputBoxBg != null)
            {
                _inputBoxBg.color = _completed
                    ? new Color(0.04f, 0.18f, 0.12f, 0.95f)
                    : new Color(0.035f, 0.055f, 0.085f, 0.95f);
            }

            if (_statusBannerBg != null)
            {
                _statusBannerBg.color = _completed
                    ? new Color(0.05f, 0.22f, 0.14f, 0.95f)
                    : new Color(0.06f, 0.09f, 0.14f, 0.9f);
            }

            RefreshStats();
        }

        /// <summary>
        /// 정확도, 타자 속도, 경과 시간을 통계 라벨과 전용 카드에 갱신한다.
        /// Accuracy, StrokesPerMinute, _elapsed를 사용하여 UI 텍스트들을 갱신한다.
        /// </summary>
        private void RefreshStats()
        {
            if (_statsLabel != null)
                _statsLabel.text = $"정확도 {Accuracy:F1}%     속도 {StrokesPerMinute:F0} 타수(타/분)     시간 {_elapsed:F1}초";

            if (_accuracyValueText != null)
                _accuracyValueText.text = $"{Accuracy:F1}%";

            if (_speedValueText != null)
                _speedValueText.text = $"{StrokesPerMinute:F0} <size=14>타수(타/분)</size>";

            if (_timeValueText != null)
                _timeValueText.text = $"{_elapsed:F1}s";
        }
    }
}
