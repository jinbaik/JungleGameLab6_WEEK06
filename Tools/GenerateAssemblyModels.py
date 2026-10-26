"""기존 TKL 조립 좌표에 맞는 관통 보강판과 MX 규격 축의 공유 메시를 생성한다."""
import json
import math
import re
import struct
from pathlib import Path

import GenerateSwitchPuller as native

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Assets/Resource/Meshes/KeyboardAssembly'
SOURCE_PREFAB = ROOT / 'Assets/Resource/Prefabs/SeperateKeyboard/Keyboard_Assembly87.prefab'
OLD_PLATE_GUID = '5cbb645043b73e74b84c51a8df1298f0'
Geometry = native.Geometry


def read(path):
    """경로의 Unity 텍스트를 BOM 유무에 관계없이 UTF-8로 반환한다."""
    return Path(path).read_text(encoding='utf-8-sig')


def blocks(text):
    """Unity YAML 문자열을 받아 객체 ID와 직렬화 블록의 사전을 반환한다."""
    return {re.search(r'&(-?\d+)',b).group(1):b for b in re.split(r'(?=--- !u!)',text)[1:]}


def vector(block, property_name):
    """블록과 벡터 필드 이름을 받아 XYZ 값을 반환한다."""
    return tuple(float(n) for n in re.search(property_name+r': \{x: ([^,]+), y: ([^,]+), z: ([^}]+)',block).groups())


def rectangle(x, z, width, depth, y):
    """중심과 크기 및 높이를 받아 위쪽을 향하는 사각 면의 정점을 반환한다."""
    a,b=width/2,depth/2
    return [(x-a,y,z-b),(x-a,y,z+b),(x+a,y,z+b),(x+a,y,z-b)]


def loft(mesh, lower, upper, cap_bottom=True, cap_top=True):
    """같은 정점 수의 아래와 위 윤곽을 받아 하우징 벽과 선택한 뚜껑을 추가한다."""
    if cap_bottom:
        mesh.face(list(reversed(lower)))
    if cap_top:
        mesh.face(upper)
    for i in range(len(lower)):
        j=(i+1)%len(lower)
        mesh.face([lower[i],lower[j],upper[j],upper[i]])


def box(mesh, center, size):
    """중심과 XYZ 크기를 받아 닫힌 직육면체를 메시 목록에 추가한다."""
    x,y,z=center
    width,height,depth=size
    loft(mesh,rectangle(x,z,width,depth,y-height/2),rectangle(x,z,width,depth,y+height/2))


def tube(mesh, x, z, radius, bottom, top, segments=8):
    """핀의 중심과 크기를 받아 저폴리 수직 원통을 추가한다."""
    rings=[[(x+radius*math.cos(-i*math.tau/segments),y,z+radius*math.sin(-i*math.tau/segments)) for i in range(segments)] for y in (bottom,top)]
    loft(mesh,*rings)


def save_mesh(mesh, name, split_index=None):
    """공유 메시와 선택한 재질 분할 위치를 받아 Unity 에셋과 GUID를 저장한다."""
    native.DEST=DEST
    guid=mesh.save(name)
    if split_index is not None:
        path=DEST/(name+'.asset')
        text=read(path)
        section=re.search(r'  m_SubMeshes:\n(.*?)  m_Shapes:',text,re.S).group(1)
        first=re.sub(r'indexCount: \d+',f'indexCount: {split_index*3}',section)
        second=re.sub(r'firstByte: \d+',f'firstByte: {split_index*6}',section)
        second=re.sub(r'indexCount: \d+',f'indexCount: {(len(mesh.faces)-split_index)*3}',second)
        text=text.replace('  m_SubMeshes:\n'+section+'  m_Shapes:','  m_SubMeshes:\n'+first+second+'  m_Shapes:')
        path.write_text(text,encoding='utf-8')
    return guid


