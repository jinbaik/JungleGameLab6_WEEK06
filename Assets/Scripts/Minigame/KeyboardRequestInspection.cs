using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed class KeyboardRequestInspection
    {
        private const string MATERIAL_ID_TAG = "KeyboardMaterialId";

        public bool MissingLower { get; private set; }
        public bool MissingPcb { get; private set; }
        public bool MissingPlate { get; private set; }
        public bool MissingUpper { get; private set; }
        public int MissingKeycaps { get; private set; }
        public int MissingSwitches { get; private set; }
        public int IncorrectSwitches { get; private set; }
        public bool IncorrectLowerMaterial { get; private set; }
        public bool IncorrectUpperMaterial { get; private set; }
        public int IncorrectGlassKeycaps { get; private set; }
        public bool Passed => !MissingLower && !MissingPcb && !MissingPlate && !MissingUpper
            && !IncorrectLowerMaterial && !IncorrectUpperMaterial && IncorrectGlassKeycaps == 0
            && MissingKeycaps == 0 && MissingSwitches == 0 && IncorrectSwitches == 0;
        public string FailureReason { get; private set; }

        /// <summary>
        /// 외부에서 미완성 검수 결과를 생성하지 않도록 생성 경로를 제한한다.
        /// 입력 없이 검사 메서드가 채울 결과를 만들며 키보드 상태는 변경하지 않는다.
        /// </summary>
        private KeyboardRequestInspection() { }

        /// <summary>
        /// 제출된 body와 소유 assembly의 실제 슬롯 및 조립 상태를 definition 조건으로 검사한다.
        /// 부족분을 독립적인 결과로 반환하며 설정 오류는 예외로 알리고 기본값 복원이나 부품 설치는 하지 않는다.
        /// </summary>
        public static KeyboardRequestInspection Inspect(Rigidbody body, KeyboardAssemblyState assembly,
            KeyboardRequestDefinition definition)
        {
            if (body == null || definition == null)
                throw new InvalidOperationException("의뢰 검수에 키보드와 의뢰 정의가 필요합니다.");
            var inspection = new KeyboardRequestInspection();
            if (!definition.RequiresAllRequiredSwitchSlots && !definition.RequiresCompleteAssembly && !definition.RequiresGlassSurfaces)
            {
                if (body.GetComponent<KeyboardPickupTarget>() == null)
                    throw new InvalidOperationException("제출 대상이 키보드 본체가 아닙니다.");
                inspection.FailureReason = string.Empty;
                return inspection;
            }
            if (assembly == null || !assembly.Initialized)
                throw new InvalidOperationException("의뢰 검수에 초기화된 키보드 조립 정보가 필요합니다.");
            KeyboardCustomization customization = assembly.GetComponent<KeyboardCustomization>();
            KeyboardInputController input = assembly.GetComponent<KeyboardInputController>();
            if (customization == null || input == null)
                throw new InvalidOperationException("의뢰 검수의 슬롯 또는 입력 바인딩 연결이 없습니다.");
            string glassId = definition.RequiresGlassSurfaces ? GetGlassMaterialId(definition) : null;

            Transform root = body.transform;
            if (definition.RequiresCompleteAssembly)
            {
                inspection.MissingLower = !IsStructureInstalled(assembly, AssemblyStructurePart.Lower, root, assembly.LowerInstalled);
                inspection.MissingPcb = !IsStructureInstalled(assembly, AssemblyStructurePart.Pcb, root, assembly.PcbPlaced);
                inspection.MissingPlate = !IsStructureInstalled(assembly, AssemblyStructurePart.Plate, root, assembly.PlateInstalled);
                inspection.MissingUpper = !IsStructureInstalled(assembly, AssemblyStructurePart.Upper, root, assembly.UpperInstalled);
            }

            if (definition.RequiresGlassSurfaces)
            {
                if (!inspection.MissingLower)
                    inspection.IncorrectLowerMaterial = !HasMatchingSurfaces(customization.CaseRenderers, definition.GlassMaterial, glassId);
                if (!inspection.MissingUpper)
                    inspection.IncorrectUpperMaterial = !HasMatchingSurfaces(assembly.UpperRenderers, definition.GlassMaterial, glassId);
            }

            IReadOnlyDictionary<Key, Transform> layout = input.GetRequiredKeycapSlots();
            if (layout.Count != customization.KeycapCount)
                throw new InvalidOperationException("필수 입력 슬롯과 키보드 슬롯 정의가 일치하지 않습니다.");
            var indices = new Dictionary<Transform, int>();
            for (int index = 0; index < customization.KeycapCount; index++)
            {
                Transform keycap = customization.GetKeycapSlotTransform(index);
                if (keycap == null) throw new InvalidOperationException("필수 키캡 슬롯 참조가 없습니다.");
                indices.Add(keycap, index);
            }
            var switches = new HashSet<KeyboardSwitch>();
            foreach (KeyValuePair<Key, Transform> slot in layout)
            {
                if (!indices.TryGetValue(slot.Value, out int index)
                    || !input.TryGetSwitch(slot.Key, out KeyboardSwitch keyboardSwitch)
                    || keyboardSwitch.transform != customization.GetSwitchSlotTransform(index)
                    || !switches.Add(keyboardSwitch))
                    throw new InvalidOperationException($"필수 슬롯 {slot.Key}의 키캡과 스위치 바인딩이 올바르지 않습니다.");

                bool keycapPresent = customization.IsPcbKeycapInstalled(index) && slot.Value.IsChildOf(root);
                if (definition.RequiresCompleteAssembly && !keycapPresent) inspection.MissingKeycaps++;
                if (definition.RequiresGlassSurfaces && keycapPresent
                    && !MatchesMaterial(customization.GetKeycapSurfaceMaterial(index), definition.GlassMaterial, glassId))
                    inspection.IncorrectGlassKeycaps++;
                if (!definition.RequiresAllRequiredSwitchSlots) continue;
                if (!customization.IsPcbSwitchInstalled(index) || !keyboardSwitch.transform.IsChildOf(root))
                {
                    inspection.MissingSwitches++;
                    continue;
                }
                if (keyboardSwitch.CurrentData == null)
                    throw new InvalidOperationException($"설치된 슬롯 {slot.Key}의 실제 스위치 데이터가 없습니다.");
                if (keyboardSwitch.CurrentData.Type != definition.RequiredSwitchType) inspection.IncorrectSwitches++;
            }

            var reasons = new List<string>();
            if (inspection.MissingLower) reasons.Add("하단부 미설치");
            if (inspection.MissingPcb) reasons.Add("PCB 미설치");
            if (inspection.MissingPlate) reasons.Add("보강판 미설치");
            if (inspection.MissingUpper) reasons.Add("상단부 미설치");
            if (inspection.IncorrectLowerMaterial) reasons.Add("하단부가 유리 재질이 아님");
            if (inspection.IncorrectUpperMaterial) reasons.Add("상단부가 유리 재질이 아님");
            if (inspection.MissingKeycaps > 0) reasons.Add($"키캡 {inspection.MissingKeycaps}개 부족");
            if (inspection.IncorrectGlassKeycaps > 0) reasons.Add($"유리 재질이 아닌 키캡 {inspection.IncorrectGlassKeycaps}개");
            if (inspection.MissingSwitches > 0) reasons.Add($"스위치 {inspection.MissingSwitches}개 부족");
            if (inspection.IncorrectSwitches > 0) reasons.Add(definition.RequiredSwitchType switch
            {
                SwitchType.Red => $"적축이 아닌 스위치 {inspection.IncorrectSwitches}개",
                SwitchType.Sans => $"샌즈 축이 아닌 스위치 {inspection.IncorrectSwitches}개",
                _ => $"요구한 축이 아닌 스위치 {inspection.IncorrectSwitches}개"
            });
            inspection.FailureReason = string.Join("\n", reasons);
            return inspection;
        }

        /// <summary>
        /// definition의 유리 원본 참조가 기존 카탈로그에 등록되어 있는지 검증하고 안정적인 ID를 반환한다.
        /// 원본의 복제용 식별 태그가 누락되거나 등록이 잘못되면 고객 실패 대신 설정 오류로 알린다.
        /// </summary>
        private static string GetGlassMaterialId(KeyboardRequestDefinition definition)
        {
            KeyboardMaterialCatalog catalog = definition.MaterialCatalog;
            if (catalog == null || definition.GlassMaterial == null || catalog.Materials.Count != catalog.Ids.Count)
                throw new InvalidOperationException("의뢰의 유리 재질 또는 카탈로그 연결이 없습니다.");
            for (int index = 0; index < catalog.Materials.Count; index++)
            {
                if (catalog.Materials[index] != definition.GlassMaterial) continue;
                string id = catalog.Ids[index];
                if (string.IsNullOrWhiteSpace(id) || definition.GlassMaterial.GetTag(MATERIAL_ID_TAG, false, string.Empty) != id)
                    throw new InvalidOperationException("유리 원본 재질의 카탈로그 ID 태그가 올바르지 않습니다.");
                return id;
            }
            throw new InvalidOperationException("유리 원본 재질이 카탈로그에 등록되지 않았습니다.");
        }

        /// <summary>
        /// renderers에 명시된 외장들의 첫 재질 슬롯이 required 또는 같은 materialId의 복제본인지 반환한다.
        /// 부속 슬롯은 검사하지 않으며 빈 목록과 렌더러 누락은 설정 오류로 알린다.
        /// </summary>
        private static bool HasMatchingSurfaces(IReadOnlyList<MeshRenderer> renderers, Material required, string materialId)
        {
            if (renderers == null || renderers.Count == 0) throw new InvalidOperationException("외장 재질 렌더러 연결이 없습니다.");
            bool matched = true;
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null) throw new InvalidOperationException("외장 재질 렌더러 참조가 없습니다.");
                if (!MatchesMaterial(renderer.sharedMaterial, required, materialId)) matched = false;
            }
            return matched;
        }

        /// <summary>
        /// 실제 적용된 material의 원본 참조 또는 복제된 카탈로그 ID를 required와 비교해 일치 여부를 반환한다.
        /// 재질 이름과 외관은 사용하지 않으며 재질 자체가 없으면 설정 오류로 알린다.
        /// </summary>
        private static bool MatchesMaterial(Material material, Material required, string materialId)
        {
            if (material == null) throw new InvalidOperationException("설치된 부품에 실제 적용 재질이 없습니다.");
            return material == required || material.GetTag(MATERIAL_ID_TAG, false, string.Empty) == materialId;
        }

        /// <summary>
        /// assembly의 kind에 명시적으로 연결된 구조 부품이 root에 실제 설치되어 있는지 반환한다.
        /// installed 상태와 실물의 소속 및 활성 여부를 함께 읽으며 참조 누락은 설정 오류로 알린다.
        /// </summary>
        private static bool IsStructureInstalled(KeyboardAssemblyState assembly, AssemblyStructurePart kind,
            Transform root, bool installed)
        {
            IReadOnlyList<Transform> parts = assembly.GetStructureParts(kind);
            if (parts == null || parts.Count == 0) throw new InvalidOperationException($"{kind} 구조 슬롯 정의가 없습니다.");
            bool present = installed;
            foreach (Transform part in parts)
            {
                if (part == null) throw new InvalidOperationException($"{kind} 구조 슬롯 참조가 없습니다.");
                present &= part.IsChildOf(root) && part.gameObject.activeInHierarchy;
            }
            return present;
        }
    }
}
