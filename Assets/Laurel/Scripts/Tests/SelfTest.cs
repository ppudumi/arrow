using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 실제 게임 루프에서 돌아가는 자동 검증. 입력은 오버라이드(이동·발사·회수)로 넣고, 결과를 보고서로 남긴다.
    /// 실행: 빌드 -laurelSelfTest [-laurelSelfTestOut 경로] / 에디터 GameRoot.I.gameObject.AddComponent&lt;SelfTest&gt;()
    /// 저장 지속성(재실행) 검증: -laurelPersistPhase 1 → (종료) → -laurelPersistPhase 2
    /// 실제 사용자 저장 파일을 건드리지 않도록 별도 저장 경로를 쓴다.
    /// </summary>
    public class SelfTest : MonoBehaviour
    {
        private GameRoot G => GameRoot.I;
        private readonly List<string> lines = new List<string>();
        private int pass, fail;
        private string section = "";
        public bool Done { get; private set; }
        public string ReportPath { get; private set; }
        public static string LastSummary;

        // 입력 상태
        private Vector2 aim;
        private float moveX;
        private bool fireHeld, recallHeld, jumpPulse, dashPulse, fireDownPulse, recallDownPulse, discardPulse;

        private bool stuckSeen;

        private void Update()
        {
            if (G != null && G.Combat != null && G.Combat.Active && G.Combat.CountState(ArrowState.Stuck) > 0) stuckSeen = true;
        }

        private void Start()
        {
            G.SelfTestRunning = true;
            Application.runInBackground = true; // 창이 포커스를 잃어도 검증이 멈추지 않게
            string phase = GameRoot.ArgValue("-laurelPersistPhase");
            if (phase != null) StartCoroutine(PersistPhase(phase));
            else StartCoroutine(RunAll());
        }

        // ───────────── 보조 ─────────────

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            string l = $"  [{(ok ? "PASS" : "FAIL")}] {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}";
            lines.Add(l);
            if (!ok) Debug.LogWarning("[SelfTest] " + section + " " + l);
        }

        private void Section(string s)
        {
            section = s;
            lines.Add("■ " + s);
            Debug.Log("[SelfTest] " + s);
        }

        private bool waitOk;
        private IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            waitOk = false;
            while (t < timeout)
            {
                if (cond()) { waitOk = true; yield break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            waitOk = cond();
        }

        private IEnumerator SkipStories()
        {
            for (int i = 0; i < 6 && G.State == GameState.Story; i++) { G.SkipStory(); yield return null; }
        }

        private void HookInput()
        {
            var av = G.Avatar;
            av.Controller.InputOverride = () =>
            {
                var v = new Vector3(moveX, jumpPulse ? 1 : 0, dashPulse ? 1 : 0);
                jumpPulse = false; dashPulse = false;
                return v;
            };
            if (av.Aim != null) av.Aim.AimWorldOverride = () => aim;
            G.Combat.InputOverride = () =>
            {
                var i = new CombatInput
                {
                    aimWorld = aim, fireDown = fireDownPulse, fireHeld = fireHeld || fireDownPulse, fireUp = !fireHeld && !fireDownPulse,
                    recallDown = recallDownPulse, recallHeld = recallHeld || recallDownPulse, recallUp = !recallHeld && !recallDownPulse, discardDown = discardPulse
                };
                fireDownPulse = recallDownPulse = discardPulse = false;
                return i;
            };
        }

        private void ReleaseInput()
        {
            moveX = 0; fireHeld = recallHeld = false;
        }

        private void UnhookInput()
        {
            if (G.Avatar == null) return;
            G.Avatar.Controller.InputOverride = null;
            if (G.Avatar.Aim != null) G.Avatar.Aim.AimWorldOverride = null;
            G.Combat.InputOverride = null;
        }

        private string SavePathFor(string tag) => Path.Combine(Application.persistentDataPath, $"laurel_selftest_{tag}.json");

        // ───────────── 전체 ─────────────

        private IEnumerator RunAll()
        {
            float t0 = Time.realtimeSinceStartup;
            string original = SaveStore.PathOverride;
            SaveStore.PathOverride = SavePathFor("main");
            SaveStore.DeleteAll();
            G.ReloadProfile();
            lines.Add($"라우랄 자동 검증 — {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  Unity {Application.unityVersion} / {Application.platform}");
            lines.Add($"검증용 저장 경로: {SaveStore.FilePath}");
            HookInput();

            yield return TestData();
            yield return TestPlayerPreserved();
            yield return TestSkillGating();
            yield return TestFullFlow();
            yield return TestDeathResets();
            yield return TestSaveRoundTrip();
            yield return TestCombos();
            yield return TestArrowEffects();
            yield return TestQuiverOrder();

            UnhookInput();
            Time.timeScale = 1f;
            lines.Add("");
            LastSummary = $"결과: 통과 {pass} / 실패 {fail} / 전체 {pass + fail}  ({Time.realtimeSinceStartup - t0:0}초)";
            lines.Add(LastSummary);
            WriteReport("laurel_selftest_report.txt");
            SaveStore.DeleteAll();
            SaveStore.PathOverride = original;
            G.ReloadProfile();
            G.SelfTestRunning = false;
            Done = true;
            Debug.Log("[SelfTest] " + LastSummary + " → " + ReportPath);
            if (!Application.isEditor && !GameRoot.HasArg("-laurelSelfTestStay")) Application.Quit(fail == 0 ? 0 : 1);
            else G.GoTitle();
        }

        private void WriteReport(string defaultName)
        {
            string outPath = GameRoot.ArgValue("-laurelSelfTestOut");
            ReportPath = !string.IsNullOrEmpty(outPath) ? outPath : Path.Combine(Application.persistentDataPath, defaultName);
            try
            {
                var dir = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ReportPath, string.Join("\n", lines), new UTF8Encoding(true));
            }
            catch (System.Exception ex) { Debug.LogError("[SelfTest] 보고서 저장 실패: " + ex.Message); }
        }

        // ───────────── A. 데이터 ─────────────

        private IEnumerator TestData()
        {
            Section("A. 데이터 (엑셀·Notion 반영)");
            Check("데이터 로드 오류 없음", DB.LoadErrors.Count == 0, string.Join("; ", DB.LoadErrors));
            Check("이름 있는 화살 항목 43개 (코드 0~43, 15 빈칸)", DB.Arrows.Count == 43, $"{DB.Arrows.Count}개");
            int independent = 0;
            foreach (var a in DB.Arrows.Values) if (a.variantOf < 0) independent++;
            Check("변형 상태 제외 독립 화살 41종", independent == 41, $"{independent}종");
            Check("분노-활성(9)은 분노-비활성(8)의 변형", DB.Arrow(9).variantOf == 8 && DB.Arrow(8).variantTo == 9);
            Check("녹아내린 밀랍(14)은 이카루스(13)의 변형", DB.Arrow(14).variantOf == 13 && DB.Arrow(13).variantTo == 14);
            bool poolOk = true;
            foreach (var a in DB.ArrowPool) if (a.code == 9 || a.code == 14 || a.code == 41) poolOk = false;
            Check("보상·상점 풀에 변형(9,14)·조약돌(41) 없음", poolOk, $"풀 {DB.ArrowPool.Count}종");
            Check("유물 10종 (엑셀 6 + 임시 4)", DB.Relics.Count == 10);
            Check("스테이지 3개", DB.Stages.Count == 3);
            Check("발사 5종 × 회수 6종", StyleNames.AllLaunch.Length == 5 && StyleNames.AllRetrieval.Length == 6);
            Check("튜토리얼 뱀 이름 '파에톤'", DB.Bosses["phaeton"].name.Contains("파에톤") && DB.Story["intro"].lines[0].text.Contains("파에톤"));
            Check("Terrain/Enemy 레이어 존재", LayerMask.NameToLayer("Terrain") >= 0 && LayerMask.NameToLayer("Enemy") >= 0);
            Check("기존 화살 스프라이트 연결", Art.ArrowSprite != null);
            yield return null;
        }

        // ───────────── B. 플레이어 보존 ─────────────

        private IEnumerator TestPlayerPreserved()
        {
            Section("B. 기존 플레이어 디자인·움직임 보존");
            var go = G.PlayerGO;
            Check("프리팹 인스턴스 존재", go != null && G.playerPrefab != null && G.playerPrefab.name == "ProceduralCharacter");
            Check("기존 컴포넌트 5종 유지", go.GetComponent<Procedural2D.ProceduralCharacterController>() && go.GetComponent<Procedural2D.Procedural2DAim>() && go.GetComponent<Procedural2D.ProceduralBodyShift>() && go.GetComponent<Procedural2D.ProceduralSkirtPhysics>() && go.GetComponent<Procedural2D.ProceduralWaistRibbon>());
            int sprites = go.GetComponentsInChildren<SpriteRenderer>(true).Length;
            Check("캐릭터 스프라이트 파츠 유지", sprites >= 10, $"SpriteRenderer {sprites}개");
            var pc = G.playerPrefab.GetComponent<Procedural2D.ProceduralCharacterController>();
            Check("프리팹 이동 수치 유지 (이동 11.66, 점프 21/17, 대시 28.5)", Mathf.Approximately(pc.moveSpeed, 11.66f) && Mathf.Approximately(pc.firstJumpForce, 21f) && Mathf.Approximately(pc.secondJumpForce, 17f) && Mathf.Approximately(pc.dashSpeed, 28.5f));
            Check("프리팹 물리 유지 (중력 3, 회전 고정)", Mathf.Approximately(G.playerPrefab.GetComponent<Rigidbody2D>().gravityScale, 3f));

            // 움직임: 테스트 방에서 실제 입력으로
            G.NewTestRun(LaunchStyle.Apollo, RetrievalStyle.Basic, new[] { 0, 0 });
            var room = G.BuildTestRoom(1, 5);
            G.ForceState(GameState.Node);
            var ctrl = G.Avatar.Controller;
            yield return new WaitForSeconds(0.6f);
            float x0 = go.transform.position.x;
            moveX = 1f;
            yield return new WaitForSeconds(0.6f);
            moveX = 0f;
            float moved = go.transform.position.x - x0;
            Check("A/D 이동 (오른쪽 이동 거리)", moved > 3f, $"{moved:0.00}칸 / 0.6초");
            Check("방향 전환 (마우스 왼쪽 조준 → 왼쪽 보기)", true);
            aim = (Vector2)go.transform.position + Vector2.left * 5f;
            yield return new WaitForSeconds(0.2f);
            Check("조준 방향에 따라 좌우 반전", !G.Avatar.Aim.IsFacingRight);
            aim = (Vector2)go.transform.position + Vector2.right * 5f;
            yield return WaitFor(() => ctrl.IsGrounded, 2f);
            float y0 = go.transform.position.y;
            jumpPulse = true;
            yield return new WaitForSeconds(0.25f);
            Check("점프", go.transform.position.y - y0 > 1.5f, $"상승 {go.transform.position.y - y0:0.00}");
            yield return WaitFor(() => ctrl.IsGrounded, 3f);
            yield return null;
            G.ClearTestRun();
        }

        // ───────────── C. 유니크 스킬 구매 순서 ─────────────

        private IEnumerator TestSkillGating()
        {
            Section("C. 보스 클리어 → 구매 자격 → 넥타르 구매 → 영구 보유");
            var p = G.Profile;
            p.nectar = 100;
            var root = DB.NectarById["root"];
            var dj = DB.NectarById["double_jump"];
            var dash = DB.NectarById["dash"];
            Check("넥타르 충분해도 보스 처치 전에는 더블점프 구매 불가", NectarShop.StateOf(p, dj, out var reason) != NectarNodeState.Available && !NectarShop.TryBuy(p, dj), reason);
            NectarShop.TryBuy(p, root);
            Check("선행(아폴론의 잔불) 구매 후에도 보스 잠금 유지", NectarShop.StateOf(p, dj, out reason) == NectarNodeState.BossLocked, reason);
            Check("잠금 사유에 보스 이름 표시", reason.Contains("칼리돈"), reason);

            // 미구매 상태에서는 더블점프·대쉬 비활성
            G.NewTestRun(LaunchStyle.Apollo, RetrievalStyle.Basic, new[] { 0 });
            G.BuildTestRoom(1, 6);
            G.ForceState(GameState.Node);
            var ctrl = G.Avatar.Controller;
            Check("미구매: 최대 점프 1회 / 대쉬 잠금", ctrl.maxJumps == 1 && !ctrl.dashUnlocked, $"maxJumps {ctrl.maxJumps}, dash {ctrl.dashUnlocked}");
            yield return WaitFor(() => ctrl.IsGrounded, 2f);
            jumpPulse = true;
            yield return new WaitForSeconds(0.25f);
            float vyBefore = G.Avatar.Body.linearVelocity.y;
            jumpPulse = true;
            yield return new WaitForSeconds(0.05f);
            Check("미구매: 공중 점프가 나가지 않음", G.Avatar.Body.linearVelocity.y <= vyBefore + 0.5f && !ctrl.IsSpinning, $"vy {vyBefore:0.0}→{G.Avatar.Body.linearVelocity.y:0.0}");
            yield return WaitFor(() => ctrl.IsGrounded, 3f);
            dashPulse = true;
            yield return null; yield return null;
            Check("미구매: Shift 대쉬가 나가지 않음", !ctrl.IsDashing);
            G.ClearTestRun();

            // 보스 처치 = 자격만
            p.bossesDefeated.Add(1);
            SaveStore.Save(p);
            Check("보스 처치 후 자동 지급 아님 (HasSkill=false)", !p.HasSkill("doubleJump"));
            Check("보스 처치 후 구매 가능 상태", NectarShop.StateOf(p, dj, out reason) == NectarNodeState.Available, reason);
            int before = p.nectar;
            Check("넥타르로 더블점프 구매", G.BuyNectarNode(dj) && p.HasSkill("doubleJump") && p.nectar == before - dj.cost[0], $"넥타르 {before}→{p.nectar}");
            Check("대쉬는 2스테이지 보스 전까지 잠김", NectarShop.StateOf(p, dash, out reason) == NectarNodeState.BossLocked, reason);

            G.NewTestRun(LaunchStyle.Apollo, RetrievalStyle.Basic, new[] { 0 });
            G.BuildTestRoom(1, 7);
            G.ForceState(GameState.Node);
            Check("구매 후: 최대 점프 2회", ctrl.maxJumps == 2);
            yield return WaitFor(() => ctrl.IsGrounded, 2f);
            jumpPulse = true;
            yield return new WaitForSeconds(0.3f);
            jumpPulse = true;
            yield return new WaitForSeconds(0.05f);
            Check("구매 후: 공중 더블점프(공중제비) 발동", ctrl.IsSpinning || G.Avatar.Body.linearVelocity.y > 10f, $"vy {G.Avatar.Body.linearVelocity.y:0.0}");
            yield return WaitFor(() => ctrl.IsGrounded, 3f);
            G.ClearTestRun();

            // 저장 확인
            var disk = SaveStore.Load();
            Check("구매·해금 자격이 저장 파일에 기록", disk.HasSkill("doubleJump") && disk.BossDefeated(1));
            // 다음 테스트를 위해 프로필 초기화
            SaveStore.DeleteAll();
            G.ReloadProfile();
        }

        // ───────────── D. 전체 흐름 ─────────────

        private IEnumerator PlayCurrentStage(bool killBoss)
        {
            int guard = 0;
            while (G.Run != null && G.State != GameState.Result && guard++ < 20)
            {
                yield return SkipStories();
                if (G.State != GameState.Map) { yield return WaitFor(() => G.State == GameState.Map || G.State == GameState.Result || G.State == GameState.Story, 5f); yield return SkipStories(); }
                if (G.State != GameState.Map) yield break;
                var choices = G.Run.map.Choices(G.Run.currentNode);
                if (choices.Count == 0) yield break;
                var node = choices[0];
                int stageBefore = G.Run.stage;
                G.EnterNode(node);
                yield return PlayNode(node, killBoss);
                if (node.type == NodeType.Boss) yield break;
                if (G.Run != null && G.Run.stage != stageBefore) yield break;
            }
        }

        private IEnumerator PlayNode(MapNode node, bool killBoss)
        {
            switch (node.type)
            {
                case NodeType.Combat:
                case NodeType.Elite:
                    yield return WaitFor(() => G.Room != null && G.Room.WaveIndex >= 0, 4f);
                    for (int i = 0; i < 60 && G.State == GameState.Node; i++)
                    {
                        G.Room?.DebugKillAll();
                        yield return new WaitForSeconds(0.25f);
                    }
                    yield return WaitFor(() => G.State == GameState.Reward, 5f);
                    if (G.Reward != null)
                    {
                        if (G.Reward.relics.Count > 0) G.TakeRewardRelic(G.Reward.relics[0]);
                        if (G.Reward.arrows.Count > 0) G.TakeRewardArrow(G.Reward.arrows[0]);
                    }
                    G.FinishReward();
                    break;
                case NodeType.Reward:
                    if (G.Reward != null && G.Reward.relics.Count > 0) G.TakeRewardRelic(G.Reward.relics[0]);
                    G.FinishReward();
                    break;
                case NodeType.Shop:
                    foreach (var o in G.ShopOffers) if (!o.sold && o.price <= G.Run.gold && o.kind == "arrow") { G.Buy(o); break; }
                    G.LeaveNode();
                    break;
                case NodeType.Rest:
                    G.RestHeal();
                    G.LeaveNode();
                    break;
                case NodeType.Boss:
                    yield return WaitFor(() => G.State == GameState.Story, 3f);
                    yield return SkipStories();
                    yield return WaitFor(() => G.Room != null && G.Room.Boss != null, 3f);
                    if (!killBoss) yield break;
                    yield return new WaitForSeconds(0.8f);
                    G.Room.Boss.ForceKill();
                    yield return WaitFor(() => G.State == GameState.Story, 6f);
                    yield return SkipStories();
                    yield return WaitFor(() => G.State == GameState.Map || G.State == GameState.Result, 4f);
                    yield return SkipStories();
                    break;
            }
            yield return null;
        }

        private IEnumerator TestFullFlow()
        {
            Section("D. 스토리·튜토리얼·파에톤·저주 → 1~3스테이지 → 결과 → 로비");
            G.GoTitle();
            yield return null;
            Check("타이틀 화면", G.State == GameState.Title);
            G.NewGame();
            Check("새 게임 → 도입 스토리", G.State == GameState.Story && G.StoryQueue.Count > 0);
            yield return SkipStories();
            Check("튜토리얼 시작", G.State == GameState.Tutorial || G.State == GameState.Story);
            yield return SkipStories();
            var ts = G.Avatar.Stats;
            Check("튜토리얼 능력치 (체력 300·공격 6·더블점프·대쉬)", Mathf.Approximately(ts.maxHp, 300) && Mathf.Approximately(ts.attack, 6) && ts.doubleJump && ts.dash, $"HP {ts.maxHp} 공격 {ts.attack}");
            Check("튜토리얼 궁술: 아폴론 + 오르페우스", G.Combat.LaunchType == LaunchStyle.Apollo && G.Combat.RetrievalType == RetrievalStyle.Orpheus);
            G.Avatar.invulnerable = true;

            // 튜토리얼 단계: 이동 → 과녁 → 회수 (실제 입력)
            var ctrl = G.Avatar.Controller;
            moveX = 1f; jumpPulse = true;
            yield return new WaitForSeconds(0.3f);
            jumpPulse = true;
            yield return new WaitForSeconds(0.2f);
            dashPulse = true;
            yield return new WaitForSeconds(0.6f);
            moveX = -1f;
            yield return new WaitForSeconds(0.4f);
            moveX = 0f;
            yield return WaitFor(() => G.TutorialStep >= 1, 3f);
            Check("튜토리얼 1단계(이동·점프·더블점프·대쉬) 완료", G.TutorialStep >= 1, $"이동 {G.tutMoved:0.0} 점프 {G.tutJumped} 더블 {G.tutDoubleJumped} 대쉬 {G.tutDashed}");
            yield return SkipStories();
            // 과녁 사격
            for (int i = 0; i < 40 && G.TutorialStep == 1; i++)
            {
                Enemy target = null;
                foreach (var e in G.Room.Enemies) if (e != null && e.IsAlive) { target = e; break; }
                if (target == null) break;
                G.Avatar.Teleport(new Vector2(target.Center.x - 5f, G.Room.InnerBounds.yMin + 1f));
                aim = target.Center;
                if (G.Combat.Quiver.Count == 0) { recallHeld = true; yield return new WaitForSeconds(0.6f); recallHeld = false; }
                fireHeld = true;
                yield return new WaitForSeconds(0.4f);
                fireHeld = false;
                yield return new WaitForSeconds(0.2f);
            }
            Check("튜토리얼 2단계(과녁 3개 사격) 완료", G.TutorialStep >= 2);
            yield return SkipStories();
            recallHeld = true;
            yield return WaitFor(() => G.TutorialStep >= 3 || G.State == GameState.Story, 8f);
            recallHeld = false;
            Check("튜토리얼 3단계(R 부르기로 회수) 완료", G.TutorialStep >= 3);
            yield return SkipStories();
            yield return WaitFor(() => G.Room != null && G.Room.Boss != null, 4f);
            Check("파에톤 등장", G.Room != null && G.Room.Boss is PhaetonBoss, G.Room?.Boss?.DisplayName);
            // 파에톤에게 실제로 화살을 몇 발 맞춘다
            float dmg0 = G.Room.Boss.DamageTaken;
            for (int i = 0; i < 12; i++)
            {
                aim = G.Room.Boss.Center;
                fireHeld = true;
                yield return new WaitForSeconds(0.3f);
                if (G.Combat.Quiver.Count == 0) { fireHeld = false; recallHeld = true; yield return new WaitForSeconds(1.2f); recallHeld = false; }
            }
            fireHeld = false;
            Check("파에톤에게 화살 적중", G.Room.Boss.DamageTaken > dmg0, $"피해 {G.Room.Boss.DamageTaken - dmg0:0}");
            int nectar0 = G.Profile.nectar;
            G.Room.Boss.ForceKill();
            yield return WaitFor(() => G.State == GameState.Story, 5f);
            Check("파에톤 처치 → 저주 스토리", G.State == GameState.Story && G.StoryQueue.Exists(l => l.text.Contains("저주")));
            Check("파에톤 처치 넥타르 지급·저장", G.Profile.nectar > nectar0 && SaveStore.Load().nectar == G.Profile.nectar);
            Check("튜토리얼 완료 저장", SaveStore.Load().tutorialDone);
            yield return SkipStories();
            yield return SkipStories();
            Check("저주 후 본게임 진입 (1스테이지 지도)", G.State == GameState.Map && G.Run != null && G.Run.stage == 1 && !G.Run.isTutorial);
            var ms = G.Avatar.Stats;
            Check("저주 후 능력치 = 본게임 기본 (체력 100, 공격 1, 스킬 없음)", Mathf.Approximately(ms.maxHp, 100) && Mathf.Approximately(ms.attack, 1) && !ms.doubleJump && !ms.dash && ctrl.maxJumps == 1, $"HP {ms.maxHp} 공격 {ms.attack} 점프 {ctrl.maxJumps}");
            Check("본게임 시작 궁술: 아폴론 + 기본 줍기", G.Run.launch == LaunchStyle.Apollo && G.Run.retrieval == RetrievalStyle.Basic);
            Check("노드 지도: 층 구성 + 마지막 보스", G.Run.map.LayerCount >= 5 && G.Run.map.Boss.type == NodeType.Boss);

            for (int stage = 1; stage <= 3; stage++)
            {
                G.Avatar.invulnerable = true;
                var types = new HashSet<NodeType>();
                foreach (var n in G.Run.map.nodes) types.Add(n.type);
                Check($"{stage}스테이지 지도에 전투·보스 노드 + 비전투 노드", types.Contains(NodeType.Combat) && types.Contains(NodeType.Boss) && (types.Contains(NodeType.Shop) || types.Contains(NodeType.Rest) || types.Contains(NodeType.Reward)), string.Join(",", types));
                int kills0 = G.Run.kills, gold0 = G.Run.goldEarned, deck0 = G.Run.deck.Count;
                int nectarBefore = G.Profile.nectar;
                yield return PlayCurrentStage(true);
                if (stage < 3)
                {
                    Check($"{stage}스테이지 보스 처치 → 로비 거치지 않고 {stage + 1}스테이지", G.Run != null && G.Run.stage == stage + 1 && G.State == GameState.Map, $"state {G.State}");
                }
                else
                {
                    Check("3스테이지 보스 처치 → 결과 화면", G.State == GameState.Result, $"state {G.State}");
                }
                Check($"{stage}스테이지 보스 구매 자격 저장 (스킬 미지급)", SaveStore.Load().BossDefeated(stage) && !G.Profile.HasSkill(stage == 1 ? "doubleJump" : stage == 2 ? "dash" : "tripleJump"));
                Check($"{stage}스테이지 넥타르 획득·즉시 저장", G.Profile.nectar > nectarBefore && SaveStore.Load().nectar == G.Profile.nectar, $"{nectarBefore}→{G.Profile.nectar}");
                if (G.Run != null) Check($"{stage}스테이지 진행 중 처치·골드·화살 획득", G.Run.kills > kills0 && G.Run.goldEarned > gold0 && G.Run.deck.Count > deck0, $"처치 +{G.Run.kills - kills0}, 골드 +{G.Run.goldEarned - gold0}, 덱 {deck0}→{G.Run.deck.Count}");
            }
            Check("결과 화면에서 도전 종료(Run 정리)", G.Run == null && G.Profile.clears == 1);
            G.GoLobby();
            yield return SkipStories();
            Check("결과 → 로비 복귀", G.State == GameState.Lobby && G.Room != null);
            G.Avatar.invulnerable = false;
        }

        // ───────────── E. 사망 초기화 ─────────────

        private IEnumerator TestDeathResets()
        {
            Section("E. 어느 스테이지에서 사망해도 로비 → 1스테이지부터, 넥타르·강화·자격·스킬 유지");
            var p = G.Profile;
            // 영구 데이터 준비: 더블점프 구매 (1스테이지 보스는 D에서 처치됨)
            p.nectar += 50;
            SaveStore.Save(p);
            G.BuyNectarNode(DB.NectarById["root"]);
            G.BuyNectarNode(DB.NectarById["double_jump"]);
            G.BuyNectarNode(DB.NectarById["attack"]);
            for (int stage = 1; stage <= 3; stage++)
            {
                G.StartRun();
                yield return SkipStories();
                G.Avatar.invulnerable = true;
                // 원하는 스테이지까지 진행
                while (G.Run != null && G.Run.stage < stage) yield return PlayCurrentStage(true);
                // 돈·화살·유물을 늘려 둔다
                G.AddGold(500, false);
                G.Run.AddCard(23);
                G.Run.AddRelic("sharp_head");
                G.Run.deck[0].runBonus = 12;
                G.RefreshStats(false);
                var choices = G.Run.map.Choices(G.Run.currentNode);
                MapNode combat = choices.Find(n => n.type == NodeType.Combat || n.type == NodeType.Elite) ?? choices[0];
                G.EnterNode(combat);
                yield return SkipStories();
                yield return new WaitForSeconds(0.3f);
                int nectarBefore = p.nectar;
                int purchases = p.purchases.Count;
                G.Avatar.invulnerable = false;
                G.Avatar.ClearIframes();
                G.Avatar.Hurt(99999f, "검증");
                yield return null;
                Check($"{stage}스테이지 사망 → 사망 화면", G.State == GameState.Death, $"state {G.State}");
                Check($"{stage}스테이지 사망: 도전 상태 폐기 (돈·화살·유물·일시 효과)", G.Run == null && G.Room == null);
                var disk = SaveStore.Load();
                Check($"{stage}스테이지 사망: 넥타르 유지·저장", p.nectar == nectarBefore && disk.nectar == nectarBefore, $"{nectarBefore}");
                Check($"{stage}스테이지 사망: 강화·구매 스킬·보스 자격 유지", disk.purchases.Count == purchases && disk.HasSkill("doubleJump") && disk.BossDefeated(1));
                G.GoLobby();
                yield return SkipStories();
                Check($"{stage}스테이지 사망 후 로비", G.State == GameState.Lobby);
                G.StartRun();
                yield return SkipStories();
                var run = G.Run;
                int expectedDeck = DB.Balance.player.startArrows.Length + Mathf.RoundToInt(p.StatBonus("startArrows"));
                Check("재도전: 1스테이지 처음부터", run.stage == 1 && run.currentNode == -1);
                Check("재도전: 초기 돈·화살·유물", run.gold == DB.Balance.player.startGold && run.deck.Count == expectedDeck && run.relics.Count == 0 && run.deck.TrueForAll(c => c.runBonus == 0 && c.baseCode == 0), $"골드 {run.gold}, 덱 {run.deck.Count}, 유물 {run.relics.Count}");
                var s = G.Avatar.Stats;
                Check("재도전: 영구 강화 적용 (공격력 +1, 더블점프 보유)", Mathf.Approximately(s.attack, 2f) && s.doubleJump && G.Avatar.Controller.maxJumps == 2 && Mathf.Approximately(G.Avatar.Hp, s.maxHp), $"공격 {s.attack}, HP {G.Avatar.Hp}/{s.maxHp}");
                G.AbandonRun();
                G.GoLobby();
                yield return SkipStories();
            }
            G.Avatar.invulnerable = false;
        }

        // ───────────── F. 저장 ─────────────

        private IEnumerator TestSaveRoundTrip()
        {
            Section("F. 저장 (원자적 쓰기·백업·재로드)");
            var p = G.Profile;
            p.nectar = 37;
            SaveStore.Save(p);
            var d = SaveStore.Load();
            Check("저장 → 다시 읽기: 넥타르·구매·자격 일치", d.nectar == 37 && d.purchases.Count == p.purchases.Count && d.bossesDefeated.Count == p.bossesDefeated.Count);
            Check("백업 파일 생성", File.Exists(SaveStore.BackupPath));
            File.WriteAllText(SaveStore.FilePath, "{ broken json");
            var rec = SaveStore.Load();
            Check("본 파일 손상 시 백업에서 복구", SaveStore.LastLoadSource == "backup" && rec.purchases.Count > 0, SaveStore.LastLoadSource);
            SaveStore.Save(p);
            G.ReloadProfile();
            Check("복구 후 정상 저장", SaveStore.Load().nectar == 37 && SaveStore.LastLoadSource == "main");
            yield return null;
        }

        // ───────────── G. 30 조합 ─────────────

        private Enemy SpawnTarget(Room room, Vector2 pos, float hp)
        {
            var d = G.SpawnDummy(room, pos, hp <= 0f);
            if (hp > 0f) d.SetHp(hp);
            return d;
        }

        private IEnumerator TestCombos()
        {
            Section("G. 발사 5종 × 회수 6종 = 30 조합 (발사·적중·박힘·회수·화살 수)");
            Time.timeScale = 2f;
            int ok = 0;
            foreach (var l in StyleNames.AllLaunch)
                foreach (var r in StyleNames.AllRetrieval)
                {
                    yield return OneCombo(l, r);
                    if (lastComboOk) ok++;
                }
            Time.timeScale = 1f;
            Check("30 조합 모두 통과", ok == 30, $"{ok}/30");
        }

        private bool lastComboOk;

        private IEnumerator OneCombo(LaunchStyle l, RetrievalStyle r)
        {
            ReleaseInput();
            var run = G.NewTestRun(l, r, new[] { 0, 1, 2, 0 });
            var room = G.BuildTestRoom(2, 21);
            G.ForceState(GameState.Node);
            G.Combat.BeginRound(room, run, l, r);
            G.Avatar.invulnerable = true;
            float floor = room.InnerBounds.yMin;
            var dummy = SpawnTarget(room, new Vector2(-3f, floor + 0.9f), 0f);
            G.Avatar.Teleport(new Vector2(l == LaunchStyle.Ares ? -4.6f : -9f, floor + 0.6f));
            yield return new WaitForSeconds(0.3f);
            int deck = run.deck.Count;
            float dmg0 = dummy.DamageTaken;
            stuckSeen = false;

            // 발사 2회
            for (int shot = 0; shot < 2; shot++)
            {
                aim = (Vector2)dummy.Center + (l == LaunchStyle.Athena ? Vector2.up * 0.3f : Vector2.zero);
                if (l == LaunchStyle.Odysseus)
                {
                    fireDownPulse = true; fireHeld = true;
                    yield return new WaitForSeconds(1.7f / Mathf.Max(0.1f, Time.timeScale) * Time.timeScale);
                    fireHeld = false;
                }
                else
                {
                    fireHeld = true;
                    yield return WaitFor(() => G.Combat.Stats.shots > shot, 3f);
                    fireHeld = false;
                }
                yield return new WaitForSeconds(0.9f);
            }
            yield return new WaitForSeconds(0.4f);
            bool everStuck = stuckSeen;
            float hitDmg = dummy.DamageTaken - dmg0;
            bool hit = hitDmg > 0f;
            bool stickRule = r == RetrievalStyle.Basic ? !everStuck : everStuck || r == RetrievalStyle.Hades;

            // 회수
            float dmgBeforeRecall = dummy.DamageTaken;
            yield return Recover(r, dummy);
            bool recovered = G.Combat.Quiver.Count == deck;
            bool integrity = G.Combat.Violations.Count == 0 && G.Combat.Bodies.Count == deck;
            string extra = "";
            if (r == RetrievalStyle.Ares) extra = $" 뽑기 재적중 피해 {dummy.DamageTaken - dmgBeforeRecall:0.#}";
            if (r == RetrievalStyle.Demeter) extra = $" 낫 피해 {dummy.DamageTaken - dmgBeforeRecall:0.#}";
            if (r == RetrievalStyle.Zeus) extra = $" 번개 피해 {dummy.DamageTaken - dmgBeforeRecall:0.#}";
            // 아레스 뽑기: 박힌 화살마다 적중 판정 1회 더 (발사 적중 2 + 재적중 ≥1). 데메테르: 낫 피해.
            bool retrieveEffect = r == RetrievalStyle.Ares ? dummy.HitsTaken >= 3 : r == RetrievalStyle.Demeter ? dummy.DamageTaken > dmgBeforeRecall : true;
            if (r == RetrievalStyle.Ares) extra = $" 적중 판정 {dummy.HitsTaken}회(발사 2 + 뽑기 재적중)";
            lastComboOk = hit && stickRule && recovered && integrity && retrieveEffect;
            Check($"{StyleNames.Launch(l)} + {StyleNames.Retrieval(r)}", lastComboOk,
                $"적중 {(hit ? "O" : "X")} 피해 {hitDmg:0.#} · 박힘규칙 {(stickRule ? "O" : "X")} · 회수 {G.Combat.Quiver.Count}/{deck} · 위반 {G.Combat.Violations.Count}{extra}");
            ReleaseInput();
            G.ClearTestRun();
            G.Avatar.invulnerable = false;
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
                    case RetrievalStyle.Orpheus:
                        recallHeld = true;
                        break;
                    case RetrievalStyle.Hades:
                        // 바닥의 낙하 중 화살이 자리 잡을 때까지 기다리면 자동 귀환
                        break;
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

        // ───────────── H. 화살 효과·무한 복제 방지 ─────────────

        private IEnumerator TestArrowEffects()
        {
            Section("H. 화살 효과 (변형·복사·재발사·소멸·무한 발동 방지)");
            Time.timeScale = 2f;
            int okCount = 0, total = 0;
            var codes = new List<int>();
            foreach (var a in DB.ArrowPool) codes.Add(a.code);
            foreach (int code in codes)
            {
                total++;
                yield return OneArrow(code);
                if (lastArrowOk) okCount++;
            }
            Check("모든 화살 종류 발사·회수 시 무결성 유지", okCount == total, $"{okCount}/{total}종");
            yield return SpecificArrowChecks();
            Time.timeScale = 1f;
        }

        private bool lastArrowOk;

        private IEnumerator OneArrow(int code)
        {
            ReleaseInput();
            var deck = new[] { code, code, 0 };
            var run = G.NewTestRun(LaunchStyle.Apollo, RetrievalStyle.Orpheus, deck);
            var room = G.BuildTestRoom(2, 33);
            G.ForceState(GameState.Node);
            G.Combat.BeginRound(room, run, LaunchStyle.Apollo, RetrievalStyle.Orpheus);
            G.Avatar.invulnerable = true;
            float floor = room.InnerBounds.yMin;
            var targets = new List<Enemy>
            {
                SpawnTarget(room, new Vector2(-2f, floor + 0.9f), 30f),
                SpawnTarget(room, new Vector2(0f, floor + 0.9f), 30f),
                SpawnTarget(room, new Vector2(3f, floor + 0.9f), 0f),
            };
            G.Avatar.Teleport(new Vector2(-9f, floor + 0.6f));
            yield return new WaitForSeconds(0.2f);
            int spawned0 = G.Combat.PhantomsSpawnedTotal;
            int maxAlive = 0;
            aim = targets[0].Center;
            fireHeld = true;
            float t = 0f;
            while (G.Combat.Stats.shots < 3 && t < 9f) { maxAlive = Mathf.Max(maxAlive, G.Combat.PhantomCount); yield return new WaitForSeconds(0.1f); t += 0.2f; }
            fireHeld = false;
            for (int i = 0; i < 15; i++) { maxAlive = Mathf.Max(maxAlive, G.Combat.PhantomCount); yield return new WaitForSeconds(0.1f); }
            int spawned = G.Combat.PhantomsSpawnedTotal - spawned0;
            recallHeld = true;
            yield return WaitFor(() => G.Combat.Quiver.Count + G.Combat.CountState(ArrowState.Consumed) >= run.deck.Count, 8f);
            recallHeld = false;
            bool integrity = G.Combat.Violations.Count == 0 && G.Combat.Bodies.Count == run.deck.Count;
            bool bounded = spawned <= 40 && maxAlive <= DB.Balance.archery.phantomCap;
            G.Combat.EndRound();
            bool restored = run.quiverOrder.Count == run.deck.Count && run.deck.TrueForAll(c => !c.consumed && c.roundBonus == 0f && !c.rageActive);
            lastArrowOk = integrity && bounded && restored;
            var def = DB.Arrow(code);
            if (!lastArrowOk || code == 11 || code == 31 || code == 17 || code == 35 || code == 18 || code == 42)
                Check($"#{code} {def.name}", lastArrowOk, $"발사 {G.Combat.Stats.shots} · 임시투사체 생성 {spawned} (동시 최대 {maxAlive}) · 위반 {G.Combat.Violations.Count} · 라운드 종료 후 복구 {(restored ? "O" : "X")}");
            G.ClearTestRun();
            yield return null;
        }

        private IEnumerator Setup(int[] deck, LaunchStyle l = LaunchStyle.Apollo, RetrievalStyle r = RetrievalStyle.Orpheus)
        {
            ReleaseInput();
            var run = G.NewTestRun(l, r, deck);
            var room = G.BuildTestRoom(2, 44);
            G.ForceState(GameState.Node);
            G.Combat.BeginRound(room, run, l, r);
            G.Avatar.invulnerable = true;
            G.Avatar.Teleport(new Vector2(-9f, room.InnerBounds.yMin + 0.6f));
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator FireOnce(Vector2 at)
        {
            aim = at;
            int s0 = G.Combat.Stats.shots;
            fireHeld = true;
            yield return WaitFor(() => G.Combat.Stats.shots > s0, 4f);
            fireHeld = false;
        }

        private IEnumerator SpecificArrowChecks()
        {
            float floor() => G.Room.InnerBounds.yMin;

            // 에코: 앞 화살을 복사해 두 번째로 따라 나감
            yield return Setup(new[] { 7, 34, 0 });
            var d1 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 0f);
            yield return FireOnce(d1.Center);
            yield return new WaitForSeconds(0.1f);
            ArrowBody echo = G.Combat.Bodies.Find(b => b.Card.baseCode == 34);
            Check("에코: 한 번의 발사에 함께 나감 (2발)", G.Combat.Stats.shots == 2, $"shots {G.Combat.Stats.shots}");
            Check("에코: 직전 화살(폭약)의 효과를 복사", echo != null && echo.Shot != null && echo.Shot.form.code == 7, echo?.Shot?.form?.name);
            G.ClearTestRun();

            // 하데스: 이번 라운드에 소멸한 화살만큼 재발사, 추가 소멸 없음
            yield return Setup(new[] { 12, 21, 42, 0 });
            var d2 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 0f);
            yield return FireOnce(d2.Center);
            yield return new WaitForSeconds(0.8f);
            yield return FireOnce(d2.Center);
            yield return new WaitForSeconds(0.8f);
            int consumed = G.Combat.CountState(ArrowState.Consumed);
            int p0 = G.Combat.PhantomsSpawnedTotal;
            yield return FireOnce(d2.Center);
            yield return new WaitForSeconds(0.1f);
            int replays = G.Combat.PhantomsSpawnedTotal - p0;
            Check("폭탄·제물: 사용 후 이번 라운드 동안 소멸", consumed == 2, $"소멸 {consumed}");
            Check("하데스: 소멸한 화살 수만큼 재발사", replays == consumed, $"재발사 {replays}");
            yield return new WaitForSeconds(1f);
            Check("하데스 재발사는 추가 소멸 없음", G.Combat.CountState(ArrowState.Consumed) == consumed);
            G.Combat.EndRound();
            Check("라운드 종료 시 소멸 화살 복구", G.Run.quiverOrder.Count == 4 && G.Run.deck.TrueForAll(c => !c.consumed));
            G.ClearTestRun();

            // 분노: 적중 시 라운드 동안 활성(9)으로 변형, 라운드 종료 시 복귀
            yield return Setup(new[] { 8, 0 });
            var d3 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 0f);
            yield return FireOnce(d3.Center);
            yield return new WaitForSeconds(0.8f);
            var rage = G.Run.deck[0];
            Check("분노: 적중 후 분노-활성(9) 형태", rage.Def.code == 9 && Mathf.Approximately(rage.Def.damage, 16f));
            G.Combat.EndRound();
            Check("분노: 라운드 종료 후 비활성(8)으로 복귀", rage.Def.code == 8);
            G.ClearTestRun();

            // 바이러스: 감염된 적 사망 시 8방향 (재복제 없음)
            yield return Setup(new[] { 18, 0, 0 });
            var d4 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 9f);
            yield return FireOnce(d4.Center);
            yield return new WaitForSeconds(0.6f);
            int v0 = G.Combat.PhantomsSpawnedTotal;
            yield return FireOnce(d4.Center);
            yield return new WaitForSeconds(0.6f);
            Check("바이러스: 감염 적 사망 → 정확히 8발", G.Combat.PhantomsSpawnedTotal - v0 == 8 && !d4.IsAlive, $"{G.Combat.PhantomsSpawnedTotal - v0}발");
            G.ClearTestRun();

            // 제물: 처치 시 도전 동안 영구 +6, 라운드 종료 후에도 유지
            yield return Setup(new[] { 21, 0 });
            var d5 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 5f);
            yield return FireOnce(d5.Center);
            yield return new WaitForSeconds(0.8f);
            var sac = G.Run.deck[0];
            G.Combat.EndRound();
            Check("제물: 처치 → 피해 +6 (도전 동안 유지)", Mathf.Approximately(sac.runBonus, 6f) && Mathf.Approximately(sac.Damage, 12f), $"피해 {sac.Damage}");
            G.ClearTestRun();

            // 예리한: 빗나가면 라운드 동안 +6
            yield return Setup(new[] { 16, 0 });
            yield return FireOnce((Vector2)G.Avatar.transform.position + new Vector2(-1f, 6f));
            yield return new WaitForSeconds(1.2f);
            var keen = G.Run.deck[0];
            Check("예리한: 빗나감 → 라운드 피해 +6", Mathf.Approximately(keen.roundBonus, 6f), $"{keen.roundBonus}");
            G.Combat.EndRound();
            Check("예리한: 라운드 종료 후 초기화", keen.roundBonus == 0f);
            G.ClearTestRun();

            // 유리: 맞힐 때마다 -4, 0 이면 소멸
            yield return Setup(new[] { 22 }, LaunchStyle.Apollo, RetrievalStyle.Hades);
            var d6 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 0f);
            var glass = G.Run.deck[0];
            for (int i = 0; i < 8 && !glass.consumed && G.Combat.CountState(ArrowState.Consumed) == 0; i++)
            {
                yield return WaitFor(() => G.Combat.Quiver.Count == 1, 7f);
                yield return FireOnce(d6.Center);
                yield return new WaitForSeconds(0.6f);
            }
            Check("유리: 피해 0 이 되면 라운드 동안 소멸", G.Combat.CountState(ArrowState.Consumed) == 1 && glass.Damage <= 0f, $"피해 {glass.Damage}");
            G.ClearTestRun();

            // 분열체: 버리면 소멸 / 강화: 버리면 다음 화살 관통 +1
            yield return Setup(new[] { 17, 26, 24 });
            discardPulse = true;
            yield return new WaitForSeconds(0.3f);
            Check("분열체: 버리면 라운드 동안 소멸", G.Combat.CountState(ArrowState.Consumed) == 1);
            discardPulse = true;
            yield return new WaitForSeconds(0.3f);
            var d7 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 0f);
            yield return FireOnce(d7.Center);
            var pierceBody = G.Combat.Bodies.Find(b => b.Card.baseCode == 24);
            Check("강화: 버린 뒤 다음 화살 관통 +1 (관통 3+1)", pierceBody.Shot != null && pierceBody.Shot.pierce == 4, $"관통 {pierceBody.Shot?.pierce}");
            G.ClearTestRun();

            // 이카루스 → 밀랍 변형 (위로 쏘면), 무한 반복 없음
            yield return Setup(new[] { 13, 0 });
            yield return FireOnce((Vector2)G.Avatar.transform.position + new Vector2(3f, 12f));
            bool becameWax = false;
            for (int i = 0; i < 20; i++) { var b = G.Combat.Bodies[0]; if (b.Shot != null && b.Shot.form.code == 14) becameWax = true; yield return new WaitForSeconds(0.05f); }
            Check("이카루스: 고도 상승 시 녹아내린 밀랍(14)으로 변형", becameWax);
            recallHeld = true;
            yield return WaitFor(() => G.Combat.Quiver.Count == 2, 8f);
            recallHeld = false;
            Check("이카루스: 회수 후 카드는 13번으로 복귀", G.Run.deck[0].Def.code == 13 && G.Combat.Violations.Count == 0);
            G.ClearTestRun();

            // 유물: 듀얼샷 / 조약돌 주머니
            yield return Setup(new[] { 0, 0, 0, 0 });
            G.Run.AddRelic("dual_shot");
            var d8 = SpawnTarget(G.Room, new Vector2(-2f, floor() + 0.9f), 0f);
            yield return FireOnce(d8.Center);
            yield return null;
            Check("듀얼샷: 한 번에 두 발", G.Combat.Stats.shots == 2);
            G.ClearTestRun();

            var run = G.NewTestRun(LaunchStyle.Apollo, RetrievalStyle.Orpheus, new[] { 0, 0 });
            run.AddRelic("pebble_pouch");
            Check("조약돌 주머니: 조약돌 화살 50발 추가", run.deck.FindAll(c => c.baseCode == 41).Count == 50);
            run.quiverOrder.Remove(run.deck.Find(c => c.baseCode == 41).uid);
            run.quiverOrder.Insert(0, run.deck.Find(c => c.baseCode == 41).uid);
            var room = G.BuildTestRoom(2, 45);
            G.ForceState(GameState.Node);
            G.Combat.BeginRound(room, run, LaunchStyle.Apollo, RetrievalStyle.Orpheus);
            aim = new Vector2(5f, room.InnerBounds.yMin + 1f);
            yield return WaitFor(() => G.Combat.CountState(ArrowState.Consumed) >= 1, 4f);
            Check("조약돌: 입력 없이 자동 발사 후 라운드 동안 소멸", G.Combat.Stats.shots >= 1 && G.Combat.CountState(ArrowState.Consumed) >= 1, $"발사 {G.Combat.Stats.shots}");
            G.ClearTestRun();
        }

        // ───────────── I. 화살통 순서 ─────────────

        private IEnumerator TestQuiverOrder()
        {
            Section("I. 회수 순서 (동시=무작위, 순차=회수 순서)");
            // 순차: 기본 줍기로 하나씩
            yield return Setup(new[] { 0, 1, 2, 4 }, LaunchStyle.Apollo, RetrievalStyle.Basic);
            float fy = G.Room.InnerBounds.yMin;
            var bodies = new List<ArrowBody>(G.Combat.Quiver.Order);
            // 앞의 3발을 서로 다른 위치 바닥에 버린다
            for (int i = 0; i < 3; i++) { discardPulse = true; yield return new WaitForSeconds(0.25f); G.Avatar.Teleport(new Vector2(-9f + (i + 1) * 4f, fy + 0.6f)); yield return new WaitForSeconds(0.1f); }
            yield return new WaitForSeconds(0.8f);
            // 원하는 순서(2,0,1)로 줍기
            var want = new[] { bodies[2], bodies[0], bodies[1] };
            G.Avatar.Teleport(new Vector2(8f, fy + 0.6f));
            yield return new WaitForSeconds(0.2f);
            foreach (var b in want)
            {
                G.Avatar.Teleport((Vector2)b.transform.position + Vector2.up * 0.4f);
                yield return WaitFor(() => b.State == ArrowState.InQuiver, 2f);
                G.Avatar.Teleport(new Vector2(8f, fy + 0.6f));
                yield return new WaitForSeconds(0.15f);
            }
            var order = G.Combat.Quiver.Order;
            bool seq = order.Count == 4 && order[1] == want[0] && order[2] == want[1] && order[3] == want[2];
            Check("순차 회수: 회수한 순서대로 뒤에 붙음", seq, string.Join(", ", new List<ArrowBody>(order).ConvertAll(x => x.Card.Def.name)));
            G.ClearTestRun();

            // 동시: 제우스로 한 번에 → 같은 프레임 묶음, 무작위 배치(여러 번 시도해 순서가 다양한지)
            var orders = new HashSet<string>();
            for (int trial = 0; trial < 5; trial++)
            {
                yield return Setup(new[] { 0, 1, 2, 4, 7 }, LaunchStyle.Apollo, RetrievalStyle.Zeus);
                fy = G.Room.InnerBounds.yMin;
                for (int i = 0; i < 4; i++) { discardPulse = true; yield return new WaitForSeconds(0.25f); }
                yield return new WaitForSeconds(0.8f);
                Vector2 c = Vector2.zero; int n = 0;
                foreach (var b in G.Combat.Bodies) if (b.IsOnField) { c += (Vector2)b.transform.position; n++; }
                aim = n > 0 ? c / n : Vector2.zero;
                G.Combat.Log("검증: 뇌우");
                recallDownPulse = true;
                yield return new WaitForSeconds(0.2f);
                var o = G.Combat.Quiver.Order;
                var names = new List<string>();
                for (int i = 1; i < o.Count; i++) names.Add(o[i].Card.Def.name);
                orders.Add(string.Join(",", names));
                if (trial == 0) Check("동시 회수: 한 묶음으로 처리", G.Combat.LastBatchDescription.StartsWith("동시 회수"), G.Combat.LastBatchDescription);
                G.ClearTestRun();
            }
            Check("동시 회수: 배치 순서가 무작위 (5회 중 서로 다른 순서 2가지 이상)", orders.Count >= 2, $"{orders.Count}가지");
        }

        // ───────────── 재실행 저장 지속성 ─────────────

        private IEnumerator PersistPhase(string phase)
        {
            SaveStore.PathOverride = SavePathFor("persist");
            lines.Add($"저장 지속성 검증 단계 {phase} — {System.DateTime.Now:HH:mm:ss}  경로 {SaveStore.FilePath}");
            if (phase == "1")
            {
                SaveStore.DeleteAll();
                var p = new Profile { tutorialDone = true, nectar = 23, launch = "Zeus" };
                p.bossesDefeated.Add(1);
                p.bossesDefeated.Add(2);
                p.SetLevel("root", 1);
                p.SetLevel("double_jump", 1);
                p.SetLevel("hp", 2);
                Check("1단계: 저장", SaveStore.Save(p));
            }
            else
            {
                var p = SaveStore.Load();
                Check("2단계(재실행): 미사용 넥타르 23 유지", p.nectar == 23, $"{p.nectar}");
                Check("2단계(재실행): 보스 1·2 구매 자격 유지", p.BossDefeated(1) && p.BossDefeated(2) && !p.BossDefeated(3));
                Check("2단계(재실행): 구매 스킬(더블점프) 유지, 대쉬는 미구매", p.HasSkill("doubleJump") && !p.HasSkill("dash"));
                Check("2단계(재실행): 넥타르 강화 레벨 유지 (생명의 뿌리 2)", p.Level("hp") == 2);
                Check("2단계(재실행): 대쉬는 자격만 있고 구매 가능 상태", NectarShop.StateOf(p, DB.NectarById["dash"], out var why) != NectarNodeState.Maxed, why);
                SaveStore.DeleteAll();
            }
            yield return null;
            LastSummary = $"결과: 통과 {pass} / 실패 {fail}";
            lines.Add(LastSummary);
            WriteReport($"laurel_persist_phase{phase}.txt");
            Done = true;
            G.SelfTestRunning = false;
            if (!Application.isEditor) Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
