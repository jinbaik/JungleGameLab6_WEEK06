using System.Collections.Generic;
using System.IO;

using UnityEngine;

using UnityEditor;

public static class RocketKeycapBuilder
{
    private const string MESH_ROOT = "Assets/Resource/Meshes/Rocket";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/Rocket";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/Rocket/keycap-rocket.prefab";

    /// <summary>1u 규격의 로켓 본체와 분리된 불꽃 메시 및 재질을 생성한다. 동작 컴포넌트 없이 불꽃이 꺼진 외형 프리팹과 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Rocket Keycap")]
    public static void Build()
    {
        foreach (string path in new[] { MESH_ROOT, MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/rocket-keycap" }) Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
        Material[] materials = {
            Material("ivory", new Color(0.94f, 0.90f, 0.78f), 0.48f),
            Material("red", new Color(0.76f, 0.025f, 0.018f), 0.62f),
            Material("glass", new Color(0.025f, 0.085f, 0.12f), 0.92f),
            Material("engine", new Color(1f, 0.28f, 0.025f), 0.58f),
            Material("nozzle", new Color(0.055f, 0.045f, 0.04f), 0.55f)
        };
        Material[] flameMaterials = {
            Material("flame-orange", new Color(1f, 0.12f, 0.008f), 0.3f, true),
            Material("flame-yellow", new Color(1f, 0.68f, 0.035f), 0.3f, true),
            Material("flame-core", new Color(1f, 0.95f, 0.55f), 0.3f, true)
        };
        Geometry body = new Geometry(5);
        body.Lathe(new[] { new Vector2(0f,.22f), new Vector2(.13f,.22f), new Vector2(.18f,.30f), new Vector2(.22f,.43f), new Vector2(.235f,.53f), new Vector2(.231f,.62f), new Vector2(.212f,.72f), new Vector2(.192f,.77f), new Vector2(0f,.77f) }, 0, Matrix4x4.identity);
        body.Lathe(new[] { new Vector2(0f,.758f),new Vector2(.197f,.758f),new Vector2(.207f,.77f),new Vector2(.202f,.79f),new Vector2(.185f,.80f),new Vector2(.155f,.858f),new Vector2(.108f,.929f),new Vector2(.055f,.991f),new Vector2(.025f,1.014f),new Vector2(0f,1.022f) },1, Matrix4x4.identity);
        body.Lathe(new[] { new Vector2(0f,.13f),new Vector2(.112f,.13f),new Vector2(.137f,.15f),new Vector2(.18f,.275f),new Vector2(0f,.275f) },3, Matrix4x4.identity);
        body.Lathe(new[] { new Vector2(0f,.075f),new Vector2(.075f,.075f),new Vector2(.07f,.145f),new Vector2(0f,.145f) },4, Matrix4x4.identity);
        body.Lathe(new[] { new Vector2(.045f,.075f),new Vector2(.06f,.1f),new Vector2(.12f,.019f),new Vector2(.122f,.006f),new Vector2(.115f,0f),new Vector2(.088f,0f),new Vector2(.045f,.061f),new Vector2(.045f,.075f) },3, Matrix4x4.identity);
        body.Lathe(new[] {new Vector2(.089f,0f),new Vector2(.12f,0f),new Vector2(.125f,.008f),new Vector2(.122f,.02f),new Vector2(.105f,.02f),new Vector2(.089f,0f)},4,Matrix4x4.identity);
        for(int i=0;i<3;i++) body.Fin(i*120f);
        Quaternion windowRotation = Quaternion.Euler(-78f,0f,0f);
        Matrix4x4 window = Matrix4x4.TRS(new Vector3(0f,.571f,-.255f),windowRotation,Vector3.one);
        body.Torus(.097f,.014f,1,window);
        body.Lathe(new[] {new Vector2(0f,0f),new Vector2(.081f,0f),new Vector2(.083f,.008f),new Vector2(.072f,.019f),new Vector2(.04f,.026f),new Vector2(0f,.029f)},2,window);
        Mesh bodyMesh = SaveMesh(body.ToMesh("keycap-rocket-body"));
        Geometry flame = new Geometry(3);
        flame.Lathe(new[] {new Vector2(0f,-.33f),new Vector2(.025f,-.27f),new Vector2(.061f,-.18f),new Vector2(.079f,-.09f),new Vector2(.065f,0f),new Vector2(0f,0f)},0,Matrix4x4.identity,32);
        flame.Lathe(new[] {new Vector2(0f,-.255f),new Vector2(.032f,-.18f),new Vector2(.049f,-.08f),new Vector2(.04f,0f),new Vector2(0f,0f)},1,Matrix4x4.Translate(new Vector3(0f,0f,-.046f)),32);
        flame.Lathe(new[] {new Vector2(0f,-.17f),new Vector2(.02f,-.11f),new Vector2(.027f,-.035f),new Vector2(.02f,0f),new Vector2(0f,0f)},2,Matrix4x4.Translate(new Vector3(0f,0f,-.078f)),24);
        Mesh flameMesh=SaveMesh(flame.ToMesh("keycap-rocket-flame"));
        GameObject root=new GameObject("keycap-rocket");
        try
        {
            root.transform.localScale=Vector3.one*.01905f;
            MeshObject("RocketBody", root.transform, bodyMesh, materials);
            GameObject flameRoot=new GameObject("FlameRoot");
            flameRoot.transform.SetParent(root.transform,false);
            MeshObject("FlameMesh",flameRoot.transform,flameMesh,flameMaterials);
            flameRoot.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,PREFAB_PATH);
            AssetDatabase.SaveAssets();
            Render(root,"front",false,false,true);
            Render(root,"game",false,false,false);
            Render(root,"neighbors",true,false,false);
            Render(root,"flame",false,true,false);
            Debug.Log($"ROCKET_CREATED body={bodyMesh.bounds} bodyVertices={bodyMesh.vertexCount} flameVertices={flameMesh.vertexCount} defaultFlame={flameRoot.activeSelf}");
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>name과 color로 URP Lit 재질을 저장한다. smoothness와 emission을 반영한 재질 참조를 반환한다.</summary>
    private static Material Material(string name, Color color, float smoothness, bool emission=false)
    {
        string path=MATERIAL_ROOT+"/keycap-rocket-"+name+".mat";
        Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path); }
        material.SetColor("_BaseColor",color);
        material.SetFloat("_Smoothness",smoothness);
        if(emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor",color*.65f); }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>mesh를 이름에 맞는 에셋 경로에 저장한다. 재생성 시 기존 GUID를 유지한 메시 참조를 반환한다.</summary>
    private static Mesh SaveMesh(Mesh mesh)
    {
        string path=MESH_ROOT+"/"+mesh.name+".asset";
        Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null) { AssetDatabase.CreateAsset(mesh,path); return mesh; }
        existing.Clear(); existing.vertices=mesh.vertices; existing.normals=mesh.normals; existing.uv=mesh.uv;
        existing.subMeshCount=mesh.subMeshCount;
        for(int i=0;i<mesh.subMeshCount;i++) existing.SetTriangles(mesh.GetTriangles(i),i);
        existing.RecalculateBounds(); EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh); return existing;
    }

    /// <summary>parent 아래에 mesh와 materials를 사용하는 외형 오브젝트를 생성한다. 물리나 게임 동작이 없는 오브젝트를 반환한다.</summary>
    private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material[] materials)
    {
        GameObject value=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        value.transform.SetParent(parent,false); value.GetComponent<MeshFilter>().sharedMesh=mesh; value.GetComponent<MeshRenderer>().sharedMaterials=materials;
        return value;
    }

