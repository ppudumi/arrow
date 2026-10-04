using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Procedural2D;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Archery
{
    /// <summary>
    /// 빌드 안에서 실제 게임 루프로 궁술을 검증하는 자동 테스트.
    /// - 로비의 '자동 검증 실행' 버튼, 또는 실행 인자 -archerySelfTest 로 시작한다.
    /// - 궁술 입력은 ArcheryCombat.InputOverride 로, 이동 입력은 Input System 가상 키 이벤트로 넣는다.
    /// - 결과: persistentDataPath/archery_selftest_report.txt (+ -archerySelfTestOut 경로)
    /// </summary>
    public class ArcherySelfTest : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string Progress { get; private set; } = "";
        public static string LastSummary { get; private set; } = "";
        public static string LastReportPath { get; private set; } = "";

        private const float TimeScale = 2f;

        private ArcheryTestFlow flow;
        private bool quitWhenDone;
        private ArcheryInput sim;
        private readonly List<string> lines = new List<string>();
        private int pass, fail;
        private string screenshotDir;
        private int shotIndex;

        /// <summary>도메인 리로드를 끈 Play 모드 설정에서도 이전 실행의 상태가 남지 않게 한다</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Running = false;
            Progress = "";
        }

        public static bool RequestedFromCommandLine() => Array.IndexOf(Environment.GetCommandLineArgs(), "-archerySelfTest") >= 0;

        private static string ArgValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static void Launch(ArcheryTestFlow flow, bool quitWhenDone)
        {
            if (Running) return;
            var t = flow.gameObject.AddComponent<ArcherySelfTest>();
            t.flow = flow;
            t.quitWhenDone = quitWhenDone;
        }

        private IEnumerator Start()
        {
            Running = true;
            Application.runInBackground = true;
            screenshotDir = ArgValue("-archeryScreenshots");
            if (!string.IsNullOrEmpty(screenshotDir)) Directory.CreateDirectory(screenshotDir);
#if ENABLE_INPUT_SYSTEM
            var prevBackground = InputSystem.settings.backgroundBehavior;
            var prevEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            // 에디터: Game 뷰에 포커스가 없어도 가상 키 입력이 게임으로 전달되게 한다 (테스트 후 복원)
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
#endif
            float prevScale = Time.timeScale;
            var prevLaunch = flow.SelectedLaunch;
            var prevRetrieval = flow.SelectedRetrieval;
            Time.timeScale = TimeScale;
            lines.Add($"궁술 테스트 빌드 자동 검증 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Unity {Application.unityVersion} / {(Application.isEditor ? "에디터 Play 모드" : "플레이어")} {Application.platform} / timeScale {TimeScale}");
            lines.Add("");

            if (flow.Current == ArcheryTestFlow.FlowScreen.Lobby)
            {
                yield return null;
                Screenshot("lobby");
                yield return null;
            }
            LobbyOrderCheck();
            yield return Run("A. 30가지 조합 기본 동작", AllCombinations());
            yield return Run("B. 기존 이동·애니메이션", MovementAndAnimation());
            yield return Run("C. 찌르기 빗나감 시 화살 미소모", AresMissDoesNotConsume());
            yield return Run("D. 뽑기 재적중 정확히 1회", PullRetriggersExactlyOnce());
            yield return Run("E. 자동 회수가 비행 중 화살을 가져오지 않음", AutoRecallIgnoresFlying());
            yield return Run("F. 동시/순차 회수 화살통 배치", QuiverOrdering());
            yield return Run("G. 중복 회수·중복 피해 방지", NoDuplicateRecoveryOrDamage());
            yield return Run("H. 발사 간격·공격속도·연사 버프", IntervalsAndHermes());
            yield return Run("I. 아테나 무게별 궤적", AthenaWeight());
            yield return Run("J. 오디세우스 충전·피해 계산", OdysseusCharge());
            yield return Run("K. 오르페우스 거리별 귀환·R 놓기", OrpheusBehaviour());
            yield return Run("L. 제우스 번개·재사용 대기", ZeusBehaviour());
            yield return Run("M. 데메테르 낫 피해·간격", DemeterBehaviour());
            yield return Run("N. 초기화·로비 복귀 정리", ResetAndLobbyCleanup());

            Time.timeScale = prevScale;
            ReleaseAllKeys();
#if ENABLE_INPUT_SYSTEM
            InputSystem.settings.backgroundBehavior = prevBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = prevEditorBehavior;
#endif
            if (flow.Current == ArcheryTestFlow.FlowScreen.Battle) flow.ReturnToLobby();
            flow.LoadoutOverride = null;
            flow.SelectedLaunch = prevLaunch;
            flow.SelectedRetrieval = prevRetrieval;

            lines.Add("");
            lines.Add($"결과: 통과 {pass} / 실패 {fail} / 전체 {pass + fail}");
            string report = string.Join("\n", lines);
            try
            {
                LastReportPath = Path.Combine(Application.persistentDataPath, "archery_selftest_report.txt");
                File.WriteAllText(LastReportPath, report, Encoding.UTF8);
                string extra = ArgValue("-archerySelfTestOut");
                if (!string.IsNullOrEmpty(extra)) File.WriteAllText(extra, report, Encoding.UTF8);
            }
            catch (Exception e) { Debug.LogError("[ArcherySelfTest] 보고서 저장 실패: " + e.Message); }
            Debug.Log("[ArcherySelfTest]\n" + report);

            var sb = new StringBuilder();
            sb.Append(fail == 0 ? $"<color=#7fff9f><b>자동 검증 통과 {pass}/{pass + fail}</b></color>" : $"<color=#ff7070><b>자동 검증 실패 {fail}건</b> (통과 {pass}/{pass + fail})</color>");
            sb.Append($"\n보고서: {LastReportPath}");
            int shown = 0;
            foreach (var l in lines)
            {
                if (l.StartsWith("  [FAIL]") && shown < 6) { sb.Append("\n<color=#ff9090>").Append(l.Trim()).Append("</color>"); shown++; }
            }
            LastSummary = sb.ToString();
            Running = false;
            Progress = "";
            if (quitWhenDone)
            {
                yield return null;
                Application.Quit(fail == 0 ? 0 : 1);
            }
            Destroy(this);
        }

        private IEnumerator Run(string name, IEnumerator body)
        {
            Progress = name;
            lines.Add("■ " + name);
            int failBefore = fail;
            // 예외가 나도 다음 섹션을 계속 진행
            while (true)
            {
                object current;
                try
                {
                    if (!body.MoveNext()) break;
                    current = body.Current;
                }
                catch (Exception e)
                {
                    Check(false, "예외 없이 실행", e.GetType().Name + ": " + e.Message);
                    break;
                }
                yield return current;
            }
            if (flow.Combat != null && flow.Combat.Violations.Count > 0)
            {
                Check(false, "무결성 위반 없음", string.Join(" | ", flow.Combat.Violations));
            }
            lines.Add(fail == failBefore ? "  → 섹션 통과" : $"  → 섹션 실패 {fail - failBefore}건");
            ReleaseAllKeys();
        }

        private void Check(bool ok, string name, string detail = "")
        {
            if (ok) pass++; else fail++;
            lines.Add($"  [{(ok ? "PASS" : "FAIL")}] {name}" + (string.IsNullOrEmpty(detail) ? "" : $" — {detail}"));
        }

        private static bool Approx(float a, float b, float tol = 0.011f) => Mathf.Abs(a - b) <= tol;

        // ───────────── 공통 보조 ─────────────

        private ArcheryInput ReadSim()
        {
            var r = sim;
            sim.fireDown = sim.fireUp = sim.recallDown = sim.recallUp = false;
            return r;
        }

        private IEnumerator Begin(LaunchStyle l, RetrievalStyle r, params string[] loadout)
        {
            flow.SelectedLaunch = l;
            flow.SelectedRetrieval = r;
            flow.LoadoutOverride = new List<string>(loadout);
            sim = new ArcheryInput();
            flow.StartBattle();
            flow.Combat.InputOverride = ReadSim;
            yield return null;
            yield return null;
            yield return WaitGame(0.3f);
        }

        private IEnumerator WaitGame(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                SyncMouse();
                yield return null;
            }
        }

        private bool waitResult;
        private IEnumerator WaitUntil(Func<bool> cond, float timeoutGame)
        {
            float end = Time.time + timeoutGame;
            waitResult = false;
            while (Time.time < end)
            {
                if (cond()) { waitResult = true; yield break; }
                SyncMouse();
                yield return null;
            }
            waitResult = cond();
        }

        private void Aim(Vector2 world) => sim.aimWorld = world;

        private IEnumerator TapFire()
        {
            sim.fireDown = true; sim.fireHeld = true;
            yield return null;
            sim.fireHeld = false; sim.fireUp = true;
            yield return null;
        }

        private IEnumerator TapRecall()
        {
            sim.recallDown = true; sim.recallHeld = true;
            yield return null;
            sim.recallHeld = false; sim.recallUp = true;
            yield return null;
        }

        private ArcheryCombat C => flow.Combat;
        private ArcheryEnemy DummyA => flow.FindEnemy("허수아비 A");
        private ArcheryEnemy LightningDummy => flow.FindEnemy("번개 시험 허수아비");

        /// <summary>Procedural2DAim 조준 연출이 테스트 조준점을 바라보도록 가상 마우스 위치를 맞춘다</summary>
        private void SyncMouse()
        {
#if ENABLE_INPUT_SYSTEM
            var cam = Camera.main;
            if (cam == null || Mouse.current == null || flow.Combat == null) return;
            Vector3 sp = cam.WorldToScreenPoint(sim.aimWorld);
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = new Vector2(sp.x, sp.y) });
#endif
        }

        private void Screenshot(string tag)
        {
            if (string.IsNullOrEmpty(screenshotDir)) return;
            ScreenCapture.CaptureScreenshot(Path.Combine(screenshotDir, $"{++shotIndex:00}_{tag}.png"));
        }

        private ArcheryArrow LastFired()
        {
            ArcheryArrow best = null;
            foreach (var a in C.Arrows)
            {
                if (a.State == ArrowState.InQuiver) continue;
                if (best == null || a.LaunchTime > best.LaunchTime) best = a;
            }
            return best;
        }

        /// <summary>로비 회수 목록: 기본 줍기가 맨 위, 나머지 상대 순서 유지, 표시 이름 = 적용 궁술</summary>
        private void LobbyOrderCheck()
        {
            lines.Add("■ 0. 로비 회수 유형 목록");
            var order = ArcheryTestUI.LobbyRetrievalOrder;
            var expected = new[] { RetrievalStyle.Basic, RetrievalStyle.Orpheus, RetrievalStyle.Ares, RetrievalStyle.Demeter, RetrievalStyle.Hades, RetrievalStyle.Zeus };
            bool same = order.Length == expected.Length;
            var labels = new StringBuilder();
            for (int i = 0; i < order.Length; i++)
            {
                if (i < expected.Length && order[i] != expected[i]) same = false;
                labels.Append($"{i + 1}.{ArcheryConfig.RetrievalName(order[i])} ");
            }
            Check(same, "기본 — 줍기 맨 위, 나머지 순서 유지, 6종 중복 없음", labels.ToString());

            // 각 버튼이 고르는 값으로 실제 생성되는 회수 궁술이 같은지
            bool mapped = true;
            var detail = new StringBuilder();
            foreach (var st in order)
            {
                var b = RetrievalBehaviour.Create(st, null);
                if (b.Style != st) mapped = false;
                detail.Append($"{ArcheryConfig.RetrievalName(st)}→{b.GetType().Name} ");
            }
            Check(mapped, "표시 유형과 실제 적용 궁술 일치", detail.ToString());
            if (quitWhenDone) // 앱을 막 실행한 상태에서만 초기 선택값을 확인할 수 있다
            {
                Check(flow.SelectedRetrieval == RetrievalStyle.Basic && flow.SelectedLaunch == LaunchStyle.Apollo, "초기 선택값 유지 (아폴론 / 기본 줍기)",
                    $"{flow.SelectedLaunch} / {flow.SelectedRetrieval}");
            }
        }

        // ───────────── A. 30가지 조합 ─────────────

        private IEnumerator AllCombinations()
        {
            for (int li = 0; li < 5; li++)
            {
                for (int ri = 0; ri < 6; ri++)
                {
                    var l = (LaunchStyle)li;
                    var r = (RetrievalStyle)ri;
                    Progress = $"A. 조합 {li * 6 + ri + 1}/30: {ArcheryConfig.LaunchName(l)} + {ArcheryConfig.RetrievalName(r)}";
                    yield return Combination(l, r);
                }
            }
        }

        private IEnumerator Combination(LaunchStyle l, RetrievalStyle r)
        {
            string tag = $"{l}+{r}";
            yield return Begin(l, r, "basic", "basic", "basic");
            var enemy = DummyA;
            Vector2 target = enemy.transform.position;
            flow.TeleportPlayer(l == LaunchStyle.Ares ? new Vector2(target.x - 1.6f, 1.2f) : new Vector2(target.x - 3.5f, 1.2f));
            yield return WaitGame(0.25f);
            Aim(target);

            float hp0 = enemy.Hp;
            int hits0 = enemy.HitCount;

            // 1) 발사
            if (l == LaunchStyle.Odysseus)
            {
                sim.fireDown = true; sim.fireHeld = true;
                yield return WaitGame(1.3f);
                sim.fireHeld = false; sim.fireUp = true;
                yield return null;
            }
            else
            {
                yield return TapFire();
            }

            yield return WaitUntil(() => enemy.HitCount > hits0, 3f);
            bool hit = waitResult;
            yield return WaitGame(0.25f);
            float expected = 7f;
            if (l == LaunchStyle.Odysseus)
            {
                var ody = (OdysseusLaunch)C.Launcher;
                expected = ody.LastDamage;
                float norm = Mathf.Clamp(ody.LastChargeSeconds, 1.2f, 1.5f);
                bool mathOk = Approx(expected, 6f * norm + 1f, 0.02f);
                if (!mathOk) Check(false, $"{tag} 차지 피해 계산", $"실제 {ody.LastChargeSeconds:0.000}초 → {expected:0.00}");
            }
            bool damageOk = hit && enemy.HitCount == hits0 + 1 && Approx(hp0 - enemy.Hp, expected, 0.02f);
            if (r == RetrievalStyle.Ares && l == LaunchStyle.Ares)
            {
                // 찌르기 거리 = 뽑기 거리라 0.5초 뒤 자동 뽑기가 일어날 수 있음 → 첫 적중 피해만 확인
                damageOk = hit && enemy.HitCount >= hits0 + 1;
            }
            int quiverAfterShot = C.Quiver.Count;

            // 2) 화살 위치 상태
            var arrow = LastFired();
            bool stateOk;
            if (r == RetrievalStyle.Basic)
            {
                yield return WaitUntil(() => arrow == null || arrow.State == ArrowState.Grounded || arrow.State == ArrowState.InQuiver, 3f);
                stateOk = arrow != null && arrow.StuckEnemy == null && !arrow.History.Contains(ArrowState.Stuck);
            }
            else if (r == RetrievalStyle.Hades || (r == RetrievalStyle.Ares && l == LaunchStyle.Ares))
            {
                stateOk = arrow != null && arrow.History.Contains(ArrowState.Stuck);
            }
            else
            {
                stateOk = arrow != null && arrow.State == ArrowState.Stuck && arrow.StuckEnemy == enemy;
            }
            if (hit) Screenshot($"combo_{tag}_hit");

            // 3) 회수
            float hpBeforeRecall = enemy.Hp;
            int hitsBeforeRecall = enemy.HitCount;
            float recallStart = Time.time;
            switch (r)
            {
                case RetrievalStyle.Basic:
                    if (arrow != null && arrow.State == ArrowState.Grounded) flow.TeleportPlayer((Vector2)arrow.transform.position + Vector2.up * 0.6f);
                    yield return WaitUntil(() => C.Quiver.Count == 3, 3f);
                    break;
                case RetrievalStyle.Orpheus:
                    sim.recallDown = true; sim.recallHeld = true;
                    yield return WaitUntil(() => C.Quiver.Count == 3, 7f);
                    sim.recallHeld = false; sim.recallUp = true;
                    yield return null;
                    break;
                case RetrievalStyle.Ares:
                    flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 1.6f, 1.2f));
                    yield return WaitUntil(() => C.Quiver.Count == 3, 3f);
                    break;
                case RetrievalStyle.Demeter:
                    flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 2.2f, 1.2f));
                    yield return WaitGame(0.2f);
                    Aim(enemy.transform.position);
                    yield return TapRecall();
                    yield return WaitUntil(() => C.Quiver.Count == 3, 1f);
                    break;
                case RetrievalStyle.Hades:
                    yield return WaitUntil(() => C.Quiver.Count == 3, 7f);
                    break;
                case RetrievalStyle.Zeus:
                    if (arrow != null) Aim(arrow.transform.position);
                    yield return null;
                    yield return TapRecall();
                    yield return WaitUntil(() => C.Quiver.Count == 3, 1f);
                    break;
            }
            bool recovered = waitResult && C.Quiver.Count == 3 && C.CountState(ArrowState.InQuiver) == 3;
            float recallTime = Time.time - recallStart;

            string retrievalDetail = "";
            bool retrievalOk = recovered;
            switch (r)
            {
                case RetrievalStyle.Orpheus:
                case RetrievalStyle.Hades:
                    // 하데스는 박힌 직후 귀환이 시작되므로 '박힌 뒤 ~5초' 이내
                    retrievalDetail = $"귀환 {recallTime:0.00}초";
                    if (r == RetrievalStyle.Orpheus) retrievalOk &= recallTime > 4.7f && recallTime < 5.5f;
                    break;
                case RetrievalStyle.Ares:
                    if (l != LaunchStyle.Ares)
                    {
                        float pullDmg = hpBeforeRecall - enemy.Hp;
                        retrievalOk &= enemy.HitCount == hitsBeforeRecall + 1 && Approx(pullDmg, 7f, 0.02f);
                        retrievalDetail = $"뽑기 재적중 피해 {pullDmg:0.##}";
                    }
                    retrievalOk &= arrow != null && CountHits(arrow, HitSource.Pull) == 1;
                    break;
                case RetrievalStyle.Demeter:
                    retrievalOk &= Approx(hpBeforeRecall - enemy.Hp, 2f, 0.02f);
                    retrievalDetail = $"낫 피해 {hpBeforeRecall - enemy.Hp:0.##}";
                    break;
                case RetrievalStyle.Zeus:
                    retrievalOk &= Approx(hpBeforeRecall - enemy.Hp, 0.5f, 0.02f) && ((ZeusStorm)C.Retriever).CooldownRemaining > 5f;
                    retrievalDetail = $"번개 피해 {hpBeforeRecall - enemy.Hp:0.##}, 대기 {((ZeusStorm)C.Retriever).CooldownRemaining:0.0}초";
                    break;
            }

            bool ok = damageOk && quiverAfterShot <= 2 + (r == RetrievalStyle.Basic ? 1 : 0) && stateOk && retrievalOk && C.Violations.Count == 0;
            Check(ok, $"{ArcheryConfig.LaunchName(l)} + {ArcheryConfig.RetrievalName(r)}",
                $"적중 {(hit ? "O" : "X")} 피해 {hp0 - hpBeforeRecall:0.##}(기대 {expected:0.##}) · 상태 {(stateOk ? "O" : "X")}" +
                $" · 회수 {(recovered ? "O" : "X")} {retrievalDetail}" + (C.Violations.Count > 0 ? " · 무결성 위반" : ""));
        }

        private static int CountHits(ArcheryArrow a, HitSource s)
        {
            int n = 0;
            foreach (var h in a.HitHistory) if (h == s) n++;
            return n;
        }

        // ───────────── B. 이동·애니메이션 ─────────────

        private IEnumerator MovementAndAnimation()
        {
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Basic, "basic", "basic");
            var player = flow.Player;
            var ctrl = player.GetComponent<ProceduralCharacterController>();
            var aim = player.GetComponent<Procedural2DAim>();
            var rb = player.GetComponent<Rigidbody2D>();
            Check(ctrl != null && ctrl.enabled, "기존 이동 컨트롤러 유지", "ProceduralCharacterController");
            Check(aim != null && aim.enabled && !aim.legacyCombatEnabled, "기존 조준 연출 유지 + 기존 사격/회수 비활성", "Procedural2DAim");
            Check(GameObject.Find("Quiver_Screen_Canvas") == null && GameObject.Find("Recall_Range_Circle") == null, "기존 탄창 HUD 미생성 (중복 HUD 없음)");

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            // 착지 대기
            yield return WaitUntil(() => ctrl.IsGrounded, 2f);
            float x0 = player.transform.position.x;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            float maxLegAngle = 0f;
            float end = Time.time + 0.6f;
            while (Time.time < end)
            {
                if (ctrl.legUpperL != null) maxLegAngle = Mathf.Max(maxLegAngle, Quaternion.Angle(Quaternion.identity, ctrl.legUpperL.localRotation));
                yield return null;
            }
            float moved = player.transform.position.x - x0;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return WaitGame(0.2f);
            Check(moved > 2f, "D 키 오른쪽 이동", $"{moved:0.00} 유닛");
            Check(maxLegAngle > 5f, "보행 다리 애니메이션", $"허벅지 최대 {maxLegAngle:0.0}°");

            float xl = player.transform.position.x;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.A));
            yield return WaitGame(0.4f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return WaitGame(0.1f);
            Check(player.transform.position.x < xl - 1f, "A 키 왼쪽 이동", $"{player.transform.position.x - xl:0.00}");

            // 1단 점프 최고 높이 (테스트 설정 점프력 적용 확인)
            yield return WaitUntil(() => ctrl.IsGrounded, 2f);
            yield return WaitGame(0.3f);
            float cfgJump = flow.Config.playerFirstJumpForce;
            float g = Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;
            float expectedHeight = cfgJump * cfgJump / (2f * g);
            float y0 = player.transform.position.y, maxY = y0;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            float jumpEnd = Time.time + 2f;
            bool rising = true;
            while (Time.time < jumpEnd && (rising || player.transform.position.y > maxY - 0.5f))
            {
                maxY = Mathf.Max(maxY, player.transform.position.y);
                if (rb.linearVelocity.y < -0.5f) rising = false;
                yield return null;
            }
            float jumpHeight = maxY - y0;
            Check(Approx(ctrl.firstJumpForce, cfgJump, 0.001f) && Approx(ctrl.secondJumpForce, 17f, 0.001f),
                "점프력 설정 적용 (1단 테스트 설정값, 2단 프리팹 값 유지)", $"1단 {ctrl.firstJumpForce:0.##} / 2단 {ctrl.secondJumpForce:0.##}");
            Check(Mathf.Abs(jumpHeight - expectedHeight) < 0.4f && jumpHeight < 7.49f * 0.85f,
                "1단 점프 최고 높이 약 20% 낮춤 (기존 약 7.49)", $"측정 {jumpHeight:0.00} / 계산 {expectedHeight:0.00} 유닛");

            yield return WaitUntil(() => ctrl.IsGrounded, 3f);
            yield return WaitGame(0.2f);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return null;
            yield return null;
            float vy = rb.linearVelocity.y;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Check(vy > 5f, "Space 점프", $"상승 속도 {vy:0.0}");
            yield return WaitGame(0.12f);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return null;
            yield return null;
            bool spinning = ctrl.IsSpinning;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Check(spinning, "2단 점프 공중제비 애니메이션");
            yield return WaitUntil(() => ctrl.IsGrounded, 3f);

            yield return WaitGame(0.6f);
            float xd = player.transform.position.x;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.LeftShift));
            yield return null;
            yield return null;
            bool dashing = ctrl.IsDashing;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return WaitGame(0.3f);
            Check(dashing && Mathf.Abs(player.transform.position.x - xd) > 2f, "Shift 대시", $"{player.transform.position.x - xd:0.00} 유닛");

            // 조준 방향에 따른 좌우 반전
            Aim((Vector2)player.transform.position + new Vector2(-6f, 1f));
            yield return WaitGame(0.2f);
            bool facedLeft = !aim.IsFacingRight;
            Aim((Vector2)player.transform.position + new Vector2(6f, 1f));
            yield return WaitGame(0.2f);
            Check(facedLeft && aim.IsFacingRight, "마우스 방향 좌우 반전·조준 연출");

            // 발사 반동 연출과 이동 병행
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            yield return TapFire();
            yield return WaitGame(0.3f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Check(C.Quiver.Count == 1, "이동 중 발사", $"화살통 {C.Quiver.Count}");
            Screenshot("movement");
#else
            Check(false, "Input System 비활성", "이동 테스트 불가");
#endif
        }

        private void ReleaseAllKeys()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
