using UnityEngine;

public static class KeyboardToonSurface
{
    private static readonly int _centerProperty = Shader.PropertyToID("_ToonBoundsCenter");
    private static readonly int _extentsProperty = Shader.PropertyToID("_ToonBoundsExtents");

    /// <summary>
    /// renderer의 Mesh 경계를 속성 블록에 기록해 표면에 고정된 Toon 테두리 기준으로 사용한다.
    /// 반환값 없이 렌더러별 경계만 설정하고 기존 사진 등 다른 속성은 유지한다.
    /// </summary>
    public static void ApplyBounds(MeshRenderer renderer)
    {
        if (renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty(_centerProperty)) return;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Bounds bounds = filter.sharedMesh.bounds;
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetVector(_centerProperty, bounds.center);
        properties.SetVector(_extentsProperty, bounds.extents);
        renderer.SetPropertyBlock(properties);
    }
}
