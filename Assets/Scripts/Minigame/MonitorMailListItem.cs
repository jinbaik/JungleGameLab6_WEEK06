using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class MonitorMailListItem : MonoBehaviour, IPointerClickHandler
    {
        [Header("Row UI")]
        [SerializeField] private Text _sender;
        [SerializeField] private Text _subject;
        [SerializeField] private Text _date;
        [SerializeField] private Image _background;
        [SerializeField] private GameObject _selectionMark;

        [Header("Colors")]
        [SerializeField] private Color _normalColor = new Color32(52, 76, 94, 255);
        [SerializeField] private Color _selectedColor = new Color32(40, 66, 84, 255);
        [SerializeField] private Color _folderColor = new Color32(30, 39, 52, 255);
        private bool _isFolder;

        public MonitorMailEntry Message { get; private set; }
        public string FolderKey { get; private set; }
        public event Action<MonitorMailListItem> Selected;

        /// <summary>
        /// message의 제목과 날짜를 하위 목록에 표시하고 발신자 이름은 숨긴다.
        /// 원본 SO와 독립된 수신 기록을 보관하고 선택 강조를 해제한다.
        /// </summary>
        public void Initialize(MonitorMailEntry message)
        {
            Message = message;
            _isFolder = false;
            _sender.gameObject.SetActive(false);
            _subject.text = message.Subject;
            _date.text = message.Date;
            RectTransform subjectRect = (RectTransform)_subject.transform;
            subjectRect.anchorMin = new Vector2(0.1f, 0.3f);
            subjectRect.anchorMax = new Vector2(0.94f, 0.94f);
            RectTransform dateRect = (RectTransform)_date.transform;
            dateRect.anchorMin = new Vector2(0.1f, dateRect.anchorMin.y);
            ((RectTransform)transform).sizeDelta = new Vector2(0f, 88f);
            SetSelected(false);
        }

        /// <summary>
        /// key와 label 및 count로 고객 또는 휴지통의 접기 버튼을 구성한다.
        /// expanded에 따라 기호를 표시하고 메일 대신 폴더 식별자를 보관한다.
        /// </summary>
        public void InitializeFolder(string key, string label, int count, bool expanded)
        {
            FolderKey = key;
            _isFolder = true;
            _sender.gameObject.SetActive(false);
            _date.gameObject.SetActive(false);
            RectTransform title = (RectTransform)_subject.transform;
            title.anchorMin = new Vector2(0.04f, 0f);
            title.anchorMax = new Vector2(0.96f, 1f);
            _subject.fontSize = 19;
            _subject.fontStyle = FontStyle.Bold;
            _subject.text = $"{(expanded ? "▼" : "▶")} {label} ({count})";
            ((RectTransform)transform).sizeDelta = new Vector2(0f, 52f);
            SetSelected(false);
        }

        /// <summary>
        /// selected에 따라 행 배경과 왼쪽 강조선을 변경한다.
        /// 메일 데이터와 목록 순서는 변경하지 않는다.
        /// </summary>
        public void SetSelected(bool selected)
        {
            _background.color = _isFolder ? _folderColor : selected ? _selectedColor : _normalColor;
            _selectionMark.SetActive(selected);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left
                && !Game.Tutorials.FirstEncounterTutorial.IsBlocking)
            {
                if (AudioManager.instance != null) AudioManager.instance.PlayButtonSound();
                Selected?.Invoke(this);
            }
        }
    }
}