    /// <summary>source를 정면 또는 게임 시점에서 렌더링한다. neighbors와 flame으로 비교 상태를 정하고 name의 PNG를 저장한 뒤 임시 자원을 해제한다.</summary>
    private static void Render(GameObject source,string name,bool neighbors,bool flame,bool frontal)
    {
        PreviewRenderUtility preview=new PreviewRenderUtility();
        GameObject group=new GameObject("RocketPreview");
        try
        {
            GameObject rocket=Object.Instantiate(source,group.transform); rocket.transform.localScale=Vector3.one;
            rocket.transform.Find("FlameRoot").gameObject.SetActive(flame);
            if(neighbors)
                for(int z=-1;z<=1;z++) for(int x=-1;x<=1;x++)
                {
                    if(x==0&&z==0) continue;
                    GameObject key=MeshObject("Neighbor",group.transform,AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resource/Meshes/Keycap_1u.asset"),new[] {AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Porcelain_PBT.mat")});
                    key.transform.localPosition=new Vector3(x,0f,z);
                }
            preview.AddSingleGO(group); preview.camera.orthographic=true; preview.camera.orthographicSize=neighbors?2.05f:.78f;
            preview.camera.nearClipPlane=.01f; preview.camera.farClipPlane=20f;
            preview.camera.transform.position=frontal?new Vector3(0f,.8f,-3f):new Vector3(.6f,3.2f,-2.2f);
            preview.camera.transform.LookAt(new Vector3(0f,flame?.33f:.45f,0f));
            preview.camera.clearFlags=CameraClearFlags.SolidColor; preview.camera.backgroundColor=new Color(.075f,.095f,.135f);
            preview.lights[0].intensity=1.3f; preview.lights[0].transform.rotation=Quaternion.Euler(45f,-35f,0f);
            preview.lights[1].intensity=.8f; preview.lights[1].transform.rotation=Quaternion.Euler(35f,145f,0f);
            preview.ambientColor=new Color(.42f,.45f,.52f);
            preview.BeginStaticPreview(new Rect(0f,0f,1000f,1000f)); preview.Render(true);
            Texture2D image=preview.EndStaticPreview(); File.WriteAllBytes("output/rocket-keycap/rocket-"+name+"-unity.png",image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        finally { preview.Cleanup(); }
    }

