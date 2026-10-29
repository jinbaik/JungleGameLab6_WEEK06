using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Player
{
    internal sealed class WorkshopKeyboardPlacementPreview : IDisposable
    {
        private readonly List<(Mesh Mesh, Matrix4x4 LocalMatrix)> _meshes
            = new List<(Mesh Mesh, Matrix4x4 LocalMatrix)>();
        private readonly Material _outlineMaterial;
        private readonly Material _depthMaterial;
        private readonly Camera _camera;
        private Vector3 _scale;

        /// <summary>
        /// 기존 외곽선 재질과 깊이 마스크 Shader 및 출력 Camera로 표시 전용 재질을 준비한다.
        /// outlineColor가 있으면 복제 재질에만 적용하며 원본은 유지하고 생성 재질은 Dispose에서 해제한다.
        /// </summary>
        public WorkshopKeyboardPlacementPreview(Material outlineMaterial, Shader depthShader, Camera camera, Color? outlineColor = null)
        {
            _outlineMaterial = new Material(outlineMaterial) { renderQueue = 3001 };
            if (outlineColor.HasValue)
                _outlineMaterial.SetColor("_OutlineColor", outlineColor.Value);
            _depthMaterial = new Material(depthShader) { renderQueue = 3000 };
            _camera = camera;
        }

        /// <summary>
        /// keyboardRoot의 메시와 본체 기준 변환 및 배율을 저장하며 includeInactive가 참이면 숨긴 설치 대상도 포함한다.
        /// targetRenderer가 지정되면 해당 렌더러만 저장하며 메시 자산과 원본 동작 및 물리 상태는 유지한다.
        /// </summary>
        public void Prepare(Transform keyboardRoot, bool includeInactive = false, MeshRenderer targetRenderer = null)
        {
            _meshes.Clear();
            _scale = keyboardRoot.lossyScale;
            foreach (MeshRenderer renderer in keyboardRoot.GetComponentsInChildren<MeshRenderer>(includeInactive))
            {
                if ((targetRenderer != null && renderer != targetRenderer)
                    || !renderer.enabled || (!includeInactive && !renderer.gameObject.activeInHierarchy)
                    || (renderer.sharedMaterial != null && renderer.sharedMaterial.shader == _outlineMaterial.shader))
                    continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                _meshes.Add((filter.sharedMesh, keyboardRoot.worldToLocalMatrix * renderer.localToWorldMatrix));
            }
        }

        /// <summary>
        /// pose의 최종 배치 자세에 캐시한 메시 외곽선을 이번 프레임의 출력 카메라에만 그린다.
        /// 색상 없는 깊이 마스크로 내부 면을 가리고 기존 외곽선 재질을 사용하며 표시 오브젝트는 생성하지 않는다.
        /// </summary>
        public void Show(Pose pose)
        {
            Matrix4x4 rootMatrix = Matrix4x4.TRS(pose.position, pose.rotation, _scale);
            foreach (var entry in _meshes)
            {
                Matrix4x4 matrix = rootMatrix * entry.LocalMatrix;
                for (int submesh = 0; submesh < entry.Mesh.subMeshCount; submesh++)
                {
                    Graphics.DrawMesh(entry.Mesh, matrix, _depthMaterial, 2, _camera, submesh,
                        null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                    Graphics.DrawMesh(entry.Mesh, matrix, _outlineMaterial, 2, _camera, submesh,
                        null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                }
            }
        }

        /// <summary>color로 현재 외곽선 색을 바꾼다. 공유 원본 재질은 유지하고 다음 표시부터 적용한다.</summary>
        public void SetOutlineColor(Color color)
        {
            _outlineMaterial.SetColor("_OutlineColor", color);
        }

        /// <summary>
        /// 표시 전용 재질과 캐시를 해제한다.
        /// 원본 메시 자산과 원본 외곽선 재질 및 카메라는 유지한다.
        /// </summary>
        public void Dispose()
        {
            _meshes.Clear();
            UnityEngine.Object.Destroy(_outlineMaterial);
            UnityEngine.Object.Destroy(_depthMaterial);
        }
    }
}
