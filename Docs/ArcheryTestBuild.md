# 궁술 테스트 빌드

발사 궁술 5종 × 회수 궁술 6종(30조합)을 직접 시험하는 테스트 빌드입니다.
기획 근거는 작업 명세와 `궁수 로그라이크 (1).xlsx`의 `궁술`, `화살`, `수치` 시트입니다.
명세에 없어서 테스트용으로 정한 값과 규칙은 **[잠정]**으로 표시했습니다. 확정된 기획이 아닙니다.

## 실행

| 항목 | 위치 |
| --- | --- |
| macOS 빌드 | `Builds/ArcheryTest/macOS/ArcheryTest.app` (git 제외 폴더) |
| 테스트 씬 | `Assets/pjs/Scenes/ArcheryTest.unity` (자동 생성) |
| 수치 설정 | `Assets/pjs/Resources/ArcheryConfig.asset` |
| 코드 | `Assets/pjs/Scripts/Archery/`, `Assets/pjs/Editor/ArcheryTestBuilder.cs` |

- 에디터: `Assets/pjs/Scenes/ArcheryTest.unity`를 열고 Play를 누릅니다. `Tools > Archery > Build macOS Test Build`로 빌드합니다. 빌드는 이 씬을 그대로 쓰고, 씬이 없을 때만 새로 만듭니다. `Create Test Scene` 메뉴는 씬을 새로 만들어 덮어씁니다.
- 명령줄 빌드(에디터를 닫은 상태):
  `Unity -batchmode -quit -projectPath <프로젝트> -executeMethod Procedural2D.Editor.ArcheryTestBuilder.BuildMacFromCommandLine`
- 자동 검증: 로비의 **자동 검증 실행** 버튼을 누르거나,
  `Builds/ArcheryTest/macOS/ArcheryTest.app/Contents/MacOS/laurel -screen-fullscreen 0 -archerySelfTest -archerySelfTestOut report.txt`로 실행합니다.
  `-archeryScreenshots <폴더>`를 붙이면 화면 캡처도 저장합니다. 보고서는 앱 persistentDataPath의 `archery_selftest_report.txt`에도 저장됩니다.
  최근 실행 결과는 `Docs/ArcheryTestBuild_SelfTestReport.txt`에 있습니다(100/100 통과).
- 프로젝트의 Build Settings 씬 목록은 바꾸지 않았습니다. 빌드할 때 테스트 씬만 지정합니다.

## 로비

- 발사 궁술 목록: 아폴론 · 헤르메스 · 아테나 · 오디세우스 · 아레스
- 회수 궁술 목록: **기본 — 줍기** · 오르페우스 · 아레스(뽑기) · 데메테르 · 하데스 · 제우스
  (표시 순서는 `ArcheryTestUI.LobbyRetrievalOrder`. 버튼 이름과 적용 궁술은 같은 값에서 나옵니다.)
- 초기 선택: 아폴론 + 기본 줍기

## 조작

| 입력 | 동작 |
| --- | --- |
| A / D, Space(2단), Shift | 기존 이동, 점프, 대시 (기존 코드 그대로 사용) |
| 좌클릭 | 발사. 누르고 있으면 간격마다 연속 발사합니다. 오디세우스는 누르는 동안 충전하고 놓으면 발사합니다. 아레스는 찌릅니다. |
| R | 오르페우스: 누르고 있는 동안 부르기 / 데메테르: 낫 / 제우스: 뇌우 |
| F5 / Esc | 전투 초기화 / 로비 복귀 |
| F1 | 범위 표시 켜기·끄기 (줍기·뽑기 원, 찌르기 상자, 낫 부채꼴, 뇌우 원, 곡사 예상 궤적) |
| F2 | 번개 시험 허수아비 양옆 바닥에 화살 2발을 배치하는 테스트 보조 기능 |
| F3 | 적 체력과 피해 기록 초기화 |

## 구조

- `ArrowDefinition`: 화살 한 종의 데이터입니다. 피해, 활시위, 무게, 색, 적중 효과 목록을 가집니다. 덱빌딩 카드에 해당합니다.
- `ArcheryArrow`: 화살 한 발입니다. 상태는 `InQuiver`, `Flying`, `Falling`, `Grounded`(지형), `Stuck`(적), `Returning` 중 하나입니다.
- `ArrowQuiver`: 순서가 있는 화살통입니다. 앞쪽 화살을 먼저 사용합니다.
- `LaunchBehaviour` 5종, `RetrievalBehaviour` 6종: 서로 독립적으로 만들어지고 `ArcheryCombat`이 조합해서 실행합니다.
  같은 신 이름(아레스)이라도 `AresThrustLaunch`와 `AresPull`은 별개의 클래스입니다.
