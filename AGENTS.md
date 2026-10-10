# AGENTS.md

## 0. Git

- 커밋 메세지 제안만 가능
- git cli 수행 금지

## 1. Comment

- Unity `MonoBehaviour`의 Lifecycle 및 Callback 메서드를 제외한 모든 메서드의 상단에 XML `summary` 주석을 작성한다.
- `summary`에는 다음 내용을 포함한다.
  - 메서드가 수행하는 동작
  - 메서드가 사용하는 주요 입력값
  - 메서드가 반환하는 값 또는 변경하는 주요 상태
- 내부 주석은 한국어로 구성된 짧은 평문 문장으로 작성하며, 2~3문장을 초과하지 않는다.
- 내부 주석에는 이모지와 Markdown 문법을 사용하지 않는다.

```csharp
/// <summary>
/// 이동 입력을 받아 현재 속도를 목표 속도로 변경한다.
/// moveInput을 사용하며, 변경된 속도를 _velocity에 저장한다.
/// </summary>
private void ProcessMovement(Vector2 moveInput)
{
    Vector3 direction = new Vector3(moveInput.x, 0f, moveInput.y);

    // 지면 이동에서는 입력 방향을 즉시 속도에 반영하지 않고 가속도로 처리한다.
    _velocity = Vector3.MoveTowards(
        _velocity,
        direction * _moveSpeed,
        _moveAcceleration * Time.fixedDeltaTime);
}
```

---

## 2. Field

### Inspector 설정값

- Inspector에 노출해야 하는 값만 `[SerializeField]`를 사용한다.
- `[SerializeField] public`은 사용하지 않는다.
- `[SerializeField]`가 필요하지 않은 필드는 `private`으로 선언한다.

### 외부 공개 상태

- 클래스 외부에서 값을 참조해야할 경우에만 `public` 프로퍼티를 사용한다.

---

## 3. Field 순서

### 역할 별 1차 분류

필드는 역할에 따라 [Header("")] attribute를 사용하여 그룹화한다.

그룹은 다음 순서로 배치한다.

1. `const`
2. `static`
3. `[Header("")]`
4. ...

### 접근 제한자 별 2차 분류

각 [Header("")] 그룹 내부의 필드는 다음 순서로 배치한다.

1. `[SerializeField] private`
2. `private/protected`
3. `public`

---

## 4. Access Modifier

### Field

- 모든 필드는 접근 제한자를 명시한다.

### Method

- 모든 메서드는 접근 제한자를 명시한다.
- 단, Unity Lifecycle, Callback 메서드는 접근 제한자를 생략한다.
- 단, Unity Lifecycle, Callback 메서드에 virtual, override, abstract 등 Method Modifier을 사용하는 경우에는 접근 제한자를 명시한다.

---

## 5. Using

`using`은 다음 순서로 작성한다.

1. `System`
2. `UnityEngine`
3. Unity 패키지
4. 외부 패키지
5. 프로젝트 namespace

각 그룹 사이에는 한 줄을 넣는다.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using Cinemachine;

using Game.Player;
using Game.System;
```

- 사용하지 않는 `using`은 제거한다.
- 같은 그룹 안에서는 알파벳 순서로 정렬한다.

---

## 6. Naming

- private 필드: `_camelCase`
- public 프로퍼티: `PascalCase`
- 메서드: `PascalCase`
- 지역 변수: `camelCase`
- 상수: `SCREAMING_SNAKE_CASE`
- enum 타입: `PascalCase`
- enum 값: `PascalCase`

---

## 7. Scope

- 요청받은 기능과 직접 관련된 코드만 수정한다.
- 요청받지 않은 클래스, 메서드, 변수의 이름을 변경하지 않는다.
- 요청받지 않은 리팩터링을 하지 않는다.
- 기존 동작을 변경하는 코드는 요청된 경우에만 수정한다.
- 테스트 코드 작성 금지

## 8. Code Structure

- Unity Lifecycle 순서와 Inspector 설정을 고려하여 불필요한 `null` check를 추가하지 않는다.

## 9. Coroutine

- Coroutine 내부에서는 Time.deltaTime만을 사용해야 한다.

## 10. Local Instructions

- 프로젝트 루트에 LOCAL.md가 존재하면 개인 작업 지침으로 추가로 읽고 적용한다.

## 11. Unity-Cli 사용 지침

- 이벤트 연결 방식은 C# Event Action 바인딩으로만 한다.
- UnityEventTools.AddIntPersistentListener() 함수처럼 컴퓨터만 알고 개발자가 추적하기 어려운 이벤트 바인딩 방식을 피해야 한다.
