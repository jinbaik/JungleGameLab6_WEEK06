using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class KeyboardEditSidebarIcon : MaskableGraphic
    {
        private const int CIRCLE_SEGMENTS = 20;

        [Header("Sidebar Icon")]
        [SerializeField] private IconKind _kind;
        [SerializeField, Range(1f, 3f)] private float _strokeWidth = 1.7f;

        private enum IconKind
        {
            General,
            Attack,
            Keycap,
            Switch,
            Led,
            Case,
            Plate,
            Photo,
            Chevron,
            Check,
            Dot
        }

        /// <summary>
        /// 부품 버튼의 열림 상태를 오른쪽 아이콘에 반영한다.
        /// selected가 true이면 체크를, false이면 화살표를 선택하고 메시 갱신을 요청한다.
        /// </summary>
        public void SetChecked(bool selected)
        {
            IconKind kind = selected ? IconKind.Check : IconKind.Chevron;
            if (_kind == kind)
                return;

            _kind = kind;
            SetVerticesDirty();
        }

        /// <summary>
        /// 설정된 아이콘 종류를 24 단위 도형으로 그려 UI 메시를 구성한다.
        /// helper에 현재 RectTransform 크기와 color를 반영한 선과 원의 정점을 추가한다.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            switch (_kind)
            {
                case IconKind.General:
                    DrawRectangle(helper, 3f, 7f, 18f, 11f, 2f);
                    DrawPath(helper, 7, 11, 9, 11);
                    DrawPath(helper, 12, 11, 14, 11);
                    DrawPath(helper, 17, 11, 17.1f, 11);
                    DrawPath(helper, 7, 15, 17, 15);
                    break;
                case IconKind.Attack:
                    DrawPath(helper, 13, 2, 4, 14, 11, 14, 10, 22, 20, 9, 13, 9, 13, 2);
                    break;
                case IconKind.Keycap:
                    DrawPath(helper, 5, 8, 19, 8, 21, 18, 3, 18, 5, 8);
                    DrawPath(helper, 8, 11, 16, 11);
                    break;
                case IconKind.Switch:
                    DrawRectangle(helper, 4f, 5f, 16f, 14f, 2f);
                    DrawPath(helper, 12, 8, 12, 16);
                    DrawPath(helper, 8, 12, 16, 12);
                    break;
                case IconKind.Led:
                    DrawPath(helper, 9, 18, 15, 18);
                    DrawPath(helper, 10, 21, 14, 21);
                    DrawPath(helper, 9, 15, 7.5f, 13.5f, 6, 11, 6, 8, 7, 5.5f,
                        9, 3.7f, 12, 3, 15, 3.7f, 17, 5.5f, 18, 8, 18, 11,
                        16.5f, 13.5f, 15, 15, 14, 18, 10, 18, 9, 15);
                    DrawPath(helper, 12, 1, 12, 2);
                    DrawPath(helper, 3, 4, 4, 5);
                    DrawPath(helper, 21, 5, 22, 4);
                    break;
                case IconKind.Case:
                    DrawRectangle(helper, 3f, 6f, 18f, 12f, 2f);
                    DrawPath(helper, 7, 10, 17, 10);
                    DrawPath(helper, 7, 14, 11, 14);
                    break;
                case IconKind.Plate:
                    DrawPath(helper, 3, 9, 12, 4, 21, 9, 12, 14, 3, 9);
                    DrawPath(helper, 3, 14, 12, 19, 21, 14);
                    break;
                case IconKind.Photo:
                    DrawRectangle(helper, 3f, 4f, 18f, 16f, 2f);
                    DrawRectangle(helper, 6f, 7f, 4f, 4f, 2f);
                    DrawPath(helper, 4, 18, 9, 13, 13, 16, 17, 11, 20, 15);
                    break;
                case IconKind.Chevron:
                    DrawPath(helper, 9, 6, 15, 12, 9, 18);
                    break;
                case IconKind.Check:
                    DrawPath(helper, 5, 12, 10, 17, 19, 7);
                    break;
                case IconKind.Dot:
                    AddDisc(helper, ToLocalPoint(12f, 12f), rectTransform.rect.width * 0.5f);
                    break;
            }
        }

        /// <summary>
        /// 지정한 사각형의 둥근 모서리를 이어 테두리를 그린다.
        /// x, y, width, height, radius를 24 단위 좌표로 사용하여 helper에 선을 추가한다.
        /// </summary>
        private void DrawRectangle(VertexHelper helper, float x, float y, float width, float height, float radius)
        {
            Vector2[] centers =
            {
                new Vector2(x + width - radius, y + radius),
                new Vector2(x + width - radius, y + height - radius),
                new Vector2(x + radius, y + height - radius),
                new Vector2(x + radius, y + radius)
            };
            Vector2 first = Vector2.zero;
            Vector2 previous = Vector2.zero;
            for (int corner = 0; corner < 4; corner++)
            {
                for (int step = 0; step <= 5; step++)
                {
                    float angle = (corner * 90f - 90f + step * 18f) * Mathf.Deg2Rad;
                    Vector2 point = centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    Vector2 local = ToLocalPoint(point.x, point.y);
                    if (corner == 0 && step == 0)
                        first = local;
                    else
                        AddLine(helper, previous, local);

                    previous = local;
                }
            }

            AddLine(helper, previous, first);
        }

        /// <summary>
        /// 입력한 좌표 쌍을 순서대로 이어 둥근 선 경로를 그린다.
        /// coordinates의 x와 y를 현재 UI 좌표로 변환해 helper에 선을 추가한다.
        /// </summary>
        private void DrawPath(VertexHelper helper, params float[] coordinates)
        {
            for (int index = 2; index < coordinates.Length; index += 2)
            {
                AddLine(helper, ToLocalPoint(coordinates[index - 2], coordinates[index - 1]),
                    ToLocalPoint(coordinates[index], coordinates[index + 1]));
            }
        }

        /// <summary>
        /// 24 단위 아이콘 좌표를 현재 RectTransform의 로컬 좌표로 변환한다.
        /// x와 y를 정사각형 중앙 영역에 맞추며 변환한 위치를 반환한다.
        /// </summary>
        private Vector2 ToLocalPoint(float x, float y)
        {
            Rect rect = rectTransform.rect;
            float scale = Mathf.Min(rect.width, rect.height) / 24f;
            return rect.center + new Vector2(x - 12f, 12f - y) * scale;
        }

        /// <summary>
        /// 두 로컬 위치 사이에 현재 굵기의 선과 둥근 끝을 그린다.
        /// start와 end를 사용하며 helper에 색상이 적용된 사각형 및 원 정점을 추가한다.
        /// </summary>
        private void AddLine(VertexHelper helper, Vector2 start, Vector2 end)
        {
            float radius = _strokeWidth * Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 48f;
            Vector2 direction = (end - start).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * radius;
            int first = helper.currentVertCount;
            helper.AddVert(start - normal, color, Vector2.zero);
            helper.AddVert(start + normal, color, Vector2.zero);
            helper.AddVert(end + normal, color, Vector2.zero);
            helper.AddVert(end - normal, color, Vector2.zero);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
            AddDisc(helper, start, radius);
            AddDisc(helper, end, radius);
        }

        /// <summary>
        /// 로컬 중심 위치에 채워진 원을 그린다.
        /// center와 radius를 사용하며 helper에 현재 색상의 삼각형 부채꼴을 추가한다.
        /// </summary>
        private void AddDisc(VertexHelper helper, Vector2 center, float radius)
        {
            int first = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            for (int index = 0; index <= CIRCLE_SEGMENTS; index++)
            {
                float angle = index * Mathf.PI * 2f / CIRCLE_SEGMENTS;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                helper.AddVert(point, color, Vector2.zero);
                if (index > 0)
                    helper.AddTriangle(first, first + index + 1, first + index);
            }
        }
    }
}
