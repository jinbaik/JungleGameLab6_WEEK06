import ast, math, re, struct, uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Resource/Meshes/Environment'
OUT.mkdir(parents=True, exist_ok=True)
source = (ROOT / 'Tools/GenerateKeycapFragments.py').read_text()
functions = [node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef)]
exec(compile(ast.Module(body=functions, type_ignores=[]), 'GenerateKeycapFragments.py', 'exec'))
template = (ROOT / 'Assets/Resource/Meshes/Keycap_1u.asset').read_text()
KEY = 'WindowLightBeam'
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
shader = ROOT / 'Assets/Resource/Shaders/WindowLightBeam.shader'
shader_guid = meta(shader, 'ShaderImporter')
script_guid = meta(ROOT / 'Assets/Scripts/Environment/WindowLightDust.cs', 'MonoImporter')
materials = ROOT / 'Assets/Resource/Materials/Environment'
materials.mkdir(parents=True, exist_ok=True)

def material(name, alpha, dust):
    path = materials / (name + '.mat')
    path.write_text(HEADER + f'--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_ObjectHideFlags: 0\n  m_Name: {name}\n  m_Shader: {{fileID: 4800000, guid: {shader_guid}, type: 3}}\n  m_ValidKeywords: []\n  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_CustomRenderQueue: 3000\n  stringTagMap:\n    RenderType: Transparent\n  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs: []\n    m_Ints: []\n    m_Floats:\n    - _DustMode: {dust}\n    m_Colors:\n    - _Color: {{r: 1, g: 0.91, b: 0.72, a: {alpha}}}\n')
    return meta(path, fileid=2100000)

beam_material = material('WindowSunbeam', .035, 0)
dust_material = material('WindowDust', .5, 1)
background = (ROOT / 'Assets/Resource/Prefabs/Keyboard_TKL_87_Background.prefab').read_text()
renderer_template = re.search(r'--- !u!23 &\d+\n(.*?)(?=--- !u!)', background, re.S).group(1)
prefab = HEADER + obj(100, 'WindowSunlight', [101]) + transform(101, 100, children=[201, 301, 401, 501, 601, 701, 801])
direction = (.12, -.6, -1)
for index in range(6):
    column, row = divmod(index, 3)
    x = (-2.73, 2.73)[column]
    y = (row - 1) * 2.51
    width, height = 2.35, 1.10
    pairs = [((x-width, y, 0), (x+width, y, 0)), ((x, y-height, 0), (x, y+height, 0)), ((x-width*.75, y-height*.75, 0), (x+width*.75, y+height*.75, 0))]
    triangles, uvs = [], []
    for a, b in pairs:
        def endpoint(v):
            distance = min((v[1] + 9.85 + 3.7) / .6, 27.5)
            return add(v, mul(direction, distance))
        c, d = endpoint(b), endpoint(a)
        triangles.extend([(a, b, d), (b, c, d)])
        uvs.extend([(0, 0), (1, 0), (0, 1), (1, 0), (1, 1), (0, 1)])
    mesh_guid, _ = meshasset(triangles, index + 1)
    mesh_path = OUT / f'{KEY}_Fragment_{index+1}.asset'
    mesh = mesh_path.read_text()
    packed = bytearray.fromhex(re.search(r'_typelessdata: (\w+)', mesh).group(1))
    for vertex, uv in enumerate(uvs): struct.pack_into('<2f', packed, vertex * 48 + 40, *uv)
    mesh = re.sub(r'_typelessdata: \w+', '_typelessdata: ' + packed.hex(), mesh)
    mesh_path.write_text(mesh)
    i = (index + 2) * 100
    prefab += obj(i, f'Sunbeam_{index+1}', [i+1, i+2, i+3]) + transform(i+1, i, 101)
    prefab += f'--- !u!33 &{i+2}\nMeshFilter:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {i}}}\n  m_Mesh: {{fileID: 4300000, guid: {mesh_guid}, type: 2}}\n'
    renderer = re.sub(r'm_GameObject: \{fileID: \d+\}', f'm_GameObject: {{fileID: {i}}}', renderer_template)
    renderer = re.sub(r'm_CastShadows: \d+', 'm_CastShadows: 0', renderer)
    renderer = re.sub(r'm_ReceiveShadows: \d+', 'm_ReceiveShadows: 0', renderer)
    renderer = re.sub(r'm_Materials:\n  - .*', f'm_Materials:\n  - {{fileID: 2100000, guid: {beam_material}, type: 2}}', renderer)
    prefab += f'--- !u!23 &{i+3}\n' + renderer
prefab += obj(800, 'WindowDust', [801, 802]) + transform(801, 800, 101).replace('m_LocalPosition: {x: 0, y: 0, z: 0}', 'm_LocalPosition: {x: 1, y: -5.4, z: -9}')
prefab += f'--- !u!114 &802\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: 800}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::WindowLightDust\n  _material: {{fileID: 2100000, guid: {dust_material}, type: 2}}\n  _volume: {{x: 8, y: 6, z: 10}}\n  _emissionRate: 6\n'
path = ROOT / 'Assets/Resource/Prefabs/WindowSunlight.prefab'
path.write_text(prefab)
prefab_guid = meta(path, 'PrefabImporter')
scene_path = ROOT / 'Assets/Scenes/Main.unity'
scene = scene_path.read_text()
sun = re.search(r'--- !u!108 &410087040\n(.*?)(?=--- !u!)', scene, re.S).group(0)
updated = re.sub(r'm_Color: \{[^}]+\}', 'm_Color: {r: 1, g: 0.94, b: 0.82, a: 1}', sun)
updated = re.sub(r'm_Intensity: [^\n]+', 'm_Intensity: 2.6', updated)
updated = re.sub(r'm_Type: [12](\n    m_Resolution)', r'm_Type: 2\1', updated)
updated = re.sub(r'm_NormalBias: [^\n]+', 'm_NormalBias: 0.08', updated)
scene = scene.replace(sun, updated)
pitch = math.atan2(.6, math.sqrt(1+.12**2)) / 2
yaw = math.atan2(.12, -1) / 2
q = (math.cos(yaw)*math.sin(pitch), math.sin(yaw)*math.cos(pitch), -math.sin(yaw)*math.sin(pitch), math.cos(yaw)*math.cos(pitch))
sun_transform = re.search(r'--- !u!4 &410087041\n(.*?)(?=--- !u!)', scene, re.S).group(0)
updated = re.sub(r'm_LocalRotation: \{[^}]+\}', 'm_LocalRotation: {x: %.8g, y: %.8g, z: %.8g, w: %.8g}' % q, sun_transform)
scene = scene.replace(sun_transform, updated)
if '&3000000000\n' not in scene:
    instance = '--- !u!1001 &3000000000\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {fileID: 0}\n    m_Modifications:\n'
    for axis, value in zip('xyz', (0, 9.85, 15.1)):
        instance += f'    - target: {{fileID: 101, guid: {prefab_guid}, type: 3}}\n      propertyPath: m_LocalPosition.{axis}\n      value: {value}\n      objectReference: {{fileID: 0}}\n'
    instance += f'    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n  m_SourcePrefab: {{fileID: 100100000, guid: {prefab_guid}, type: 3}}\n'
    scene = scene.replace('--- !u!1660057539 &9223372036854775807', instance + '--- !u!1660057539 &9223372036854775807')
    scene += '  - {fileID: 3000000000}\n'
scene_path.write_text(scene)
print('Main: aligned sunlight, six saved beam meshes and floating dust at the existing room window.')
