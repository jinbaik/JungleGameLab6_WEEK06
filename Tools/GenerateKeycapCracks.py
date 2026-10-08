import math, random, re, struct, sys, uuid
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = next((ROOT / 'Library/PackageCache').glob('com.unity.render-pipelines.universal@*'))
TEXTURES = ROOT / 'Assets/Resource/Textures/Keyboard'
MATERIALS = ROOT / 'Assets/Resource/Materials/Keyboard'
SHADERS = ROOT / 'Assets/Resource/Shaders/Keyboard'

def meta(path, importer, extra=''):
    target = Path(str(path) + '.meta')
    if target.exists(): return re.search(r'guid: (\w+)', target.read_text()).group(1)
    guid = uuid.uuid4().hex
    target.write_text(f'fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n{extra}  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')
    return guid

for folder in (TEXTURES, MATERIALS, SHADERS):
    folder.mkdir(parents=True, exist_ok=True)
    for parent in (folder.parent, folder):
        if not Path(str(parent)+'.meta').exists():
            Path(str(parent)+'.meta').write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')

shader = SHADERS / 'KeycapCracks.shadergraph'
graph = (PACKAGE / 'Shaders/Decal.shadergraph').read_text()
graph = graph.replace('"affectsNormalBlend": true', '"affectsNormalBlend": false').replace('"affectsNormal": true', '"affectsNormal": false')
if '--textures-only' not in sys.argv and '--wrap-sides' not in sys.argv: shader.write_text(graph, encoding='utf-8')
shader_guid = meta(shader, 'ShaderGraphImporter')

rng = random.Random(41)
paths = []
for index in range(8):
    angle = index * math.tau / 8 + rng.uniform(-.2, .2)
    points = [(256, 240)]
    for step in range(1, 10):
        distance = step * rng.uniform(19, 24)
        points.append((256 + math.cos(angle)*distance + rng.uniform(-9, 9), 240 + math.sin(angle)*distance + rng.uniform(-9, 9)))
    points.append((256 + math.cos(angle)*420, 240 + math.sin(angle)*420))
    paths.append(points)

materials = []
for stage, arms, length in [(1, 2, 11), (2, 5, 11), (3, 8, 11)]:
    image = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for arm in range(arms):
        points = paths[arm][:length]
        segments = [points]
        if stage > 1:
            for i in (2, 4):
                x, y = points[i]
                segments.append([(x,y), (x+22,y-14), (x+32,y-31), (x+47,y-36)])
        for line in segments:
            scaled = [(x*2,y*2) for x,y in line]
            crack_width = (24, 36, 48)[stage-1]
            draw.line(scaled, fill=(255,250,240,255), width=crack_width+16, joint='curve')
            draw.line(scaled, fill=(0,0,0,255), width=crack_width, joint='curve')
    image = image.resize((512,512), Image.Resampling.LANCZOS)
    texture = TEXTURES / f'KeycapCracks_{stage}.png'
    image.save(texture)
    if '--textures-only' in sys.argv or '--wrap-sides' in sys.argv: continue
    texture_guid = meta(texture, 'TextureImporter', '  serializedVersion: 13\n  mipmaps:\n    enableMipMap: 1\n    sRGBTexture: 1\n  textureSettings:\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  maxTextureSize: 512\n  textureType: 0\n  textureShape: 1\n  alphaSource: 1\n  alphaIsTransparency: 1\n  isReadable: 0\n')
    material = (PACKAGE / 'Runtime/Materials/Decal.mat').read_text()
    material = material[material.index('%YAML'):]
    material = re.sub(r'--- !u!114 .*?(?=--- !u!21)', '', material, flags=re.S)
    material = material.replace('m_Name: Decal', f'm_Name: KeycapCracks_{stage}')
    material = re.sub(r'm_Shader: \{[^}]+\}', f'm_Shader: {{fileID: -6465566751694194690, guid: {shader_guid}, type: 3}}', material)
    material = material.replace('- Base_Map:\n        m_Texture: {fileID: 0}', f'- Base_Map:\n        m_Texture: {{fileID: 2800000, guid: {texture_guid}, type: 3}}')
    material = material.replace('Normal_Blend: 0.5', 'Normal_Blend: 0')
    path = MATERIALS / f'KeycapCracks_{stage}.mat'
    path.write_text(material, encoding='utf-8')
    materials.append(meta(path, 'NativeFormatImporter', '  mainObjectFileID: 2100000\n'))