def layout():
    """기존 87키 프리팹을 읽어 축의 XZ 장착 좌표 목록을 반환한다."""
    data=blocks(read(SOURCE_PREFAB))
    positions=[]
    for block in data.values():
        if block.startswith('--- !u!1 ') and re.search(r'm_Name: Switch_\d+\n',block):
            transform=data[re.search(r'component: \{fileID: (\d+)\}',block).group(1)]
            x,_,z=vector(transform,'m_LocalPosition')
            positions.append((x,z))
    if len(positions)!=87 or len(set(positions))!=87:
        raise ValueError('87키 장착 좌표를 확인해야 합니다.')
    return sorted(positions,key=lambda p:(-p[1],p[0]))


def surface(mesh, holes, y, upward):
    """구멍 사각형 목록과 높이를 받아 구멍을 제외한 보강판 평면을 추가한다."""
    bands=sorted({-3.49,3.49,*[h[i] for h in holes for i in (1,3)]})
    for za,zb in zip(bands,bands[1:]):
        middle=(za+zb)/2
        intervals=sorted((h[0],h[2]) for h in holes if h[1]<middle<h[3])
        merged=[]
        for left,right in intervals:
            if merged and left<=merged[-1][1]:
                merged[-1][1]=max(merged[-1][1],right)
            else:
                merged.append([left,right])
        cursor=-9.4
        for left,right in merged+[[9.4,9.4]]:
            if left>cursor+1e-8:
                points=rectangle((cursor+left)/2,middle,left-cursor,zb-za,y)
                mesh.face(points if upward else list(reversed(points)))
            cursor=max(cursor,right)


def plate(positions):
    """기존 축 좌표를 받아 87개 관통 구멍과 스테빌라이저 구멍이 있는 보강판을 반환한다."""
    mesh=Geometry()
    holes=[]
    half=14/19.05/2
    for x,z in positions:
        x-=9.25
        z-=2.8
        holes.append((x-half,z-half,x+half,z+half))
    for x,z,offset in [(14,4.4,.625),(13.875,2.4,.625),(1.125,1.4,.625),(13.625,1.4,.625),(6.875,.4,2.625)]:
        for sign in (-1,1):
            cx=x+offset*sign-9.25
            cz=z-2.8
            holes.append((cx-.145,cz-.29,cx+.145,cz+.29))
    expanded=[(a-.024,b-.024,c+.024,d+.024) for a,b,c,d in holes]
    for i,a in enumerate(expanded):
        if not (-9.4<a[0]<a[2]<9.4 and -3.49<a[1]<a[3]<3.49):
            raise ValueError('보강판 바깥으로 구멍이 나갑니다.')
        for b in expanded[i+1:]:
            if min(a[2],b[2])>max(a[0],b[0]) and min(a[3],b[3])>max(a[1],b[1]):
                raise ValueError('구멍이 겹칩니다.')
    surface(mesh,expanded,.04,True)
    surface(mesh,holes,-.04,False)
    for inner,outer in zip(holes,expanded):
        a,b,c,d=inner
        low=rectangle((a+c)/2,(b+d)/2,c-a,d-b,-.04)
        mid=[(x,.016,z) for x,_,z in low]
        a,b,c,d=outer
        top=rectangle((a+c)/2,(b+d)/2,c-a,d-b,.04)
        for i in range(4):
            j=(i+1)%4
            mesh.face([low[j],low[i],mid[i],mid[j]])
            mesh.face([mid[j],mid[i],top[i],top[j]])
    outer_low=rectangle(0,0,18.8,6.98,-.04)
    outer_top=rectangle(0,0,18.8,6.98,.04)
    loft(mesh,outer_low,outer_top,False,False)
    return mesh,holes


