using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class ThanksForPlayingHudView : MonoBehaviour
    {
        [Header("Runtime State")]
        private KeyboardInteractionController _interactionController;
        private bool _shown;

        /// <summary>
        /// controller의 마지막 구역 완료 이벤트를 기존 지갑 HUD에 연결한다.
        /// HUD에 표시 컴포넌트를 추가하거나 재사용하며 중복 이벤트 연결을 막는다.
        /// </summary>
        public static void Attach(KeyboardInteractionController controller)
        {
            Canvas hud = FindFirstObjectByType<WalletHudController>().GetComponentInParent<Canvas>();
            ThanksForPlayingHudView view = hud.GetComponent<ThanksForPlayingHudView>();
            if (view == null)
                view = hud.gameObject.AddComponent<ThanksForPlayingHudView>();
            if (view._interactionController != null)
                view._interactionController.FinalRegionCompleted -= view.Show;
            view._interactionController = controller;
            controller.FinalRegionCompleted += view.Show;
        }

        void OnDestroy()
        {
            if (_interactionController != null)
                _interactionController.FinalRegionCompleted -= Show;
        }

        /// <summary>
        /// 마지막 구역 완료 시 기존 HUD를 검정색 전체 화면과 흰색 문구로 덮는다.
        /// HUD의 RectTransform 크기를 사용하여 표시하고 중복 생성을 막는 상태를 저장한다.
        /// </summary>
        private void Show()
        {
            if (_shown) return;
            _shown = true;
            GameObject panel = new GameObject("ThanksForPlaying", typeof(RectTransform), typeof(Canvas), typeof(Image));
            panel.transform.SetParent(transform, false);
            Stretch(panel.GetComponent<RectTransform>());
            Canvas canvas = panel.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            Image background = panel.GetComponent<Image>();
            background.color = Color.black;
            background.raycastTarget = false;

            GameObject message = new GameObject("Message", typeof(RectTransform), typeof(Text));
            message.transform.SetParent(panel.transform, false);
            Stretch(message.GetComponent<RectTransform>());
            Text text = message.GetComponent<Text>();
            text.text = "thanks for playing";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        /// <summary>
        /// rect를 부모의 전체 크기에 맞춘다.
        /// 앵커와 오프셋 및 배율을 설정하여 화면 크기에 맞게 영역을 채운다.
        /// </summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
