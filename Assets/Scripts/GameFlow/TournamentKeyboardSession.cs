using UnityEngine;

using Game.Workshop;

namespace Game.Tournament
{
    public static class TournamentKeyboardSession
    {
        private static string _initialJson;
        private static string _currentJson;
        private static string _workshopJson;
        private static bool _returnRequested;
        private static bool _returnAttempted;

        public static bool HasSelection => !string.IsNullOrEmpty(_initialJson);

        /// <summary>
        /// 플레이 실행 시작 시 정적 세션을 비운다.
        /// 입력과 반환값 없이 이전 실행의 선택과 현재 구성을 제거한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            WorkshopKeyboardMemory.Reset();
            _initialJson = null;
            _currentJson = null;
            _workshopJson = null;
            _returnRequested = false;
            _returnAttempted = false;
        }

        /// <summary>
        /// state를 전투 구성으로, workshopOriginal을 복귀용 원본으로 각각 복사한다.
        /// 원본 객체를 보관하지 않고 JSON으로 독립된 상태를 유지한다.
        /// </summary>
        public static void Select(KeyboardCustomizationState state, KeyboardCustomizationState workshopOriginal)
        {
            WorkshopKeyboardMemory.Reset();
            _initialJson = JsonUtility.ToJson(state);
            _currentJson = _initialJson;
            _workshopJson = JsonUtility.ToJson(workshopOriginal);
            _returnRequested = false;
            _returnAttempted = false;
        }

        /// <summary>
        /// 명시적인 작업실 복귀를 예약한다. 원본이 있을 때만 복원을 요청하고 이전 전투 선택은 제거한다.
        /// 입력과 반환값 없이 전투 재진입에서 오래된 선택을 사용하지 않도록 한다.
        /// </summary>
        public static void RequestWorkshopReturn()
        {
            _returnRequested = !string.IsNullOrEmpty(_workshopJson);
            _initialJson = null;
            WorkshopKeyboardMemory.RequestReturn(_returnRequested);
        }

        /// <summary>
        /// 명시적인 복귀 요청의 원본을 state로 한 번만 제공한다.
        /// 성공 여부를 반환하며 실패 후에도 원본은 유지하고 자동 재시도는 차단한다.
        /// </summary>
        public static bool TryBeginWorkshopReturn(out KeyboardCustomizationState state)
        {
            state = null;
            if (!_returnRequested || _returnAttempted) return false;
            _returnAttempted = true;
            state = JsonUtility.FromJson<KeyboardCustomizationState>(_workshopJson);
            return true;
        }

        /// <summary>
        /// 복원과 반환 대기열 등록이 완료된 원본을 제거한다.
        /// 입력과 반환값 없이 같은 복귀 요청의 재실행을 방지한다.
        /// </summary>
        public static void CompleteWorkshopReturn()
        {
            _workshopJson = null;
            _returnRequested = false;
        }

        /// <summary>
        /// 시작 구성으로 새 토너먼트의 현재 구성을 초기화한다.
        /// 입력과 반환값 없이 이전 실행의 임시 변경을 제거한다.
        /// </summary>
        public static void Begin()
        {
            if (!HasSelection)
            {
                WorkshopKeyboardMemory.Reset();
                _workshopJson = null;
                _returnRequested = false;
                _returnAttempted = false;
            }
            _currentJson = _initialJson;
        }

        /// <summary>
        /// state를 현재 토너먼트 구성으로 복사한다.
        /// 작업실 시작 구성과 저장 파일은 변경하지 않는다.
        /// </summary>
        public static void UpdateCurrent(KeyboardCustomizationState state)
        {
            _currentJson = JsonUtility.ToJson(state);
        }

        /// <summary>작업실에서 선택한 원본 구성을 독립 객체로 반환한다. 상점 상품 추출에 사용하며 현재 전투 구성은 변경하지 않는다.</summary>
        public static KeyboardCustomizationState GetSelection()
        {
            return string.IsNullOrEmpty(_initialJson) ? null : JsonUtility.FromJson<KeyboardCustomizationState>(_initialJson);
        }

        /// <summary>
        /// 현재 구성을 독립된 상태 객체로 반환한다.
        /// 선택한 구성이 없으면 null을 반환한다.
        /// </summary>
        public static KeyboardCustomizationState GetCurrent()
        {
            return string.IsNullOrEmpty(_currentJson) ? null : JsonUtility.FromJson<KeyboardCustomizationState>(_currentJson);
        }
    }
}
