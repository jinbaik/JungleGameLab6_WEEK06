"""Keyboard_Enhanced의 기존 보강판 위에 TKL 구역형 상단 프레임을 만든다."""
import re
import sys

import FitEnhancedKeyboardPCB as fit

OPENINGS = {
    'main': (-.12, 15.27, -.18, 4.96),
    'escape': (-.12, 1.12, 5.12, 6.20),
    'function_1_4': (1.90, 6.10, 5.12, 6.20),
    'function_5_8': (6.40, 10.60, 5.12, 6.20),
    'function_9_12': (10.90, 15.10, 5.12, 6.20),
    'utility': (15.34, 18.50, 5.12, 6.20),
    'editing': (15.38, 18.50, 2.85, 4.96),
    'arrows_bottom': (15.38, 18.50, -.18, .92),
    'arrow_up': (16.38, 17.42, .92, 1.92),
}


def rectangle(bounds):
    """네 경계값을 받아 프레임 개구부의 XZ 사각 윤곽을 반환한다."""
    left, right, bottom, top = bounds
    return [(left - 9.25, bottom - 2.8), (right - 9.25, bottom - 2.8),
            (right - 9.25, top - 2.8), (left - 9.25, top - 2.8)]


def main():
    """기존 하우징 윤곽과 87키 배치를 읽어 전용 상단 메시와 프리팹 자식을 저장한다."""
    source = fit.PREFAB.read_bytes().decode('utf-8')
    blocks = fit.assembly.blocks(source)
    mesh_only = '--mesh-only' in sys.argv
    if 'm_Name: CNC_UpperFrame' in source and not mesh_only:
        raise ValueError('상단 프레임이 이미 존재합니다.')
    lower, _ = fit.decode(fit.ROOT / 'Assets/Resource/Meshes/LowerCase_TKL87.asset')
    outline = [(v[0], v[2]) for v in lower[300:400]]
    holes = [rectangle(bounds) for bounds in OPENINGS.values()]
    frame = fit.native.Geometry()
    fit.surface(frame, outline, holes, .062, True)
    fit.surface(frame, outline, holes, 0, False)
    fit.walls(frame, outline, 0, .062)
    for hole in holes[:-2]:
        fit.walls(frame, hole, 0, .062, True)
    arrows = [(15.38, -.18), (18.50, -.18), (18.50, .92),
              (17.42, .92), (17.42, 1.92), (16.38, 1.92),
              (16.38, .92), (15.38, .92)]
    fit.walls(frame, [(x - 9.25, z - 2.8) for x, z in arrows], 0, .062, True)
    mesh_guid = fit.save(frame, 'UpperFrame_Enhanced_TKL87')
    if mesh_only:
        print('Updated upper frame mesh:', mesh_guid, OPENINGS)
        return

    template = fit.assembly.blocks(fit.assembly.read(fit.PCB.parent / 'PCB_TKL_87.prefab'))
    new_ids = ['8852000000000000001', '8852000000000000002',
               '8852000000000000003', '8852000000000000004']
    if any(key in blocks for key in new_ids):
        raise ValueError('상단 프레임 ID가 기존 오브젝트와 중복됩니다.')
    chunk = ''.join(template[key] for key in
                    ('3875010945220954598', '3617002600534611429',
                     '1670135297115357774', '1482783399198548079'))
    for old, new in zip(('3875010945220954598', '3617002600534611429',
                         '1670135297115357774', '1482783399198548079'), new_ids):
        chunk = chunk.replace(old, new)
    chunk = chunk.replace('m_Name: PCB_Board', 'm_Name: CNC_UpperFrame')
    chunk = chunk.replace('m_LocalPosition: {x: 0, y: 0, z: 0}',
                          'm_LocalPosition: {x: 9.25, y: 0.918, z: 2.8}')
    chunk = chunk.replace('m_Father: {fileID: 1474237672040320188}',
                          'm_Father: {fileID: 6484209102016570918}')
    chunk = chunk.replace('d6e5ed3659aa97d43adc89a3098d326b', mesh_guid)
    chunk = chunk.replace('24d710d218aa3184a83f33c24be9e285',
                          'eb9bef93fd5c48d4da2e0d9f41939e65')
    root = blocks['6484209102016570918']
    source = source.replace(root, root.replace('  m_Father:',
                            f'  - {{fileID: {new_ids[1]}}}\r\n  m_Father:'))
    fit.PREFAB.write_bytes((source + chunk.replace('\n', '\r\n')).encode('utf-8'))
    print('Saved upper frame:', mesh_guid, OPENINGS)


if __name__ == '__main__':
    main()
