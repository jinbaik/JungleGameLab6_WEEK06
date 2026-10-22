using UnityEngine;

using UnityEngine.InputSystem;

namespace Game.Tournament
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class TournamentShopCameraZoom : MonoBehaviour
    {
        [Header("Zoom")]
        [SerializeField, Range(1f, 179f)] private float _minimumFieldOfView = 40f;
        [SerializeField, Range(1f, 179f)] private float _maximumFieldOfView = 100f;
        [SerializeField, Min(0.01f)] private float _fieldOfViewStep = 2f;
        private Camera _camera;
        private float _defaultFieldOfView;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _defaultFieldOfView = _camera.fieldOfView;
        }

        void OnEnable()
        {
            _camera.fieldOfView = _defaultFieldOfView;
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !InGameCursor.CanProcessPointer)
                return;

            float scrollDelta = mouse.scroll.y.ReadValue();
            if (scrollDelta == 0f)
                return;

            float fieldOfView = Mathf.Clamp(
                _camera.fieldOfView - scrollDelta * _fieldOfViewStep,
                _minimumFieldOfView,
                _maximumFieldOfView);
            _camera.fieldOfView = fieldOfView;
        }
    }
}
