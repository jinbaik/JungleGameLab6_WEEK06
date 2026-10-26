"""현재 Enhanced 프리팹의 PCB 체결부와 독립 나사 여섯 개를 모델링한다."""
import re

import FitEnhancedKeyboardPCB as fit

MOUNTS = [(x, z) for z in (6.10, -.50) for x in (1.25, 9.25, 17.25)]


def main():
    """현재 프리팹과 기존 메시를 읽어 대응 체결부 및 공용 나사 메시를 저장한다."""
    source = fit.PREFAB.read_bytes().decode('utf-8')
    blocks = fit.assembly.blocks(source)
    if 'PCB_Screw_Upper_Left' in source:
        raise ValueError('나사가 이미 존재하므로 기존 배치를 덮어쓰지 않습니다.')
    positions = []
    for block in blocks.values():
        if block.startswith('--- !u!1 ') and re.search(r'm_Name: Switch_\d+\r?\n', block):
            transform = blocks[re.search(r'component: \{fileID: (\d+)\}', block)[1]]
            positions.append(fit.assembly.vector(transform, 'm_LocalPosition'))

    # 외부 형상과 기존 스위치 구멍은 유지하며 체결부만 함께 옮긴다.
    vertices, faces = fit.decode(fit.ROOT / 'Assets/Resource/Meshes/LowerCase_TKL87.asset')
    housing = fit.native.Geometry()
    housing.vertices = vertices
    housing.faces = [face for face in faces if not all(abs(vertices[i][1] - .28) < 1e-6 for i in face)]
    rim = [(v[0], v[2]) for v in vertices[300:400]]
    inner = [(x * (9.48 / 9.545), z * (3.57 / 3.62)) for x, z in rim]
    fit.surface(housing, rim, [inner], .28, True)
    fit.walls(housing, inner, -.16, .28, True)
    fit.surface(housing, inner, [], -.16, True)
    for x, z in MOUNTS:
        outer = fit.circle(x - 9.25, z - 2.8, .15)
        pilot = fit.circle(x - 9.25, z - 2.8, .045)
        fit.walls(housing, outer, -.16, .09)
        fit.walls(housing, pilot, -.12, .09, True)
        fit.surface(housing, outer, [pilot], .09, True)
        fit.surface(housing, pilot, [], -.12, True)
    fit.save(housing, 'LowerCase_Enhanced_Cavity')

    original, _ = fit.decode(fit.PCB / 'PCB_Board.asset')
    outline = [(v[0], v[2]) for v in original[:8]]
    holes = [fit.circle(x, z, .065) for x, z in MOUNTS]
    for x, _, z in positions:
        holes += [fit.circle(x, z, .108), fit.circle(x - .269, z, .042), fit.circle(x + .269, z, .042), fit.circle(x - .20, z + .15, .033), fit.circle(x + .16, z + .25, .033)]
    board = fit.native.Geometry()
    fit.surface(board, outline, holes, .715, True)
    fit.surface(board, outline, holes, .665, False)
    fit.walls(board, outline, .665, .715)
    for hole in holes:
        fit.walls(board, hole, .665, .715, True)
    fit.save(board, 'PCB_Enhanced_Board')

    # 원점은 머리 아랫면 중심이며 모든 나사의 회전축은 로컬 Y이다.
    screw = fit.native.Geometry()
    shaft = fit.circle(0, 0, .04, 24)
    head = fit.circle(0, 0, .10, 24)
    crown = fit.circle(0, 0, .09, 24)
    socket = fit.circle(0, 0, .045, 6)
    fit.walls(screw, shaft, -.24, 0)
    fit.surface(screw, shaft, [], -.24, False)
    fit.surface(screw, head, [shaft], 0, False)
    fit.walls(screw, head, 0, .08)
    for i in range(24):
        j = (i + 1) % 24
        screw.face([(head[i][0], .08, head[i][1]), (crown[i][0], .10, crown[i][1]), (crown[j][0], .10, crown[j][1]), (head[j][0], .08, head[j][1])])
    fit.surface(screw, crown, [socket], .10, True)
    fit.walls(screw, socket, .065, .10, True)
    fit.surface(screw, socket, [], .065, True)
    screw_guid = fit.save(screw, 'PCB_Screw_HexSocket')
    material_guid = 'd76f514a9a2ecec4d9346ff56354ca6e'

    pcb = fit.assembly.blocks(fit.assembly.read(fit.PCB.parent / 'PCB_TKL_87.prefab'))
    go_template = pcb['3875010945220954598']
    transform_template = pcb['3617002600534611429']
    filter_template = pcb['1670135297115357774']
    renderer_template = pcb['1482783399198548079']
    additions = []
    children = ''
    for index, (x, z) in enumerate(MOUNTS):
        ids = [str(8851000000000000000 + index * 10 + i) for i in range(1, 5)]
        if any(key in blocks for key in ids):
            raise ValueError('새 오브젝트 ID가 기존 프리팹과 중복됩니다.')
        name = 'PCB_Screw_' + ('Upper_' if index < 3 else 'Lower_') + ('Left', 'Center', 'Right')[index % 3]
        chunk = go_template + transform_template + filter_template + renderer_template
        for old, new in zip(('3875010945220954598', '3617002600534611429', '1670135297115357774', '1482783399198548079'), ids):
            chunk = chunk.replace(old, new)
        chunk = chunk.replace('m_Name: PCB_Board', 'm_Name: ' + name)
        chunk = chunk.replace('m_LocalPosition: {x: 0, y: 0, z: 0}', f'm_LocalPosition: {{x: {x}, y: 0.54, z: {z}}}')
        chunk = chunk.replace('m_Father: {fileID: 1474237672040320188}', 'm_Father: {fileID: 6484209102016570918}')
        chunk = chunk.replace('d6e5ed3659aa97d43adc89a3098d326b', screw_guid).replace('24d710d218aa3184a83f33c24be9e285', material_guid)
        additions.append(chunk)
        children += f'  - {{fileID: {ids[1]}}}\r\n'
    root = blocks['6484209102016570918']
    source = source.replace(root, root.replace('  m_Father:', children + '  m_Father:'))
    source += ''.join(additions).replace('\n', '\r\n')
    fit.PREFAB.write_bytes(source.encode('utf-8'))
    print('Saved six screws; shared mesh:', screw_guid, '; mounts:', MOUNTS)


if __name__ == '__main__':
    main()
