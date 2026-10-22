using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace KeyboardModeling.Assembly
{
    public sealed class AssemblyCustomizationStorage : MonoBehaviour
    {
        [Header("Legacy Storage")]
        [SerializeField] private KeyboardCustomizationStorage _storage;
        [SerializeField] private KeyboardMaterialCatalog _materialCatalog;

        [Header("Assembly Targets")]
        [SerializeField] private AssemblyCatalogUI _catalog;
        [SerializeField] private AssemblyDragInput _dragInput;
        [SerializeField] private AssemblyImageCustomization _images;
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<string> _ids = new List<string>();

        public KeyboardCustomizationStorage Storage => _storage;

        void Awake()
        {
            Initialize();
        }

        /// <summary>기존 저장소에 조립 상태 변환 함수를 연결한다. 입력값 없이 기존 파일 경로와 JSON 구조를 재사용한다.</summary>
        public void Initialize()
        {
            _storage.SetStateHandlers(Capture, Apply);
        }

        /// <summary>현재 카탈로그를 읽어 재질과 기존 ID 대응을 준비한다. 입력값 없이 조회 목록만 갱신한다.</summary>
        private void BuildRegistry()
        {
            _materials.Clear();
            _ids.Clear();
            if (_materialCatalog != null)
                for (int index = 0; index < _materialCatalog.Materials.Count; index++)
                {
                    _materials.Add(_materialCatalog.Materials[index]);
                    _ids.Add(_materialCatalog.Ids[index]);
                }
            foreach (Material material in _catalog.Materials) RegisterMaterial(material);
            foreach (AssemblyCatalogEntry entry in _catalog.Entries) RegisterMaterial(entry.KeycapMaterial);
            foreach (MeshRenderer renderer in _catalog.CaseRenderers) RegisterMaterial(renderer.sharedMaterial);
            RegisterMaterial(_catalog.PlateRenderer.sharedMaterial);
        }

        /// <summary>material이 아직 없으면 이름 기반 ID로 보완한다. 기존 카탈로그 ID와 재질은 유지한다.</summary>
        private void RegisterMaterial(Material material)
        {
            if (material == null || _materials.Contains(material)) return;
            string id = "material:" + Uri.EscapeDataString(material.name);
            if (_ids.Contains(id)) throw new InvalidOperationException("Duplicate material name: " + material.name);
            _materials.Add(material);
            _ids.Add(id);
        }

        /// <summary>현재 장착 키캡과 축, 재질, LED 및 사진을 기존 저장 데이터로 반환한다. 재고와 분리 파츠 위치는 저장하지 않는다.</summary>
        private KeyboardCustomizationState Capture()
        {
            _dragInput.CancelDrag();
            BuildRegistry();
            AssemblyPartSlot[] keys = _dragInput.Slots.Where(s => !s.IsSwitch).ToArray();
            AssemblyPartSlot[] switches = _dragInput.Slots.Where(s => s.IsSwitch).ToArray();
            var paths = new List<string>();
            var materials = new List<int>();
            for (int index = 0; index < _catalog.CaseRenderers.Count; index++)
            {
                paths.Add("Case:" + index);
                materials.Add(_materials.IndexOf(_catalog.CaseRenderers[index].sharedMaterial));
            }
            foreach (AssemblyPartSlot slot in keys)
            {
                paths.Add("Keycap:" + slot.Key);
                materials.Add(slot.IsInstalled ? _materials.IndexOf(slot.InstalledPart.CatalogEntry.KeycapMaterial) : -1);
            }
            AssemblyLEDSettings led = _catalog.LEDSettings;
            return new KeyboardCustomizationState(led.LEDEnabled, led.LEDColor, led.LEDIntensity,
                new[] { materials[0], -1, 0, 0 }, _ids.ToArray(), paths.ToArray(), Enumerable.Repeat(1, paths.Count).ToArray(),
                materials.ToArray(), switches.Select(s => s.IsInstalled ? s.InstalledPart.CatalogEntry.SwitchData.Type : default).ToArray(),
                default, led.CurrentLEDPattern, led.LEDSpeed, _images.CaptureImages(),
                _ids[_materials.IndexOf(_catalog.PlateRenderer.sharedMaterial)], PlateEffect.None,
                keys.Select(s => s.IsInstalled).ToArray(), switches.Select(s => s.IsInstalled).ToArray(),
                keys.Select(s => s.Key).ToArray(), keys.Select(s => s.IsInstalled && s.InstalledPart.CatalogEntry.KeycapDefinition != null
                    ? s.InstalledPart.CatalogEntry.KeycapDefinition.Id : "").ToArray());
        }

        /// <summary>state의 기존 저장 항목을 검증하고 현재 조립 키보드에 복원한다. 호환되지 않는 배치나 재고는 적용 전에 거부한다.</summary>
        private void Apply(KeyboardCustomizationState state)
        {
            _dragInput.CancelDrag();
            BuildRegistry();
            AssemblyPartSlot[] keys = _dragInput.Slots.Where(s => !s.IsSwitch).ToArray();
            AssemblyPartSlot[] switches = _dragInput.Slots.Where(s => s.IsSwitch).ToArray();
            int cases = _catalog.CaseRenderers.Count;
            if (state == null || state.Version != 6 || state.Selection == null || state.Selection.Length != 4
                || state.KeycapKeys == null || !state.KeycapKeys.SequenceEqual(keys.Select(s => s.Key))
                || state.KeycapInstalled == null || state.KeycapInstalled.Length != keys.Length
                || state.KeycapDefinitionIds == null || state.KeycapDefinitionIds.Length != keys.Length
                || state.SwitchInstalled == null || state.SwitchInstalled.Length != switches.Length
                || state.SwitchTypes == null || state.SwitchTypes.Length != switches.Length
                || state.RendererPaths == null || state.RendererPaths.Length != cases + keys.Length
                || state.MaterialIndices == null || state.MaterialIndices.Length != cases + keys.Length
                || state.CatalogIds == null || state.SlotCounts == null || state.SlotCounts.Length != cases + keys.Length
                || state.SlotCounts.Any(c => c != 1) || !Enum.IsDefined(typeof(LEDPattern), state.LedPattern)
                || float.IsNaN(state.LedIntensity) || float.IsInfinity(state.LedIntensity)
                || float.IsNaN(state.LedSpeed) || float.IsInfinity(state.LedSpeed))
                throw new InvalidOperationException("Saved keyboard layout is incompatible with this assembly scene.");
            var caseMaterials = new Material[cases];
            for (int index = 0; index < cases; index++)
            {
                if (state.RendererPaths[index] != "Case:" + index) throw new InvalidOperationException("Saved case layout is incompatible.");
                caseMaterials[index] = ResolveMaterial(state, state.MaterialIndices[index]);
            }
            int plateIndex = _ids.IndexOf(state.PlateMaterialId);
            if (plateIndex < 0) throw new InvalidOperationException("Saved plate material is unavailable.");
            var desired = new Dictionary<AssemblyPartSlot, AssemblyCatalogEntry>();
            for (int index = 0; index < keys.Length; index++)
            {
                if (state.RendererPaths[cases + index] != "Keycap:" + keys[index].Key)
                    throw new InvalidOperationException("Saved keycap layout is incompatible.");
                if (!state.KeycapInstalled[index]) continue;
                Material material = ResolveMaterial(state, state.MaterialIndices[cases + index]);
                string id = state.KeycapDefinitionIds[index];
                AssemblyCatalogEntry entry = _catalog.Entries.FirstOrDefault(e => !e.IsSwitch &&
                    (string.IsNullOrEmpty(id) ? !e.IsArtisan && e.KeycapMaterial == material
                        : e.KeycapDefinition != null && e.KeycapDefinition.Id == id));
                if (entry == null || (entry.IsArtisan && keys[index].KeycapShape != _dragInput.KeycapSlot.KeycapShape))
                    throw new InvalidOperationException("Saved keycap is unavailable or incompatible.");
                if (!switches.Where((s, i) => state.SwitchInstalled[i]).Any(s => s.Key == keys[index].Key))
                    throw new InvalidOperationException("Saved keycap requires an installed switch.");
                desired.Add(keys[index], entry);
            }
            for (int index = 0; index < switches.Length; index++)
            {
                if (!state.SwitchInstalled[index]) continue;
                AssemblyCatalogEntry entry = _catalog.Entries.FirstOrDefault(e => e.IsSwitch && e.SwitchData.Type == state.SwitchTypes[index]);
                if (entry == null) throw new InvalidOperationException("Saved switch is unavailable.");
                desired.Add(switches[index], entry);
            }
            foreach (IGrouping<AssemblyCatalogEntry, KeyValuePair<AssemblyPartSlot, AssemblyCatalogEntry>> group in desired.GroupBy(p => p.Value))
            {
                int returned = _dragInput.Slots.Count(s => s.IsInstalled && s.InstalledPart.CatalogEntry == group.Key);
                if (group.Count() > group.Key.AvailableQuantity + returned)
                    throw new InvalidOperationException("Not enough inventory to restore the saved parts.");
            }
            Texture2D[] textures = _images.PrepareImages(state.ImageState);
            bool restored = false;
            try
            {
                _images.ClearPhotoImagePreview();
                foreach (AssemblyPartSlot slot in keys.Concat(switches))
                    if (slot.IsInstalled) slot.InstalledPart.CatalogEntry.StorePart(slot.InstalledPart, true);
                foreach (AssemblyPartSlot slot in switches.Concat(keys))
                    if (desired.TryGetValue(slot, out AssemblyCatalogEntry entry)) RestorePart(slot, entry);
                for (int index = 0; index < cases; index++) _catalog.CaseRenderers[index].sharedMaterial = caseMaterials[index];
                _catalog.PlateRenderer.sharedMaterial = _materials[plateIndex];
                AssemblyLEDSettings led = _catalog.LEDSettings;
                led.SetLEDColor(state.LedColor);
                led.SetLEDPattern(state.LedPattern);
                led.SetLEDIntensity(state.LedIntensity);
                led.SetLEDSpeed(state.LedSpeed);
                led.SetLEDEnabled(state.LedEnabled);
                _images.RestoreImages(state.ImageState, textures);
                restored = true;
                _catalog.RefreshInventory();
            }
            finally
            {
                if (!restored) foreach (Texture2D texture in textures) if (texture != null) Destroy(texture);
            }
        }

        /// <summary>state의 index를 현재 등록 재질로 해석해 반환한다. 누락되거나 잘못된 ID는 예외로 알린다.</summary>
        private Material ResolveMaterial(KeyboardCustomizationState state, int index)
        {
            if (index < 0 || index >= state.CatalogIds.Length) throw new InvalidOperationException("Saved material index is invalid.");
            int mapped = _ids.IndexOf(state.CatalogIds[index]);
            if (mapped < 0) throw new InvalidOperationException("Saved material is unavailable.");
            return _materials[mapped];
        }

        /// <summary>entry의 실물을 slot에 재장착한다. 기존 재고 예약과 집기 등록을 사용하며 파일은 변경하지 않는다.</summary>
        private void RestorePart(AssemblyPartSlot slot, AssemblyCatalogEntry entry)
        {
            if (!entry.TryReserve()) throw new InvalidOperationException("Saved part inventory is unavailable.");
            AssemblyDraggablePart part = entry.TakeStoredPart();
            if (part == null) part = Instantiate(entry.Prefab, slot.transform.position, slot.transform.rotation);
            part.Initialize(slot, part.PartCollider, false);
            part.SetCatalogEntry(entry);
            part.PrepareInventoryPart(entry, _dragInput.KeycapSlot.KeycapShape);
            part.gameObject.SetActive(true);
            bool installed = slot.Install(part);
            entry.FinishReservation(installed);
            if (!installed) throw new InvalidOperationException("Saved part could not be installed.");
            _dragInput.RegisterRestoredPart(part);
        }
    }
}
