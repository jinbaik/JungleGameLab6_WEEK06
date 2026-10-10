using System;

using UnityEngine;

namespace KeyboardModeling
{
    [CreateAssetMenu(
        fileName = "KeycapRarityProgression",
        menuName = "Game/Keycap Rarity Progression")]
    public sealed class KeycapRarityProgression : ScriptableObject
    {
        [Header("Stages")]
        [SerializeField]
        private Stage[] _stages =
        {
            new Stage(0,  new KeycapRarityDistribution(100f, 0f, 0f, 0f)),
            new Stage(4,  new KeycapRarityDistribution(85f, 15f, 0f, 0f)),
            new Stage(8,  new KeycapRarityDistribution(70f, 25f, 5f, 0f)),
            new Stage(12, new KeycapRarityDistribution(60f, 28f, 10f, 2f)),
            new Stage(16, new KeycapRarityDistribution(50f, 30f, 15f, 5f))
        };

        /// <summary>
        /// 완료한 키보드 수에 해당하는 구간 번호를 반환한다.
        /// completedKeyboards를 사용하며 설정과 진행 상태는 변경하지 않는다.
        /// </summary>
        public int GetStage(int completedKeyboards)
        {
            for (int index = _stages.Length - 1; index >= 0; index--)
            {
                if (completedKeyboards >= _stages[index].RequiredCompletedKeyboards)
                {
                    return index;
                }
            }

            return 0;
        }

        /// <summary>
        /// 완료한 키보드 수에 해당하는 등급 확률표의 복사본을 반환한다.
        /// completedKeyboards를 사용하며 에셋의 원본 확률은 변경하지 않는다.
        /// </summary>
        public KeycapRarityDistribution GetDistribution(int completedKeyboards)
        {
            return _stages[GetStage(completedKeyboards)].Distribution.Copy();
        }

        /// <summary>
        /// 구간의 시작값과 확률표가 유효한지 검사한다.
        /// 첫 구간은 0대부터 시작해야 하며 이후 시작값은 증가해야 한다.
        /// </summary>
        public void Validate()
        {
            if (_stages == null || _stages.Length == 0 || _stages[0] == null || _stages[0].RequiredCompletedKeyboards != 0)
            {
                throw new InvalidOperationException("Rarity progression must start at 0.");
            }

            int previous = -1;
            foreach (Stage stage in _stages)
            {
                if (stage == null || stage.RequiredCompletedKeyboards <= previous || stage.Distribution == null)
                {
                    throw new InvalidOperationException("Invalid rarity progression stage.");
                }

                stage.Distribution.Validate();
                previous = stage.RequiredCompletedKeyboards;
            }
        }

        [Serializable]
        public sealed class Stage
        {
            [SerializeField, Min(0)] private int _requiredCompletedKeyboards;
            [SerializeField] private KeycapRarityDistribution _distribution;

            public int RequiredCompletedKeyboards => _requiredCompletedKeyboards;
            public KeycapRarityDistribution Distribution => _distribution;

            /// <summary>
            /// requiredCompletedKeyboards와 distribution으로 구간 설정을 만든다.
            /// 전달받은 시작 대수와 확률표를 저장한다.
            /// </summary>
            public Stage(int requiredCompletedKeyboards, KeycapRarityDistribution distribution)
            {
                _requiredCompletedKeyboards = requiredCompletedKeyboards;
                _distribution = distribution;
            }
        }
    }
}