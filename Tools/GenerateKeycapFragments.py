import math, re, struct, uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Prefabs/KeyboardFragment'
SOURCE = ROOT / 'Assets/Resource/Prefabs/Keyboard/Prefabs/Keycaps/Keycap_1u.prefab'
OUT.mkdir(parents=True, exist_ok=True)

def meta(path, importer='NativeFormatImporter', fileid=4300000):
    target = Path(str(path) + '.meta')
    if target.exists(): return re.search(r'guid: (\w+)', target.read_text()).group(1)
    guid = uuid.uuid4().hex
    body = f'fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n'
    if importer == 'NativeFormatImporter': body += f'  mainObjectFileID: {fileid}\n'
    target.write_text(body + '  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    return guid

def vec(text, field):
    match = re.search(field + r': \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}', text)
    return tuple(map(float, match.groups()))

def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def mul(a,s): return tuple(x*s for x in a)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(v): return mul(v,1/max(math.sqrt(dot(v,v)),1e-12))

text = SOURCE.read_text()
blocks = {int(i):(int(t), b) for t,i,b in re.findall(r'--- !u!(\d+) &(\d+)\n(.*?)(?=--- !u!|\Z)',text,re.S)}
transforms = {}
for _,(kind,b) in blocks.items():
    if kind == 4:
        transforms[int(re.search(r'm_GameObject: \{fileID: (\d+)',b).group(1))] = (vec(b,'m_LocalPosition'),vec(b,'m_LocalScale'),int(re.search(r'm_Father: \{fileID: (\d+)',b).group(1)))
rootgo = next(go for go,(p,s,parent) in transforms.items() if parent == 0)
roottransform = next(i for i,(kind,b) in blocks.items() if kind==4 and f'm_GameObject: {{fileID: {rootgo}}}' in b)
guidpaths = {}
for path in (ROOT/'Assets/Resource/Prefabs/Keyboard/Meshes').glob('*.asset'):
    guidpaths[re.search(r'guid: (\w+)',Path(str(path)+'.meta').read_text()).group(1)] = path

def worldlocal(go):
    p,s,parent=transforms[go]
    if parent == roottransform: return p,s
    parentgo=int(re.search(r'm_GameObject: \{fileID: (\d+)',blocks[parent][1]).group(1))
    pp,ss=worldlocal(parentgo)
    return add(pp,tuple(p[i]*ss[i] for i in range(3))),tuple(s[i]*ss[i] for i in range(3))

triangles=[]
template=None
rendererblock=None
material=None
for _,(kind,b) in blocks.items():
    if kind==23 and 'm_RendererPriority' in b and rendererblock is None:
        rendererblock=b
        material=re.search(r'm_Materials:\n  - (.*)',b).group(1)
    if kind!=33: continue
    go=int(re.search(r'm_GameObject: \{fileID: (\d+)',b).group(1))
    guid=re.search(r'm_Mesh: .*guid: (\w+)',b).group(1)
    mesh=guidpaths[guid].read_text()
    if guid=='1587b8289e2cdc34ba310d54acd0a0bd': template=mesh
    count=int(re.search(r'm_VertexCount: (\d+)',mesh).group(1))
    data=bytes.fromhex(re.search(r'_typelessdata: (\w+)',mesh).group(1))
    stride=len(data)//count
    p,s=worldlocal(go)
    vertices=[add(p,tuple(v*s[k] for k,v in enumerate(struct.unpack_from('<3f',data,j*stride)))) for j in range(count)]
    ib=bytes.fromhex(re.search(r'm_IndexBuffer: (\w+)',mesh).group(1))
    indices=struct.unpack('<'+'H'*(len(ib)//2),ib)
    triangles.extend(tuple(vertices[indices[j+k]] for k in range(3)) for j in range(0,len(indices),3))

def clip(tris,n,d):
    result=[]; boundary=[]
    for tri in tris:
        poly=[]; hits=[]
        for a,b in zip(tri,tri[1:]+tri[:1]):
            da=dot(n,a)-d; db=dot(n,b)-d
            if da>=-1e-8: poly.append(a)
            if (da>1e-8 and db<-1e-8) or (da<-1e-8 and db>1e-8):
                p=add(a,mul(sub(b,a),da/(da-db))); poly.append(p); hits.append(p)
        if len(hits)==2: boundary.append(hits)
        for j in range(1,len(poly)-1): result.append((poly[0],poly[j],poly[j+1]))
    # Each connected cut contour is capped; original exterior triangles retain their shape.
    def key(v): return tuple(round(x,5) for x in v)
    graph={}; points={}
    for a,b in boundary:
        ka,kb=key(a),key(b)
        if ka==kb: continue
        points[ka]=a;points[kb]=b
        graph.setdefault(ka,set()).add(kb);graph.setdefault(kb,set()).add(ka)
    edges={tuple(sorted((a,b))) for a,vs in graph.items() for b in vs}
    while edges:
        a,b=next(iter(edges)); edges.remove(tuple(sorted((a,b)))); loop=[a,b]
        while loop[-1]!=loop[0]:
            candidates=[q for q in graph[loop[-1]] if tuple(sorted((loop[-1],q))) in edges]
            if not candidates: break
            q=candidates[0];edges.remove(tuple(sorted((loop[-1],q))));loop.append(q)
        if loop[-1]!=loop[0] or len(loop)<4: continue
        ps=[points[q] for q in loop[:-1]]; center=mul(tuple(map(sum,zip(*ps))),1/len(ps))
        for a,b in zip(ps,ps[1:]+ps[:1]):
            if dot(cross(sub(a,center),sub(b,center)),n)>0: a,b=b,a
            result.append((center,a,b))
    return result

def meshasset(tris,index):
    vertices=[]; packed=bytearray()
    for tri in tris:
        normal=unit(cross(sub(tri[1],tri[0]),sub(tri[2],tri[0])))
        for v in tri:
            vertices.append(v); packed.extend(struct.pack('<12f',*v,*normal,1,0,0,1,v[0],v[2]))
    lo=tuple(map(min,zip(*vertices)));hi=tuple(map(max,zip(*vertices)))
    center=mul(add(lo,hi),.5);extent=mul(sub(hi,lo),.5)
    def fmt(v): return '{x: %.8g, y: %.8g, z: %.8g}'%v
    mesh=template
    mesh=re.sub(r'm_Name: .*',f'm_Name: Keycap_1u_Fragment_{index}',mesh,count=1)
    for field in ['indexCount','vertexCount','m_VertexCount']:
        mesh=re.sub(field+r': \d+',field+f': {len(vertices)}',mesh,count=1)
    mesh=re.sub(r'm_IndexBuffer: \w+','m_IndexBuffer: '+struct.pack('<'+'H'*len(vertices),*range(len(vertices))).hex(),mesh)
    mesh=re.sub(r'm_DataSize: \d+',f'm_DataSize: {len(packed)}',mesh)
    mesh=re.sub(r'_typelessdata: \w+','_typelessdata: '+packed.hex(),mesh)
    mesh=re.sub(r'm_Center: \{[^}]+\}','m_Center: '+fmt(center),mesh)
    mesh=re.sub(r'm_Extent: \{[^}]+\}','m_Extent: '+fmt(extent),mesh)
    path=OUT/f'Keycap_1u_Fragment_{index}.asset';path.write_text(mesh)
    return meta(path),len(tris)

HEADER='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
def obj(i,name,components):
    return f'--- !u!1 &{i}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n'+''.join(f'  - component: {{fileID: {c}}}\n' for c in components)+f'  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_IsActive: 1\n'
def transform(i,go,parent=0,children=(),scale=1):
    return f'--- !u!4 &{i}\nTransform:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\n  m_LocalPosition: {{x: 0, y: 0, z: 0}}\n  m_LocalScale: {{x: {scale}, y: {scale}, z: {scale}}}\n  m_Children:'+ ('\n'+''.join(f'  - {{fileID: {c}}}\n' for c in children) if children else ' []\n')+f'  m_Father: {{fileID: {parent}}}\n'
meshguids=[];counts=[]
for index,(sx,sz) in enumerate([(1,1),(-1,1),(-1,-1),(1,-1)],1):
    tris=clip(triangles,(sx,0,sx*.28),sx*.04)
    tris=clip(tris,(sz*-.2,sz*.12,sz),sz*-.02)
    guid,count=meshasset(tris,index);meshguids.append(guid);counts.append(count)
prefab=HEADER+obj(100,'Keycap_1u_Fragments',[101])+transform(101,100,children=[201,301,401,501])
for index,guid in enumerate(meshguids,2):
    i=index*100
    prefab+=obj(i,f'Fragment_{index-1}',[i+1,i+2,i+3,i+4,i+5])+transform(i+1,i,101)
    prefab+=f'--- !u!33 &{i+2}\nMeshFilter:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {i}}}\n  m_Mesh: {{fileID: 4300000, guid: {guid}, type: 2}}\n'
    renderer=re.sub(r'm_GameObject: \{fileID: \d+\}',f'm_GameObject: {{fileID: {i}}}',rendererblock)
    prefab+=f'--- !u!23 &{i+3}\n'+renderer
    prefab+=f'--- !u!54 &{i+4}\nRigidbody:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {i}}}\n  serializedVersion: 5\n  m_Mass: 0.002\n  m_LinearDamping: 0.2\n  m_AngularDamping: 0.4\n  m_UseGravity: 1\n  m_IsKinematic: 0\n  m_Interpolate: 1\n  m_Constraints: 0\n  m_CollisionDetection: 0\n'
    prefab+=f'--- !u!64 &{i+5}\nMeshCollider:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {i}}}\n  m_Material: {{fileID: 0}}\n  m_IsTrigger: 0\n  m_Enabled: 1\n  serializedVersion: 5\n  m_Convex: 1\n  m_CookingOptions: 30\n  m_Mesh: {{fileID: 4300000, guid: {guid}, type: 2}}\n'
path=OUT/'Keycap_1u_Fragments.prefab';path.write_text(prefab);fragmentguid=meta(path,'PrefabImporter')
healthguid=meta(ROOT/'Assets/Scripts/Keyboard/KeycapHealth.cs','MonoImporter')
particleguid=meta(ROOT/'Assets/Scripts/Keyboard/KeycapBreakParticles.cs','MonoImporter')
particles=HEADER+obj(100,'Keycap_1u_Particles',[101,102])+transform(101,100)
particles+=f'--- !u!114 &102\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: 100}}\n  m_Enabled: 1\n  m_Script: {{fileID: 11500000, guid: {particleguid}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n  _fragmentMeshes:\n'+''.join(f'  - {{fileID: 4300000, guid: {g}, type: 2}}\n' for g in meshguids)+f'  _material: {material}\n  _count: 24\n  _lifetime: 1.5\n'
path=OUT/'Keycap_1u_Particles.prefab';path.write_text(particles);particlesguid=meta(path,'PrefabImporter')
# Two standalone comparison prefabs inherit the original model without changing it.
for mode,name in [(0,'Keycap_1u_FragmentDemo'),(1,'Keycap_1u_ParticleDemo')]:
    demo=text.replace(f'  - component: {{fileID: 4383910516040011548}}',f'  - component: {{fileID: 4383910516040011548}}\n  - component: {{fileID: 900000000000000000}}')
    demo+=f'--- !u!114 &900000000000000000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {rootgo}}}\n  m_Enabled: 1\n  m_Script: {{fileID: 11500000, guid: {healthguid}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n  _maxHP: 3\n  _breakMode: {mode}\n  _fragmentsPrefab: {{fileID: 100, guid: {fragmentguid}, type: 3}}\n  _particlesPrefab: {{fileID: 100, guid: {particlesguid}, type: 3}}\n  _fragmentSpeed: 0.3\n  _fragmentSpin: 8\n  _effectLifetime: 3\n'
    path=OUT/(name+'.prefab');path.write_text(demo);meta(path,'PrefabImporter')
print('Generated four clipped fragments:', counts)
print('Created fragment, particle, and two HP comparison prefabs in', OUT)

from PIL import Image, ImageDraw
image=Image.new('RGB',(1400,650),(28,33,42)); draw=ImageDraw.Draw(image)
colors=[(229,219,197),(216,204,178),(238,228,205),(200,189,164)]
draw.text((45,30),'1u KEYCAP: ASSEMBLED',fill='white');draw.text((745,30),'FOUR CLIPPED FRAGMENTS: EXPLODED',fill='white')
for panel in range(2):
    faces=[]
    for index,(sx,sz) in enumerate([(1,1),(-1,1),(-1,-1),(1,-1)]):
        tris=clip(clip(triangles,(sx,0,sx*.28),sx*.04),(sz*-.2,sz*.12,sz),sz*-.02)
        offset=(sx*.16,0,sz*.16) if panel else (0,0,0)
        for tri in tris:
            points=[add(p,offset) for p in tri]
            normal=unit(cross(sub(points[1],points[0]),sub(points[2],points[0])))
            shade=.55+.45*max(0,dot(normal,unit((-.3,1,-.7))))
            color=tuple(int(c*shade) for c in colors[index])
            projected=[(350+panel*700+(p[0]-p[2])*220,415+(p[0]+p[2])*90-p[1]*320) for p in points]
            depth=sum(p[0]+p[2]+p[1]*.4 for p in points)/3
            faces.append((depth,projected,color))
    for _,points,color in sorted(faces,key=lambda f:f[0]): draw.polygon(points,fill=color)
image.save(OUT/'Keycap_1u_FragmentPreview.png')
