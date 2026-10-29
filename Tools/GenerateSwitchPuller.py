"""TX AL CNC Switch Puller의 제품 사진을 기준으로 정적 Unity 에셋을 생성한다."""
import math
import re
import struct
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/Resource/Materials/Workshop/Tools/Puller/KeycapPuller_Clip'
DEST = SOURCE.parent / 'SwitchPuller_TX_Gray'
PREFAB = ROOT / 'Assets/Resource/Prefabs/Workshop/Tools/SwitchPuller_TX_Gray.prefab'


def meta(path, kind='NativeFormatImporter', main=4300000):
    """경로와 임포터를 받아 기존 GUID를 유지하며 메타 파일을 저장한다."""
    target = Path(str(path) + '.meta')
    guid = re.search(r'guid: (\w+)', target.read_text()).group(1) if target.exists() else uuid.uuid4().hex
    extra = 'folderAsset: yes\n' if path.is_dir() else ''
    target.write_text(f'fileFormatVersion: 2\nguid: {guid}\n{extra}{kind}:\n  externalObjects: {{}}\n' +
                      (f'  mainObjectFileID: {main}\n' if kind == 'NativeFormatImporter' else '') +
                      '  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    return guid


def cross(a, b, c):
    """평면의 세 점을 받아 방향 판별용 외적을 반환한다."""
    return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])


def clip_height(polygon, height, keep_above):
    """윤곽과 절단 높이를 받아 위쪽 또는 아래쪽 윤곽을 반환한다."""
    result=[]
    for i,a in enumerate(polygon):
        b=polygon[(i+1)%len(polygon)]
        inside_a=(a[1]>=height) if keep_above else (a[1]<=height)
        inside_b=(b[1]>=height) if keep_above else (b[1]<=height)
        if inside_a:
            result.append(a)
        if inside_a!=inside_b:
            ratio=(height-a[1])/(b[1]-a[1])
            result.append((a[0]+(b[0]-a[0])*ratio,height))
    clean=[]
    for point in result:
        if not clean or math.dist(clean[-1],point)>1e-8:
            clean.append(point)
    if len(clean)>1 and math.dist(clean[0],clean[-1])<1e-8:
        clean.pop()
    return clean


