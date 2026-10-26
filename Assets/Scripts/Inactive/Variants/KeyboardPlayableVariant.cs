using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardPlayableVariant : MonoBehaviour
    {
        [Header("Matching Game Prefab")]
        [SerializeField] private KeyboardPlayableVariantDefinition _definition;
        public GameObject GamePrefab => _definition.GamePrefab;
    }
}
