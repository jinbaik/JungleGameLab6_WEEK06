import ast, random, re, struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Resource/Prefabs/KeyboardFragment'
OUT.mkdir(parents=True, exist_ok=True)
source = (ROOT / 'Tools/GenerateKeycapFragments.py').read_text()
functions = [node for node in ast.parse(source).body if isinstance(node, ast.FunctionDef)]
import math, uuid
exec(compile(ast.Module(body=functions, type_ignores=[]), str(ROOT / 'Tools/GenerateKeycapFragments.py'), 'exec'))
template = (ROOT / 'Assets/Resource/Meshes/Keycap_1u.asset').read_text()
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
background = (ROOT / 'Assets/Resource/Prefabs/Keyboard_TKL_87_Background.prefab').read_text()
blocks = {int(i): (int(t), b) for t, i, b in re.findall(r'--- !u!(\d+) &(\d+)\n(.*?)(?=--- !u!|\Z)', background, re.S)}
names = {i: re.search(r'm_Name: (.*)', b).group(1) for i, (kind, b) in blocks.items() if kind == 1}
guidpaths = {re.search(r'guid: (\w+)', Path(str(path) + '.meta').read_text()).group(1): path for path in (ROOT / 'Assets/Resource/Meshes').glob('*.asset')}
parts = []
for _, (kind, block) in blocks.items():
    if kind != 33: continue
    go = int(re.search(r'm_GameObject: \{fileID: (\d+)', block).group(1))
    if names[go] not in ('AnodizedAluminum', 'BrushedBrass', 'Rubber'): continue
    path = guidpaths[re.search(r'm_Mesh: .*guid: (\w+)', block).group(1)]
    mesh = path.read_text()
    count = int(re.search(r'm_VertexCount: (\d+)', mesh).group(1))
    data = bytes.fromhex(re.search(r'_typelessdata: (\w+)', mesh).group(1))
    stride = len(data) // count
    vertices = [struct.unpack_from('<3f', data, index * stride) for index in range(count)]
    index_data = bytes.fromhex(re.search(r'm_IndexBuffer: (\w+)', mesh).group(1))
    index_format = 'I' if 'm_IndexFormat: 1' in mesh else 'H'
    indices = struct.unpack('<' + index_format * (len(index_data) // struct.calcsize(index_format)), index_data)
    triangles = [tuple(vertices[indices[j + k]] for k in range(3)) for j in range(0, len(indices), 3)]
    renderer = next(b for _, (t, b) in blocks.items() if t == 23 and f'm_GameObject: {{fileID: {go}}}' in b)
    parts.append((names[go], triangles, renderer))
all_vertices = [v for _, tris, _ in parts for tri in tris for v in tri]
low = tuple(map(min, zip(*all_vertices)))
high = tuple(map(max, zip(*all_vertices)))
randomizer = random.Random(618)
seeds = [(low[0] + (high[0] - low[0]) * (x + randomizer.uniform(.18, .82)) / 4, (low[1] + high[1]) / 2 + randomizer.uniform(-.7, .7), low[2] + (high[2] - low[2]) * (z + randomizer.uniform(.12, .88)) / 3) for x in range(4) for z in range(3)]
prefab = HEADER + obj(100, 'Keyboard_TKL_87_Fragments', [101]) + transform(101, 100, children=[i * 1000 + 1 for i in range(2, 14)])
for chunk in range(12):
    i = (chunk + 2) * 1000
    meshes = []
    collision_triangles = []
    for part_name, triangles, renderer in parts:
        tris = triangles
        for neighbor, seed in enumerate(seeds):
            if neighbor == chunk: continue
            normal = unit(sub(seeds[chunk], seed))
            midpoint = mul(add(seeds[chunk], seed), .5)
            tris = clip(tris, normal, dot(normal, midpoint))
            if not tris: break
        if not tris: continue
        KEY = 'Keyboard_TKL_87_' + part_name
        guid, _ = meshasset(tris, chunk + 1)
        collision_triangles.extend(tris)
        meshes.append((guid, renderer))
    KEY = 'Keyboard_TKL_87_Collision'
    collision_guid, _ = meshasset(collision_triangles, chunk + 1)
    prefab += obj(i, f'Fragment_{chunk + 1}', [i + 1, i + 2, i + 3]) + transform(i + 1, i, 101, children=[i + 11 + j * 10 for j in range(len(meshes))])
    prefab += f'--- !u!54 &{i+2}\nRigidbody:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {i}}}\n  serializedVersion: 5\n  m_Mass: 0.08\n  m_LinearDamping: 0.2\n  m_AngularDamping: 0.4\n  m_UseGravity: 1\n  m_IsKinematic: 0\n  m_Interpolate: 1\n  m_Constraints: 0\n  m_CollisionDetection: 1\n'
    prefab += f'--- !u!64 &{i+3}\nMeshCollider:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {i}}}\n  m_Material: {{fileID: 0}}\n  m_IsTrigger: 0\n  m_Enabled: 1\n  serializedVersion: 5\n  m_Convex: 1\n  m_CookingOptions: 30\n  m_Mesh: {{fileID: 4300000, guid: {collision_guid}, type: 2}}\n'
    for j, (guid, renderer) in enumerate(meshes):
        m = i + 10 + j * 10
        prefab += obj(m, f'Chassis_{j + 1}', [m + 1, m + 2, m + 3]) + transform(m + 1, m, i + 1)
        prefab += f'--- !u!33 &{m+2}\nMeshFilter:\n  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {m}}}\n  m_Mesh: {{fileID: 4300000, guid: {guid}, type: 2}}\n'
        prefab += f'--- !u!23 &{m+3}\n' + re.sub(r'm_GameObject: \{fileID: \d+\}', f'm_GameObject: {{fileID: {m}}}', renderer)
path = OUT / 'Keyboard_TKL_87_Fragments.prefab'
path.write_text(prefab)
guid = meta(path, 'PrefabImporter')
scene = ROOT / 'Assets/Scenes/Main.unity'
text = scene.read_text()
field = f'  _keyboardFragmentsPrefab: {{fileID: 100, guid: {guid}, type: 3}}'
if '_keyboardFragmentsPrefab:' in text: text = re.sub(r'  _keyboardFragmentsPrefab: .*', field, text)
else: text = text.replace('  _behindCount: 40\n', '  _behindCount: 40\n' + field + '\n')
if text != scene.read_text(): scene.write_text(text)
print('Created 12 irregular chassis fragments with slanted capped cuts and convex mesh colliders.')
