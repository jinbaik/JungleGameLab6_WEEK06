using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using KeyboardModeling.Assembly;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class WorkshopBoundaryReturn : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private BoxCollider _boundary;
        [SerializeField] private WorkshopKeycapPlacement _placement;

        [Header("Return Timing")]
        [SerializeField, Min(0.01f)] private float _checkInterval = 0.1f;
        [SerializeField, Min(0f)] private float _returnDelay = 2f;
        [SerializeField, Min(0.01f)] private float _flightDuration = 0.6f;
        private float _checkElapsed;
        private readonly Dictionary<AssemblyDraggablePart, PendingReturn> _pending = new Dictionary<AssemblyDraggablePart, PendingReturn>();

        private sealed class PendingReturn
        {
            public int Version { get; set; }
            public RawImage Icon { get; set; }
        }

        void Update()
        {
            _checkElapsed += Time.deltaTime;
            if (_checkElapsed < _checkInterval) return;
            _checkElapsed = 0f;
            IReadOnlyList<AssemblyDraggablePart> parts = _placement.LooseParts;
            for (int index = 0; index < parts.Count; index++)
            {
                AssemblyDraggablePart part = parts[index];
                if (part == null || _pending.ContainsKey(part) || !CanReturn(part) || IsInside(part)) continue;
                PendingReturn pending = new PendingReturn { Version = part.InteractionVersion };
                _pending.Add(part, pending);
                StartCoroutine(ReturnAfterExit(part, pending));
            }
        }

        /// <summary>
        /// part가 활성 자유 파츠이고 집기나 풀러 반환 대상이 아닌지 반환한다.
        /// 파츠의 설치 상태와 재고는 변경하지 않는다.
        /// </summary>
        private bool CanReturn(AssemblyDraggablePart part)
        {
            return part.gameObject.activeInHierarchy && part.CatalogEntry != null
                && !_placement.IsReturnBlocked(part);
        }

        /// <summary>
        /// part의 기준점을 boundary의 로컬 좌표로 변환하여 영역 안인지 반환한다.
        /// Collider의 center와 size 및 Transform의 회전과 배율을 반영한다.
        /// </summary>
        private bool IsInside(AssemblyDraggablePart part)
        {
            Vector3 local = _boundary.transform.InverseTransformPoint(part.transform.position) - _boundary.center;
            Vector3 half = _boundary.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>
        /// part가 pending 버전으로 영역 밖에 머물면 지연 후 아이콘 비행으로 반환한다.
        /// 재진입과 재집기는 취소하며 편집 종료 시 아이콘 없이 재고에 반환한다.
        /// </summary>
        private IEnumerator ReturnAfterExit(AssemblyDraggablePart part, PendingReturn pending)
        {
            float elapsed = 0f;
            while (elapsed < _returnDelay)
            {
                yield return null;
                if (part == null || part.InteractionVersion != pending.Version || !CanReturn(part) || IsInside(part))
                {
                    CancelReturn(part, pending);
                    yield break;
                }
                elapsed += Time.deltaTime;
            }
            if (!_placement.IsEditing)
            {
                CompleteReturn(part, pending);
                yield break;
            }
            pending.Icon = _placement.CreateReturnIcon(part.CatalogEntry);
            Vector3 screen = _placement.ReturnCamera.WorldToScreenPoint(part.transform.position);
            if (screen.z <= 0f)
            {
                Vector2 direction = new Vector2(Screen.width * 0.5f - screen.x, Screen.height * 0.5f - screen.y);
                if (direction.sqrMagnitude < 0.001f) direction = Vector2.down;
                screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                    + direction.normalized * Mathf.Max(Screen.width, Screen.height);
            }
            Vector2 start = ToCanvasPosition(pending.Icon, screen);
            pending.Icon.rectTransform.localPosition = start;
            part.gameObject.SetActive(false);
            elapsed = 0f;
            while (elapsed < _flightDuration && _placement.IsEditing)
            {
                yield return null;
                if (part == null || part.InteractionVersion != pending.Version || _placement.IsReturnBlocked(part))
                {
                    CancelReturn(part, pending);
                    yield break;
                }
                elapsed += Time.deltaTime;
                Vector2 destination = ToCanvasPosition(pending.Icon, _placement.GetReturnDestination(part.CatalogEntry));
                pending.Icon.rectTransform.localPosition = Vector2.Lerp(start, destination, Mathf.Clamp01(elapsed / _flightDuration));
            }
            CompleteReturn(part, pending);
        }

        /// <summary>
        /// screen을 icon 부모 Canvas의 로컬 좌표로 변환하여 반환한다.
        /// 아이콘 전체가 화면 안에 보이도록 위치를 제한하며 재고는 변경하지 않는다.
        /// </summary>
        private Vector2 ToCanvasPosition(RawImage icon, Vector2 screen)
        {
            RectTransform canvas = (RectTransform)icon.transform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, Vector2.zero, null, out Vector2 min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, new Vector2(Screen.width, Screen.height), null, out Vector2 max);
            Vector2 half = icon.rectTransform.rect.size * 0.5f + Vector2.one * 8f;
            return new Vector2(Mathf.Clamp(local.x, min.x + half.x, Mathf.Max(min.x + half.x, max.x - half.x)),
                Mathf.Clamp(local.y, min.y + half.y, Mathf.Max(min.y + half.y, max.y - half.y)));
        }

        /// <summary>
        /// part의 pending 예약과 아이콘을 제거하고 비행 중 숨긴 동일 버전 파츠를 복원한다.
        /// 재고 수량은 변경하지 않는다.
        /// </summary>
        private void CancelReturn(AssemblyDraggablePart part, PendingReturn pending)
        {
            _pending.Remove(part);
            if (pending.Icon == null) return;
            Destroy(pending.Icon.gameObject);
            if (part != null && part.InteractionVersion == pending.Version) part.gameObject.SetActive(true);
        }

        /// <summary>
        /// part와 pending의 버전을 확인하고 예약을 정리한 후 기존 재고 경로로 반환한다.
        /// 이미 집거나 반환된 파츠의 수량은 중복 증가시키지 않는다.
        /// </summary>
        private void CompleteReturn(AssemblyDraggablePart part, PendingReturn pending)
        {
            bool valid = part != null && part.InteractionVersion == pending.Version && !_placement.IsReturnBlocked(part);
            CancelReturn(part, pending);
            if (valid) _placement.ReturnLoosePart(part);
        }

        void OnDisable()
        {
            StopAllCoroutines();
            foreach (var pair in new List<KeyValuePair<AssemblyDraggablePart, PendingReturn>>(_pending))
                CompleteReturn(pair.Key, pair.Value);
        }
    }
}