#endif
            sim.fireHeld = sim.recallHeld = false;
        }

        // ───────────── C. 찌르기 빗나감 ─────────────

        private IEnumerator AresMissDoesNotConsume()
        {
            yield return Begin(LaunchStyle.Ares, RetrievalStyle.Basic, "basic", "light", "bronze");
            flow.TeleportPlayer(new Vector2(-16f, 1.2f));
            yield return WaitGame(0.3f);
            var front = C.Quiver.Peek();
            Aim(new Vector2(-19f, 4f));
            yield return TapFire();
            yield return WaitGame(0.3f);
            var thrust = (AresThrustLaunch)C.Launcher;
            Check(thrust.Misses == 1 && thrust.Hits == 0, "빗나감 판정", $"적중 {thrust.Hits} / 빗나감 {thrust.Misses}");
            Check(C.Quiver.Count == 3 && C.Quiver.Peek() == front && front.State == ArrowState.InQuiver, "빗나가면 화살 소모 없음",
                $"화살통 {C.Quiver.Count}/3, 다음 화살 {C.Quiver.Peek()?.Label}");

            // 적중하면 소모된다
            var enemy = DummyA;
            flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 1.6f, 1.2f));
            yield return WaitGame(0.6f);
            Aim(enemy.transform.position);
            int h0 = enemy.HitCount;
            yield return TapFire();
            yield return WaitGame(0.1f);
            Check(thrust.Hits == 1 && C.Quiver.Count == 2 && enemy.HitCount == h0 + 1, "적중하면 화살 1발 소모", $"화살통 {C.Quiver.Count}");
            Screenshot("ares_thrust");
        }

        // ───────────── D. 뽑기 재적중 ─────────────

        private IEnumerator PullRetriggersExactlyOnce()
        {
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Ares, "bronze", "basic");
            var enemy = DummyA;
            flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 4f, 1.2f));
            yield return WaitGame(0.3f);
            Aim(enemy.transform.position);
            float hp0 = enemy.Hp;
            int kb0 = enemy.KnockbackCount;
            yield return TapFire();               // 청동 (넉백 효과)
            yield return WaitUntil(() => C.Launcher.IsReady, 3f);
            Aim(enemy.transform.position);
            yield return TapFire();               // 기본
            yield return WaitUntil(() => C.CountState(ArrowState.Stuck) == 2, 3f);
            Check(waitResult, "두 화살 모두 적에게 박힘", $"박힘 {C.CountState(ArrowState.Stuck)}");
            float shotDamage = hp0 - enemy.Hp;
            Check(Approx(shotDamage, 11f + 7f, 0.02f), "발사 적중 피해 (청동 11 + 기본 7)", $"{shotDamage:0.##}");
            Check(enemy.KnockbackCount == kb0 + 1, "발사 시 청동 화살 넉백 1회", $"{enemy.KnockbackCount - kb0}");

            // 0.5초 전에는 뽑히지 않음
            var basic = C.Arrows.Find(a => a.Def.id == "basic");
            var bronze = C.Arrows.Find(a => a.Def.id == "bronze");
            yield return WaitUntil(() => basic.State == ArrowState.Stuck, 1f);
            float hpBefore = enemy.Hp;
            int kbBefore = enemy.KnockbackCount;
            flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 1.5f, 1.2f));
            yield return null;
            yield return null;
            bool notYet = basic.StateTime < 0.5f ? basic.State == ArrowState.Stuck : true;
            Check(notYet, "박힌 지 0.5초 전에는 뽑지 않음", $"박힌 시간 {basic.StateTime:0.00}초");

            // 청동 넉백으로 적이 밀려나면 제자리로 돌아올 때까지 기다린다
            yield return WaitUntil(() => C.Quiver.Count == 2, 7f);
            float pullDamage = hpBefore - enemy.Hp;
            Check(waitResult, "다가가면 박힌 화살 모두 회수", $"화살통 {C.Quiver.Count}/2");
            Check(CountHits(bronze, HitSource.Pull) == 1 && CountHits(basic, HitSource.Pull) == 1, "화살마다 재적중 정확히 1회",
                $"청동 {string.Join(",", bronze.HitHistory)} / 기본 {string.Join(",", basic.HitHistory)}");
            Check(Approx(pullDamage, 18f, 0.02f), "재적중 피해 = 화살 피해 + 공격력 (11 + 7)", $"{pullDamage:0.##}");
            Check(enemy.KnockbackCount == kbBefore + 1, "재적중 시 청동 화살 적중 효과(넉백) 다시 발동", $"{enemy.KnockbackCount - kbBefore}회");
            yield return WaitGame(1f);
            Check(CountHits(bronze, HitSource.Pull) == 1 && CountHits(basic, HitSource.Pull) == 1, "1초 뒤에도 추가 재적중 없음");
        }

        // ───────────── E. 자동 회수와 비행 중 화살 ─────────────

        private IEnumerator AutoRecallIgnoresFlying()
        {
            foreach (var r in new[] { RetrievalStyle.Hades, RetrievalStyle.Orpheus })
            {
                yield return Begin(LaunchStyle.Apollo, r, "basic");
                Vector2 p = flow.Player.transform.position;
                Aim(p + new Vector2(-12f, 9f)); // 왼쪽 위 멀리 → 사거리 초과 후 낙하 또는 벽
                if (r == RetrievalStyle.Orpheus) { sim.recallDown = true; sim.recallHeld = true; }
                yield return TapFire();
                if (r == RetrievalStyle.Orpheus) sim.recallHeld = true;
                var arrow = C.Arrows[0];
                bool flyingObserved = false, wrong = false;
                float end = Time.time + 8f;
                while (Time.time < end && arrow.State != ArrowState.InQuiver)
                {
                    if (arrow.State == ArrowState.Flying) flyingObserved = true;
                    if (arrow.State == ArrowState.Returning && arrow.History.Count >= 2)
                    {
                        var prev = arrow.History[arrow.History.Count - 2];
                        if (prev == ArrowState.Flying) wrong = true;
                    }
                    SyncMouse();
                    yield return null;
                }
                sim.recallHeld = false; sim.recallUp = true;
                yield return null;
                string hist = string.Join("→", arrow.History);
                bool returnedAfterLanding = arrow.History.Contains(ArrowState.Returning) &&
                                            arrow.History.IndexOf(ArrowState.Grounded) >= 0 &&
                                            arrow.History.IndexOf(ArrowState.Grounded) < arrow.History.LastIndexOf(ArrowState.Returning);
                Check(flyingObserved && !wrong && returnedAfterLanding && arrow.State == ArrowState.InQuiver,
                    $"{ArcheryConfig.RetrievalName(r)}: 비행 중에는 귀환하지 않고 착지 후 귀환", hist);
            }
        }

        // ───────────── F. 화살통 배치 ─────────────

        private IEnumerator QuiverOrdering()
        {
            // 단위 검사: 동시 묶음은 무작위, 순차는 순서 유지
            var temp = new GameObject("QuiverUnitTest");
            var fakes = new List<ArcheryArrow>();
            for (int i = 0; i < 6; i++) fakes.Add(temp.AddComponent<ArcheryArrow>());
            int nonIdentity = 0;
            bool allPermutations = true;
            for (int trial = 0; trial < 20; trial++)
            {
                var q = new ArrowQuiver(1000 + trial);
                var added = q.AddRecoveredBatch(new List<ArcheryArrow>(fakes));
                bool same = true;
                for (int i = 0; i < fakes.Count; i++) if (q.Order[i] != fakes[i]) same = false;
                if (!same) nonIdentity++;
                if (q.Count != 6 || q.HasDuplicates()) allPermutations = false;
            }
            Check(allPermutations && nonIdentity >= 15, "동시 회수 묶음은 무작위 순서 (20회 중 순서가 바뀐 횟수)", $"{nonIdentity}/20");
            var seq = new ArrowQuiver(7);
            for (int i = 0; i < 6; i++) seq.AddRecoveredBatch(new List<ArcheryArrow> { fakes[i] });
            bool kept = true;
            for (int i = 0; i < 6; i++) if (seq.Order[i] != fakes[i]) kept = false;
            Check(kept, "순차 회수는 회수된 순서 유지");
            var dup = new ArrowQuiver(3);
            dup.AddRecoveredBatch(new List<ArcheryArrow> { fakes[0], fakes[0], fakes[1] });
            dup.AddRecoveredBatch(new List<ArcheryArrow> { fakes[1] });
            Check(dup.Count == 2 && !dup.HasDuplicates(), "같은 화살 중복 투입 거부", $"화살통 {dup.Count}");
            Destroy(temp);

            // 통합: 줍기 순차 (바닥 3발을 차례로 줍기)
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Basic, "basic", "light", "bronze", "basic");
            C.DebugPlaceArrows(new Vector2(-12f, 0.12f), new Vector2(-10f, 0.12f), new Vector2(-8.4f, 0.12f));
            var placed = new List<ArcheryArrow>();
            foreach (var a in C.Arrows) if (a.State == ArrowState.Grounded) placed.Add(a);
            yield return null;
            foreach (var x in new[] { -12f, -10f, -8.4f })
            {
                flow.TeleportPlayer(new Vector2(x, 1.0f));
                yield return WaitGame(0.25f);
            }
            yield return WaitUntil(() => C.Quiver.Count == 4, 1f);
            var order = C.Quiver.Order;
            bool seqOk = order.Count == 4 && order[1] == placed[0] && order[2] == placed[1] && order[3] == placed[2];
            Check(seqOk, "통합: 순차로 주운 화살은 주운 순서대로 뒤에 배치", OrderText());

            // 통합: 제우스 동시 회수
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Zeus, "basic", "light", "bronze", "basic", "light");
            Vector2 c = new Vector2(-12f, 0.12f);
            C.DebugPlaceArrows(c + new Vector2(-1.5f, 0), c + new Vector2(-0.5f, 0), c + new Vector2(0.5f, 0), c + new Vector2(1.5f, 0));
            Aim(c);
            yield return null;
            yield return TapRecall();
            yield return null;
            Check(C.Quiver.Count == 5 && C.LastBatchDescription.StartsWith("동시 회수 4발"), "통합: 뇌우로 동시에 회수한 4발은 한 묶음(무작위 배치)", C.LastBatchDescription);
        }

        private string OrderText()
        {
            var sb = new StringBuilder();
            foreach (var a in C.Quiver.Order) sb.Append(a.Label).Append(' ');
            return sb.ToString();
        }

        // ───────────── G. 중복 회수·중복 피해 ─────────────

        private IEnumerator NoDuplicateRecoveryOrDamage()
        {
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Zeus, "basic", "basic");
            C.DebugPlaceArrows(new Vector2(-12f, 0.12f));
            var a = C.Arrows[0];
            bool first = C.TryRecover(a, "검사1");
            bool second = C.TryRecover(a, "검사2");
            yield return null;
            bool third = C.TryRecover(a, "검사3");
            Check(first && !second && !third && C.Quiver.Count == 2 && !C.Quiver.HasDuplicates(), "같은 화살 두 번 회수 거부",
                $"1회 {first} / 같은 프레임 {second} / 화살통 안 {third} / 화살통 {C.Quiver.Count}");

            // 같은 프레임에 줍기 + 뇌우가 겹쳐도 1발만
            C.DebugPlaceArrows(new Vector2(-15.5f, 0.12f));
            var b = C.Arrows.Find(x => x.State == ArrowState.Grounded);
            flow.TeleportPlayer(new Vector2(-15.5f, 1f));
            Aim(new Vector2(-15.5f, 0.12f));
            sim.recallDown = true; sim.recallHeld = true;
            yield return null;
            sim.recallHeld = false;
            yield return null;
            Check(C.Quiver.Count == 2 && !C.Quiver.HasDuplicates() && b.State == ArrowState.InQuiver, "줍기와 뇌우가 겹쳐도 1회만 회수", $"화살통 {C.Quiver.Count}");

            // 비행 중 화살 회수 거부
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Basic, "basic");
            Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 6f));
            yield return TapFire();
            var f = C.Arrows[0];
            bool flying = f.State == ArrowState.Flying;
            bool rejected = !C.TryRecover(f, "비행중");
            Check(flying && rejected, "비행 중 화살은 회수 대상 아님", f.State.ToString());

            // 한 번의 비행 = 한 번의 피해
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Hades, "basic", "light");
            var enemy = DummyA;
            flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 3.5f, 1.2f));
            yield return WaitGame(0.2f);
            Aim(enemy.transform.position);
            int h0 = enemy.HitCount;
            yield return TapFire();
            yield return WaitGame(1.2f);
            var shot = C.Arrows[0];
            Check(enemy.HitCount == h0 + 1 && CountHits(shot, HitSource.Shot) == 1, "한 발 = 피해 1회", $"적 피격 {enemy.HitCount - h0}회");
        }

        // ───────────── H. 간격·공격속도 ─────────────

        private IEnumerator IntervalsAndHermes()
        {
            // 아폴론: 간격 = 1초 × 다음 화살 활시위 배율
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Basic, "basic", "bronze", "light", "basic");
            Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 6f));
            var times = new List<float>();
            int lastCount = C.Quiver.Count;
            sim.fireDown = true; sim.fireHeld = true;
            float end = Time.time + 4f;
            while (Time.time < end && times.Count < 4)
            {
                yield return null;
                if (C.Quiver.Count < lastCount) { times.Add(Time.time); lastCount = C.Quiver.Count; }
            }
            sim.fireHeld = false; sim.fireUp = true;
            yield return null;
            float tol = 0.06f * TimeScale;
            if (times.Count == 4)
            {
                float i1 = times[1] - times[0], i2 = times[2] - times[1], i3 = times[3] - times[2];
                Check(Approx(i1, 1.4f, tol) && Approx(i2, 0.5f, tol) && Approx(i3, 1.0f, tol), "아폴론 간격 = 1초 × 활시위(청동 1.4, 가벼운 0.5, 기본 1.0)",
                    $"{i1:0.00} / {i2:0.00} / {i3:0.00}초");
            }
            else Check(false, "아폴론 연속 발사", $"발사 {times.Count}회");

            // 헤르메스: 스택·간격 단축·최대 5스택
            yield return Begin(LaunchStyle.Hermes, RetrievalStyle.Basic, "light", "light", "light", "light", "light", "light", "light", "light");
            Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 6f));
            var herm = (HermesLaunch)C.Launcher;
            times.Clear();
            var stacksAtShot = new List<int>();
            lastCount = C.Quiver.Count;
            sim.fireDown = true; sim.fireHeld = true;
            end = Time.time + 6f;
            while (Time.time < end && times.Count < 8)
            {
                int stacksBefore = herm.Stacks;
                yield return null;
                if (C.Quiver.Count < lastCount) { times.Add(Time.time); stacksAtShot.Add(stacksBefore); lastCount = C.Quiver.Count; }
            }
            sim.fireHeld = false; sim.fireUp = true;
            yield return null;
            Check(herm.Stacks == 5, "헤르메스 최대 5스택", $"현재 {herm.Stacks}스택, 남은 {herm.BuffRemaining:0.0}초");
            bool intervalsOk = times.Count == 8;
            var sb = new StringBuilder();
            for (int i = 1; i < times.Count; i++)
            {
                float expected = 1.25f * 0.5f / (1f + 0.1f * Mathf.Min(5, i));
                float actual = times[i] - times[i - 1];
                sb.Append($"{actual:0.00}({expected:0.00}) ");
                if (!Approx(actual, expected, tol)) intervalsOk = false;
            }
            Check(intervalsOk, "헤르메스 간격 = 1.25 × 0.5 ÷ (1 + 0.1 × 스택)", sb.ToString());

            yield return WaitGame(5.3f);
            Check(herm.Stacks == 0 && Approx(C.AttackSpeedMultiplier, 1f), "버프 5초 후 만료", $"{herm.Stacks}스택");
        }

        // ───────────── I. 아테나 ─────────────

        private IEnumerator AthenaWeight()
        {
            yield return Begin(LaunchStyle.Athena, RetrievalStyle.Basic, "light", "bronze", "basic");
            var g = new Dictionary<string, float>();
            for (int k = 0; k < 3; k++)
            {
                yield return WaitUntil(() => C.Launcher.IsReady, 3f);
                Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 4f));
                yield return TapFire();
                var a = LastFired();
                yield return WaitGame(0.18f);
                g[a.Def.id] = EstimateGravity(a);
                if (k == 1) Screenshot("athena_arc");
            }
            Check(g["light"] < g["basic"] && g["basic"] < g["bronze"], "무거울수록 더 빨리 떨어짐 (위치 기반 추정치)",
                $"추정 낙하가속 가벼운 {g["light"]:0.0} / 기본 {g["basic"]:0.0} / 청동 {g["bronze"]:0.0} (설정 9 / 18 / 27)");
            float ratio = g["bronze"] / Mathf.Max(0.01f, g["light"]);
            Check(ratio > 2.4f && ratio < 3.6f, "낙하 가속이 무게에 비례 (청동/가벼운 ≈ 3)", $"{ratio:0.00}");

            // 아폴론은 같은 각도에서 직선 비행
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Basic, "bronze");
            Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 4f));
            yield return TapFire();
            var s = LastFired();
            yield return WaitGame(0.18f);
            float gs = EstimateGravity(s);
            Check(Mathf.Abs(gs) < 0.5f, "비교: 아폴론 화살은 낙하하지 않음", $"추정 낙하가속 {gs:0.00}");
        }

        /// <summary>
        /// 발사선에서 벗어난 수직 거리로 낙하 가속을 추정한다 (프레임/물리 스텝 시각 차이에 영향받지 않도록
        /// 경과 시간을 발사선 방향 이동거리 ÷ 속력으로 계산).
        /// </summary>
        private static float EstimateGravity(ArcheryArrow a)
        {
            Vector2 dir = a.LaunchVelocity.normalized;
            Vector2 d = (Vector2)a.transform.position - a.LaunchOrigin;
            float along = Vector2.Dot(d, dir);
            float perp = dir.x * d.y - dir.y * d.x;   // 발사선 기준 수직 이탈(부호 포함)
            float t = along / Mathf.Max(0.01f, a.LaunchVelocity.magnitude);
            float cos = Mathf.Abs(dir.x);
            // 진행 방향이 왼쪽이면 아래쪽 이탈의 부호가 반대
            float down = dir.x < 0f ? perp : -perp;
            return 2f * down / Mathf.Max(0.0001f, t * t * cos);
        }

        // ───────────── J. 오디세우스 ─────────────

        private IEnumerator OdysseusCharge()
        {
            var cfg = flow.Config;
            var basic = cfg.FindArrow("basic");
            float[] secs = { 1.2f, 1.3f, 1.4f, 1.5f };
            float[] excel = { 7.2f, 7.8f, 8.4f, 9.0f };
            var sb = new StringBuilder();
            bool ok = true;
            for (int i = 0; i < 4; i++)
            {
                float d = OdysseusLaunch.ChargeDamage(cfg, basic, 0f, secs[i]);
                sb.Append($"{secs[i]}초→{d:0.0} ");
                if (!Approx(d, excel[i], 0.001f)) ok = false;
            }
            Check(ok, "엑셀 예시와 일치 (기본 화살 6, 공격력 제외)", sb.ToString());
            Check(Approx(OdysseusLaunch.ChargeDamage(cfg, basic, 1f, 1.3f), 8.8f, 0.001f), "공격력 1 포함 시 1.3초 = 6×1.3 + 1 = 8.8");

            yield return Begin(LaunchStyle.Odysseus, RetrievalStyle.Basic, "basic", "bronze");
            var ody = (OdysseusLaunch)C.Launcher;
            Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 6f));
            sim.fireDown = true; sim.fireHeld = true;
            yield return WaitGame(0.6f);
            Check(ody.Charging && ody.ChargeElapsed > 0.5f, "누르고 있으면 충전 진행", $"{ody.ChargeElapsed:0.00}초");
            sim.fireHeld = false; sim.fireUp = true;
            yield return null;
            Check(C.Quiver.Count == 2 && !ody.Charging, "최소 충전 전 놓으면 취소, 화살 소모 없음", $"화살통 {C.Quiver.Count}");

            sim.fireDown = true; sim.fireHeld = true;
            yield return WaitGame(3f);
            float capped = ody.ChargeElapsed;
            sim.fireHeld = false; sim.fireUp = true;
            yield return null;
            Check(Approx(capped, 1.5f, 0.001f) && Approx(ody.LastDamage, 10f, 0.01f), "최대 1.5초에서 충전 고정 → 6×1.5+1 = 10", $"충전 {capped:0.00}초, 피해 {ody.LastDamage:0.00}");

            // 청동(활시위 1.4): 필요 충전시간이 1.4배
            var bronze = C.Quiver.Peek();
            float min = ody.MinChargeFor(bronze.Def);
            Check(Approx(min, 1.68f, 0.001f) && Approx(ody.MaxChargeFor(bronze.Def), 2.1f, 0.001f), "청동 화살 충전 범위 1.68~2.1초 (활시위 1.4배)", $"{min:0.00}~{ody.MaxChargeFor(bronze.Def):0.00}");
            sim.fireDown = true; sim.fireHeld = true;
            yield return WaitGame(1.9f);
            sim.fireHeld = false; sim.fireUp = true;
            yield return null;
            float norm = ody.NormalizedCharge(bronze.Def, ody.LastChargeSeconds);
            Check(C.Quiver.Count == 0 && Approx(ody.LastDamage, 10f * norm + 1f, 0.01f), "청동 차지샷 피해 = 10 × 기준충전 + 1",
                $"실제 {ody.LastChargeSeconds:0.00}초 → 기준 {norm:0.00}초 → {ody.LastDamage:0.00}");
            Screenshot("odysseus");
        }

        // ───────────── K. 오르페우스 ─────────────

        private IEnumerator OrpheusBehaviour()
        {
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Orpheus, "light", "bronze");
            Vector2 p = flow.Player.transform.position;
            C.DebugPlaceArrows(new Vector2(p.x + 3f, 0.12f), new Vector2(p.x + 14f, 0.12f));
            var near = C.Arrows[0];
            var far = C.Arrows[1];
            yield return null;
            sim.recallDown = true; sim.recallHeld = true;
            yield return WaitGame(0.5f);
            Vector2 n0 = near.transform.position, f0 = far.transform.position;
            yield return WaitGame(0.2f);
            float vNear = Vector2.Distance(n0, near.transform.position) / 0.2f;
            float vFar = Vector2.Distance(f0, far.transform.position) / 0.2f;
            Check(vFar > vNear * 2f, "멀리 있는 화살이 더 빠르게 귀환", $"먼 화살 {vFar:0.0} / 가까운 화살 {vNear:0.0} 유닛/초");
            Screenshot("orpheus_call");
            float start = Time.time - 0.7f;
            yield return WaitUntil(() => C.Quiver.Count == 2, 6f);
            float total = Time.time - start;
            Check(waitResult && total > 4.8f && total < 5.4f, "무게·거리와 무관하게 5초에 걸쳐 회수", $"{total:0.00}초");
            Check(C.LastBatchDescription.StartsWith("동시 회수 2발"), "같은 순간 출발한 화살은 동시 회수(무작위 배치)", C.LastBatchDescription);
            sim.recallHeld = false; sim.recallUp = true;
            yield return null;

            // R 놓기 → 그 자리 낙하 (잠정 규칙)
            p = flow.Player.transform.position;
            C.DebugPlaceArrows(new Vector2(p.x + 10f, 0.12f));
            sim.recallDown = true; sim.recallHeld = true;
            yield return WaitGame(1.0f);
            var arr = C.Arrows.Find(a => a.State == ArrowState.Returning);
            sim.recallHeld = false; sim.recallUp = true;
            yield return null;
            yield return null;
            bool dropped = arr != null && (arr.State == ArrowState.Falling || arr.State == ArrowState.Grounded);
            yield return WaitUntil(() => arr != null && arr.State == ArrowState.Grounded, 3f);
            Check(dropped && waitResult && C.Quiver.Count == 1, "R을 놓으면 귀환 중이던 화살이 그 자리에서 떨어짐 (잠정)", arr != null ? string.Join("→", arr.History) : "없음");
        }

        // ───────────── L. 제우스 ─────────────

        private IEnumerator ZeusBehaviour()
        {
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Zeus, "basic", "basic", "basic");
            var dummy = LightningDummy;
            Vector2 d = dummy.transform.position;
            flow.TeleportPlayer(new Vector2(d.x - 5.5f, 1.2f));
            yield return WaitGame(0.8f);
            C.DebugPlaceArrows(new Vector2(d.x - 2.2f, 0.12f), new Vector2(d.x + 2.2f, 0.12f));
            Aim(new Vector2(d.x, 0.5f));
            float hp0 = dummy.Hp;
            yield return null;
            yield return TapRecall();
            Screenshot("zeus_lightning");
            var zeus = (ZeusStorm)C.Retriever;
            Check(C.Quiver.Count == 3 && Approx(hp0 - dummy.Hp, 0.5f, 0.001f) && zeus.LastBoltHits == 1,
                "화살 사이에 있는 적에게 번개 (공격력÷2 = 0.5)", $"피해 {hp0 - dummy.Hp:0.##}, 회수 {zeus.LastRecalled}발");
            Check(zeus.CooldownRemaining > 5.8f, "재사용 대기 6초 시작", $"{zeus.CooldownRemaining:0.0}초");

            C.DebugPlaceArrows(new Vector2(d.x - 1f, 0.12f));
            yield return TapRecall();
            Check(C.Quiver.Count == 2, "대기 중에는 회수되지 않음", $"화살통 {C.Quiver.Count}");
            yield return WaitUntil(() => zeus.CooldownRemaining <= 0f, 7f);
            Aim(new Vector2(d.x - 1f, 0.12f));
            yield return TapRecall();
            Check(C.Quiver.Count == 3, "대기 종료 후 다시 사용 가능");

            // 박힌 화살 + 같은 적 중복: 같은 적에게 박힌 2발 → 번개 1회 (잠정)
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Zeus, "basic", "basic");
            var enemy = DummyA;
            flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 3.5f, 1.2f));
            yield return WaitGame(0.2f);
            Aim(enemy.transform.position);
            yield return TapFire();
            yield return WaitUntil(() => C.Launcher.IsReady, 3f);
            Aim(enemy.transform.position + Vector3.up * 0.5f);
            yield return TapFire();
            yield return WaitUntil(() => C.CountState(ArrowState.Stuck) == 2, 2f);
            float hp1 = enemy.Hp;
            Aim(enemy.transform.position);
            yield return TapRecall();
            zeus = (ZeusStorm)C.Retriever;
            Check(C.Quiver.Count == 2 && Approx(hp1 - enemy.Hp, 0.5f, 0.001f), "박힌 화살 회수 시 번개, 같은 적은 1회만 (잠정)", $"피해 {hp1 - enemy.Hp:0.##}");

            // 범위 밖이면 대기시간 미소모 (잠정)
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Zeus, "basic");
            Aim(new Vector2(10f, 10f));
            yield return TapRecall();
            Check(((ZeusStorm)C.Retriever).CooldownRemaining <= 0f, "범위에 화살이 없으면 대기시간 소모 안 함 (잠정)");
        }

        // ───────────── M. 데메테르 ─────────────

        private IEnumerator DemeterBehaviour()
        {
            yield return Begin(LaunchStyle.Apollo, RetrievalStyle.Demeter, "basic", "basic", "basic");
            var dem = (DemeterHarvest)C.Retriever;
            Check(Approx(dem.SwingInterval, 0.5f, 0.001f), "낫 간격 = 1초 × 0.5 (잠정 해석)", $"{dem.SwingInterval:0.00}초");
            Vector2 p = flow.Player.transform.position;
            // 앞쪽(오른쪽) 2발, 뒤쪽 1발
            C.DebugPlaceArrows(new Vector2(p.x + 1.5f, 0.12f), new Vector2(p.x + 2.8f, 0.12f), new Vector2(p.x - 2.5f, 0.12f));
            Aim(p + new Vector2(5f, 0f));
            yield return null;
            yield return TapRecall();
            Screenshot("demeter");
            Check(C.Quiver.Count == 2 && C.CountState(ArrowState.Grounded) == 1, "낫 범위 안 화살만 회수 (뒤쪽 화살 제외)", $"화살통 {C.Quiver.Count}");
            yield return TapRecall();
            Check(C.LastBatchDescription.StartsWith("동시 회수 2발"), "낫으로 회수한 화살은 동시 회수", C.LastBatchDescription);

            var enemy = DummyA;
            flow.TeleportPlayer(new Vector2(enemy.transform.position.x - 2.4f, 1.2f));
            yield return WaitGame(0.6f);
            Aim(enemy.transform.position);
            float hp0 = enemy.Hp;
            yield return TapRecall();
            Check(Approx(hp0 - enemy.Hp, 2f, 0.001f), "낫 피해 = 공격력 × 2", $"{hp0 - enemy.Hp:0.##}");
            float hp1 = enemy.Hp;
            yield return TapRecall();
            Check(Approx(hp1 - enemy.Hp, 0f, 0.001f), "간격 안에 다시 누르면 휘두르지 않음");
        }

        // ───────────── N. 초기화·로비 ─────────────

        private IEnumerator ResetAndLobbyCleanup()
        {
            yield return Begin(LaunchStyle.Hermes, RetrievalStyle.Zeus, "light", "light", "light", "light");
            Aim((Vector2)flow.Player.transform.position + new Vector2(-10f, 0.5f));
            sim.fireDown = true; sim.fireHeld = true;
            yield return WaitGame(1.0f);
            sim.fireHeld = false; sim.fireUp = true;
            yield return WaitGame(0.8f);
            var lastArrow = LastFired();
            if (lastArrow != null) Aim(lastArrow.transform.position);
            yield return TapRecall();
            var herm = (HermesLaunch)C.Launcher;
            var zeus = (ZeusStorm)C.Retriever;
            Check(herm.Stacks > 0 && zeus.CooldownRemaining > 0f, "초기화 전 상태: 버프·대기시간 존재", $"스택 {herm.Stacks}, 대기 {zeus.CooldownRemaining:0.0}");

            flow.ResetBattle();
            flow.Combat.InputOverride = ReadSim;
            sim = new ArcheryInput();
            yield return null;
            yield return null;
            var arrows = FindObjectsByType<ArcheryArrow>(FindObjectsSortMode.None);
            var enemies = FindObjectsByType<ArcheryEnemy>(FindObjectsSortMode.None);
            var combats = FindObjectsByType<ArcheryCombat>(FindObjectsSortMode.None);
            herm = (HermesLaunch)C.Launcher;
            zeus = (ZeusStorm)C.Retriever;
            Check(arrows.Length == 4 && C.Quiver.Count == 4 && C.CountState(ArrowState.InQuiver) == 4, "초기화: 화살 전부 화살통으로 새로 생성 (잔여 화살 없음)", $"씬의 화살 {arrows.Length}");
            Check(herm.Stacks == 0 && zeus.CooldownRemaining == 0f, "초기화: 버프·재사용 대기 정리");
            Check(enemies.Length == 6 && combats.Length == 1, "초기화: 적·플레이어 중복 없음", $"적 {enemies.Length}, 전투 컨트롤러 {combats.Length}");

            // 차지·낫 대기 중 로비 복귀
            yield return Begin(LaunchStyle.Odysseus, RetrievalStyle.Demeter, "basic", "basic");
            sim.fireDown = true; sim.fireHeld = true;
            yield return WaitGame(0.5f);
            yield return TapRecall();
            sim.fireHeld = true;
            var ody = (OdysseusLaunch)C.Launcher;
            var dem = (DemeterHarvest)C.Retriever;
            Check(ody.Charging && dem.CooldownRemaining > 0f, "로비 복귀 전 상태: 충전 중·낫 대기", $"충전 {ody.ChargeElapsed:0.00}, 대기 {dem.CooldownRemaining:0.00}");
            flow.ReturnToLobby();
            sim = new ArcheryInput();
            yield return null;
            yield return null;
            int leftArrows = FindObjectsByType<ArcheryArrow>(FindObjectsSortMode.None).Length;
            int leftEnemies = FindObjectsByType<ArcheryEnemy>(FindObjectsSortMode.None).Length;
            int leftCombat = FindObjectsByType<ArcheryCombat>(FindObjectsSortMode.None).Length;
            int leftPlayers = FindObjectsByType<ProceduralCharacterController>(FindObjectsSortMode.None).Length;
            Check(flow.Current == ArcheryTestFlow.FlowScreen.Lobby && leftArrows == 0 && leftEnemies == 0 && leftCombat == 0 && leftPlayers == 0,
                "로비 복귀: 화살·적·플레이어·궁술 상태 모두 제거", $"화살 {leftArrows}, 적 {leftEnemies}, 전투 {leftCombat}, 플레이어 {leftPlayers}");

            yield return Begin(LaunchStyle.Odysseus, RetrievalStyle.Demeter, "basic", "basic");
            ody = (OdysseusLaunch)C.Launcher;
            dem = (DemeterHarvest)C.Retriever;
            Check(!ody.Charging && ody.ChargeElapsed == 0f && dem.CooldownRemaining <= 0f && C.Quiver.Count == 2, "다시 시작: 충전·대기시간 없음");
        }
    }
}