class Geometry:
    """하드 에지 법선을 가진 삼각형 데이터를 보관한다."""
    def __init__(self):
        """입력 없이 정점과 삼각형 목록을 초기화한다."""
        self.vertices, self.faces = [], []

    def face(self, points):
        """외향 순서의 면 좌표를 받아 평면 법선과 삼각형을 추가한다."""
        a, b, c = points[:3]
        u, v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
        n = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
        length = math.sqrt(sum(x*x for x in n))
        if length < 1e-10:
            return
        start = len(self.vertices)
        self.vertices.extend((*p, *(x/length for x in n)) for p in points)
        self.faces.extend((start, start+i, start+i+1) for i in range(1, len(points)-1))

    def extrude(self, polygon, low, high):
        """XY 윤곽과 Z 두께를 받아 닫힌 입체 메시를 추가한다."""
        p = list(polygon)
        if sum(p[i][0]*p[(i+1)%len(p)][1]-p[(i+1)%len(p)][0]*p[i][1] for i in range(len(p))) < 0:
            p.reverse()
        remaining = list(range(len(p)))
        while len(remaining) > 2:
            for j, b in enumerate(remaining):
                a, c = remaining[j-1], remaining[(j+1)%len(remaining)]
                if cross(p[a], p[b], p[c]) <= 1e-9:
                    continue
                if any(all(cross(p[x], p[y], p[k]) >= -1e-9 for x, y in ((a,b),(b,c),(c,a))) for k in remaining if k not in (a,b,c)):
                    continue
                self.face([(*p[k], high) for k in (a,b,c)])
                self.face([(*p[k], low) for k in (c,b,a)])
                remaining.pop(j)
                break
            else:
                raise ValueError('윤곽 삼각분할 실패')
        for i, a in enumerate(p):
            b = p[(i+1)%len(p)]
            self.face([(*a,low), (*b,low), (*b,high), (*a,high)])

    def cylinder(self, x, y, radius, low, high, count=16):
        """중심과 반경 및 높이를 받아 저폴리 원통을 추가한다."""
        self.extrude([(x+radius*math.cos(i*math.tau/count), y+radius*math.sin(i*math.tau/count)) for i in range(count)], low, high)

    def bevel(self, polygon, low, high, width=.018):
        """윤곽과 깊이를 받아 작은 CNC 모서리 경사가 있는 입체를 추가한다."""
        p = list(polygon)
        if sum(p[i][0]*p[(i+1)%len(p)][1]-p[(i+1)%len(p)][0]*p[i][1] for i in range(len(p))) < 0:
            p.reverse()
        inset = []
        for i,b in enumerate(p):
            a,c = p[i-1],p[(i+1)%len(p)]
            normals = []
            for u,v in ((a,b),(b,c)):
                dx,dy = v[0]-u[0],v[1]-u[1]
                length = math.hypot(dx,dy)
                normals.append((-dy/length,dx/length))
            n,m = normals
            divisor = max(.15,1+n[0]*m[0]+n[1]*m[1])
            inset.append((b[0]+width*(n[0]+m[0])/divisor,b[1]+width*(n[1]+m[1])/divisor))
        caps = Geometry()
        caps.extrude(inset,low,high)
        for face in caps.faces:
            vertices = [caps.vertices[k] for k in face]
            if abs(vertices[0][5]) > .99:
                self.face([v[:3] for v in vertices])
        for i,a in enumerate(p):
            j=(i+1)%len(p)
            b=p[j]
            self.face([(*a,low+width),(*b,low+width),(*b,high-width),(*a,high-width)])
            self.face([(*a,high-width),(*b,high-width),(*inset[j],high),(*inset[i],high)])
            self.face([(*b,low+width),(*a,low+width),(*inset[i],low),(*inset[j],low)])

    def ring(self, x, y, outer, inner, z, count=24):
        """중심과 두 반경 및 깊이를 받아 표면 표식용 원형 띠를 추가한다."""
        for i in range(count):
            a,b=i*math.tau/count,(i+1)*math.tau/count
            self.face([(x+outer*math.cos(a),y+outer*math.sin(a),z),
                       (x+outer*math.cos(b),y+outer*math.sin(b),z),
                       (x+inner*math.cos(b),y+inner*math.sin(b),z),
                       (x+inner*math.cos(a),y+inner*math.sin(a),z)])

    def save(self, name):
        """메시 이름을 받아 Unity 직렬화 파일을 저장하고 GUID를 반환한다."""
        template = (SOURCE / 'FrontSteel.asset').read_text()
        template = template.replace('m_Name: FrontSteel', f'm_Name: {name}')
        for key, value in [('indexCount', len(self.faces)*3), ('vertexCount',len(self.vertices)), ('m_VertexCount',len(self.vertices)), ('m_DataSize',len(self.vertices)*24)]:
            template = re.sub(rf'({key}: )\d+', rf'\g<1>{value}', template)
        template = re.sub(r'm_IndexBuffer: .*', 'm_IndexBuffer: '+b''.join(struct.pack('<3H', *f) for f in self.faces).hex(), template)
        template = re.sub(r'_typelessdata: .*', '_typelessdata: '+b''.join(struct.pack('<6f', *v) for v in self.vertices).hex(), template)
        mins = [min(v[i] for v in self.vertices) for i in range(3)]
        maxs = [max(v[i] for v in self.vertices) for i in range(3)]
        for key, vals in [('m_Center',[(a+b)/2 for a,b in zip(mins,maxs)]), ('m_Extent',[(b-a)/2 for a,b in zip(mins,maxs)])]:
            template = re.sub(rf'{key}: \{{[^}}]+\}}', key+': {x: %g, y: %g, z: %g}'%tuple(vals), template)
        template = template.replace('m_IsReadable: 1', 'm_IsReadable: 0').replace('m_KeepVertices: 1','m_KeepVertices: 0').replace('m_KeepIndices: 1','m_KeepIndices: 0')
        path = DEST / (name+'.asset')
        path.write_text(template)
        return meta(path)


