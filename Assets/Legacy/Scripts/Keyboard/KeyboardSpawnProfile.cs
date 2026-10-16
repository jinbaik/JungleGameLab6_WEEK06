using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling
{
    public sealed class KeyboardSpawnProfile
    {
        private readonly ReadOnlyCollection<KeycapData> _keycaps;
        private readonly KeycapRarityDistribution _distribution;

        public int ProgressionStage { get; }
        public int Seed { get; }
        public IReadOnlyList<KeycapData> Keycaps => _keycaps;
        public KeycapRarityDistribution Distribution => _distribution.Copy();

        /// <summary>
        /// progressionStage, seed, distribution과 keycaps로 배치 결과를 보관한다.
        /// 전달받은 확률표와 키캡 목록의 복사본을 저장한다.
        /// </summary>
        public KeyboardSpawnProfile(
            int progressionStage,
            int seed,
            KeycapRarityDistribution distribution,
            List<KeycapData> keycaps)
        {
            ProgressionStage = progressionStage;
            Seed = seed;
            _distribution = distribution.Copy();
            _keycaps = new List<KeycapData>(keycaps).AsReadOnly();
        }

        public sealed class KeycapData
        {
            public Key Key { get; }
            public KeycapRarity Rarity { get; }
            public int MaxHP { get; }
            public long Reward { get; }
            public Material ShellMaterial { get; }
            public Mesh ShellMesh { get; }
            public Material BandMaterial { get; }
            public AudioClip PressSound { get; }
            public AudioClip BreakSound { get; }

            /// <summary>
            /// key, rarity, 체력, 보상, 외형과 효과음을 받아 키캡 생성 결과를 만든다.
            /// 유효한 값을 검사하고 확정된 등급별 설정을 저장한다.
            /// </summary>
            public KeycapData(
                Key key,
                KeycapRarity rarity,
                int maxHP,
                long reward,
                Material shellMaterial,
                Mesh shellMesh,
                Material bandMaterial,
                AudioClip pressSound = null,
                AudioClip breakSound = null)
            {
                if (maxHP <= 0 || reward <= 0 ||
                    !Enum.IsDefined(typeof(KeycapRarity), rarity) || shellMaterial == null)
                {
                    throw new ArgumentException("Invalid keycap spawn data.");
                }

                Key = key;
                Rarity = rarity;
                MaxHP = maxHP;
                Reward = reward;
                ShellMaterial = shellMaterial;
                ShellMesh = shellMesh;
                BandMaterial = bandMaterial;
                PressSound = pressSound;
                BreakSound = breakSound;
            }
        }
    }
}
