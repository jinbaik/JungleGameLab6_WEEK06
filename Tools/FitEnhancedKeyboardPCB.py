"""Keyboard_Enhanced 전용 하우징 내부와 기존 PCB의 장착 메시를 맞춘다."""
import json
import math
import re
import struct
from pathlib import Path

import GenerateAssemblyModels as assembly
import GenerateSwitchPuller as native

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Assets/Resource/Meshes/KeyboardAssembly/Enhanced'
PREFAB = ROOT / 'Assets/Resource/Prefabs/SeperateKeyboard/Keyboard_Enhanced.prefab'
PCB = ROOT / 'Assets/Resource/Prefabs/SeperateKeyboard/PCB'
MOUNTS = [(x, z) for z in (-.50, 6.14) for x in (.15, 9.25, 18.35)]


def decode(path):
    """메시 경로에서 정점의 위치와 법선, 삼각형 인덱스를 읽어 반환한다."""
    text = assembly.read(path)
    count = int(re.search(r'm_VertexCount: (\d+)', text)[1])
    raw = bytes.fromhex(re.search(r'_typelessdata: (\w+)', text)[1])
    stride = len(raw) // count
    vertices = [struct.unpack_from('<6f', raw, i * stride) for i in range(count)]
    raw_indices = bytes.fromhex(re.search(r'm_IndexBuffer: (\w+)', text)[1])
    return vertices, list(struct.iter_unpack('<3H', raw_indices))


def circle(x, z, radius, count=12):
    """중심과 반경을 받아 XZ 평면의 반시계 방향 다각형을 반환한다."""
    return [(x + radius * math.cos(i * math.tau / count), z + radius * math.sin(i * math.tau / count)) for i in range(count)]


def interval(polygon, z):
    """볼록 다각형과 Z 좌표를 받아 수평 단면의 좌우 X 좌표를 반환한다."""
    intersections = []
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        if abs(a[1] - b[1]) > 1e-10 and min(a[1], b[1]) - 1e-8 <= z <= max(a[1], b[1]) + 1e-8:
            intersections.append(a[0] + (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]))
    return min(intersections), max(intersections)


def surface(mesh, outline, holes, y, upward):
    """외곽과 구멍 윤곽 및 높이를 받아 구멍을 제외한 평면을 추가한다."""
    levels = sorted(set(round(p[1], 8) for poly in [outline, *holes] for p in poly))
    for low, high in zip(levels, levels[1:]):
        if high - low < 1e-7:
            continue
        mid = (low + high) / 2
        active = sorted((h for h in holes if min(p[1] for p in h) < mid < max(p[1] for p in h)), key=lambda h: interval(h, mid)[0])
        left_low, right_low = interval(outline, low)
        left_high, right_high = interval(outline, high)
        for hole in active + [None]:
            end_low, next_low = interval(hole, low) if hole else (right_low, right_low)
            end_high, next_high = interval(hole, high) if hole else (right_high, right_high)
            points = [(left_low, y, low), (left_high, y, high), (end_high, y, high), (end_low, y, low)]
            clean = []
            for point in points:
                if not clean or math.dist(clean[-1], point) > 1e-7:
                    clean.append(point)
            if len(clean) > 2 and math.dist(clean[0], clean[-1]) < 1e-7:
                clean.pop()
            if len(clean) >= 3:
                mesh.face(clean if upward else list(reversed(clean)))
            left_low, left_high = next_low, next_high


def walls(mesh, polygon, bottom, top, inward=False):
    """윤곽과 높이를 받아 외벽 또는 구멍 안쪽 벽을 추가한다."""
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        points = [(a[0], bottom, a[1]), (a[0], top, a[1]), (b[0], top, b[1]), (b[0], bottom, b[1])]
        mesh.face(list(reversed(points)) if inward else points)


def save(mesh, name):
    """메시와 이름을 받아 Enhanced 전용 에셋을 저장하고 GUID를 반환한다."""
    if len(mesh.vertices) > 65535:
        raise ValueError('16비트 메시 정점 범위를 초과했습니다.')
    native.DEST = DEST
    return mesh.save(name)