- `ArrowHitResolver`: 발사 적중, 찌르기 적중, 뽑기 재적중이 모두 지나가는 적중 파이프라인입니다.
  피해를 적용한 뒤 화살 데이터의 적중 효과(`hitEffects`)를 실행합니다. 새 화살 효과는 `ArrowHitEffectType`에 추가하면 세 경로에 모두 적용됩니다.
- 중복 방지
  - 화살통으로 들어가는 경로는 `ArcheryCombat.TryRecover` 하나뿐입니다. 이 함수는 `InQuiver`와 `Flying` 상태의 화살, 그리고 같은 프레임에 이미 회수된 화살을 거부합니다.
  - 화살통에서 나가는 경로는 `ArrowQuiver.TakeNext` 하나뿐입니다.
  - 한 번 비행하는 동안 적중 판정은 1회입니다(`MarkHitResolved`).
  - 매 프레임 화살통 수와 `InQuiver` 상태 수가 같은지, 중복이 없는지 검사하고 HUD에 표시합니다.
- 기존 코드 변경은 `Procedural2DAim`에 `legacyCombatEnabled` 스위치와 `PlayShotRecoil()`을 추가한 것뿐입니다.
  테스트 빌드에서는 조준과 자세 연출만 기존 코드를 쓰고, 기존 사격·회수·탄창 HUD는 끕니다. (기존 `PrototypeScene`은 삭제되었고, 기존 사격 코드는 스위치를 켜면 그대로 동작합니다.)

## 플레이어 점프 (테스트 씬 전용)

| 항목 | 변경 전 | 변경 후 | 설정 위치 |
| --- | --- | --- | --- |
| 1단 점프력 | 21 (최고 높이 약 7.49) | **18.78** (약 5.99, 20% 낮춤) | `ArcheryConfig.asset` → `playerFirstJumpForce` |
| 2단 점프력 | 17 | 17 (유지) | 프리팹 `ProceduralCharacter` → `secondJumpForce` |

- 높이는 점프력의 제곱에 비례합니다(`높이 = 점프력² ÷ (2 × 9.81 × 중력배율 3)`). 높이를 k배로 하려면 점프력을 `21 × √k`로 둡니다. 예: 0.9 → 19.92, 0.7 → 17.57.
- 테스트 씬에서 플레이어를 생성할 때만 덮어씁니다. 프리팹 값(21)은 바뀌지 않습니다. 0 이하로 두면 프리팹 값을 사용합니다.

## 공통 수치와 계산

| 항목 | 값 | 근거 |
| --- | --- | --- |
| 체력 / 공격력 | 100 / 1 (로비에서 체력 변경 가능) | 명세 |
| 일반 최종 피해 | 화살 피해 + 공격력 | 명세 |
| 활시위 배율 | 활시위(초) ÷ `referenceDrawSeconds`(1초). 기본 1.0, 가벼운 0.5, 청동 1.4 | [잠정] 정규화 기준 |
| 발사 간격 | 기본 간격 × 다음 화살 활시위 배율 ÷ 공격속도 배율 | 명세의 관계 + [잠정] 적용 방식 |
| 공격속도 배율 | 1 + 증가 합계 (헤르메스 5스택이면 1.5) | [잠정] 합산 방식 |

**[잠정] 간격 적용 방식**: 마지막 발사 이후 `dt × 공격속도 배율`을 누적합니다. 누적값이 `기본 간격 × 화살통 맨 앞 화살의 활시위 배율`에 도달하면 그 화살을 발사합니다.
명세의 "활시위 계산 후 발사"를 "쏠 화살의 활시위가 그 화살을 쏘기 전 대기시간을 정한다"로 해석했습니다.
버프가 중간에 바뀌어도 남은 간격에 바로 반영됩니다. 첫 발은 바로 나갑니다.

**동시 회수와 순차 회수**: 같은 프레임에 회수된 화살을 하나의 묶음으로 봅니다. 2발 이상이면 무작위로 섞어 화살통 뒤에 붙입니다(동시 회수).
따로따로 회수된 화살은 회수된 순서대로 뒤에 붙습니다(순차 회수).
뽑기, 낫, 뇌우, 같은 순간 출발한 부르기 화살은 동시 회수가 됩니다. 줍기와 시차가 있는 부름은 순차 회수가 됩니다.

