using UnityEngine;

namespace Game.Player
{
    public sealed partial class KeyboardEditOverlayUI
    {
        private RectTransform _legacyCatalogStorage;
        private RectTransform _trayModeControls;

        /// <summary>새 트레이를 사용하는 화면의 이전 카탈로그 표시만 정리한다. 공용 참조와 동작 컴포넌트는 보관하고 모드 버튼의 기존 입력은 유지한다.</summary>
        public void HideLegacyCatalogDisplay()
        {
            if (_legacyCatalogStorage == null)
            {
                _legacyCatalogStorage = new GameObject("LegacyCatalogStorage", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
                _legacyCatalogStorage.SetParent(transform, false);
                CanvasGroup group = _legacyCatalogStorage.GetComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                _legacyCatalogStorage.gameObject.SetActive(false);
                _trayModeControls = new GameObject("WorkshopModeControls", typeof(RectTransform)).GetComponent<RectTransform>();
                _trayModeControls.SetParent(transform, false);
                _trayModeControls.anchorMin = _trayModeControls.anchorMax = Vector2.up;
                _trayModeControls.pivot = Vector2.zero;
                _trayModeControls.anchoredPosition = new Vector2(24f, -98f);
                _trayModeControls.sizeDelta = new Vector2(340f, 30f);
                KeyboardEditButton[] buttons = MoveDrawerModeButtons(_trayModeControls);
                for (int index = 0; index < buttons.Length; index++)
                {
                    RectTransform button = (RectTransform)buttons[index].transform;
                    button.anchorMin = button.anchorMax = button.pivot = Vector2.zero;
                    button.anchoredPosition = new Vector2(index * 170f, 0f);
                    button.sizeDelta = new Vector2(166f, 30f);
                }
            }
            if (_keyboardEditPanel != null) _keyboardEditPanel.gameObject.SetActive(false);
            ParkLegacyCatalogElement(_caseMaterialViewport);
            ParkLegacyCatalogElement(_pcbViewport);
            ParkLegacyCatalogElement(_assemblyActions);
            if (_upperCard != null) ParkLegacyCatalogElement(_upperCard.transform);
            if (_lowerCard != null) ParkLegacyCatalogElement(_lowerCard.transform);
            if (_pcbWorldDrag != null) ParkLegacyCatalogElement(_pcbWorldDrag.transform);
            _trayModeControls.gameObject.SetActive(_pullerController.IsEditing);
        }

        /// <summary>element가 아직 이전 오버레이 바로 아래에 있으면 비활성 보관 영역으로 옮긴다. 새 트레이로 이전된 요소와 해당 참조는 변경하지 않는다.</summary>
        private void ParkLegacyCatalogElement(Transform element)
        {
            if (element != null && element.parent == transform)
                element.SetParent(_legacyCatalogStorage, false);
        }
    }
}