if '--textures-only' in sys.argv: sys.exit(0)
projector_guid = re.search(r'guid: (\w+)', (PACKAGE/'Runtime/Decal/DecalProjector.cs.meta').read_text()).group(1)
for path in (ROOT/'Assets/Resource/Prefabs/Keycaps').glob('*.prefab'):
    text = path.read_text(encoding='utf-8')
    blocks = {int(i):(int(t),b) for t,i,b in re.findall(r'--- !u!(\d+) &(\d+)\n(.*?)(?=--- !u!|\Z)',text,re.S)}
    root_id, root = next((i,b) for i,(t,b) in blocks.items() if t==4 and 'm_Father: {fileID: 0}' in b)
    size = re.search(r'm_Size: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}', next(b for t,b in blocks.values() if t==65))
    width, _, height = map(float, size.groups())
    if '_damageDecal:' in text:
        projector_id = int(re.search(r'_damageDecal: \{fileID: (\d+)', text).group(1))
        projector = blocks[projector_id][1]
        projector = re.sub(r'm_StartAngleFade: .*', 'm_StartAngleFade: 180', projector)
        projector = re.sub(r'm_EndAngleFade: .*', 'm_EndAngleFade: 180', projector)
        projector = re.sub(r'm_Size: \{[^}]+\}', f'm_Size: {{x: {width*1.001}, y: {height*1.001}, z: 0.54}}', projector)
        text = text.replace(blocks[projector_id][1], projector, 1)
        projector_go = int(re.search(r'm_GameObject: \{fileID: (\d+)', projector).group(1))
        decal_transform = next(b for t,b in blocks.values() if t==4 and f'm_GameObject: {{fileID: {projector_go}}}' in b)
        text = text.replace(decal_transform, re.sub(r'm_LocalPosition: \{[^}]+\}', 'm_LocalPosition: {x: 0, y: 0.26, z: 0}', decal_transform), 1)
        path.write_text(text, encoding='utf-8')
        print('Wrapped cracks over top and sides:', path.stem)
        continue
    text = text.replace(root, root.replace('  m_Children:\n', '  m_Children:\n  - {fileID: 910000001}\n'), 1)
    fields = '  _damageDecal: {fileID: 910000002}\n  _crackMaterials:\n' + ''.join(f'  - {{fileID: 2100000, guid: {g}, type: 2}}\n' for g in materials)
    text = text.replace('  _maxHP:', fields+'  _maxHP:', 1)
    text += f'''--- !u!1 &910000000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 910000001}}
  - component: {{fileID: 910000002}}
  m_Layer: 0
  m_Name: DamageDecal
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &910000001
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: 910000000}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0.7071068, y: 0, z: 0, w: 0.7071068}}
  m_LocalPosition: {{x: 0, y: 0.26, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children: []
  m_Father: {{fileID: {root_id}}}
  m_LocalEulerAnglesHint: {{x: 90, y: 0, z: 0}}
--- !u!114 &910000002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: 910000000}}
  m_Enabled: 0
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {projector_guid}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  m_Material: {{fileID: 2100000, guid: {materials[0]}, type: 2}}
  m_DrawDistance: 1000
  m_FadeScale: 0.9
  m_StartAngleFade: 180
  m_EndAngleFade: 180
  m_UVScale: {{x: 1, y: 1}}
  m_UVBias: {{x: 0, y: 0}}
  m_RenderingLayerMask: 1
  m_ScaleMode: 1
  m_Offset: {{x: 0, y: 0, z: 0}}
  m_Size: {{x: {width*1.001}, y: {height*1.001}, z: 0.54}}
  m_FadeFactor: 1
  version: 1
'''
    path.write_text(text, encoding='utf-8')
    print('Connected damage decal:', path.stem)

if '--wrap-sides' in sys.argv: sys.exit(0)
for path in (ROOT/'Assets/Settings/PC_Renderer.asset', ROOT/'Assets/Settings/Mobile_Renderer.asset'):
    text = path.read_text(encoding='utf-8')
    if 'guid: a1614fc811f8f184697d9bee70ab9fe5' in text: continue
    feature_id = 910000003
    if 'm_RendererFeatures: []' in text:
        text = text.replace('m_RendererFeatures: []', f'm_RendererFeatures:\n  - {{fileID: {feature_id}}}')
    else:
        text = text.replace('  m_RendererFeatures:\n', f'  m_RendererFeatures:\n  - {{fileID: {feature_id}}}\n')
    text = re.sub(r'm_RendererFeatureMap: *(\w*)', lambda m:'m_RendererFeatureMap: '+struct.pack('<q',feature_id).hex()+m.group(1), text)
    text += f'''--- !u!114 &{feature_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: a1614fc811f8f184697d9bee70ab9fe5, type: 3}}
  m_Name: KeycapDecals
  m_EditorClassIdentifier: 
  m_Active: 1
  m_Settings:
    technique: 2
    maxDrawDistance: 1000
    decalLayers: 0
    dBufferSettings:
      surfaceData: 0
    screenSpaceSettings:
      normalBlend: 1
'''
    path.write_text(text, encoding='utf-8')
    print('Enabled decal rendering:', path.stem)