def switch():
    """MX 도면 비율을 사용해 상하 하우징과 십자 스템 및 접점 핀의 메시를 반환한다."""
    top,bottom,stem=Geometry(),Geometry(),Geometry()
    # 위쪽은 경사 하우징과 스템 구멍을 분리해 블록 형태를 없앤다.
    lower=rectangle(0,0,.79,.79,.026)
    upper=rectangle(0,0,.54,.54,.346)
    top.face(list(reversed(lower)))
    for edge in range(4):
        next_edge=(edge+1)%4
        if edge not in (1,3):
            top.face([lower[edge],lower[next_edge],upper[next_edge],upper[edge]])
            continue
        def point(u,v):
            """면 안의 가로와 세로 비율을 받아 경사진 하우징 위의 위치를 반환한다."""
            return tuple((lower[edge][k]*(1-u)+lower[next_edge][k]*u)*(1-v)+
                         (upper[edge][k]*(1-u)+upper[next_edge][k]*u)*v for k in range(3))
        us,vs=(0,.32,.68,1),(0,.20,.80,1)
        for i in range(3):
            for j in range(3):
                if i==1 and j==1:
                    continue
                top.face([point(us[i],vs[j]),point(us[i+1],vs[j]),point(us[i+1],vs[j+1]),point(us[i],vs[j+1])])
        boundary=[point(.32,.20),point(.68,.20),point(.68,.80),point(.32,.80)]
        direction=-1 if edge==1 else 1
        recessed=[(x,y,z+direction*.065) for x,y,z in boundary]
        top.face(recessed)
        for i in range(4):
            j=(i+1)%4
            top.face([boundary[i],boundary[j],recessed[j],recessed[i]])
    aperture=rectangle(0,0,.242,.242,.346)
    well=rectangle(0,0,.242,.242,.235)
    for i in range(4):
        j=(i+1)%4
        top.face([upper[i],upper[j],aperture[j],aperture[i]])
        top.face([aperture[i],aperture[j],well[j],well[i]])
    for sign in (-1,1):
        box(top,(sign*.374,.075,0),(.065,.16,.19))
        box(top,(sign*.284,.215,0),(.085,.11,.10))
    # 하부 몸체는 14mm 구멍 안으로 들어가고 테두리 플랜지가 판 위에 걸린다.
    loft(bottom,rectangle(0,0,.66,.66,-.262),rectangle(0,0,.728,.728,-.015))
    box(bottom,(0,.005,0),(.82,.042,.82))
    for sign in (-1,1):
        for side in (-1,1):
            box(bottom,(sign*.351,-.10,side*.22),(.034,.22,.073))
    tube(bottom,0,0,.10,-.415,-.26,10)
    for x in (-.269,.269):
        tube(bottom,x,0,.036,-.36,-.26)
    split_index=len(bottom.faces)
    for x,z in ((-.20,.15),(.16,.25)):
        box(bottom,(x,-.34,z),(.026,.156,.048))
    box(stem,(0,.335,0),(.232,.055,.232))
    cross=[(-.105,-.0315),(-.0315,-.0315),(-.0315,-.105),(.0315,-.105),(.0315,-.0315),(.105,-.0315),(.105,.0315),(.0315,.0315),(.0315,.105),(-.0315,.105),(-.0315,.0315),(-.105,.0315)]
    raw=Geometry()
    raw.bevel(cross,.36,.535,.005)
    offset=len(stem.vertices)
    stem.vertices.extend((x,z,-y,nx,nz,-ny) for x,y,z,nx,ny,nz in raw.vertices)
    stem.faces.extend(tuple(k+offset for k in f) for f in raw.faces)
    return top,bottom,stem,split_index


