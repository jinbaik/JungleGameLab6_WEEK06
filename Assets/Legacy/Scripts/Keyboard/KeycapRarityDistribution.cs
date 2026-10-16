using System;

using UnityEngine;

namespace KeyboardModeling
{
    [Serializable]
    public sealed class KeycapRarityDistribution
    {
        [Header("Weights in Rarity Order")]
        [SerializeField] private float[] _weights;

        /// <summary>
        /// 일반부터 전설까지의 weights를 복사하여 독립적인 등급 분포를 구성한다.
        /// 음수, 비정상 값, 등급 수 불일치와 합계 0은 설정 오류로 거부한다.
        /// </summary>
        public KeycapRarityDistribution(params float[] weights)
        {
            _weights = (float[])weights.Clone();
            Validate();
        }

        /// <summary>현재 가중치를 복사한 분포를 반환하여 원본 설정의 외부 변경을 방지한다.</summary>
        public KeycapRarityDistribution Copy()
        {
            return new KeycapRarityDistribution(_weights);
        }

        /// <summary>rarity의 가중치를 전체 합계로 나눈 0부터 1까지의 최종 확률을 반환한다.</summary>
        public float GetProbability(KeycapRarity rarity)
        {
            Validate();
            return (float)(_weights[(int)rarity] / GetTotalWeight());
        }

        /// <summary>random의 독립적인 난수와 누적 가중치를 사용하여 일반을 포함한 등급 하나를 반환한다.</summary>
        public KeycapRarity Roll(System.Random random)
        {
            double point = random.NextDouble() * GetTotalWeight();
            for (int index = 0; index < _weights.Length; index++)
            {
                point -= _weights[index];
                if (point < 0d)
                {
                    return (KeycapRarity)index;
                }
            }

            throw new InvalidOperationException("Rarity distribution contains no selectable tier.");
        }

        /// <summary>등급별 가중치 수, 유효 범위와 합계를 검사하며 잘못된 설정이면 예외를 발생시킨다.</summary>
        public void Validate()
        {
            if (_weights == null || _weights.Length != Enum.GetValues(typeof(KeycapRarity)).Length)
            {
                throw new InvalidOperationException("Rarity weights must include every tier in enum order.");
            }

            for (int index = 0; index < _weights.Length; index++)
            {
                float weight = _weights[index];
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                {
                    throw new InvalidOperationException("Rarity weights must be finite and non-negative.");
                }
            }

            if (GetTotalWeight() <= 0d)
            {
                throw new InvalidOperationException("Rarity weight sum must be positive.");
            }
        }

        /// <summary>모든 등급의 가중치를 더한 합계를 반환하며 분포를 변경하지 않는다.</summary>
        private double GetTotalWeight()
        {
            double total = 0d;
            for (int index = 0; index < _weights.Length; index++)
            {
                total += _weights[index];
            }

            return total;
        }
    }
}
