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

        /// <summary>type과 count로 상품 이름, 효과 및 특수 키캡의 색상과 무늬를 화면에 표시한다.</summary>
        public void Show(SpecialKeycapType type, int count)
        {
            _title.text = $"{TournamentView.GetName(type)} 키캡 × {count}";
            _description.text = TournamentView.GetDescription(type);
            _keycap.color = type == SpecialKeycapType.Explosion ? new Color32(188, 2, 2, 255)
                : type == SpecialKeycapType.Bonus ? new Color32(242, 169, 0, 255) : new Color32(247, 238, 216, 255);
            _mark.gameObject.SetActive(type == SpecialKeycapType.Row || type == SpecialKeycapType.Column || type == SpecialKeycapType.All);
            _mark.sizeDelta = type == SpecialKeycapType.Row ? new Vector2(110, 32)
                : type == SpecialKeycapType.Column ? new Vector2(32, 100) : new Vector2(110, 100);
            _symbol.text = type == SpecialKeycapType.Bonus ? "×2" : type == SpecialKeycapType.Explosion ? "+" : "";
        }
    }
}