## 궁술별 규칙과 잠정 해석

### 발사

- **아폴론**: 기본 간격 1초로 직사합니다.
- **헤르메스**: 기본 간격 1.25초입니다. 발사한 뒤 공격속도 +10% 스택을 얻습니다(5초, 최대 5스택).
  - [잠정] 스택을 얻으면 모든 스택의 지속시간을 5초로 갱신하고, 만료되면 모든 스택이 함께 사라집니다(`hermesRefreshMode`). 스택마다 따로 만료되는 방식도 설정에서 고를 수 있습니다.
  - [잠정] 스택은 발사한 뒤 붙으므로 다음 발사부터 빨라집니다.
- **아테나**: 기본 간격 0.6초입니다. 낙하 가속은 `무게 × 18`입니다([잠정] `athenaGravityPerWeight`). 가벼운 화살 9, 기본 화살 18, 청동 화살 27입니다. F1로 다음 화살의 예상 궤적을 볼 수 있습니다.
- **직사 공통 [잠정]**: 아테나가 아닌 화살은 중력 없이 날아갑니다. 32유닛을 날아가면 추진력을 잃고 떨어집니다(`straightMaxRange`). 속도는 26유닛/초입니다(`arrowSpeed`).
- **오디세우스**: 충전 입력과 피해 계산을 분리했습니다(`OdysseusLaunch.ChargeDamage`).
  - [잠정] 실제 충전 범위는 `1.2~1.5초 × 활시위 배율 ÷ 공격속도`입니다. 청동 화살은 1.68~2.1초입니다.
  - [잠정] 기준 충전시간은 `실제 충전 × 공격속도 ÷ 활시위 배율`이고 1.2~1.5초로 제한합니다. 배율은 `기준 충전시간 ÷ 1초`입니다.
    이 해석은 엑셀 예시 1.2초=7.2, 1.3초=7.8, 1.4초=8.4, 1.5초=9.0(기본 화살 6)과 정확히 일치합니다.
    "0.1초마다 10%"는 기준 1초에 대해 0.1초당 +10%로 읽었습니다.
  - [잠정] 최종 피해는 `화살 피해 × 배율 + 공격력`입니다(예: 1.3초 → 8.8). `odysseusMultiplierIncludesAttack`을 켜면 `(화살 피해 + 공격력) × 배율`로 계산합니다.
  - [잠정] 최소 충전 전에 버튼을 놓으면 발사를 취소하고 화살을 소모하지 않습니다. 최대 충전에 도달하면 더 이상 충전되지 않고 유지됩니다.
- **아레스(찌르기)**: 기본 간격 0.5초입니다.
  - [잠정] 판정은 플레이어 중심에서 마우스 방향으로 길이 2.2, 폭 0.9인 상자입니다. 상자 안에서 가장 가까운 적 1명만 찌릅니다.
  - 적중하면 화살 1발을 소모합니다. 그 화살은 적에게 박히고, 기본 줍기를 골랐다면 적 앞에 떨어집니다. 이후 처리는 회수 궁술 규칙을 따릅니다.
  - 빗나가면 화살을 소모하지 않습니다. [잠정] 빗나가도 간격은 소모합니다(`aresMissConsumesInterval`).

### 회수 (모두 자동 줍기를 포함합니다. [잠정] 반경 1.3)

- **화살 위치 [잠정]**: 바닥, 벽, 발판에 꽂히거나 떨어진 화살을 모두 "바닥에 떨어진 화살"(`Grounded`)로 봅니다.
- **오르페우스**: R을 누르고 있는 동안 비행 중이 아닌 모든 화살이 5초에 걸쳐 돌아옵니다.
  - 남은 거리는 `시작 거리 × (1-t)^3`입니다. 그래서 멀리 있는 화살은 빠르게, 가까운 화살은 느리게 오고, 무게와 상관없이 정확히 5초에 도착합니다. 지수는 `returnEaseExponent`로 조정합니다.
  - [잠정] R을 놓으면 돌아오던 화살이 그 자리에서 떨어집니다(`orpheusReleaseMode`). 계속 돌아오게 하는 방식도 고를 수 있습니다.
  - [잠정] R을 누르고 있는 동안 새로 떨어진 화살도 합류합니다.
