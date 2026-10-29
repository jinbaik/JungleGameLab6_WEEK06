using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyBoundaryReturn : MonoBehaviour
    {
        private const float RETURN_DELAY = 2f;

        [Header("Scene References")]
        [SerializeField] private BoxCollider _boundary;
        [SerializeField] private AssemblyDragInput _dragInput;
        [SerializeField] private AssemblyCatalogUI _catalog;
        [SerializeField] private AssemblyFocusController _focus;

        [Header("Boundary Check")]
        [SerializeField, Min(0.01f)] private float _checkInterval = 0.1f;
        private float _checkElapsed;

        [Header("Return Flight")]
        [SerializeField, Min(0.01f)] private float _flightDuration = 0.6f;
        private readonly Dictionary<AssemblyDraggablePart, PendingReturn> _pending = new Dictionary<AssemblyDraggablePart, PendingReturn>();

        private sealed class PendingReturn
        {
            public int Version { get; set; }
            public RawImage Icon { get; set; }
            public bool Hidden { get; set; }
        }

        void OnEnable()
        {
            _dragInput.PartPicked += CancelReturn;
            _dragInput.ExtractedReturnScheduled += CancelReturn;
            _focus.FocusChanged += HandleFocusChanged;
        }

        void Update()
        {
            _checkElapsed += Time.deltaTime;
            if (_checkElapsed < _checkInterval)
                return;
            _checkElapsed = 0f;
            IReadOnlyList<AssemblyDraggablePart> parts = _dragInput.RegisteredParts;
            for (int index = 0; index < parts.Count; index++)
            {
                AssemblyDraggablePart part = parts[index];
                if (part == null)
                    continue;
                if (_pending.TryGetValue(part, out PendingReturn current))
                {
                    if (!current.Hidden && (!CanReturn(part) || IsInsideBoundary(part)
                        || part.InteractionVersion != current.Version))
                        CancelReturn(part);
                    continue;
                }
                if (CanReturn(part) && !IsInsideBoundary(part))
                    ScheduleReturn(part);
            }
        }

        /// <summary>영역 밖의 part에 최초 조작 버전으로 2초 예약을 만든다. 등록된 예약은 주기 검사로 갱신하지 않는다.</summary>
        private void ScheduleReturn(AssemblyDraggablePart part)
        {
            PendingReturn pending = new PendingReturn { Version = part.InteractionVersion };
            _pending.Add(part, pending);
            StartCoroutine(ReturnAfterExit(part, pending));
        }

        /// <summary>part가 보관 가능한 자유 부품인지 검사한다. 장착, 집기, 도구 회수와 식별 불가 부품은 false를 반환한다.</summary>
        private bool CanReturn(AssemblyDraggablePart part)
        {
            return part.gameObject.activeInHierarchy && part.CatalogEntry != null
                && (part.Slot == null || part.Slot.InstalledPart != part)
                && !_dragInput.IsHoldingPart(part) && !_dragInput.HasExtractedReturn(part);
        }

        /// <summary>part의 월드 기준점을 BoxCollider 로컬 좌표로 변환한다. 회전과 스케일 및 center와 size를 반영하여 경계 안인지 반환한다.</summary>
        private bool IsInsideBoundary(AssemblyDraggablePart part)
        {
            Vector3 local = _boundary.transform.InverseTransformPoint(part.transform.position) - _boundary.center;
            Vector3 half = _boundary.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>part와 pending의 조작 버전이 유지되면 2초 후 반환한다. 집중 중에는 아이콘 비행 완료까지 재고 증가를 미룬다.</summary>
        private IEnumerator ReturnAfterExit(AssemblyDraggablePart part, PendingReturn pending)
        {
            float elapsed = 0f;
            while (elapsed < RETURN_DELAY)
            {
                yield return null;
                if (!IsCurrent(part, pending))
                    yield break;
                if (!CanReturn(part) || part.InteractionVersion != pending.Version || IsInsideBoundary(part))
                {
                    CancelReturn(part);
                    yield break;
                }
                elapsed += Time.deltaTime;
            }
            if (!CanReturn(part) || part.InteractionVersion != pending.Version || IsInsideBoundary(part))
            {
                CancelReturn(part);
                yield break;
            }
            if (!_focus.IsFocused)
            {
                CompleteReturn(part, pending);
                yield break;
            }
            Vector3 worldPosition = part.transform.position;
            pending.Icon = _catalog.CreateReturnIcon(part.CatalogEntry);
            Vector2 start = _catalog.GetReturnIconStart(pending.Icon, _dragInput.InputCamera, worldPosition);
            pending.Icon.rectTransform.localPosition = start;
            pending.Hidden = true;
            part.gameObject.SetActive(false);
            elapsed = 0f;
            while (elapsed < _flightDuration)
            {
                yield return null;
                if (!IsCurrent(part, pending))
                    yield break;
                if (part.InteractionVersion != pending.Version || _dragInput.HasExtractedReturn(part))
                {
                    CancelReturn(part);
                    yield break;
                }
                elapsed += Time.deltaTime;
                Vector2 position = Vector2.Lerp(start, _catalog.GetReturnIconDestination(pending.Icon), Mathf.Clamp01(elapsed / _flightDuration));
                pending.Icon.rectTransform.localPosition = _catalog.ClampReturnIcon(pending.Icon, position);
            }
            CompleteReturn(part, pending);
        }

        /// <summary>part와 pending이 여전히 같은 예약인지 반환한다. 파괴된 부품은 예약과 아이콘을 제거한다.</summary>
        private bool IsCurrent(AssemblyDraggablePart part, PendingReturn pending)
        {
            if (part == null)
            {
                CancelReturn(part);
                return false;
            }
            return _pending.TryGetValue(part, out PendingReturn current) && current == pending;
        }

        /// <summary>part의 예약과 효과를 제거한다. 비행 중 숨긴 실물은 같은 버전일 때만 활성화하여 다른 회수 경로에 넘긴다.</summary>
        private void CancelReturn(AssemblyDraggablePart part)
        {
            if (!_pending.TryGetValue(part, out PendingReturn pending))
                return;
            _pending.Remove(part);
            if (pending.Icon != null)
                Destroy(pending.Icon.gameObject);
            if (pending.Hidden && part != null && part.InteractionVersion == pending.Version)
                part.gameObject.SetActive(true);
        }

        /// <summary>유효한 part의 예약을 먼저 제거하고 기존 인벤토리 경로로 반환한다. pending의 버전이 바뀌었으면 수량을 늘리지 않는다.</summary>
        private void CompleteReturn(AssemblyDraggablePart part, PendingReturn pending)
        {
            bool valid = part != null && part.InteractionVersion == pending.Version && !_dragInput.HasExtractedReturn(part)
                && (pending.Hidden || (CanReturn(part) && !IsInsideBoundary(part)));
            CancelReturn(part);
            if (valid)
                _dragInput.ReturnLoosePart(part);
        }

        /// <summary>focused가 거짓이면 영역 복귀 대기와 비행을 즉시 확정한다. 도구 회수 예약은 변경하지 않는다.</summary>
        private void HandleFocusChanged(bool focused)
        {
            if (focused)
                return;
            foreach (var pair in new List<KeyValuePair<AssemblyDraggablePart, PendingReturn>>(_pending))
                CompleteReturn(pair.Key, pair.Value);
        }

        void OnDisable()
        {
            _dragInput.PartPicked -= CancelReturn;
            _dragInput.ExtractedReturnScheduled -= CancelReturn;
            _focus.FocusChanged -= HandleFocusChanged;
            HandleFocusChanged(false);
        }
    }
}
