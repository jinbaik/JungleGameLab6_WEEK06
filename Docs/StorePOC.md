# Store POC

## 실행

1. Unity에서 `Assets/Scenes/Store.unity`를 열고 Play를 누른다.
2. 키보드를 사선으로 내려다보는 시점에서 1초 동안 대기한다.
3. 카메라가 0.8초 동안 모니터 앞으로 이동한 뒤 모니터 화면에 상점 UI가 나타난다.
4. 모니터의 `+100` 버튼으로 임시 재화를 받고 강화 항목의 `Buy` 버튼으로 레벨을 올린다.
5. Tab 또는 `Close [Tab]` 버튼으로 상점을 닫으면 키보드 시점으로 돌아온다. Tab으로 다시 진입할 수 있다.

재화가 부족하면 구매 버튼이 비활성화된다. 최대 레벨에서는 `MAX`가 표시된다.
상점 패널을 닫아도 잔액과 레벨은 유지된다. Play를 종료하고 다시 실행하면 초기화된다.
UI 문구는 POC용 영문이며, 강화 효과와 저장 기능은 포함하지 않는다.
상점은 모니터 화면의 World Space Canvas에 표시한다. 대기 및 카메라 이동 중에는 UI와 상점 입력을 차단한다.

## 씬 외형

`StoreSetup` 아래의 `Desk`, `Keyboard`, `Monitor`는 기본 큐브로 만든 외형 오브젝트이다.
별도의 기능 스크립트와 충돌 판정은 연결하지 않는다.
`Monitor / ShopCanvas`가 모니터 화면 앞에 배치되어 있으며, 재화 지급 버튼도 같은 Canvas에 포함된다.

## Inspector 설정

- `StoreSession / GameSession`: 시작 재화와 사용할 강화 에셋 목록.
- `StorePresentation / StorePresentation`: 카메라와 UI 참조, 첫 진입 대기 시간, 이동 시간과 두 시점의 시야각.
- `StorePresentation / KeyboardView`, `MonitorView`: 키보드 및 모니터 시점의 위치와 회전.
- `StoreSetup / Monitor / ShopCanvas / Page / StoreDemo`: 클릭당 임시 보상 금액.
- `Assets/Data/Upgrades`: 강화 ID, 이름, 설명과 단계별 가격.
- `Assets/Prefabs/Shop/ShopItem.prefab`: 개별 강화 항목 UI.
- `Assets/Prefabs/Shop/ShopCanvas.prefab`: 상점 패널 및 보유 재화 UI.

가격 목록의 첫 항목은 레벨 0에서 1로 구매하는 비용이다.
가격 목록의 길이가 최대 레벨이며, 각 강화의 ID는 세션에서 중복되지 않아야 한다.
현재 프리팹의 목록 영역은 세 가지 강화 표시를 기준으로 구성되어 있다.

## 구조와 게임플레이 연결

- `Wallet`: 정수 재화 보관과 지급. 구매 지출은 `UpgradeService`에서 처리한다.
- `UpgradeDefinition`: 강화 설정 에셋. 플레이 중 레벨은 에셋에 기록하지 않는다.
- `UpgradeService`: 구매 조건 확인과 현재 레벨 관리. 실패하면 잔액과 레벨을 유지한다.
- `GameSession`: 세션의 지갑과 강화 서비스를 `Awake`에서 생성한다.
- `ShopView`, `ShopItemView`: 구매 요청 전달과 상태 변경에 따른 UI 갱신.
- `StoreDemo`: 파괴 보상을 대신하는 임시 재화 지급 버튼.
- `StorePresentation`: 첫 자동 진입과 상점 상태 변경에 따른 카메라 이동 및 모니터 UI 표시.

실제 파괴 보상은 공유 중인 `GameSession.Wallet.Add(amount)`로 지급한다.
실제 강화 효과는 `GameSession.Upgrades.GetLevel(id)`로 레벨을 조회하거나
`LevelChanged` 이벤트를 구독하여 적용한다.

메인 씬에서 사용할 때는 `GameSession`과 `ShopCanvas` 프리팹을 배치하고,
`ShopView`의 `Game Session` 참조에 같은 세션을 연결한다.
Canvas의 상위 오브젝트는 활성 상태로 유지하고 `ShopView.SetOpen(isOpen)`으로 패널을 전환한다.
상점 표시 상태는 `ShopView.IsOpen`으로 조회할 수 있다.
기존 EventSystem에 Input System UI 모듈이 구성되어 있으면 그것을 사용한다.
모니터 연출을 재사용하려면 Canvas를 World Space로 배치하고 `StorePresentation`의 참조와 두 시점을 연결한다.
`ShopView.OpenStateChanged`는 패널 표시 상태의 변경을 알리고, `StorePresentation`은 이동이 끝나면 UI를 표시한다.
`ShopView.SetInputEnabled(isEnabled)`로 카메라 이동 중 상점 전환 입력을 제한한다.
첫 자동 진입은 Store POC 확인용이며, 실제 키보드 파괴 입력 제한은 메인 씬 통합 시 연결한다.
