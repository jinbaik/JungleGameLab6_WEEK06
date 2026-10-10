using UnityEngine;

namespace KeyboardModeling
{
    [CreateAssetMenu(menuName = "Keyboard/Playable Variant")]
    public sealed class KeyboardPlayableVariantDefinition : ScriptableObject
    {
        [Header("Game Prefab")]
        [SerializeField] private GameObject _gamePrefab;
        public GameObject GamePrefab => _gamePrefab;
    }
}
