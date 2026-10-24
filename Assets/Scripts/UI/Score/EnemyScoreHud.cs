using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class EnemyScoreHud : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private KeyboardScoreCombo _scoreCombo;
        [SerializeField] private Text _scoreText;

        void OnEnable()
        {
            _scoreCombo.StateChanged += RefreshScore;
            RefreshScore();
        }

        void OnDisable()
        {
            _scoreCombo.StateChanged -= RefreshScore;
        }

        /// <summary>
        /// 연결된 적 점수 상태를 읽어 적 스코어 문구를 갱신한다.
        /// _scoreCombo.Score를 표시하며 플레이어 점수와 적 점수 상태는 변경하지 않는다.
        /// </summary>
        private void RefreshScore()
        {
            _scoreText.text = $"ENEMY SCORE: {_scoreCombo.Score}";
        }
    }
}
