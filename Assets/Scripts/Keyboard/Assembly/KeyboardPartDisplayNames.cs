using System.Collections.Generic;

using UnityEngine;

using Game.Tournament;

namespace KeyboardModeling.Assembly
{
    public static class KeyboardPartDisplayNames
    {
        private static readonly Dictionary<string, string> _materialNames = new Dictionary<string, string>
        {
            { "AnodizedAluminum", "알루미늄" },
            { "Porcelain_PBT", "도자기" },
            { "KeycapIronAlloy", "철 합금" },
            { "KeycapGold", "금" },
            { "KeycapWoodWalnut", "호두나무" },
            { "KeycapGlassClear", "유리" },
            { "KeycapMarbleWhite", "대리석" },
            { "KeycapCelestial", "우주" },
            { "NeonGraphite", "네온" },
            { "GundamPanel_Blue", "파란 건담 패널" },
            { "GundamPanel_Red", "빨간 건담 패널" },
            { "GundamPanel_White", "흰 건담 패널" },
            { "GundamPanel_Yellow", "노란 건담 패널" },
            { "AtomicPurple", "보라색" },
            { "Aurora", "오로라" },
            { "Lava", "용암" },
            { "Sakura", "벚꽃" },
            { "SunsetVapor", "노을" },
            { "Toon", "만화풍" },
            { "PCB_Black", "검은색" },
            { "PCB_Green", "초록색" },
            { "PCB_White", "흰색" }
        };
        private static readonly HashSet<string> _missingNames = new HashSet<string>();

        /// <summary>material의 원본 에셋 이름에 대응하는 한글 표시 이름을 반환한다. 원본과 ID는 유지하며 미등록 이름은 한 번 경고하고 기본 표시 이름을 반환한다.</summary>
        public static string MaterialName(Material material)
        {
            if (material == null) return "기본";
            if (_materialNames.TryGetValue(material.name, out string displayName)) return displayName;
            if (_missingNames.Add(material.name))
                Debug.LogWarning($"부품 표시 이름 미등록: {material.name}", material);
            return "기타 재질";
        }

        /// <summary>definition의 기존 표시 이름을 우선 사용하고 없으면 material의 한글 이름으로 키캡 이름을 반환한다. 남아 있는 영문 상품 표기만 한글로 바꾸며 원본 정의는 유지한다.</summary>
        public static string KeycapName(KeycapDefinition definition, Material material)
        {
            return definition != null ? definition.DisplayName.Replace("Phoebe", "피비").Replace("PBT", "피비티")
                : MaterialName(material) + " 키캡";
        }

        /// <summary>type에 대응하는 스위치의 한글 카드 이름을 반환한다. 실제 축 타입과 사운드 및 저장 식별자는 변경하지 않는다.</summary>
        public static string SwitchName(SwitchType type)
        {
            return type switch
            {
                SwitchType.Blue => "청축",
                SwitchType.Red => "적축",
                SwitchType.Brown => "갈축",
                SwitchType.Sans => "샌즈축",
                SwitchType.Pop => "팝축",
                SwitchType.Typewriter => "타자기축",
                SwitchType.FireWorks => "불꽃놀이축",
                SwitchType.Animal => "동물축",
                _ => "기타 스위치"
            };
        }
    }
}