    private sealed class Geometry
    {
        private readonly List<Vector3> _vertices=new List<Vector3>();
        private readonly List<int>[] _triangles;
        private readonly Dictionary<(int,int,int,int),int> _indices=new Dictionary<(int,int,int,int),int>();

        /// <summary>slots 수만큼 재질별 삼각형 저장소를 생성한다. 새 메시 작성 상태를 초기화한다.</summary>
        public Geometry(int slots)
        {
            _triangles=new List<int>[slots]; for(int i=0;i<slots;i++) _triangles[i]=new List<int>();
        }

        /// <summary>position과 group으로 공유 정점을 검색하거나 추가한다. 정점 배열 인덱스를 반환한다.</summary>
        private int Vertex(Vector3 position,int group)
        {
            var key=(Mathf.RoundToInt(position.x*1000000),Mathf.RoundToInt(position.y*1000000),Mathf.RoundToInt(position.z*1000000),group);
            if(_indices.TryGetValue(key,out int index)) return index;
            index=_vertices.Count; _vertices.Add(position); _indices.Add(key,index); return index;
        }

        /// <summary>a,b,c의 삼각형을 slot에 추가한다. normal 방향으로 면을 정렬하며 group의 정점 연결을 유지한다.</summary>
        private void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal,int slot,int group)
        {
            Vector3 cross=Vector3.Cross(b-a,c-a); if(cross.sqrMagnitude<1e-15f) return;
            if(Vector3.Dot(cross,normal)<0f) { Vector3 swap=b; b=c; c=swap; }
            _triangles[slot].Add(Vertex(a,group)); _triangles[slot].Add(Vertex(b,group)); _triangles[slot].Add(Vertex(c,group));
        }

