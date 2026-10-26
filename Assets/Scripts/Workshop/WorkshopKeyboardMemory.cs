using System;
using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

namespace Game.Workshop
{
    [Serializable]
    public sealed class WorkshopKeyboardRecord
    {
        [SerializeField] private string _prefabId;
        [SerializeField] private string _rackId;
        [SerializeField] private string _customizationJson;
        [SerializeField] private bool _isExcludedFromRequests;
        [SerializeField] private Vector3 _position;
        [SerializeField] private Quaternion _rotation;
        [SerializeField] private Vector3 _scale;
        [SerializeField] private bool _hasCover;
        [SerializeField] private Vector3 _coverPosition;
        [SerializeField] private Quaternion _coverRotation;
        [SerializeField] private Vector3 _coverScale;

        public string PrefabId => _prefabId;
        public string RackId => _rackId;
        public string CustomizationJson => _customizationJson;
        public bool IsExcludedFromRequests => _isExcludedFromRequests;
        public Vector3 Position => _position;
        public Quaternion Rotation => _rotation;
        public Vector3 Scale => _scale;
        public bool HasCover => _hasCover;
        public Vector3 CoverPosition => _coverPosition;
        public Quaternion CoverRotation => _coverRotation;
        public Vector3 CoverScale => _coverScale;

        /// <summary>prefabId와 rackId, body의 의뢰 제외 상태·자세·커스터마이징을 독립 데이터로 복사한다. cover가 있으면 커버의 로컬 외형도 보관하며 씬 객체 참조는 저장하지 않는다.</summary>
        public WorkshopKeyboardRecord(string prefabId, string rackId, Rigidbody body,
            KeyboardCustomizationState state, KeyboardProtectionCover cover, bool isExcludedFromRequests)
        {
            _prefabId = prefabId;
            _rackId = rackId;
            _customizationJson = JsonUtility.ToJson(state);
            _isExcludedFromRequests = isExcludedFromRequests;
            _position = body.position;
            _rotation = body.rotation;
            _scale = body.transform.lossyScale;
            _hasCover = cover != null;
            if (cover == null) return;
            _coverPosition = cover.transform.localPosition;
            _coverRotation = cover.transform.localRotation;
            _coverScale = cover.transform.localScale;
        }
    }

    public static class WorkshopKeyboardMemory
    {
        private static string _snapshotJson;
        private static int _count;
        private static bool _requested;
        private static readonly HashSet<int> _attempted = new HashSet<int>();
        private static readonly HashSet<int> _completed = new HashSet<int>();

        [Serializable]
        private sealed class Snapshot
        {
            [SerializeField] private WorkshopKeyboardRecord[] _records;
            public WorkshopKeyboardRecord[] Records => _records;

            /// <summary>records를 JSON 직렬화의 루트로 보관한다. 실제 메모리 보관 시 별도 JSON으로 복사하므로 입력 배열의 후속 변경은 전달되지 않는다.</summary>
            public Snapshot(WorkshopKeyboardRecord[] records) { _records = records; }
        }

        /// <summary>이전 실행 또는 이전 작업실 스냅샷과 완료 상태를 비운다. 입력과 반환값 없이 새 플레이와 직접 전투 실행의 오래된 복원을 방지한다.</summary>
        public static void Reset()
        {
            _snapshotJson = null;
            _count = 0;
            _requested = false;
            _attempted.Clear();
            _completed.Clear();
        }

        /// <summary>records 전체를 최신 작업실 상태로 교체한다. 빈 배열도 반영하고 원본 객체와 독립된 JSON만 메모리에 남긴다.</summary>
        public static void Select(WorkshopKeyboardRecord[] records)
        {
            Reset();
            _snapshotJson = JsonUtility.ToJson(new Snapshot(records));
            _count = records.Length;
        }

        /// <summary>fromWorkshop이 true일 때만 저장된 잔여 키보드의 복귀를 예약한다. 직접 전투 실행과 빈 목록은 복원 요청을 남기지 않는다.</summary>
        public static void RequestReturn(bool fromWorkshop)
        {
            if (!fromWorkshop || _count == 0) { Reset(); return; }
            _requested = !string.IsNullOrEmpty(_snapshotJson);
        }

        /// <summary>명시적인 복귀 요청이 있으면 독립된 복원 목록을 반환한다. 전투용 키보드의 반환 완료와는 별도로 데이터를 유지한다.</summary>
        public static WorkshopKeyboardRecord[] GetPendingReturn()
        {
            return _requested ? JsonUtility.FromJson<Snapshot>(_snapshotJson).Records : null;
        }

        /// <summary>index 항목의 복원 시도를 한 번만 승인한다. 실패한 항목도 자동 재시도하지 않아 성공 항목의 중복 생성과 무한 재시도를 막는다.</summary>
        public static bool TryBeginItem(int index)
        {
            return _requested && index >= 0 && index < _count && _attempted.Add(index);
        }

        /// <summary>index 항목의 생성과 연결 성공을 기록한다. 전 항목이 성공한 경우에만 잔여 키보드 스냅샷을 제거한다.</summary>
        public static void CompleteItem(int index)
        {
            _completed.Add(index);
            if (_completed.Count == _count) Reset();
        }
    }
}
