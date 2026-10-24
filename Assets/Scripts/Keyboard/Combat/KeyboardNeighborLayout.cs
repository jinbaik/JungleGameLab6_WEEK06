using System;

using UnityEngine;

namespace KeyboardModeling
{
    /// <summary>
    /// 키보드 평면 위치로 중심 키별 주변 키 순서를 계산하여 보관한다.
    /// </summary>
    public sealed class KeyboardNeighborLayout
    {
        private readonly int[][] _neighbors;

        /// <summary>
        /// positions의 각 키를 중심으로 거리순 주변 인덱스를 계산한다.
        /// 중심 키를 제외하고 동거리에서는 원래 인덱스 순서로 정렬한 목록을 저장한다.
        /// </summary>
        public KeyboardNeighborLayout(Vector2[] positions)
        {
            _neighbors = new int[positions.Length][];
            for (int centerIndex = 0; centerIndex < positions.Length; centerIndex++)
            {
                Vector2 center = positions[centerIndex];
                int[] neighbors = new int[positions.Length - 1];
                int nextIndex = 0;
                for (int index = 0; index < positions.Length; index++)
                {
                    if (index != centerIndex) neighbors[nextIndex++] = index;
                }
                Array.Sort(neighbors, (left, right) =>
                {
                    int comparison = (positions[left] - center).sqrMagnitude.CompareTo((positions[right] - center).sqrMagnitude);
                    return comparison != 0 ? comparison : left.CompareTo(right);
                });
                _neighbors[centerIndex] = neighbors;
            }
        }

        /// <summary>
        /// targetIndex 중심의 주변 후보 수를 반환한다.
        /// 체력과 무관한 배치 목록을 사용하여 파괴된 칸 때문에 범위가 확장되지 않도록 한다.
        /// </summary>
        public int GetNeighborCount(int targetIndex)
        {
            return _neighbors[targetIndex].Length;
        }

        /// <summary>
        /// targetIndex 중심에서 거리순 neighborIndex에 해당하는 키 인덱스를 반환한다.
        /// 저장된 배치 순서만 참조하며 키캡 상태는 변경하지 않는다.
        /// </summary>
        public int GetNeighbor(int targetIndex, int neighborIndex)
        {
            return _neighbors[targetIndex][neighborIndex];
        }
    }
}
