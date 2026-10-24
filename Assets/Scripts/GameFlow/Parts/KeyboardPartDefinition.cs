using UnityEngine;

namespace Game.Tournament
{
    public abstract class KeyboardPartDefinition : ScriptableObject
    {
        [Header("Shop")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField, Min(0)] private int _price = 60;
        [SerializeField, Min(1)] private int _quantity = 1;
        public string Id => _id;
        public string DisplayName => _displayName;
        public int Price => _price;
        public int Quantity => _quantity;
        public abstract string Description { get; }
    }
}
