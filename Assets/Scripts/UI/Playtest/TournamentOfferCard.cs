using UnityEngine;
using UnityEngine.UI;

namespace Game.Tournament
{
    public sealed class TournamentOfferCard : MonoBehaviour
    {
        [Header("Card")]
        [SerializeField] private Text _title;
        [SerializeField] private Text _description;
        [SerializeField] private Image _keycap;
        [SerializeField] private RectTransform _mark;
        [SerializeField] private Text _symbol;

        /// <summary>part의 이름, 구매 수량과 능력을 카드에 표시한다. null 상품은 빈 슬롯으로 표시하며 게임 상태는 변경하지 않는다.</summary>
        public void Show(KeyboardPartDefinition part)
        {
            _title.text = part != null ? $"{part.DisplayName} ×{part.Quantity}" : "상품 없음";
            _description.text = part != null ? part.Description : "작업실에서 부품을 선택해 주세요.";
            _mark.gameObject.SetActive(false);
            _symbol.text = part is KeycapDefinition ? "KEY" : part is SwitchDefinition ? "SW" : part is PlateDefinition ? "PL" : part is HousingDefinition ? "CASE" : "";
            Material material = part is KeycapDefinition keycap ? keycap.Material : part is HousingDefinition housing ? housing.Material : part is PlateDefinition plate ? plate.Data.Material : null;
            _keycap.color = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : new Color32(225, 225, 225, 255);
        }
    }
}
