using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tournament
{
    [RequireComponent(typeof(Image))]
    public sealed class TournamentInventoryItemView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Item")]
        [SerializeField] private Image _background;
        [SerializeField] private Image _thumbnail;
        [SerializeField] private Text _count;
        [SerializeField] private Text _status;
        [SerializeField] private Text _fallback;
        private KeyboardPartDefinition _part;
        private Texture2D _thumbnailTexture;
        private Sprite _thumbnailSprite;

        public event Action<KeyboardPartDefinition> LeftClicked;
        public event Action<KeyboardPartDefinition> RightClicked;
        public event Action<KeyboardPartDefinition> Hovered;
        public event Action HoverEnded;

        /// <summary>part의 보유·장착 수량과 thumbnail을 표시한다. selected를 배경에 반영하며 게임 상태는 변경하지 않는다.</summary>
        public void SetState(KeyboardPartDefinition part, int owned, int equipped, bool selected, Texture2D thumbnail)
        {
            _part = part;
            if (_thumbnailTexture != thumbnail)
            {
                if (_thumbnailSprite != null) Destroy(_thumbnailSprite);
                _thumbnailTexture = thumbnail;
                _thumbnailSprite = thumbnail != null
                    ? Sprite.Create(thumbnail, new Rect(0f, 0f, thumbnail.width, thumbnail.height), new Vector2(0.5f, 0.5f))
                    : null;
                _thumbnail.sprite = _thumbnailSprite;
            }
            _thumbnail.gameObject.SetActive(thumbnail != null);
            _fallback.gameObject.SetActive(thumbnail == null);
            _fallback.text = part is KeycapDefinition ? "KEY" : part is SwitchDefinition ? "SW" : part is PlateDefinition ? "PL" : "CASE";
            _count.text = part is KeycapDefinition ? $"×{owned - equipped}" : $"Lv.{equipped}/3";
            _status.text = selected ? "선택 중" : equipped > 0 ? $"적용 {equipped}" : "";
            _background.color = selected ? new Color32(34, 57, 111, 255)
                : equipped > 0 ? new Color32(13, 28, 66, 255) : new Color32(1, 7, 54, 255);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_part == null || !isActiveAndEnabled) return;
            if (eventData.button == PointerEventData.InputButton.Left || eventData.button == PointerEventData.InputButton.Right)
                AudioManager.instance.PlayButtonSound();
            if (eventData.button == PointerEventData.InputButton.Left) LeftClicked?.Invoke(_part);
            else if (eventData.button == PointerEventData.InputButton.Right) RightClicked?.Invoke(_part);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_part != null) Hovered?.Invoke(_part);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HoverEnded?.Invoke();
        }

        void OnDestroy()
        {
            if (_thumbnailSprite != null) Destroy(_thumbnailSprite);
        }
    }
}
