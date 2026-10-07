# 라우랄 (Laurel) — 1~3스테이지 플레이 빌드

Unity **6000.3.25f1 (6.3 LTS)** · URP 2D · 브랜치 `laurel-dev`

## 실행

| 항목 | 위치 |
| --- | --- |
| macOS 빌드 | `Builds/Laurel/macOS/Laurel.app` (git 제외 폴더) |
| Windows 빌드 | `Builds/Laurel/Windows/Laurel.exe`, 배포용 `Builds/Laurel/Laurel-Windows.zip` |
| 에디터 | `Assets/Laurel/Scenes/Laurel_Main.unity` 를 열고 Play |
| 빌드 메뉴 | `Tools > Laurel > Build macOS` / `Build Windows x64 + ZIP` |
| 명령줄 빌드 | `unity build --target StandaloneOSX --execute-method Laurel.EditorTools.LaurelBuilder.BuildMacFromCommandLine .` |
| 저장 파일 | macOS `~/Library/Application Support/DefaultCompany/laurel/laurel_profile.json` (+ `.bak`) / Windows `%USERPROFILE%\AppData\LocalLow\DefaultCompany\laurel\` |

### 자동 검증

```
Laurel.app/Contents/MacOS/laurel -laurelSelfTest -laurelSelfTestOut report.txt
# 재실행 후 저장 유지 검증 (두 번 실행)
laurel -laurelPersistPhase 1 -laurelSelfTestOut p1.txt
laurel -laurelPersistPhase 2 -laurelSelfTestOut p2.txt
```

검증은 실제 사용자 저장 파일 대신 `laurel_selftest_*.json` 을 씁니다. 최근 결과는 `Docs/Laurel_SelfTest_Report.txt`.

## 조작

| 입력 | 동작 |
| --- | --- |
| A / D | 이동 (기존 플레이어 그대로) |
| Space | 점프. **더블점프·3단 점프는 로비에서 구매한 뒤에만** (튜토리얼에서는 체험 가능) |
| Shift | 대쉬 — **구매한 뒤에만** |
| 마우스 | 조준 (좌우 반전·팔·활 자세는 기존 코드) |
| 좌클릭 | 발사. 누르고 있으면 간격마다 연사. 오디세우스는 누르고 있다가 놓기, 아레스는 찌르기 |
| R | 회수 궁술 (오르페우스: 누르고 있기 / 데메테르: 낫 / 제우스: 뇌우) |
| 우클릭 / Q | 화살통 맨 앞 화살 버리기 |
| Esc | 일시정지 (조작법, 도전 포기, 타이틀) |
| Enter / Space / 클릭 | 대화 넘기기 |
| F12 | 개발자 모드 (F9 넥타르 +20, F10 방 정리, F11 보스 해금 전체) |

## 게임 흐름

1. 타이틀 → **새 게임**: 도입 스토리 → 튜토리얼(신의 힘: 체력 300·공격 6·더블점프·대쉬)
   - 이동·점프·더블점프·대쉬 → 과녁 사격 → 화살 회수 → **거대한 뱀 파에톤**
2. 파에톤 처치 → 에로스의 금화살 **저주** → 본게임 기본 상태(체력 100·공격 1·스킬 없음·아폴론+기본 줍기)로 1스테이지 지도
3. 스테이지 = 노드 지도(전투·정예·보상·상점·샘 → 보스). 노드마다 작은 정사각형 방
4. 1스테이지 보스(칼리돈의 멧돼지) → **로비 없이** 2스테이지(아켈로오스) → 3스테이지(에로스) → 결과 화면 → 로비
5. 어디서든 사망 → 사망 화면 → 로비. 재도전은 1스테이지 처음부터, 돈·화살·유물·도전 중 능력치 초기화
6. 로비: 출정 · **넥타르 강화(개미굴)** · 궁술 선택 · 기록 · 수련장(허수아비)
   - 보스 처치 → 해당 스킬 **구매 자격** → 넥타르로 구매 → 영구 보유 (자동 지급 없음)

## 작업 범위

### 보존 (기존 플레이어)

- `Assets/pjs/Prefabs/ProceduralCharacter.prefab` 과 `Assets/pjs/Sprites/Character*` 전체
- `Assets/pjs/Scripts/Procedural2D/` 의 5개 스크립트: `ProceduralCharacterController`(이동·점프·2단 점프 공중제비·대쉬·잔상·다리), `Procedural2DAim`(조준·좌우 반전·팔/활 자세·반동), `ProceduralBodyShift`, `ProceduralSkirtPhysics`, `ProceduralWaistRibbon`
- 이동 수치(이동 11.66, 점프 21/17, 대쉬 28.5, 중력 3)와 물리·입력 방식 그대로
- 플레이어 코드에 추가한 것: 입력 잠금·대쉬 잠금 스위치, 검증용 입력 오버라이드, 순간이동용 상태 초기화(`ResetMotionState`, 리본 `SnapToAnchor`, 치마 `ResetMotion`). 기존 동작은 바꾸지 않음
- `Procedural2DAim` 에서 기존 사격·탄창·회수·HUD 부분만 분리해 삭제(전투는 새 시스템이 담당)
- 프로젝트 설정·패키지·URP 설정·Input Actions 유지

### 삭제

- 기존 전투(`ArrowProjectile`), 적(`DemonEyeAI`, `EnemyDummy`, 촉수 물리), 이펙트·오디오 매니저, 카메라, `PrototypeScene`, `SampleScene`, 환경 스프라이트, BGM·SFX, 자동 조립 에디터 도구, 이전 Laurel csproj 잔재
- 이전 상태는 브랜치 `backup/pre-laurel-20261007-1409`, 궁술테스트 빌드는 태그 `backup/archery-test-build-v1.1` 과 `../arrow-backups/ArcheryTest-build-20261007-1409.zip` 에 보존

### 신규 (`Assets/Laurel/`)

| 폴더 | 내용 |
| --- | --- |
| `Resources/LaurelData/*.json` | 모든 수치·콘텐츠 데이터 |
| `Scripts/Core` | 데이터 로더, 저장(원자적 쓰기+백업), 도전 상태, 게임 흐름(`GameRoot`), 연출·효과음·카메라 |
| `Scripts/Combat` | 화살 카드·실체·임시 투사체, 화살통, 발사 5종·회수 6종, 적중 파이프라인, 플레이어 체력·스킬 연결 |
| `Scripts/Enemies` | 적 6종 AI, 보스 4종, 상태이상, 허수아비 |
| `Scripts/World` | 정사각형 방·장판·골드, 노드 지도 생성 |
| `Scripts/Meta` | 넥타르 강화 규칙, 상점·보상 |
| `Scripts/UI` | 모든 화면(IMGUI) |
| `Scripts/Tests` | 실제 게임 루프 자동 검증 |
| `Editor` | 빌드 도구 |
| `Scenes/Laurel_Main.unity` | 유일한 씬 (Unity MCP 로 GameRoot·카메라 구성, 플레이어 프리팹·화살 스프라이트 연결) |

임시로 정한 규칙과 수치는 `Docs/Laurel_Design_Decisions.md`.
