using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 궁술 테스트 씬(Laurel_ArcheryTest) 자동 검증. Play 중에 GameRoot 오브젝트에 AddComponent 하면 실행되고 결과를 Report 에 남긴다.
    /// 확인: 독립 시작 · 30 조합 선택(궁술 선택 UI 경로)·발사·적중·회수 · 기본 피해 · 피격/사망 방지 · 세이브 미기록 · 맵 폭 2.5배 · 배경/외벽 가림.
    /// 끝나면 테스트 맵을 다시 열어 둔다.
    /// </summary>
    public class ArcheryTestSceneCheck : MonoBehaviour
    {
        private GameRoot G => GameRoot.I;
        public bool Done { get; private set; }
        public int Pass { get; private set; }
        public int Fail { get; private set; }
        public string Report => sb.ToString();
        private readonly StringBuilder sb = new StringBuilder();

        private Vector2 aim;
        private bool fireHeld, fireDownPulse, recallHeld, recallDownPulse;
        private bool stuckSeen;

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) Pass++; else Fail++;
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        }

        private void Update()
        {
            if (G != null && G.Combat != null && G.Combat.Active && G.Combat.CountState(ArrowState.Stuck) > 0) stuckSeen = true;
        }

        private IEnumerator Start()
        {
            G.SelfTestRunning = true;
            Application.runInBackground = true;
            int skipped0 = SaveStore.SkippedSaves, saved0 = SaveStore.SaveCount;
            var t = G.ArcheryTest;

            sb.AppendLine("■ 독립 시작");
            Check("테스트 씬 설정(ArcheryTestArena) 감지 → 타이틀 없이 테스트 맵", t != null && G.State == GameState.ArcheryTest && G.Room != null && G.Room.name == "Room_ArcheryTest", $"상태 {G.State}");
            Check("기존 플레이어 프리팹 사용", G.PlayerGO != null && G.Avatar.Controller != null && G.Avatar.Aim != null, G.PlayerGO != null ? G.PlayerGO.name : "없음");
            Check("조작 가능 (이동·조준·전투 입력 켜짐)", G.Avatar.Controller.inputEnabled && G.Avatar.Aim.inputEnabled && G.Combat.inputEnabled);
            Check("세이브 쓰기 차단", SaveStore.Disabled);

            sb.AppendLine("■ 맵 폭");
            var room = G.Room;
            float wl = room.transform.Find("WallL").GetComponent<BoxCollider2D>().bounds.max.x;
            float wr = room.transform.Find("WallR").GetComponent<BoxCollider2D>().bounds.min.x;
            float playW = wr - wl;
            // 기준: 1스테이지 일반 전투 방 (EnterNode 와 같은 생성 경로)
            var refRoom = Room.Create("RefCheck", RoomTheme.FromStage(DB.Stage(1)), DB.Balance.world.arenaSize, 0, 1);
            float rwl = refRoom.transform.Find("WallL").GetComponent<BoxCollider2D>().bounds.max.x;
            float rwr = refRoom.transform.Find("WallR").GetComponent<BoxCollider2D>().bounds.min.x;
            float refW = rwr - rwl, refH = refRoom.InnerBounds.height;
            Destroy(refRoom.gameObject);
            Check("좌우 충돌 경계 사이 폭 = 기준 방 × 2.5", Mathf.Abs(playW - refW * 2.5f) < 0.01f, $"기준 {refW:0.##} → 테스트 {playW:0.##} (×{playW / refW:0.###})");
            Check("높이는 기준 방과 같음", Mathf.Abs(room.InnerBounds.height - refH) < 0.01f, $"{room.InnerBounds.height:0.##} / {refH:0.##}");
            int dummies = 0; float minX = 999, maxX = -999, minY = 999, maxY = -999;
            foreach (var e in room.Enemies) if (e is TrainingDummy) { dummies++; minX = Mathf.Min(minX, e.transform.position.x); maxX = Mathf.Max(maxX, e.transform.position.x); minY = Mathf.Min(minY, e.transform.position.y); maxY = Mathf.Max(maxY, e.transform.position.y); }
            Check("허수아비 여러 개 (가까운·먼·높이 다른)", dummies >= 8 && maxX - minX > 35f && maxY - minY > 8f, $"{dummies}개, x {minX:0.#}~{maxX:0.#}, y {minY:0.#}~{maxY:0.#}");
            bool platsInside = true;
            foreach (var p in room.Platforms) if (p.xMin < room.InnerBounds.xMin || p.xMax > room.InnerBounds.xMax || p.yMax > room.InnerBounds.yMax - 2f) platsInside = false;
            Check("발판이 방 안", platsInside, $"{room.Platforms.Count}개");

            sb.AppendLine("■ 배경·외벽 가림 (카메라 양 끝)");
            var bd = t.backdrop;
            var style = t.terrainStyle;
            float camBoundMax = room.InnerBounds.xMax + DB.Balance.world.wallThickness;
            float ortho = CameraRig.I.PlaySize;
            foreach (var aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f, 32f / 9f })
            {
                float halfW = ortho * aspect;
                float camX = Mathf.Max(0f, camBoundMax - halfW); // 카메라가 오른쪽 끝까지 갔을 때 (좌우 대칭)
                float imgHalf = bd.width * 0.5f, imgCenter = room.InnerBounds.center.x + bd.offset.x + camX * bd.parallax;
                float viewInsideWall = Mathf.Min(camX + halfW, room.InnerBounds.xMax); // 외벽 안쪽까지 보이는 끝
                bool imgCovers = imgCenter + imgHalf >= viewInsideWall && imgCenter - imgHalf <= camX - halfW;
                Check($"화면 {aspect:0.00}:1 — 배경 이미지가 끊기지 않음", imgCovers, $"카메라 x {camX:0.##}, 보이는 범위 {camX - halfW:0.#}~{viewInsideWall:0.#}, 이미지 {imgCenter - imgHalf:0.#}~{imgCenter + imgHalf:0.#}");
            }
            Check("외벽 바깥 가림 켜짐 (500 유닛)", style != null && style.maskOutsideWalls && style.outsideMaskSize >= 100f && room.transform.Find("OutsideMaskL") != null && room.transform.Find("OutsideMaskR") != null);
            Check("임시 단색 배경 숨김 (배경 이미지 사용)", room.transform.Find("Bg") != null && !room.transform.Find("Bg").gameObject.activeSelf);

            sb.AppendLine("■ 능력치 (강화 없는 기본값 + 모든 기능)");
            var st = G.Avatar.Stats;
            var bal = DB.Balance.player;
            Check("공격력·체력·방어·공속·이속 = 기본값", st.attack == bal.attack && st.maxHp == bal.maxHp && st.defense == bal.defense && st.attackSpeedBonus == bal.attackSpeed && Mathf.Approximately(st.moveMul, bal.moveSpeedMul), $"공격 {st.attack} 체력 {st.maxHp} 방어 {st.defense}");
            Check("더블점프·3단 점프·대쉬 사용 가능", G.Avatar.Controller.maxJumps == 3 && G.Avatar.Controller.dashUnlocked);
            bool basicDeck = G.PracticeRun.deck.Count == bal.startArrows.Length;
            foreach (var c in G.PracticeRun.deck) if (c.Damage != DB.Arrow(c.baseCode).damage) basicDeck = false;
            Check("화살 = 기본 시작 화살, 피해 보정 없음", basicDeck, $"{G.PracticeRun.deck.Count}발, 카드 피해 {(G.PracticeRun.deck.Count > 0 ? G.PracticeRun.deck[0].Damage : 0)}");

            sb.AppendLine("■ 피격·사망 방지");
            G.Avatar.ClearIframes();
            int hurtEvents = 0;
            System.Action<float> onHurt = _ => hurtEvents++;
            G.Avatar.OnHurt += onHurt;
            float hp0 = G.Avatar.Hp;
            bool h1 = G.Avatar.Hurt(30f, "검증");
            float hpAfter1 = G.Avatar.Hp;
            G.Avatar.ClearIframes();
            bool h2 = G.Avatar.Hurt(9999f, "검증");
            float hpAfter2 = G.Avatar.Hp;
            G.Avatar.ClearIframes();
            bool h3 = G.Avatar.Hurt(9999f, "검증");
            G.Avatar.OnHurt -= onHurt;
            Check("일반 피해는 그대로 깎임", h1 && Mathf.Approximately(hpAfter1, hp0 - 30f), $"{hp0} → {hpAfter1}");
            Check("치명 피해에도 체력 1 유지, 사망 없음", h2 && h3 && hpAfter2 == 1f && G.Avatar.Hp == 1f && !G.Avatar.Dead && G.State == GameState.ArcheryTest, $"체력 {G.Avatar.Hp}, 사망 {G.Avatar.Dead}");
            Check("피격 반응 발생 (OnHurt·무적시간)", hurtEvents == 3 && !G.Avatar.Hurt(1f, "무적시간 중"), $"피격 이벤트 {hurtEvents}회");
            G.StartPractice(); // 체력 다시 채움

            sb.AppendLine("■ 30 조합 (궁술 선택 UI 경로로 선택 → 발사 → 적중 → 회수)");
            HookInput();
            Time.timeScale = 2f;
            int ok = 0;
            foreach (var l in StyleNames.AllLaunch)
                foreach (var r in StyleNames.AllRetrieval)
                {
                    yield return OneCombo(l, r);
                    if (lastOk) ok++;
                }
            Time.timeScale = 1f;
            UnhookInput();
            Check("30 조합 모두 통과", ok == 30, $"{ok}/30");

            sb.AppendLine("■ 세이브");
            Check("검증 동안 세이브 파일 쓰기 0회 (Save 호출은 건너뜀)", SaveStore.SaveCount == saved0, $"쓰기 {SaveStore.SaveCount - saved0}회, 건너뜀 {SaveStore.SkippedSaves - skipped0}회");

            // 처음 상태로 되돌림
            G.SelectLaunch(LaunchStyle.Apollo);
            G.SelectRetrieval(RetrievalStyle.Basic);
            G.StartArcheryTest();
            G.SelfTestRunning = false;
            sb.AppendLine($"결과: 통과 {Pass} / 실패 {Fail}");
            Debug.Log("[ArcheryTestSceneCheck]\n" + sb);
            Done = true;
        }

        private bool lastOk;

        private IEnumerator OneCombo(LaunchStyle l, RetrievalStyle r)
        {
            fireHeld = recallHeld = false;
            // 궁술 선택 UI 와 같은 경로: 패널 열기 → 버튼(SelectLaunch/SelectRetrieval) → 닫기
            G.OpenPanel(GameState.ArcherySelect);
            G.SelectLaunch(l);
            G.SelectRetrieval(r);
            bool lockedOut = G.Avatar.Controller.inputEnabled; // 패널이 열려 있는 동안은 조작이 꺼져야 한다
            G.ClosePanel();
            bool selected = G.State == GameState.ArcheryTest && G.Combat.LaunchType == l && G.Combat.RetrievalType == r && G.Avatar.Controller.inputEnabled && !lockedOut;

            var room = G.Room;
            Enemy dummy = null;
            foreach (var e in room.Enemies) if (e is TrainingDummy && Mathf.Abs(e.transform.position.x + 15f) < 0.5f && e.transform.position.y < -9f) dummy = e;
            float floor = room.InnerBounds.yMin;
            G.Avatar.Teleport(new Vector2(dummy.transform.position.x + (l == LaunchStyle.Ares ? -1.6f : -6f), floor + 0.6f));
            yield return new WaitForSeconds(0.3f);
            int deck = G.PracticeRun.deck.Count;
            float dmg0 = dummy.DamageTaken;
            int shots0 = G.Combat.Stats.shots;
            stuckSeen = false;
            var firstHitDamages = new List<float>();
            for (int shot = 0; shot < 2; shot++)
            {
                aim = (Vector2)dummy.Center + (l == LaunchStyle.Athena ? Vector2.up * 0.3f : Vector2.zero);
                float before = dummy.DamageTaken;
                if (l == LaunchStyle.Odysseus)
                {
                    fireDownPulse = true; fireHeld = true;
                    yield return new WaitForSeconds(1.7f);
                    fireHeld = false;
                }
                else
                {
                    fireHeld = true;
                    float w = 0f;
                    while (G.Combat.Stats.shots <= shots0 + shot && w < 3f) { w += Time.unscaledDeltaTime; yield return null; }
                    fireHeld = false;
                }
                yield return new WaitForSeconds(0.9f);
                firstHitDamages.Add(dummy.DamageTaken - before);
            }
            yield return new WaitForSeconds(0.4f);
            float hitDmg = dummy.DamageTaken - dmg0;
            bool hit = hitDmg > 0f;
            bool stickRule = r == RetrievalStyle.Basic ? !stuckSeen : stuckSeen || r == RetrievalStyle.Hades;
            float dmgBeforeRecall = dummy.DamageTaken;
            yield return Recover(r, dummy);
            bool recovered = G.Combat.Quiver.Count == deck;
            bool integrity = G.Combat.Violations.Count == 0 && G.Combat.Bodies.Count == deck;
            bool alive = dummy != null && dummy.IsAlive;
            // 고유 피해: 아폴론·아테나·헤르메스 기본 화살 1발 = 화살 6 + 공격력 1 = 7, 오디세우스 = 6 × 충전초 + 1 (7.2~9.0 → 8.2~10)
            string dmgNote = string.Join("/", firstHitDamages.ConvertAll(x => x.ToString("0.#")));
            bool dmgRule = true;
            float shot1 = firstHitDamages.Count > 0 ? firstHitDamages[0] : 0f;
            if (l == LaunchStyle.Apollo || l == LaunchStyle.Hermes || l == LaunchStyle.Athena) dmgRule = Mathf.Approximately(shot1, 7f);
            else if (l == LaunchStyle.Odysseus) dmgRule = shot1 >= 8.1f && shot1 <= 10.05f;
            else if (l == LaunchStyle.Ares) dmgRule = shot1 >= 7f;
            bool retrieveEffect = r == RetrievalStyle.Demeter ? dummy.DamageTaken > dmgBeforeRecall : true;
            lastOk = selected && hit && dmgRule && stickRule && recovered && integrity && alive && retrieveEffect;
            Check($"{StyleNames.Launch(l)} + {StyleNames.Retrieval(r)}", lastOk,
                $"선택 {(selected ? "O" : "X")} · 1발 피해 {dmgNote} · 박힘규칙 {(stickRule ? "O" : "X")} · 회수 {G.Combat.Quiver.Count}/{deck} · 위반 {G.Combat.Violations.Count} · 허수아비 생존 {(alive ? "O" : "X")}");
            fireHeld = recallHeld = false;
            yield return null;
        }

        private IEnumerator Recover(RetrievalStyle r, Enemy dummy)
        {
            var c = G.Combat;
            int deck = c.Bodies.Count;
            float t = 0f;
            while (c.Quiver.Count < deck && t < 9f)
            {
                switch (r)
                {
                    case RetrievalStyle.Orpheus: recallHeld = true; break;
                    case RetrievalStyle.Hades: break;
                    case RetrievalStyle.Zeus:
                        foreach (var b in c.Bodies) if (b.IsOnField) { aim = b.transform.position; break; }
                        if (c.Retriever.CooldownRemaining <= 0f) recallDownPulse = true;
                        break;
                    case RetrievalStyle.Demeter:
                        foreach (var b in c.Bodies) if (b.IsOnField) { G.Avatar.Teleport((Vector2)b.transform.position + Vector2.left * 1.5f + Vector2.up * 0.3f); aim = b.transform.position; break; }
                        if (c.Retriever.CooldownRemaining <= 0f) recallDownPulse = true;
                        break;
                    case RetrievalStyle.Ares:
                        bool stuck = false;
                        foreach (var b in c.Bodies) if (b.State == ArrowState.Stuck) { stuck = true; G.Avatar.Teleport((Vector2)dummy.Center + Vector2.left * 1.4f); break; }
                        if (!stuck) foreach (var b in c.Bodies) if (b.State == ArrowState.Grounded) { G.Avatar.Teleport((Vector2)b.transform.position + Vector2.up * 0.4f); break; }
                        break;
                    default:
                        foreach (var b in c.Bodies) if (b.State == ArrowState.Grounded) { G.Avatar.Teleport((Vector2)b.transform.position + Vector2.up * 0.4f); break; }
                        break;
                }
                yield return new WaitForSeconds(0.2f);
                t += 0.2f;
            }
            recallHeld = false;
            yield return null;
        }

        private void HookInput()
        {
            var av = G.Avatar;
            av.Controller.InputOverride = () => Vector3.zero;
            if (av.Aim != null) av.Aim.AimWorldOverride = () => aim;
            G.Combat.InputOverride = () =>
            {
                var i = new CombatInput
                {
                    aimWorld = aim, fireDown = fireDownPulse, fireHeld = fireHeld || fireDownPulse, fireUp = !fireHeld && !fireDownPulse,
                    recallDown = recallDownPulse, recallHeld = recallHeld || recallDownPulse, recallUp = !recallHeld && !recallDownPulse
                };
                fireDownPulse = recallDownPulse = false;
                return i;
            };
        }

        private void UnhookInput()
        {
            G.Avatar.Controller.InputOverride = null;
            if (G.Avatar.Aim != null) G.Avatar.Aim.AimWorldOverride = null;
            G.Combat.InputOverride = null;
        }
    }
}
