# Laurel 프로젝트 최적화 정리

현재 코드베이스에서 프레임 드랍, GC(가비지 컬렉션) 스파이크, 불필요한 GPU/CPU 비용을 유발할 수 있는 요소와 개선 방안을 정리한 문서입니다.

---

## 1. 픽셀 렌더링 & 외곽선 (`PixelRenderEffect.cs`) — 우선순위: 높음

> 대상: `Assets/Laurel/Scripts/Core/PixelRenderEffect.cs`

| 항목 | 현재 동작 (문제점) | 개선 방안 | 기대 효과 |
|---|---|---|---|
| **매 프레임 `Renderer` 탐색** | `OnBeginCameraRendering`(L167), `RenderPlayerSilhouette`(L212)에서 매 프레임 `GetComponentsInChildren<Renderer>(true)` 호출 | 플레이어 스폰/파츠 변경 시 1회만 캐싱 (`Renderer[]`) | 프레임당 GC 할당 제거 |
| **매 프레임 레이어 스왑** | 매 프레임 플레이어 자식들의 `gameObject.layer`를 `31`로 바꿨다 복구(L172, L217) | 비주얼(렌더러 전용) 자식만 처음부터 `31`번 레이어로 고정하고, `mainCam.cullingMask` 비트플래그만 토글 | CPU 레이어/컬링 재평가 비용 제거 |
| **미사용 `RenderTexture` 생성** | `OutlineOnly` 모드에서도 `rt`(24bit depth)와 `charRt`를 모두 생성·유지(L123~135) | `FullScene` 모드일 때만 `rt` 생성 | VRAM 절약 |
| **전체 화면 외곽선 블릿** | `OnGUI`에서 `Screen.width × Screen.height` 전체에 `PixelOutline` 셰이더 실행(L250) | 플레이어 스크린 바운딩 박스(+패딩) 영역만 잘라서 블릿 | GPU 픽셀 셰이더 부하 대폭 감소 |
| **추가 카메라 패스** | 기본 모드에서도 `mainCam` + `charCam.Render()` 2패스 실행(L219) | 장기적으로 URP `ScriptableRendererFeature`로 통합 검토 | 카메라 컬링·패스 오버헤드 감소 |

---

## 2. IMGUI(`OnGUI`) 스타일 할당 (`GameUI.cs`, `GameUI.CombatHud.cs`) — 우선순위: 높음

> 대상: `Assets/Laurel/Scripts/UI/GameUI.CombatHud.cs`, `Assets/Laurel/Scripts/UI/GameUI.cs`

| 항목 | 현재 동작 (문제점) | 개선 방안 | 기대 효과 |
|---|---|---|---|
| **매 프레임 `new GUIStyle(...)`** | `OnGUI()`(프레임당 Layout+Repaint 최소 2회 호출) 안에서 `new GUIStyle(...)`을 매번 생성 (`GameUI.CombatHud.cs` L267, L272, L319, L348, L409, L493 등 / `GameUI.cs` `StatusBtn`, `DrawArrowCard` 등) | `Init()` 또는 정적/멤버 필드에 스타일을 1회 캐싱해 재사용 | 전투 중 지속적인 GC Alloc 및 프레임 스파이크 완화 |
| **타이틀·사망 지옥불 이펙트** | `DrawHellfireBottomEffect()`(`GameUI.cs` L604~668)가 매 프레임 약 160+회 `GUI.DrawTexture` 호출 | 인게임 전투에는 영향 없으나, 필요 시 단일 커스텀 셰이더 Quad 또는 텍스처로 통합 | 타이틀/사망 화면 드로우콜 1회로 축소 |

---

## 3. 머티리얼 인스턴스화 & 셰이더 검색 (`ArrowBody.cs`, `Fx.cs`) — 우선순위: 중간

> 대상: `Assets/Laurel/Scripts/Combat/ArrowBody.cs`, `Assets/Laurel/Scripts/Core/Fx.cs`

| 항목 | 현재 동작 (문제점) | 개선 방안 | 기대 효과 |
|---|---|---|---|
| **화살/이펙트 머티리얼 매번 생성** | `ArrowBody.Setup`(L76), `AttachMythicFireParticles`(L179), `Fx.cs`(L125) 등에서 생성 시마다 `new Material(Shader.Find("Sprites/Default"))` 호출 | 공용 `static Material`을 1회 생성해 공유 (`sharedMaterial`) | `Shader.Find` 비용 제거, 머티리얼 메모리 절약 및 배칭 효율 향상 |

---

## 4. 대시 잔상 & 이펙트 오브젝트 생성 (`ProceduralCharacterController.cs`) — 우선순위: 낮음~중간

> 대상: `Assets/pjs/Scripts/Procedural2D/ProceduralCharacterController.cs`

| 항목 | 현재 동작 (문제점) | 개선 방안 | 기대 효과 |
|---|---|---|---|
| **대시 잔상(Ghost Trail) 생성/파괴** | `SpawnGhostTrail()`(L663~) 호출 시마다 `new GameObject` + `GetComponentsInChildren<SpriteRenderer>()` + `Destroy` 반복 | 잔상 오브젝트 풀링 + 플레이어 `SpriteRenderer[]` 캐싱 | 대시 순간의 GC/생성 스파이크 방지 |
| **Deprecated 물리 API** | `Physics2D.OverlapBoxNonAlloc`(L863, L912) 사용 중 (경고 `CS0618`) | `Physics2D.OverlapBox` + `ContactFilter2D`로 교체 | 최신 Unity 물리 API 호환성 유지 |

---

## 권장 진행 순서
1. **`GameUI.CombatHud.cs` / `GameUI.cs`의 `GUIStyle` 캐싱** (체감 GC 스파이크 감소 폭이 가장 큼)
2. **`PixelRenderEffect.cs` 최적화** (`Renderer[]` 캐싱 + 미사용 `rt` 생략 + 외곽선 블릿 영역 축소 + 비주얼 레이어 분리)
3. **`ArrowBody.cs` / `Fx.cs` 공용 머티리얼 캐싱**
4. **`ProceduralCharacterController.cs` 대시 잔상 풀링**