        /// <summary>profile의 반경과 높이를 segments 각도로 회전한다. matrix 변환과 slot 재질을 적용한 곡면을 메시 상태에 추가한다.</summary>
        public void Lathe(Vector2[] profile,int slot,Matrix4x4 matrix,int segments=48)
        {
            int group=_vertices.Count+100;
            for(int j=0;j<profile.Length-1;j++) for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2f/segments,b=(i+1)*Mathf.PI*2f/segments;
                Vector2 p=profile[j],q=profile[j+1],d=q-p;
                Vector3 v0=matrix.MultiplyPoint3x4(new Vector3(p.x*Mathf.Cos(a),p.y,p.x*Mathf.Sin(a)));
                Vector3 v1=matrix.MultiplyPoint3x4(new Vector3(p.x*Mathf.Cos(b),p.y,p.x*Mathf.Sin(b)));
                Vector3 v2=matrix.MultiplyPoint3x4(new Vector3(q.x*Mathf.Cos(a),q.y,q.x*Mathf.Sin(a)));
                Vector3 v3=matrix.MultiplyPoint3x4(new Vector3(q.x*Mathf.Cos(b),q.y,q.x*Mathf.Sin(b)));
                Vector3 normal=matrix.MultiplyVector(new Vector3(Mathf.Cos((a+b)*.5f)*d.y,-d.x,Mathf.Sin((a+b)*.5f)*d.y));
                Triangle(v0,v1,v2,normal,slot,group); Triangle(v1,v3,v2,normal,slot,group);
            }
        }

        /// <summary>radiusとtubeの円環をmatrixで配置する。slotの窓枠曲面を追加する。</summary>
        public void Torus(float radius,float tube,int slot,Matrix4x4 matrix)
        {
            Vector2[] profile=new Vector2[13];
            for(int i=0;i<=12;i++) { float t=i*Mathf.PI*2f/12f; profile[i]=new Vector2(radius+tube*Mathf.Cos(t),tube*Mathf.Sin(t)); }
            Lathe(profile,slot,matrix);
        }

        /// <summary>angle方向に厚みのある赤い後退翼を配置する。外周を丸めた頂点と面を本体に追加する。</summary>
        public void Fin(float angle)
        {
            Vector2[] outline={new Vector2(.12f,.16f),new Vector2(.14f,.39f),new Vector2(.19f,.43f),new Vector2(.25f,.39f),new Vector2(.31f,.32f),new Vector2(.35f,.23f),new Vector2(.37f,.08f),new Vector2(.365f,.01f),new Vector2(.35f,0f),new Vector2(.33f,.028f),new Vector2(.26f,.13f),new Vector2(.20f,.17f)};
            Quaternion rotation=Quaternion.Euler(0f,angle,0f);
            int group=_vertices.Count+200;
            Vector3 center=new Vector3(0f,.235f,-.245f);
            for(int i=0;i<outline.Length;i++)
            {
                Vector2 a=outline[i],b=outline[(i+1)%outline.Length];
                Vector3 va=new Vector3(0f,a.y,-a.x),vb=new Vector3(0f,b.y,-b.x);
                Vector3 ca=center+Vector3.right*.028f,cb=center-Vector3.right*.028f;
                Triangle(rotation*va,rotation*vb,rotation*ca,rotation*Vector3.right,1,group);
                Triangle(rotation*vb,rotation*va,rotation*cb,rotation*Vector3.left,1,group);
            }
        }

        /// <summary>누적 정점과 재질별 삼각형을 name의 메시로 변환한다. 법선과 UV 및 경계를 계산한 메시를 반환한다.</summary>
        public Mesh ToMesh(string name)
        {
            Mesh mesh=new Mesh {name=name}; mesh.SetVertices(_vertices); mesh.subMeshCount=_triangles.Length;
            for(int i=0;i<_triangles.Length;i++) mesh.SetTriangles(_triangles[i],i);
            Vector2[] uv=new Vector2[_vertices.Count]; for(int i=0;i<uv.Length;i++) uv[i]=new Vector2(_vertices[i].x+.5f,_vertices[i].y);
            mesh.uv=uv; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
    }
}



