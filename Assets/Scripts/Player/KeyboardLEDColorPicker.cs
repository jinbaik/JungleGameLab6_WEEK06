using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class KeyboardLEDColorPicker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, IInitializePotentialDragHandler, IMoveHandler
    {
        private const int TEXTURE_SIZE = 256;
        private const float OUTER_RADIUS = 246f / 516f;
        private const float INNER_RADIUS = 205f / 516f;
        private const float SQUARE_SIZE = 274f / 516f;

        [Header("Runtime State")]
        private RectTransform _rect;
        private RectTransform _hueHandle;
        private RectTransform _colorHandle;
        private Texture2D _ringTexture;
        private Texture2D _squareTexture;
        private float _hue;
        private float _saturation;
        private float _value;
        private int _dragMode = -1;
        private Color32[] _squarePixels;

        public event Action<Color> ColorChanged;

        /// <summary>
        /// circleSprite를 표시 핸들에 사용해 색조 링과 채도·명도 사각형을 구성한다.
        /// 생성한 텍스처와 UI 참조를 보관하며 실제 LED 설정은 변경하지 않는다.
        /// </summary>
        public void Initialize(Sprite circleSprite)
        {
            _rect = (RectTransform)transform;
            _ringTexture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false);
            _ringTexture.wrapMode = TextureWrapMode.Clamp;
            Color32[] ringPixels = new Color32[TEXTURE_SIZE * TEXTURE_SIZE];
            for (int y = 0; y < TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < TEXTURE_SIZE; x++)
                {
                    Vector2 point = new Vector2((x + 0.5f) / TEXTURE_SIZE - 0.5f, (y + 0.5f) / TEXTURE_SIZE - 0.5f);
                    float radius = point.magnitude;
                    float hue = Mathf.Repeat(Mathf.Atan2(point.x, point.y) / (2f * Mathf.PI), 1f);
                    Color color = Color.HSVToRGB(hue, 1f, 1f);
                    color.a = Mathf.Clamp01((OUTER_RADIUS - radius) * TEXTURE_SIZE)
                        * Mathf.Clamp01((radius - INNER_RADIUS) * TEXTURE_SIZE);
                    ringPixels[y * TEXTURE_SIZE + x] = color;
                }
            }
            _ringTexture.SetPixels32(ringPixels);
            _ringTexture.Apply(false, true);
            RawImage ring = gameObject.AddComponent<RawImage>();
            ring.texture = _ringTexture;

            _squareTexture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            _squareTexture.wrapMode = TextureWrapMode.Clamp;
            _squarePixels = new Color32[128 * 128];
            RectTransform square = CreateCenteredRect("SaturationValue", _rect, Vector2.one * (_rect.sizeDelta.x * SQUARE_SIZE));
            RawImage squareImage = square.gameObject.AddComponent<RawImage>();
            squareImage.texture = _squareTexture;
            squareImage.raycastTarget = false;
            _hueHandle = CreateHandle("HueHandle", _rect, circleSprite);
            _colorHandle = CreateHandle("ColorHandle", _rect, circleSprite);
            UpdateSquare();
        }

        /// <summary>
        /// color의 HSV 값으로 색상환과 선택 핸들을 동기화한다.
        /// 무채색에서는 기존 색조를 유지하며 LED 적용 이벤트는 발생시키지 않는다.
        /// </summary>
        public void SetColor(Color color)
        {
            Color.RGBToHSV(color, out float hue, out _saturation, out _value);
            if (_saturation > 0f) _hue = hue;
            _saturation = Mathf.Clamp01(_saturation);
            _value = Mathf.Clamp01(_value);
            UpdateSquare();
            UpdateHandles();
        }

        /// <summary>
        /// eventData의 좌클릭 위치를 색조 링 또는 채도·명도 영역으로 판별한다.
        /// 드래그 모드를 저장하고 선택 색상을 즉시 변경하며 키보드 방향 입력도 받을 수 있게 한다.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _dragMode = GetPointerDragMode(eventData.position, eventData.pressEventCamera);
            if (_dragMode < 0) return;
            EventSystem.current.SetSelectedGameObject(gameObject, eventData);
            UpdateFromPointer(eventData);
        }

        /// <summary>position과 camera로 실제 색조 링 또는 채도·명도 드래그 모드를 반환한다. 유효 영역 밖은 -1이며 색상은 변경하지 않는다.</summary>
        private int GetPointerDragMode(Vector2 position, Camera camera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, position, camera, out Vector2 point);
            point = (point - _rect.rect.center) / _rect.rect.width;
            float radius = point.magnitude;
            if (radius >= INNER_RADIUS - 12f / 516f && radius <= OUTER_RADIUS + 12f / 516f) return 0;
            return Mathf.Abs(point.x) <= SQUARE_SIZE * 0.5f && Mathf.Abs(point.y) <= SQUARE_SIZE * 0.5f ? 1 : -1;
        }

        /// <summary>position의 유효 색상 영역과 현재 선택 상태로 state에 LED 조작을 추가한다. 실제 입력 처리와 동일한 드래그 모드를 조회한다.</summary>
        public void CollectControlHints(WorkshopControlHintState state, Vector2 position)
        {
            if (_dragMode >= 0 || GetPointerDragMode(position, null) >= 0)
                state.Add("좌클릭 드래그", "LED 색상 조절");
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            {
                state.Add("방향키", "색조 조절");
                state.Add("Shift + 방향키", "색조 크게 조절");
            }
        }

        /// <summary>
        /// eventData의 좌클릭 해제로 현재 색상환 드래그를 끝낸다.
        /// 선택 색상은 유지하며 드래그 모드만 초기화한다.
        /// </summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _dragMode = -1;
        }

        /// <summary>
        /// eventData의 포인터 이동을 현재 드래그 영역에 적용한다.
        /// 링 밖에서도 색조 선택을 유지하고 사각형의 채도·명도는 경계값으로 제한한다.
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (_dragMode >= 0) UpdateFromPointer(eventData);
        }

        /// <summary>
        /// eventData의 드래그 시작 임계값을 해제해 작은 색상 조정도 즉시 반영한다.
        /// LED 값은 변경하지 않고 포인터 이벤트의 드래그 설정만 변경한다.
        /// </summary>
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        /// <summary>
        /// eventData의 방향 입력으로 색조를 1도씩 변경하고 Shift 입력 시 10도를 적용한다.
        /// 현재 채도·명도를 유지하며 변경한 색상을 C# 이벤트로 전달한다.
        /// </summary>
        public void OnMove(AxisEventData eventData)
        {
            float direction;
            switch (eventData.moveDir)
            {
                case MoveDirection.Up:
                case MoveDirection.Right: direction = 1f; break;
                case MoveDirection.Down:
                case MoveDirection.Left: direction = -1f; break;
                default: return;
            }
            float step = Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? 10f : 1f;
            _hue = Mathf.Repeat(_hue + direction * step / 360f, 1f);
            UpdateSquare();
            UpdateHandles();
            ColorChanged?.Invoke(Color.HSVToRGB(_hue, _saturation, _value));
            eventData.Use();
        }

        void OnDisable()
        {
            _dragMode = -1;
        }

        void OnDestroy()
        {
            Destroy(_ringTexture);
            Destroy(_squareTexture);
        }

        /// <summary>
        /// eventData의 화면 좌표를 현재 선택 영역의 HSV 값으로 변환한다.
        /// 색조 또는 채도·명도와 핸들을 갱신하고 선택된 RGB 색상을 구독자에게 전달한다.
        /// </summary>
        private void UpdateFromPointer(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out Vector2 point);
            point = (point - _rect.rect.center) / _rect.rect.width;
            if (_dragMode == 0)
            {
                _hue = Mathf.Repeat(Mathf.Atan2(point.x, point.y) / (2f * Mathf.PI), 1f);
                UpdateSquare();
            }
            else
            {
                _saturation = Mathf.Clamp01(point.x / SQUARE_SIZE + 0.5f);
                _value = Mathf.Clamp01(point.y / SQUARE_SIZE + 0.5f);
            }
            UpdateHandles();
            ColorChanged?.Invoke(Color.HSVToRGB(_hue, _saturation, _value));
        }

        /// <summary>
        /// 현재 색조를 사용해 채도·명도 사각형의 색상 텍스처를 다시 만든다.
        /// 재사용 픽셀 배열을 갱신하며 텍스처 인스턴스를 추가로 생성하지 않는다.
        /// </summary>
        private void UpdateSquare()
        {
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    _squarePixels[y * 128 + x] = Color.HSVToRGB(_hue, x / 127f, y / 127f);
            _squareTexture.SetPixels32(_squarePixels);
            _squareTexture.Apply(false);
        }

        /// <summary>
        /// 현재 HSV와 색상환 크기를 사용해 색조 및 채도·명도 핸들의 위치를 갱신한다.
        /// LED 값이나 텍스처는 변경하지 않는다.
        /// </summary>
        private void UpdateHandles()
        {
            float angle = _hue * 2f * Mathf.PI;
            _hueHandle.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle))
                * ((INNER_RADIUS + OUTER_RADIUS) * 0.5f * _rect.rect.width);
            _colorHandle.anchoredPosition = new Vector2(_saturation - 0.5f, _value - 0.5f)
                * (SQUARE_SIZE * _rect.rect.width);
        }

        /// <summary>
        /// parent 중앙에 name과 size를 갖는 UI 사각형을 생성해 반환한다.
        /// 부모 배율과 관계없이 색상환 내부 좌표를 사용할 수 있도록 중앙 앵커를 설정한다.
        /// </summary>
        private RectTransform CreateCenteredRect(string name, Transform parent, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>
        /// parent 아래에 sprite를 사용하는 흰 테두리 선택 핸들을 만들고 반환한다.
        /// name을 오브젝트 이름으로 사용하며 핸들이 포인터 입력을 가로채지 않게 한다.
        /// </summary>
        private RectTransform CreateHandle(string name, Transform parent, Sprite sprite)
        {
            RectTransform handle = CreateCenteredRect(name, parent, Vector2.one * 16f);
            Image outer = handle.gameObject.AddComponent<Image>();
            outer.sprite = sprite;
            outer.color = Color.white;
            outer.raycastTarget = false;
            RectTransform inner = CreateCenteredRect("Center", handle, Vector2.one * 10f);
            Image innerImage = inner.gameObject.AddComponent<Image>();
            innerImage.sprite = sprite;
            innerImage.color = new Color(0.04f, 0.08f, 0.12f, 0.65f);
            innerImage.raycastTarget = false;
            return handle;
        }
    }
}
