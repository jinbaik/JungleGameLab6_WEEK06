using UnityEngine;

namespace Game.Player
{
    public abstract class MonitorMiniGameDefinition : ScriptableObject
    {
        [Header("App Info")]
        [SerializeField] private string _displayName;
        [SerializeField, TextArea(2, 4)] private string _description;
        [SerializeField] private Sprite _icon;

        [Header("Screen")]
        [SerializeField] private MonitorMiniGame _screenPrefab;

        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public MonitorMiniGame ScreenPrefab => _screenPrefab;
    }
}
