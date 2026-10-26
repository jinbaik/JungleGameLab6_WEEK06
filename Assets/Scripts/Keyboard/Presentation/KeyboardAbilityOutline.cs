using System;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;

namespace Game.Tournament
{
    public sealed class KeyboardAbilityOutline : MonoBehaviour
    {
        public enum PartKind { Keycap, Switch, Plate, Housing }

        [Serializable]
        private sealed class Binding
        {
            [SerializeField] private MeshRenderer _source;
            [SerializeField] private MeshFilter _sourceFilter;
            [SerializeField] private MeshRenderer _outline;
            [SerializeField] private MeshFilter _outlineFilter;
            [SerializeField] private PartKind _kind;
            [SerializeField] private Key _key;
            private Mesh _mesh;
            public PartKind Kind => _kind;
            public Key Key => _key;

            /// <summary>visibleと元レンダラーの表示状態から外郭を切り替える。モデル変更時は元メッシュとサブメッシュ数を反映する。</summary>
            public void Refresh(bool visible)
            {
                if (_mesh != _sourceFilter.sharedMesh)
                {
                    _mesh = _sourceFilter.sharedMesh;
                    _outlineFilter.sharedMesh = _mesh;
                    Material material = _outline.sharedMaterial;
                    var materials = new Material[_mesh.subMeshCount];
                    for (int index = 0; index < materials.Length; index++) materials[index] = material;
                    _outline.sharedMaterials = materials;
                }
                _outline.enabled = visible && _source.enabled && _source.gameObject.activeInHierarchy;
            }
        }

        [Header("Authored Outlines")]
        [SerializeField] private Binding[] _bindings = new Binding[0];
        private TournamentInventory _inventory;
        private KeyboardCustomization _customization;
        private KeyboardInputController _keyboard;
        private bool _preview;

        void Awake()
        {
            _customization = GetComponent<KeyboardCustomization>();
            _keyboard = GetComponent<KeyboardInputController>();
        }

        /// <summary>inventoryを外郭表示の参照に指定する。外形を交換せず位置別の解放段階を読み取る。</summary>
        public void ConfigureInventory(TournamentInventory inventory) { _inventory = inventory; _preview = false; }

        /// <summary>activeを作業室の最大段階体験表示として設定する。購入状態や保存データは変更しない。</summary>
        public void SetPreview(bool active) { _preview = active; }

        void LateUpdate()
        {
            foreach (Binding binding in _bindings)
            {
                bool unlocked = false;
                if (_inventory != null)
                {
                    if (binding.Kind == PartKind.Keycap) unlocked = _inventory.GetKeycapLevel(binding.Key) > 0;
                    else if (binding.Kind == PartKind.Switch && _inventory.Switch != null) unlocked = _inventory.GetPartLevel(_inventory.Switch) > 0;
                    else if (binding.Kind == PartKind.Plate && _inventory.Plate != null) unlocked = _inventory.GetPartLevel(_inventory.Plate) > 0;
                    else if (binding.Kind == PartKind.Housing && _inventory.Housing != null) unlocked = _inventory.GetPartLevel(_inventory.Housing) > 0;
                }
                else if (_preview)
                {
                    if (binding.Kind == PartKind.Keycap && _keyboard.TryGetKeycapTransform(binding.Key, out Transform keycap))
                    {
                        KeycapDefinition part = _customization.GetKeycapDefinition(keycap);
                        unlocked = part != null && part.Ability != null;
                    }
                }
                binding.Refresh(unlocked);
            }
        }

        void OnDisable()
        {
            foreach (Binding binding in _bindings) binding.Refresh(false);
        }
    }
}