- **아레스(뽑기)**: 박힌 지 0.5초가 지난 화살이 있는 적에게 다가가면(적 가장자리까지 1.6 이내, [잠정]) 그 화살들을 뽑습니다.
  - 뽑을 때 화살마다 `ArrowHitResolver`로 적중 판정을 한 번 더 실행합니다. 피해와 적중 효과(청동 넉백 등)가 모두 다시 발동합니다.
  - [잠정] 0.5초가 지나지 않은 화살은 남겨 둡니다. 재적중 피해에는 차지 배율을 빼고 `화살 피해 + 공격력`을 씁니다. 넉백 방향은 처음 맞은 방향입니다.
- **데메테르**: R을 누르면 마우스 방향으로 부채꼴(반경 3.6, 150°, [잠정]) 범위에 낫을 휘두릅니다.
  - 범위 안의 적에게 `공격력 × 2` 피해를 주고, 비행 중이 아닌 화살을 모두 회수합니다.
  - [잠정] 낫 간격은 `1초 × 0.5 ÷ 공격속도`입니다. "공격속도가 2배"를 두 배 빠르다는 뜻으로 해석했습니다.
    간격을 두 배로 늘리는 해석은 `demeterIntervalFactor = 2`로, 발사 궁술의 기본 간격을 기준으로 삼는 해석은 `demeterUseLaunchBaseInterval`로 바꿀 수 있습니다.
- **하데스**: 지형에 떨어졌거나 적에게 박힌 화살이 입력 없이 5초에 걸쳐 돌아옵니다.
  - 비행 중이거나 낙하 중인 화살은 대상이 아닙니다.
  - [잠정] 그 상태가 된 즉시 귀환을 시작합니다(`hadesStartDelay = 0`).
- **제우스**: R을 누르면 마우스 주변 원(반경 3.2, [잠정]) 안의 화살을 즉시 회수합니다. 재사용 대기시간은 6초입니다. 번개 피해는 `공격력 ÷ 2`입니다.
  - [잠정] 커서에 가장 가까운 화살부터 최근접 이웃 순서로 화살을 이어 선분을 만듭니다(`zeusChainOrder`).
  - [잠정] 선분에 걸린 적(두께 0.3)과 화살이 박혀 있던 적이 번개를 맞습니다.
  - [잠정] 뇌우 한 번에 같은 적은 한 번만 맞습니다(`zeusHitOncePerEnemy`).
  - [잠정] 범위 안에 화살이 없으면 대기시간을 쓰지 않습니다.
- **기본(줍기)**: 화살이 적에게 박히지 않습니다. 맞으면 적 앞에 튕겨 떨어지므로 주울 수 있습니다.

## 조정 가능한 수치 (`ArcheryConfig`)

명세 값: `playerMaxHp`, `playerAttack`, 각 궁술의 기본 간격, 헤르메스 스택 수치, 오디세우스 1.2/1.5초, 뽑기 0.5초, 귀환 5초, 뇌우 6초와 피해 계수, 낫 피해 배율.

잠정 값: `referenceDrawSeconds`, `arrowSpeed`, `straightMaxRange`, `fallGravity`, `athenaGravityPerWeight`, `odysseusDamageReferenceSeconds`, `odysseusChargeScalesWithDraw`, `odysseusMultiplierIncludesAttack`, `aresThrustRange/Width`, `aresMissConsumesInterval`, `autoPickupRadius`, `orpheusReleaseMode`, `returnEaseExponent`, `aresPullRadius`, `demeterRadius/Angle/ReferenceInterval/IntervalFactor/UseLaunchBaseInterval/ApplyAttackSpeed`, `hadesStartDelay`, `zeusRadius/HitOncePerEnemy/ChainOrder/LineThickness/CooldownOnlyWhenRecalled`, `hermesRefreshMode`, 적 부활과 넉백 복귀 시간, 카메라 크기, 청동 넉백 거리(`arrows[].hitEffects`), 초기 화살통 구성(`defaultLoadout`).

## 테스트 공간

- 바닥이 있고, 높이가 다른 발판 5개가 있습니다. 양쪽 끝은 벽입니다.
- 적 6명을 배치했습니다.
  - 허수아비 A (지상)
  - 번개 시험 허수아비 (바닥에 하늘색 표식이 있고, F2로 양옆에 화살을 놓을 수 있습니다)
  - 발판 위 표적
  - 순찰 적
  - 부유 적
  - 원거리 표적
