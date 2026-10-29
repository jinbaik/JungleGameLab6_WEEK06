using System.Collections.Generic;

using UnityEngine;

namespace KeyboardModeling
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class NeonKeyboardGradient : MonoBehaviour
    {
        private static readonly int _keyboardWorldToLocalId = Shader.PropertyToID("_KeyboardWorldToLocal");
        private static readonly int _useKeyboardSpaceId = Shader.PropertyToID("_UseKeyboardSpace");

        [Header("Gradient Material")]
        [SerializeField] private Material _bandMaterial;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<int> _materialIndices = new List<int>();
        private MaterialPropertyBlock _properties;
        private Matrix4x4 _lastWorldToLocal;
        private bool _requiresRefresh;

        void OnEnable()
        {
            RefreshRenderers();
        }

        void OnValidate()
        {
            _requiresRefresh = true;
        }

        void LateUpdate()
        {
            if (_requiresRefresh)
            {
                RefreshRenderers();
                return;
            }
            if (_lastWorldToLocal != transform.worldToLocalMatrix)
                ApplyKeyboardSpace();
        }

        void OnDisable()
        {
            for (int index = 0; index < _renderers.Count; index++)
            {
                _renderers[index].GetPropertyBlock(_properties, _materialIndices[index]);
                _properties.SetFloat(_useKeyboardSpaceId, 0f);
                _renderers[index].SetPropertyBlock(_properties, _materialIndices[index]);
            }
        }

        /// <summary>
        /// 현재 키보드 하위에서 _bandMaterial을 사용하는 렌더러와 재질 슬롯을 찾는다.
        /// 렌더러 목록을 갱신하고 공유 재질을 복제하지 않은 채 키보드 좌표 기준을 적용한다.
        /// </summary>
        public void RefreshRenderers()
        {
            _requiresRefresh = false;
            _renderers.Clear();
            _materialIndices.Clear();
            _properties = new MaterialPropertyBlock();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    if (materials[index] != _bandMaterial || _bandMaterial == null)
                        continue;
                    _renderers.Add(renderer);
                    _materialIndices.Add(index);
                }
            }
            ApplyKeyboardSpace();
        }

        /// <summary>
        /// 현재 키보드의 월드 역변환 행렬을 네온 띠의 렌더러별 속성으로 전달한다.
        /// transform을 기준으로 셰이더 좌표계를 갱신하여 이동 및 회전 및 크기 변경 후에도 배색을 유지한다.
        /// </summary>
        private void ApplyKeyboardSpace()
        {
            _lastWorldToLocal = transform.worldToLocalMatrix;
            for (int index = 0; index < _renderers.Count; index++)
            {
                _renderers[index].GetPropertyBlock(_properties, _materialIndices[index]);
                _properties.SetMatrix(_keyboardWorldToLocalId, _lastWorldToLocal);
                _properties.SetFloat(_useKeyboardSpaceId, 1f);
                _renderers[index].SetPropertyBlock(_properties, _materialIndices[index]);
            }
        }

        /// <summary>
        /// bandMaterial을 네온 띠 재질로 저장하고 키보드의 대상 렌더러를 다시 찾는다.
        /// 각 렌더러의 그라데이션 좌표 설정을 갱신한다.
        /// </summary>
        public void Configure(Material bandMaterial)
        {
            _bandMaterial = bandMaterial;
            RefreshRenderers();
        }
    }
}
