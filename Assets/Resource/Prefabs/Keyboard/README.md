# Keyboard 모델

`Assets/Scenes/Keyboard.unity`를 열면 104키 ANSI 키보드와 촬영용 카메라, 조명이 표시됩니다.
기존 Main 씬과 프로젝트의 렌더 파이프라인 설정은 수정하지 않았습니다.

## 프리팹

- `Prefabs/Keyboard_ANSI_104.prefab`: 키캡, 스위치, 스태빌라이저, 본체가 조립된 키보드입니다.
- `Prefabs/KeyboardBody.prefab`: 하우징, 황동 보강판, 나사, 상태 표시등, USB-C 포트, 하부 받침대입니다.
- `Prefabs/Keycaps`: 1u, 1.25u, 1.5u, 1.75u, 2u, 2.25u, 2.75u, 6.25u와 세로형 2u의 총 9종입니다.

같은 크기의 키는 같은 키캡 프리팹을 재사용합니다. 각인과 색상은 조립 프리팹의 인스턴스에 지정되어 있으므로 알파벳마다 별도 프리팹을 만들지 않습니다.
키캡은 아래가 넓고 위가 좁은 사다리꼴 형태이며 둥근 모서리, 베벨, 오목한 윗면, 빈 하부, MX 십자 소켓을 포함합니다.
스페이스바와 긴 키에는 하부 보강 리브와 스태빌라이저 체결 소켓이 있습니다.
색상은 크림, 짙은 청록, 주황이며 PBT 재질에는 미세한 표면 노멀 질감을 사용합니다.

## 크기와 편집

키 간격 1u는 19.05mm이며 전체 키보드 너비는 약 45.2cm입니다.
개별 키캡과 본체 프리팹도 같은 실측 스케일로 사용할 수 있습니다.
조립 키보드 내부에서는 부모의 실측 스케일을 적용하므로 키캡 인스턴스의 로컬 스케일은 1입니다.
프리팹 모드에서 키캡의 MeshRenderer 재질 또는 Printed_Legend의 TextMesh 글자를 편집할 수 있습니다.
모든 메시와 재질은 저장된 에셋이며 플레이 시 생성 코드를 실행할 필요가 없습니다.
키 입력이나 누름 애니메이션 기능은 포함하지 않습니다.

## 미리보기와 재생성

`Preview/Keyboard.png`, `Preview/Keycap_Detail.png`, `Preview/Keycap_Underside.png`에서 완성 모습을 확인할 수 있습니다.
생성기는 `Editor/KeyboardModelBuilder.cs`이며 Unity 메뉴 `Tools > Keyboard > Build Detailed Keyboard Scene`에서 재실행할 수 있습니다.
재생성은 모델 에셋과 Keyboard 씬을 덮어쓰므로 직접 편집한 모델은 별도 이름으로 복제해 보관하세요.
재실행 전 Keyboard 씬을 닫고 저장된 다른 씬에서 실행하세요.
