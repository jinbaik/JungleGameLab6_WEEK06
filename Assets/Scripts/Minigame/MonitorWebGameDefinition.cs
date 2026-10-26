using System;

using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(fileName = "WebApp", menuName = "Keyboard/Mini Games/Web App")]
    public sealed class MonitorWebGameDefinition : MonitorMiniGameDefinition
    {
        [Header("Web Page")]
        [SerializeField] private string _url = "https://jazzkeys.plan8.co/";
        [SerializeField] private Vector2Int _resolution = new Vector2Int(1280, 720);

        public string Url => _url;
        public Vector2Int Resolution => _resolution;

        /// <summary>
        /// URL이 HTTP 또는 HTTPS이고 화면 해상도가 허용 범위인지 검사해 반환한다.
        /// 저장된 URL과 해상도를 사용하며 에셋이나 브라우저 실행 상태는 변경하지 않는다.
        /// </summary>
        public bool IsValid()
        {
            return Uri.TryCreate(_url, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && _resolution.x >= 320 && _resolution.x <= 1920
                && _resolution.y >= 240 && _resolution.y <= 1080;
        }
    }
}
