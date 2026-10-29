using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tournament
{
    [RequireComponent(typeof(Image))]
    public sealed class TournamentActionButton : MonoBehaviour, IPointerClickHandler
    {
        [Header("Button")]
        [SerializeField] private int _index;
        [SerializeField] private Text _label;
        [SerializeField] private Image _image;
        [SerializeField] private Color _normalColor = new Color(0.68f, 0.48f, 0.45f);
        [SerializeField] private Color _disabledColor = new Color(0.57f, 0.55f, 0.52f);
        private bool _interactable = true;
        public event Action<int> Clicked;

        /// <summary>text와 interactable로 버튼 문구와 조작 가능 상태를 변경한다.</summary>
        public void SetState(string text, bool interactable)
        {
            _label.text = text;
            _interactable = interactable;
            _image.color = interactable ? _normalColor : _disabledColor;
            _label.color = interactable ? Color.white : new Color(0.85f, 0.83f, 0.78f);
        }

        /// <summary>eventData의 왼쪽 클릭을 확인하여 조작 가능한 버튼의 index를 C# 이벤트로 전달한다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_interactable && isActiveAndEnabled && eventData.button == PointerEventData.InputButton.Left)
            {
                AudioManager.instance.PlayButtonSound();
                Clicked?.Invoke(_index);
            }
        }
    }
}
