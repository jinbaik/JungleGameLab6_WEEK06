using System.Collections.Generic;

using UnityEngine;

namespace Game.Tournament
{
    [CreateAssetMenu(menuName = "Keyboard/Parts/Catalog")]
    public sealed class KeyboardPartCatalog : ScriptableObject
    {
        [Header("Parts")]
        [SerializeField] private KeyboardPartDefinition[] _parts;
        [SerializeField] private KeycapDefinition _defaultKeycap;
        public KeycapDefinition DefaultKeycap => _defaultKeycap;
        public IReadOnlyList<KeyboardPartDefinition> Parts => _parts;

        /// <summary>state의 키캡 SO ID와 나머지 부품 정보를 읽어 사용된 상품 목록을 반환한다. 이전 저장만 원본 재질 ID로 해석한다. 사진 외형과 중복 수량은 상품 식별에 사용하지 않는다.</summary>
        public List<KeyboardPartDefinition> CreatePool(KeyboardCustomizationState state)
        {
            HashSet<string> keycapIds = new HashSet<string>();
            if (state.HasKeycapDefinitions)
            {
                if (state.KeycapDefinitionIds == null || state.KeycapKeys == null || state.KeycapDefinitionIds.Length != state.KeycapKeys.Length)
                    throw new System.InvalidOperationException("Saved keycap identity data is incomplete.");
                if (new HashSet<UnityEngine.InputSystem.Key>(state.KeycapKeys).Count != state.KeycapKeys.Length)
                    throw new System.InvalidOperationException("Saved keycap keys are duplicated.");
                foreach (string id in state.KeycapDefinitionIds)
                    if (!string.IsNullOrEmpty(id)) keycapIds.Add(id);
            }
            else
            {
                int slot = 0;
                for (int i = 0; i < state.RendererPaths.Length; i++)
                {
                    if (state.RendererPaths[i].Contains("PBT_SculptedShell"))
                    {
                        int index = state.MaterialIndices[slot];
                        if (index >= 0)
                        {
                            KeycapDefinition found = null;
                            foreach (KeyboardPartDefinition part in _parts)
                                if (part is KeycapDefinition keycap && keycap.MaterialId == state.CatalogIds[index])
                                {
                                    if (found != null && found != keycap)
                                        throw new System.InvalidOperationException("Legacy material matches multiple keycaps.");
                                    found = keycap;
                                }
                            if (found != null) keycapIds.Add(found.Id);
                        }
                    }
                    slot += state.SlotCounts[i];
                }
            }
            foreach (string id in keycapIds)
            {
                KeycapDefinition found = null;
                foreach (KeyboardPartDefinition part in _parts)
                    if (part is KeycapDefinition keycap && keycap.Id == id)
                    {
                        if (found != null && found != keycap) throw new System.InvalidOperationException("Duplicate keycap definition ID: " + id);
                        found = keycap;
                    }
                if (found == null) throw new System.InvalidOperationException("Saved keycap definition is unavailable: " + id);
            }
            string housingId = state.Selection[0] < 0 ? "" : state.CatalogIds[state.Selection[0]];
            List<KeyboardPartDefinition> result = new List<KeyboardPartDefinition>();
            foreach (KeyboardPartDefinition part in _parts)
            {
                bool used = part is KeycapDefinition keycap && keycapIds.Contains(keycap.Id)
                    || part is HousingDefinition housing && housing.MaterialId == housingId
                    || part is PlateDefinition plate && plate.MaterialId == state.PlateMaterialId;
                if (part is SwitchDefinition axis)
                    foreach (SwitchType type in state.SwitchTypes) used |= axis.Data.Type == type;
                if (used && (!(part is KeycapDefinition candidate) || candidate.Ability != null)) result.Add(part);
            }
            return result;
        }
    }
}
