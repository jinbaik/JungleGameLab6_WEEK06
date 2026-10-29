using System;
using System.Collections.Generic;

using UnityEngine;

using Game.Player;
using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Workshop
{
    [DisallowMultipleComponent]
    public sealed class WorkshopKeyboardStateController : MonoBehaviour
    {
        [Serializable]
        private sealed class PrefabEntry
        {
            [SerializeField] private string _id;
            [SerializeField] private GameObject _prefab;
            public string Id => _id;
            public GameObject Prefab => _prefab;
        }

        [Header("Restoration")]
        [SerializeField] private KeyboardCustomizationStorage _storage;
        [SerializeField] private WorkShop2KeyboardReturnController _returnController;
        [SerializeField] private PrefabEntry[] _prefabs;
        [SerializeField] private KeyboardProtectionCover _coverPrefab;

        [Header("Floor Support")]
        [SerializeField] private Collider[] _floorColliders;
        [SerializeField, Min(0.001f)] private float _supportDistance = 0.025f;
        [SerializeField, Range(0f, 1f)] private float _minimumSupportDot = 0.7f;
        [SerializeField, Min(0f)] private float _maximumLinearSpeed = 0.2f;
        [SerializeField, Min(0f)] private float _maximumAngularSpeed = 0.5f;
        [SerializeField, Min(0f)] private float _settleDuration = 0.2f;
        private readonly Dictionary<Rigidbody, float> _settled = new Dictionary<Rigidbody, float>();

        void FixedUpdate()
        {
            var seen = new HashSet<Rigidbody>();
            foreach (KeyboardPickupTarget pickup in FindObjectsByType<KeyboardPickupTarget>(FindObjectsSortMode.None))
            {
                Rigidbody body = pickup.GetComponent<Rigidbody>();
                if (body == null || body.gameObject.scene != gameObject.scene || !seen.Add(body)) continue;
                if (!IsFloorCandidate(body, pickup)) { _settled.Remove(body); continue; }
                _settled.TryGetValue(body, out float duration);
                _settled[body] = duration + Time.deltaTime;
            }
            foreach (Rigidbody body in new List<Rigidbody>(_settled.Keys))
                if (body == null || !seen.Contains(body)) _settled.Remove(body);
        }

        /// <summary>battleBody를 제외한 진열대와 안정된 바닥 키보드의 최신 구성을 반환한다. 실제 편집 상태와 생성 프리팹을 독립 데이터로 수집하며 파일은 변경하지 않는다.</summary>
        public WorkshopKeyboardRecord[] Capture(Rigidbody battleBody)
        {
            var records = new List<WorkshopKeyboardRecord>();
            var excluded = new HashSet<Rigidbody> { battleBody };
            foreach (WorkbenchKeyboardHolder holder in FindObjectsByType<WorkbenchKeyboardHolder>(FindObjectsSortMode.None))
                if (holder.gameObject.scene == gameObject.scene && holder.KeyboardBody != null) excluded.Add(holder.KeyboardBody);
            var rackIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyboardWallRack rack in FindObjectsByType<KeyboardWallRack>(FindObjectsSortMode.None))
            {
                if (rack.gameObject.scene != gameObject.scene) continue;
                if (string.IsNullOrWhiteSpace(rack.PersistentId) || !rackIds.Add(rack.PersistentId))
                    throw new InvalidOperationException($"진열대 ID가 비어 있거나 중복됩니다: {rack.name} / {rack.PersistentId}");
                Rigidbody body = rack.KeyboardBody;
                if (body == null || !excluded.Add(body)) continue;
                records.Add(CaptureBody(body, rack.PersistentId));
            }
            foreach (KeyboardPickupTarget pickup in FindObjectsByType<KeyboardPickupTarget>(FindObjectsSortMode.None))
            {
                Rigidbody body = pickup.GetComponent<Rigidbody>();
                if (body == null || body.gameObject.scene != gameObject.scene || excluded.Contains(body)) continue;
                if (!_settled.TryGetValue(body, out float duration) || duration < _settleDuration || !IsFloorCandidate(body, pickup)) continue;
                excluded.Add(body);
                records.Add(CaptureBody(body, ""));
            }
            return records.ToArray();
        }

        /// <summary>body의 출처 자산과 실제 커스터마이징을 rackId와 함께 수집한다. 출처가 등록되지 않으면 잘못된 종류로 복원하지 않고 예외를 반환한다.</summary>
        private WorkshopKeyboardRecord CaptureBody(Rigidbody body, string rackId)
        {
            KeyboardPickupTarget pickup = body.GetComponent<KeyboardPickupTarget>();
            foreach (PrefabEntry entry in _prefabs)
            {
                if (entry.Prefab == null || entry.Prefab != pickup.SourcePrefab) continue;
                return new WorkshopKeyboardRecord(entry.Id, rackId, body,
                    _storage.CaptureCustomization(body.GetComponent<KeyboardCustomization>()),
                    body.GetComponentInChildren<KeyboardProtectionCover>());
            }
            throw new InvalidOperationException($"작업실 키보드의 생성 프리팹이 등록되지 않았습니다: {body.name}");
        }

        /// <summary>body가 반환·집기·고정 상태가 아니고 허용 속도 이내에서 실제 바닥에 지지되는지 반환한다. 공중의 순간 정지와 다른 키보드의 지지는 제외한다.</summary>
        private bool IsFloorCandidate(Rigidbody body, KeyboardPickupTarget pickup)
        {
            if (body.isKinematic || !body.useGravity || !body.detectCollisions || _returnController.IsReturning(body)) return false;
            if (body.linearVelocity.sqrMagnitude > _maximumLinearSpeed * _maximumLinearSpeed
                || body.angularVelocity.sqrMagnitude > _maximumAngularSpeed * _maximumAngularSpeed) return false;
            Collider[] shapes = body.GetComponentsInChildren<Collider>();
            foreach (Collider floor in _floorColliders)
            {
                if (floor == null || !floor.enabled || !pickup.HasFloorContact(floor, _minimumSupportDot)) continue;
                foreach (Collider shape in shapes)
                {
                    if (!shape.enabled || shape.isTrigger || shape.attachedRigidbody != body) continue;
                    if (Physics.ComputePenetration(shape, shape.transform.position - Vector3.up * _supportDistance,
                        shape.transform.rotation, floor, floor.transform.position, floor.transform.rotation,
                        out Vector3 direction, out float distance) && direction.y >= _minimumSupportDot && distance > 0f)
                        return true;
                }
            }
            return false;
        }

        /// <summary>명시적 복귀 목록의 각 항목을 한 번씩 생성하고 진열대 또는 바닥에 복원한다. 실패 항목은 원인을 남기고 이미 성공한 항목은 다시 생성하지 않는다.</summary>
        public void RestorePendingKeyboards()
        {
            WorkshopKeyboardRecord[] records = WorkshopKeyboardMemory.GetPendingReturn();
            if (records == null) return;
            for (int index = 0; index < records.Length; index++)
            {
                if (!WorkshopKeyboardMemory.TryBeginItem(index)) continue;
                GameObject keyboard = null;
                try
                {
                    WorkshopKeyboardRecord record = records[index];
                    GameObject prefab = ResolvePrefab(record.PrefabId);
                    KeyboardWallRack rack = string.IsNullOrEmpty(record.RackId) ? null : ResolveRack(record.RackId);
                    if (rack != null && rack.KeyboardBody != null)
                        throw new InvalidOperationException($"복원할 진열대가 이미 점유되어 있습니다: {record.RackId}");
                    keyboard = Instantiate(prefab, record.Position, record.Rotation);
                    keyboard.transform.localScale = record.Scale;
                    if (record.PrefabId == "keyboard-enhanced")
                        keyboard.GetComponent<KeyboardAssemblyState>().CompleteInitialAssembly();
                    Rigidbody body = keyboard.GetComponent<Rigidbody>();
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.detectCollisions = false;
                    keyboard.GetComponent<KeyboardInputController>().SetInputEnabled(false);
                    KeyboardPickupTarget pickup = keyboard.GetComponent<KeyboardPickupTarget>();
                    if (pickup == null) pickup = keyboard.AddComponent<KeyboardPickupTarget>();
                    pickup.SetSourcePrefab(prefab);
                    _storage.ApplyCustomization(JsonUtility.FromJson<KeyboardCustomizationState>(record.CustomizationJson),
                        keyboard.GetComponent<KeyboardCustomization>());
                    RestoreCover(record, keyboard.transform);
                    if (rack != null)
                    {
                        if (!rack.TryAttachKeyboard(body, true)) throw new InvalidOperationException($"진열대 고정 실패: {record.RackId}");
                    }
                    else
                    {
                        body.isKinematic = false;
                        body.useGravity = true;
                        body.detectCollisions = true;
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                        body.WakeUp();
                    }
                    WorkshopKeyboardMemory.CompleteItem(index);
                }
                catch (Exception exception)
                {
                    if (keyboard != null) { keyboard.SetActive(false); Destroy(keyboard); }
                    Debug.LogError($"작업실 키보드 복원 실패: 항목 {index}, 프리팹 {records[index].PrefabId}, 진열대 {records[index].RackId}. 원본은 유지하고 자동 재시도하지 않습니다. {exception}", this);
                }
            }
        }

        /// <summary>id에 연결된 단일 프리팹 자산을 반환한다. 누락 또는 중복 ID는 임의 대체 없이 예외로 기록한다.</summary>
        private GameObject ResolvePrefab(string id)
        {
            GameObject result = null;
            foreach (PrefabEntry entry in _prefabs)
            {
                if (entry.Id != id) continue;
                if (result != null || entry.Prefab == null) throw new InvalidOperationException($"프리팹 ID 중복 또는 자산 누락: {id}");
                result = entry.Prefab;
            }
            return result != null ? result : throw new InvalidOperationException($"프리팹 ID를 찾을 수 없습니다: {id}");
        }

        /// <summary>id와 정확히 일치하는 현재 씬 진열대를 반환한다. 검색 순서와 Instance ID를 사용하지 않으며 누락 또는 중복이면 예외를 기록한다.</summary>
        private KeyboardWallRack ResolveRack(string id)
        {
            KeyboardWallRack result = null;
            foreach (KeyboardWallRack rack in FindObjectsByType<KeyboardWallRack>(FindObjectsSortMode.None))
            {
                if (rack.gameObject.scene != gameObject.scene || rack.PersistentId != id) continue;
                if (result != null) throw new InvalidOperationException($"진열대 ID 중복: {id}");
                result = rack;
            }
            return result != null ? result : throw new InvalidOperationException($"진열대 ID를 찾을 수 없습니다: {id}");
        }

        /// <summary>record의 보호 커버 유무와 로컬 자세를 root에 복원한다. 커스터마이징 JSON에 포함되지 않는 외형만 처리한다.</summary>
        private void RestoreCover(WorkshopKeyboardRecord record, Transform root)
        {
            foreach (KeyboardProtectionCover cover in root.GetComponentsInChildren<KeyboardProtectionCover>(true))
            {
                cover.gameObject.SetActive(false);
                Destroy(cover.gameObject);
            }
            if (!record.HasCover) return;
            if (_coverPrefab == null) throw new InvalidOperationException("보호 커버 복원 프리팹이 연결되지 않았습니다.");
            KeyboardProtectionCover restored = Instantiate(_coverPrefab, root, false);
            restored.transform.localPosition = record.CoverPosition;
            restored.transform.localRotation = record.CoverRotation;
            restored.transform.localScale = record.CoverScale;
        }
    }
}
