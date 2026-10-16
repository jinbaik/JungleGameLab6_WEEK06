using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardLegendRenderer : MonoBehaviour
    {
        [Header("Legend Material")]
        [SerializeField] private Material _materialTemplate;
        private Material _runtimeMaterial;
        private Font _font;

        void Awake()
        {
            TextMesh[] labels = GetComponentsInChildren<TextMesh>(true);

            _font = labels[0].font;
            _runtimeMaterial = new Material(_materialTemplate);

            SynchronizeFontTexture();

            foreach (TextMesh label in labels)
            {
                label.GetComponent<MeshRenderer>().sharedMaterial =
                    _runtimeMaterial;
            }
        }

        void OnEnable()
        {
            Font.textureRebuilt += HandleFontTextureRebuilt;
            SynchronizeFontTexture();
        }

        void OnDisable()
        {
            Font.textureRebuilt -= HandleFontTextureRebuilt;
        }

        void OnDestroy()
        {
            Destroy(_runtimeMaterial);
        }

        /// <summary>
        /// rebuiltFont가 키보드의 폰트인 경우 텍스처 연결을 갱신한다.
        /// 현재 폰트 텍스처를 실행용 머티리얼에 반영한다.
        /// </summary>
        private void HandleFontTextureRebuilt(Font rebuiltFont)
        {
            if (rebuiltFont == _font)
            {
                SynchronizeFontTexture();
            }
        }

        /// <summary>
        /// 키보드 폰트의 현재 텍스처를 읽어 실행용 머티리얼에 연결한다.
        /// _runtimeMaterial의 mainTexture를 변경한다.
        /// </summary>
        private void SynchronizeFontTexture()
        {
            _runtimeMaterial.mainTexture = _font.material.mainTexture;
        }
    }
}