def material(name, color, metallic, smoothness):
    """이름과 표면 설정을 받아 공용 URP 재질을 저장하고 GUID를 반환한다."""
    text = (SOURCE / 'BlackGrip.mat').read_text().replace('m_Name: BlackGrip', 'm_Name: '+name)
    for prop in ('_BaseColor', '_Color'):
        text = re.sub(rf'{prop}: \{{[^}}]+\}}', prop+': {r: %g, g: %g, b: %g, a: 1}'%color, text)
    for prop, value in [('_Metallic',metallic),('_Smoothness',smoothness)]:
        text = re.sub(rf'({prop}: )[^\n]+', rf'\g<1>{value}', text)
    text = text.replace('m_EnableInstancingVariants: 0','m_EnableInstancingVariants: 1')
    path = DEST / (name+'.mat')
    path.write_text(text)
    return meta(path, main=2100000)


def build():
    """참조 사진의 집게와 힌지를 생성하고 프리팹 및 확인용 이미지를 저장한다."""
    DEST.mkdir(exist_ok=True)
    meta(DEST, 'DefaultImporter')
    gray = material('AnodizedGray', (.29,.31,.33), .65, .38)
    steel = material('PivotSteel', (.72,.74,.75), .15, .3)
    jaws = []
    for sign in (1,-1):
        mesh = Geometry()
        # 긴 손잡이와 반대편 짧은 집게를 중앙 회전판으로 연결한다.
        outline = [(.63,2.86),(.62,2.94),(.57,3.00),(.49,3.02),(.42,3.00),(.37,2.94),
                   (.36,2.82),(.28,.76),(.24,.61),(.15,.49),(-.07,.47),(-.28,.39),
                   (-.43,.23),(-.48,.03),(-.47,-.25),(-.62,-1.46),(-.60,-1.52),
                   (-.51,-1.52),(-.34,-.62),(-.27,-.51),(-.10,-.46),(.15,-.45),
                   (.34,-.32),(.46,-.13),(.48,.05),(.43,.28),(.39,.51),(.41,.80),(.57,2.65)]
        mesh.bevel(outline,.43,.60)
        handle = clip_height(outline,.63,True)
        mesh.extrude(handle,-.60,.43)
        # 집게 안쪽에는 스위치 걸쇠를 누르는 돌기와 아래쪽 턱을 둔다.
        jaw = clip_height(outline,-.51,False)
        mesh.extrude(jaw,-.60,.43)
        mesh.bevel([(-.52,-1.50),(-.42,-1.43),(-.34,-1.32),(-.38,-1.27),(-.55,-1.38)],-.53,.37,.012)
        latch = Geometry()
        latch.bevel([(-.54,-.97),(-.49,-1.22),(-.29,-1.18),(-.30,-1.06)],-.15,.15,.012)
        offset=len(mesh.vertices)
        mesh.vertices.extend(latch.vertices)
        mesh.faces.extend(tuple(k+offset for k in f) for f in latch.faces)
        mesh.cylinder(0,0,.405,.601,.612,32)
        if sign<0:
            mesh.vertices=[(-x,y,-z,-nx,ny,-nz) for x,y,z,nx,ny,nz in mesh.vertices]
        jaws.append(mesh)
    details = Geometry()
    for x,angle in ((-.175,0),(.175,math.pi/4)):
        details.ring(x,0,.145,.133,.614,24)
        for rotation in (angle,angle+math.pi/2):
            pts=[(x+u*math.cos(rotation)-v*math.sin(rotation),u*math.sin(rotation)+v*math.cos(rotation))
                 for u,v in [(-.093,-.027),(.093,-.027),(.093,.027),(-.093,.027)]]
            details.face([(*p,.6145) for p in pts])
    template = (ROOT/'Assets/Resource/Prefabs/Workshop/Tools/KeycapPuller_Clip.prefab').read_text()
    blocks = re.split(r'(?=--- !u!)', template)[1:]
    types = {t: next(b for b in blocks if b.startswith('--- !u!'+t+' ')) for t in ('1','4','33','23')}
    output = ['%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n']
    for i, name in enumerate(('SwitchPuller_TX_Gray','FrontJaw','RearJaw','PivotHardware')):
        base = 1000+i*10
        go = re.sub(r'&\d+', '&'+str(base), types['1'], count=1)
        go = re.sub(r'm_Name: .*', 'm_Name: '+name, go)
        go = re.sub(r'  m_Component:.*?  m_Layer:', '  m_Component:\n'+''.join('  - component: {fileID: %d}\n'%(base+j) for j in ((1,) if i==0 else (1,2,3)))+'  m_Layer:', go, flags=re.S)
        tr = re.sub(r'&\d+', '&'+str(base+1), types['4'], count=1)
        tr = re.sub(r'm_GameObject: .*','m_GameObject: {fileID: %d}'%base,tr)
        tr = re.sub(r'm_LocalPosition: .*','m_LocalPosition: {x: 0, y: 0, z: 0}',tr)
        children = '\n  - {fileID: 1011}\n  - {fileID: 1021}' if i==0 else '\n  - {fileID: 1031}' if i==1 else ' []'
        tr = re.sub(r'  m_Children:.*?  m_Father:.*', '  m_Children:'+children+'\n  m_Father: {fileID: %d}'%(0 if i==0 else 1011 if i==3 else 1001),tr,flags=re.S)
        tr += '\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n'
        output.extend((go,tr))
        if i:
            mesh = (jaws+[details])[i-1]
            guid = mesh.save(name)
            for kind,j in [('33',2),('23',3)]:
                block = re.sub(r'&\d+', '&'+str(base+j),types[kind],count=1)
                block = re.sub(r'm_GameObject: .*','m_GameObject: {fileID: %d}'%base,block)
                block = re.sub(r'guid: \w+', 'guid: '+(guid if kind=='33' else steel if i==3 else gray), block)
                output.append(block)
    PREFAB.write_text(''.join(output))
    meta(PREFAB,'PrefabImporter')
    image = preview_mesh(jaws+[details])
    preview = ROOT/'output/SwitchPuller_TX_Gray.png'
    preview.parent.mkdir(exist_ok=True)
    image.save(preview)
    print('Triangles:',sum(len(m.faces) for m in jaws+[details]))
    print('Vertices:',sum(len(m.vertices) for m in jaws+[details]))
    print(PREFAB)