def patch_prefabs(guids, pin_guid):
    """새 메시 GUID로 사용 중인 시각 부품을 교체하고 기존 ID와 조립 참조를 보존한다."""
    changed=[]
    root=ROOT/'Assets/Resource/Prefabs'
    for path in root.rglob('*.prefab'):
        if '/Keyboard/Reference/' in path.as_posix() or '/Keyboard/Prefabs/' in path.as_posix():
            continue
        original=read(path)
        text=original.replace(OLD_PLATE_GUID,guids['plate'])
        data=blocks(text)
        count=0
        for go in list(data.values()):
            if not go.startswith('--- !u!1 '):
                continue
            name_match=re.search(r'm_Name: (.*)',go)
            if name_match is None:
                continue
            name=name_match.group(1)
            if name not in ('Switch_TopHousing','Switch_BottomHousing','MX_Stem_X','MX_Stem_Z'):
                continue
            component_ids=re.findall(r'component: \{fileID: (\d+)\}',go)
            for component_id in component_ids:
                component=data[component_id]
                if name=='MX_Stem_Z':
                    if component.startswith('--- !u!23 '):
                        data[component_id]=component.replace('m_Enabled: 1','m_Enabled: 0')
                    continue
                if component.startswith('--- !u!4 '):
                    component=re.sub(r'm_LocalPosition: .*','m_LocalPosition: {x: 0, y: 0, z: 0}',component)
                    component=re.sub(r'm_LocalScale: .*','m_LocalScale: {x: 1, y: 1, z: 1}',component)
                if component.startswith('--- !u!33 '):
                    kind={'Switch_TopHousing':'top','Switch_BottomHousing':'bottom','MX_Stem_X':'stem'}[name]
                    component=re.sub(r'm_Mesh: .*','m_Mesh: {fileID: 4300000, guid: '+guids[kind]+', type: 2}',component)
                    count+=1
                if component.startswith('--- !u!23 ') and name=='Switch_BottomHousing':
                    start=re.search(r'  m_Materials:\n(.*?)  m_StaticBatchInfo:',component,re.S)
                    primary=re.search(r'  - \{fileID: [^\n]+',start.group(1)).group(0)
                    component=component[:start.start()]+f'  m_Materials:\n{primary}\n  - {{fileID: 2100000, guid: {pin_guid}, type: 2}}\n  m_StaticBatchInfo:'+component[start.end():]
                data[component_id]=component
        if count:
            for go in list(data.values()):
                if not go.startswith('--- !u!1 ') or 'm_Name: Switch_TopHousing\n' not in go:
                    continue
                transform_id=re.search(r'component: \{fileID: (\d+)\}',go).group(1)
                parent_id=re.search(r'm_Father: \{fileID: (\d+)\}',data[transform_id]).group(1)
                parent_go_id=re.search(r'm_GameObject: \{fileID: (\d+)\}',data[parent_id]).group(1)
                for component_id in re.findall(r'component: \{fileID: (\d+)\}',data[parent_go_id]):
                    component=data[component_id]
                    if component.startswith('--- !u!65 '):
                        component=re.sub(r'm_Size: .*','m_Size: {x: 0.83, y: 0.97, z: 0.83}',component)
                        data[component_id]=re.sub(r'm_Center: .*','m_Center: {x: 0, y: 0.05, z: 0}',component)
            text=text[:text.index('--- !u!')]+''.join(data.values())
        if text!=original:
            path.write_text(text,encoding='utf-8')
            changed.append((str(path.relative_to(ROOT)),count))
    return changed


