using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(fileName = "HackingGame", menuName = "Keyboard/Mini Games/Hacking Game")]
    public sealed class MonitorHackingGameDefinition : MonitorMiniGameDefinition
    {
        [Header("Hacking Settings")]
        [SerializeField, Min(1)] private int _totalStages = 3;
        [SerializeField, Min(5)] private int _keystrokesPerStage = 50;

        public int TotalStages => _totalStages;
        public int KeystrokesPerStage => _keystrokesPerStage;
    }
}