- 적 머리 위에 체력, 박힌 화살 수, 최근 피해와 그 출처를 표시합니다. 피해 숫자가 떠오릅니다.
- 쓰러진 적은 2.5초 뒤 부활합니다.
- HUD에 표시하는 항목
  - 선택한 발사·회수 궁술
  - 체력, 공격력, 공격속도
  - 발사 간격 계산식과 대기시간
  - 연사 스택과 남은 시간, 충전 막대, 충전시간, 최종 피해
  - 회수 진행도, 재사용 대기시간
  - 화살 상태별 수(화살통, 비행, 낙하, 바닥, 박힘, 회수 중)
  - 화살통 순서(맨 왼쪽이 다음 화살)
  - 무결성 검사 결과와 이벤트 로그

## 검증 범위

자동 검증은 실제 빌드에서 게임 루프로 실행합니다.
- 궁술 입력은 `InputOverride`로 넣습니다.
- 이동 입력은 Input System 가상 키 이벤트로 넣습니다.
- 진행 속도는 timeScale 2입니다.

검증 항목:
- 30조합 각각의 발사, 적중 피해, 화살 상태, 회수 동작
- 기존 이동과 애니메이션: 좌우 이동, 다리 보행, 점프, 2단 점프 공중제비, 대시, 좌우 반전
- 찌르기가 빗나가면 화살을 소모하지 않음
- 뽑기 재적중이 정확히 1회이고, 적중 효과(넉백)도 다시 발동함
- 하데스와 오르페우스가 비행 중인 화살을 가져오지 않음
- 동시 회수와 순차 회수의 화살통 배치
- 중복 회수와 중복 피해가 없음
- 발사 간격, 헤르메스 버프, 아테나의 무게별 낙하, 오디세우스 피해 계산, 오르페우스 거리별 속도와 R 놓기, 제우스 번개와 대기시간, 데메테르 낫
- 초기화와 로비 복귀 시 화살, 버프, 충전, 대기시간이 정리됨

자동 검증이 확인하지 못하는 것:
- 실제 마우스·키보드 손맛, 수치 밸런스, 시각 연출의 완성도
- 사람이 직접 플레이하며 확인해야 합니다.

## Windows x64 배포 빌드

- 메뉴: `Tools > Archery > Build Windows x64 Test Build + ZIP`
- 명령줄(에디터를 닫은 상태):
  `Unity -batchmode -quit -projectPath <프로젝트> -buildTarget Win64 -executeMethod Procedural2D.Editor.ArcheryTestBuilder.BuildWindowsFromCommandLine`
- 결과물
  - 빌드 폴더: `Builds/ArcheryTest/Windows/` (`ArcheryTest.exe`, `ArcheryTest_Data/`, `UnityPlayer.dll`, `MonoBleedingEdge/` 등)
  - 배포 ZIP: `Builds/ArcheryTest/ArcheryTest-Windows.zip`
    - 안에 `ArcheryTest-Windows/` 폴더가 통째로 들어 있고, 실행 안내 `README.txt`도 포함됩니다.
    - 디버그 전용 폴더(`*_DoNotShip`)는 제외합니다.
- 시작 씬은 `ArcheryTest.unity` 하나만 지정합니다. 프로젝트 Build Settings와 macOS 빌드는 바꾸지 않습니다.
- 필요 모듈: Unity 6000.3.25f1의 **Windows Build Support (Mono)**. 모듈이 없으면 빌드하지 않고 설치 안내 메시지를 출력합니다.

### 모듈 설치 (Mac)

1. Unity Hub > Installs > 6000.3.25f1 > ⚙ > Add modules
2. **Windows Build Support (Mono)**를 체크하고 설치합니다(다운로드 약 385MB).
3. 설치가 끝나면 에디터를 다시 실행하고, 위 메뉴로 빌드합니다.

### Windows PC에서 이어서 빌드하는 경우

1. 같은 저장소를 받고, Unity Hub로 **6000.3.25f1**을 설치합니다(Windows 에디터에는 Windows 빌드 지원이 기본으로 포함됩니다).
2. 프로젝트를 엽니다. 첫 실행 시 가져오기에 시간이 걸립니다.
3. `Tools > Archery > Build Windows x64 Test Build + ZIP`을 실행합니다.
4. `Builds/ArcheryTest/ArcheryTest-Windows.zip`이 생깁니다. 같은 PC에서 압축을 풀고 `ArcheryTest.exe`를 실행해 확인합니다.