def render(meshes, colors, center, scale, view, size):
    """메시와 카메라 설정을 받아 깊이 검사를 적용한 확인용 이미지를 반환한다."""
    import numpy as np
    from PIL import Image
    width,height=size
    pixels=np.full((height,width,3),236,dtype=np.uint8)
    depth=np.full((height,width),-np.inf)
    view=np.array(view,dtype=float);view/=np.linalg.norm(view)
    reference=(0,0,-1) if view[1]>.9 else (0,1,0)
    right=np.cross(reference,view);right/=np.linalg.norm(right)
    up=np.cross(view,right)
    if view[1]>.9:
        up=-up
    light=np.array((-.4,1,.65));light/=np.linalg.norm(light)
    for mesh,color in zip(meshes,colors):
        for face in mesh.faces:
            vertices=np.array([mesh.vertices[k] for k in face])
            normal=vertices[0,3:]
            if normal@view<=0:
                continue
            xyz=vertices[:,:3]-np.array(center)
            screen=np.column_stack((width/2+xyz@right*scale,height/2-xyz@up*scale,xyz@view))
            low=np.maximum(0,np.floor(screen[:,:2].min(axis=0)).astype(int))
            high=np.minimum((width-1,height-1),np.ceil(screen[:,:2].max(axis=0)).astype(int))
            if np.any(high<low):
                continue
            xx,yy=np.meshgrid(np.arange(low[0],high[0]+1)+.5,np.arange(low[1],high[1]+1)+.5)
            a,b,c=screen
            den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den)<1e-9:
                continue
            u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
            v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
            w=1-u-v
            z=u*a[2]+v*b[2]+w*c[2]
            region=depth[low[1]:high[1]+1,low[0]:high[0]+1]
            mask=(u>=-1e-7)&(v>=-1e-7)&(w>=-1e-7)&(z>region)
            region[mask]=z[mask]
            shade=.35+.65*max(0,normal@light)
            pixels[low[1]:high[1]+1,low[0]:high[0]+1][mask]=np.array(color)*shade
    return Image.fromarray(pixels)


def build():
    """실제 부품 형태를 생성해 공유 에셋과 사용 프리팹 및 미리보기와 작업 보고서를 저장한다."""
    DEST.mkdir(exist_ok=True)
    native.meta(DEST,'DefaultImporter')
    positions=layout()
    board,holes=plate(positions)
    top,bottom,stem,split=switch()
    guids={'plate':save_mesh(board,'Plate_TKL87_Perforated'), 'top':save_mesh(top,'MX_TopHousing'),
           'bottom':save_mesh(bottom,'MX_BottomHousing',split), 'stem':save_mesh(stem,'MX_CrossStem')}
    native.DEST=DEST
    pin_guid=native.material('MX_ContactGold',(.65,.45,.14),.8,.5)
    changed=patch_prefabs(guids,pin_guid)
    output=ROOT/'output/KeyboardAssemblyModels'
    output.mkdir(exist_ok=True)
    from PIL import ImageDraw
    backing=Geometry()
    box(backing,(0,-.20,0),(18.65,.025,6.8))
    image=render([backing,board],[(30,36,42),(175,182,190)],(0,0,0),73,(.06,1,.16),(1600,680))
    ImageDraw.Draw(image).text((30,25),'TKL 87 / 87 THROUGH-HOLES + 10 STABILIZER OPENINGS / existing key positions',fill='#30343a')
    image.save(output/'Plate_TKL87.png')
    pins=Geometry()
    pins.vertices=bottom.vertices
    pins.faces=bottom.faces[split:]
    body=Geometry()
    body.vertices=bottom.vertices
    body.faces=bottom.faces[:split]
    image=render([body,pins,top,stem],[(42,46,52),(197,151,60),(77,83,92),(205,34,39)],(0,.05,0),580,(.65,.60,.8),(900,900))
    ImageDraw.Draw(image).text((30,25),'MX SWITCH / sloped housing, retaining clips, cross stem, locating pins + contacts',fill='#30343a')
    image.save(output/'MX_Switch.png')
    report={'sources':['https://www.cherry.de/en-us/product/mx2a-red','https://www.farnell.com/datasheets/1792245.pdf','https://txkeyboard.com/store1/?idx=82'],
            'plate_switch_holes':len(positions),'plate_stabilizer_holes':len(holes)-len(positions),
            'triangles':{'plate':len(board.faces),'switch':len(top.faces)+len(bottom.faces)+len(stem.faces)},
            'mesh_guids':guids,'changed_prefabs':changed}
    report_path=output/'BuildReport.json'
    if report_path.exists():
        previous=json.loads(read(report_path))
        report['changed_prefabs']=list(dict(previous.get('changed_prefabs',[])+changed).items())
    report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('mesh_guids','sources')},ensure_ascii=False,indent=2))


if __name__=='__main__':
    build()
