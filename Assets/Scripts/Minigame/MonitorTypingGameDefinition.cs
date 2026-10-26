using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(fileName = "TypingGame", menuName = "Keyboard/Mini Games/Typing Game")]
    public sealed class MonitorTypingGameDefinition : MonitorMiniGameDefinition
    {
        [Header("Typing Data")]
        [SerializeField] private TypingSentenceSet _sentenceSet;

        public TypingSentenceSet SentenceSet => _sentenceSet;
    }
}