def preview_mesh(meshes):
    """메시 목록을 받아 깊이 버퍼로 가림을 처리한 제품 형태 미리보기를 반환한다."""
    import numpy as np
    from PIL import Image, ImageDraw
    size=1200
    pixels=np.full((size,size,3),239,dtype=np.uint8)
    depth=np.full((size,size),-np.inf)
    view=np.array((.60,-.30,.74)); view/=np.linalg.norm(view)
    right=np.cross((0,1,0),view); right/=np.linalg.norm(right)
    up=np.cross(view,right)
    light=np.array((-.35,.65,.90)); light/=np.linalg.norm(light)
    for mesh,color in zip(meshes,[(111,116,122),(111,116,122),(211,214,216)]):
        for face in mesh.faces:
            vertices=np.array([mesh.vertices[k] for k in face])
            normal=vertices[0,3:]
            if normal@view<=0:
                continue
            xyz=vertices[:,:3]-np.array((0,.75,0))
            screen=np.column_stack((size/2+xyz@right*215,size/2-xyz@up*215,xyz@view))
            low=np.maximum(0,np.floor(screen[:,:2].min(axis=0)).astype(int))
            high=np.minimum(size-1,np.ceil(screen[:,:2].max(axis=0)).astype(int))
            if np.any(high<low):
                continue
            xx,yy=np.meshgrid(np.arange(low[0],high[0]+1)+.5,np.arange(low[1],high[1]+1)+.5)
            a,b,c=screen
            denominator=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(denominator)<1e-8:
                continue
            u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/denominator
            v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/denominator
            w=1-u-v
            z=u*a[2]+v*b[2]+w*c[2]
            region=depth[low[1]:high[1]+1,low[0]:high[0]+1]
            mask=(u>=-1e-7)&(v>=-1e-7)&(w>=-1e-7)&(z>region)
            region[mask]=z[mask]
            shade=.42+.58*max(0,normal@light)
            pixels[low[1]:high[1]+1,low[0]:high[0]+1][mask]=np.array(color)*shade
    image=Image.fromarray(pixels)
    ImageDraw.Draw(image).text((35,35),'AL CNC SWITCH PULLER / GRAY - geometry preview',fill='#30343a')
    return image


if __name__ == '__main__':
    build()