def modification(file_id, guid, property_name, value='', reference='{fileID: 0}'):
    """대상 ID와 속성을 받아 추적 가능한 프리팹 오버라이드 문자열을 반환한다."""
    return f'    - target: {{fileID: {file_id}, guid: {guid}, type: 3}}\n      propertyPath: {property_name}\n      value: {value}\n      objectReference: {reference}\n'


def main():
    """기존 키 좌표와 PCB를 읽어 전용 메시와 프리팹 배치 및 치수 보고서를 저장한다."""
    DEST.mkdir(exist_ok=True)
    native.meta(DEST, 'DefaultImporter')
    source = assembly.read(ROOT / 'output/KeyboardEnhanced/Keyboard_Enhanced.before.prefab')
    data = assembly.blocks(source)
    positions = []
    for block in data.values():
        if block.startswith('--- !u!1 ') and re.search(r'm_Name: Switch_\d+\n', block):
            transform = data[re.search(r'component: \{fileID: (\d+)\}', block)[1]]
            positions.append(assembly.vector(transform, 'm_LocalPosition'))
    positions.sort(key=lambda p: (-p[2], p[0]))
    if len(positions) != 87 or any(abs(y - .81) > 1e-6 for _, y, _ in positions):
        raise ValueError('기존 스위치 87개의 높이를 확인해야 합니다.')

    # 기존 외부 정점과 법선은 보존하고 윗면 안쪽에만 공동을 낸다.
    vertices, faces = decode(ROOT / 'Assets/Resource/Meshes/LowerCase_TKL87.asset')
    housing = native.Geometry()
    housing.vertices = vertices
    housing.faces = [f for f in faces if not all(abs(vertices[i][1] - .28) < 1e-6 for i in f)]
    top_ring = [(v[0], v[2]) for v in vertices[300:400]]
    inner = [(x * (9.48 / 9.545), z * (3.57 / 3.62)) for x, z in top_ring]
    surface(housing, top_ring, [inner], .28, True)
    walls(housing, inner, -.16, .28, True)
    surface(housing, inner, [], -.16, True)
    for x, z in MOUNTS:
        outer = circle(x - 9.25, z - 2.8, .13)
        pilot = circle(x - 9.25, z - 2.8, .045)
        walls(housing, outer, -.16, .09)
        walls(housing, pilot, -.12, .09, True)
        surface(housing, outer, [pilot], .09, True)
        surface(housing, pilot, [], -.12, True)
    housing_guid = save(housing, 'LowerCase_Enhanced_Cavity')

    board_vertices, _ = decode(PCB / 'PCB_Board.asset')
    outline = [(v[0], v[2]) for v in board_vertices[:8]]
    holes = [circle(x, z, .065) for x, z in MOUNTS]
    for x, _, z in positions:
        holes += [circle(x, z, .108), circle(x - .269, z, .042), circle(x + .269, z, .042), circle(x - .20, z + .15, .033), circle(x + .16, z + .25, .033)]
    board = native.Geometry()
    surface(board, outline, holes, .715, True)
    surface(board, outline, holes, .665, False)
    walls(board, outline, .665, .715)
    for hole in holes:
        walls(board, hole, .665, .715, True)
    board_guid = save(board, 'PCB_Enhanced_Board')

    # 기존 패드의 링 형태를 유지하고 각 스위치의 실제 접점 핀으로 옮긴다.
    pad_vertices, pad_faces = decode(PCB / 'PCB_SolderPads.asset')
    pads = native.Geometry()
    pads.faces = pad_faces
    pad_errors = []
    for offset in range(0, len(pad_vertices), 24):
        group = pad_vertices[offset:offset + 24]
        cx = sum(v[0] for v in group) / 24
        cz = sum(v[2] for v in group) / 24
        x, _, z = min(positions, key=lambda p: (p[0] - cx) ** 2 + (p[2] - cz) ** 2)
        dx, dz = (-.20, .15) if cx < x else (.16, .25)
        pad_errors.append(math.hypot(abs(cx - x) - .16, cz - z + .12))
        pads.vertices.extend((v[0] + x + dx - cx, v[1], v[2] + z + dz - cz, *v[3:]) for v in group)
    if max(pad_errors) > 1e-4:
        raise ValueError('원본 PCB 패드와 키 배열 대응이 예상과 다릅니다.')
    pad_guid = save(pads, 'PCB_Enhanced_SolderPads')
    traces = native.Geometry()
    for x, _, z in positions:
        for dx, dz in ((-.20, .15), (.16, .25)):
            assembly.box(traces, (x + dx, .717, z + (dz + .045 + .36) / 2), (.012, .003, .36 - dz - .045))
        assembly.box(traces, (x - .02, .717, z + .36), (.36, .003, .012))
    trace_guid = save(traces, 'PCB_Enhanced_Traces')

    body_guid = '029f15bca3a509045b23cad09a0d2be5'
    body = data['712209937148672475']
    patch = modification('4433637591095933648', body_guid, 'm_Mesh', reference=f'{{fileID: 4300000, guid: {housing_guid}, type: 2}}')
    source = source.replace(body, body.replace('    m_RemovedComponents:', patch + '    m_RemovedComponents:'))
    pcb_guid = '05b4d383e1a0ee04191318579655fd76'
    pcb_data = assembly.blocks(assembly.read(PCB.parent / 'PCB_TKL_87.prefab'))
    mods = ''
    for axis in 'xyz':
        mods += modification('1474237672040320188', pcb_guid, 'm_LocalScale.' + axis, '1')
        mods += modification('1474237672040320188', pcb_guid, 'm_LocalPosition.' + axis, '-0.175' if axis == 'y' else '0')
    replacements = {'d6e5ed3659aa97d43adc89a3098d326b': board_guid, '95c331c47e775a9448a9281a9956d187': pad_guid, '25cd65770e6867941bfd393756b421e7': trace_guid}
    for file_id, block in pcb_data.items():
        if block.startswith('--- !u!33 '):
            old_guid = re.search(r'm_Mesh: .*guid: (\w+)', block)[1]
            if old_guid in replacements:
                mods += modification(file_id, pcb_guid, 'm_Mesh', reference=f'{{fileID: 4300000, guid: {replacements[old_guid]}, type: 2}}')
    instance_id, transform_id = 8840000000000000001, 8840000000000000002
    root = data['6484209102016570918']
    source = source.replace(root, root.replace('  m_Father:', f'  - {{fileID: {transform_id}}}\n  m_Father:'))
    source += f'''--- !u!1001 &{instance_id}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: 6484209102016570918}}
    m_Modifications:
{mods}    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {pcb_guid}, type: 3}}
--- !u!4 &{transform_id} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: 1474237672040320188, guid: {pcb_guid}, type: 3}}
  m_PrefabInstance: {{fileID: {instance_id}}}
  m_PrefabAsset: {{fileID: 0}}
'''
    original_raw = (ROOT / 'output/KeyboardEnhanced/Keyboard_Enhanced.before.prefab').read_bytes().decode('utf-8')
    original_blocks = assembly.blocks(original_raw)
    updated_blocks = assembly.blocks(source)
    for key in ('6484209102016570918', '712209937148672475'):
        old_block = original_blocks[key]
        newline = '\r\n' if '\r\n' in old_block else '\n'
        original_raw = original_raw.replace(old_block, updated_blocks[key].replace('\n', newline))
    for key in (str(instance_id), str(transform_id)):
        original_raw += updated_blocks[key].replace('\n', '\r\n')
    PREFAB.write_bytes(original_raw.encode('utf-8'))
    report = {'switches': len(positions), 'source_pad_layout_error_max': max(pad_errors), 'pcb_translation': [0, -.175, 0], 'pcb_scale': [1, 1, 1], 'board_y': [.49, .54], 'switch_body_bottom_y': .548, 'pin_bottom_y': .392, 'cavity_floor_y': .24, 'mounts_xz': MOUNTS, 'post_top_y': .49, 'pilot_radius': .045, 'pcb_mount_hole_radius': .065, 'reserved_head_radius': .13, 'mesh_vertices': {name: len(mesh.vertices) for name, mesh in [('housing', housing), ('board', board), ('pads', pads), ('traces', traces)]}}
    (ROOT / 'output/KeyboardEnhanced/dimensions.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()

