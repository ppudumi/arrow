using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Archery
{
    /// <summary>
    /// 테스트 빌드용 로비/전투 HUD (IMGUI). 1920×1080 기준 좌표를 화면 크기에 맞게 확대한다.
    /// 한글 표시를 위해 OS 에 설치된 한글 폰트를 동적으로 불러온다.
    /// </summary>
    [RequireComponent(typeof(ArcheryTestFlow))]
    public class ArcheryTestUI : MonoBehaviour
    {
        public static bool PointerOverBattleButtons { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PointerOverBattleButtons = false;

        /// <summary>
        /// 로비 회수 유형 표시 순서. 버튼 이름과 선택값은 같은 enum 값에서 나오므로 표시와 실제 적용 궁술이 항상 일치한다.
        /// </summary>
        public static readonly RetrievalStyle[] LobbyRetrievalOrder =
        {
            RetrievalStyle.Basic, RetrievalStyle.Orpheus, RetrievalStyle.Ares,
            RetrievalStyle.Demeter, RetrievalStyle.Hades, RetrievalStyle.Zeus,
        };

        private const float RefW = 1920f, RefH = 1080f;
        private ArcheryTestFlow flow;
        private Font font;
        private bool stylesReady;
        private GUIStyle label, labelSmall, title, header, panel, button, buttonSel, bigButton, quiverBox, popup, enemyLabel, logStyle, textField, countStyle;
        private Texture2D panelTex, selTex, btnTex, btnHoverTex, whiteTex;
        private Rect battleButtonsRect;
        private string hpField = "";
        private Vector2 logScroll;

        private static readonly string[] FontCandidates =
        {
            "Apple SD Gothic Neo", "AppleSDGothicNeo-Regular", "AppleGothic", "Malgun Gothic", "맑은 고딕",
            "NanumGothic", "Noto Sans CJK KR", "Noto Sans KR", "Arial Unicode MS",
        };

        private void Awake()
        {
            flow = GetComponent<ArcheryTestFlow>();
        }

        private float Scale => Mathf.Min(Screen.width / RefW, Screen.height / RefH);

        private void Update()
        {
            // UI 버튼 위 클릭이 사격으로 이어지지 않게 한다
            Vector2 mouse = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) mouse = Mouse.current.position.ReadValue();
#else
            mouse = Input.mousePosition;
#endif
            float s = Scale;
            Vector2 gui = new Vector2(mouse.x / s, (Screen.height - mouse.y) / s);
            PointerOverBattleButtons = flow.Current == ArcheryTestFlow.FlowScreen.Battle && battleButtonsRect.Contains(gui);
        }

        private void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            font = LoadKoreanFont();
            panelTex = MakeTex(new Color(0.05f, 0.06f, 0.09f, 0.72f));
            selTex = MakeTex(new Color(0.18f, 0.55f, 0.85f, 0.95f));
            btnTex = MakeTex(new Color(0.16f, 0.18f, 0.24f, 0.95f));
            btnHoverTex = MakeTex(new Color(0.24f, 0.28f, 0.36f, 0.95f));
            whiteTex = MakeTex(Color.white);

            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 20, richText = true, wordWrap = true };
            label.normal.textColor = new Color(0.92f, 0.94f, 0.98f);
            labelSmall = new GUIStyle(label) { fontSize = 17 };
            logStyle = new GUIStyle(label) { fontSize = 16, wordWrap = false };
            title = new GUIStyle(label) { fontSize = 40, fontStyle = FontStyle.Bold };
            header = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold };
            header.normal.textColor = new Color(0.55f, 0.85f, 1f);
            panel = new GUIStyle(GUI.skin.box);
            panel.normal.background = panelTex;
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 21, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 10, 6, 6), richText = true };
            button.normal.background = btnTex;
            button.hover.background = btnHoverTex;
            button.active.background = selTex;
            button.normal.textColor = button.hover.textColor = button.active.textColor = Color.white;
            buttonSel = new GUIStyle(button);
            buttonSel.normal.background = selTex;
            buttonSel.hover.background = selTex;
            bigButton = new GUIStyle(button) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            bigButton.normal.background = MakeTex(new Color(0.15f, 0.6f, 0.35f, 1f));
            bigButton.hover.background = MakeTex(new Color(0.2f, 0.72f, 0.42f, 1f));
            quiverBox = new GUIStyle(label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            popup = new GUIStyle(label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            enemyLabel = new GUIStyle(label) { fontSize = 17, alignment = TextAnchor.LowerCenter, wordWrap = false };
            countStyle = new GUIStyle(label) { alignment = TextAnchor.UpperCenter };
        }

        private static Font LoadKoreanFont()
        {
            try
            {
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (var c in FontCandidates)
                {
                    if (installed.Contains(c)) return Font.CreateDynamicFontFromOSFont(c, 20);
                }
                return Font.CreateDynamicFontFromOSFont(FontCandidates, 20);
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            if (ArcherySelfTest.Running)
            {
                DrawSelfTestBanner();
            }

            if (flow.Current == ArcheryTestFlow.FlowScreen.Lobby) DrawLobby();
            else DrawBattle();
        }

        private float VirtualWidth => Screen.width / Scale;
        private float VirtualHeight => Screen.height / Scale;

        // ───────────── 로비 ─────────────

        private void DrawLobby()
        {
            var cfg = flow.Config;
            GUI.Box(new Rect(0, 0, VirtualWidth, VirtualHeight), GUIContent.none, panel);
            GUI.Label(new Rect(60, 30, 1400, 60), "궁술 테스트 빌드 — 로비", title);
            GUI.Label(new Rect(60, 88, 1700, 34), "발사 궁술 1종과 회수 궁술 1종을 각각 선택하고 테스트를 시작하세요. 두 선택은 서로 독립적입니다.", labelSmall);

            GUI.Label(new Rect(60, 140, 400, 40), "발사 궁술", header);
            for (int i = 0; i < 5; i++)
            {
                var st = (LaunchStyle)i;
                if (GUI.Button(new Rect(60, 185 + i * 66, 400, 58), ArcheryConfig.LaunchName(st), flow.SelectedLaunch == st ? buttonSel : button))
                    flow.SelectedLaunch = st;
            }

            GUI.Label(new Rect(500, 140, 400, 40), "회수 궁술", header);
            for (int i = 0; i < LobbyRetrievalOrder.Length; i++)
            {
                var st = LobbyRetrievalOrder[i];
                if (GUI.Button(new Rect(500, 185 + i * 66, 400, 58), ArcheryConfig.RetrievalName(st), flow.SelectedRetrieval == st ? buttonSel : button))
                    flow.SelectedRetrieval = st;
            }

            // 조합 설명
            float x = 950, w = Mathf.Min(900, VirtualWidth - x - 40);
            GUI.Label(new Rect(x, 140, w, 40), "선택한 조합", header);
            var sb = new StringBuilder();
            sb.Append($"<b><color=#9fd8ff>발사: {ArcheryConfig.LaunchName(flow.SelectedLaunch)}</color></b>\n");
            sb.Append(cfg.launchDescriptions[(int)flow.SelectedLaunch]).Append("\n\n");
            sb.Append($"<b><color=#ffd27f>회수: {ArcheryConfig.RetrievalName(flow.SelectedRetrieval)}</color></b>\n");
            sb.Append(cfg.retrievalDescriptions[(int)flow.SelectedRetrieval]).Append("\n\n");
            sb.Append("<color=#a0a8b8>모든 회수 유형은 바닥의 화살에 가까이 가면 자동으로 줍습니다.");
            if (flow.SelectedRetrieval == RetrievalStyle.Basic) sb.Append("\n이 조합에서는 화살이 적에게 박히지 않고 떨어집니다.");
            if (flow.SelectedLaunch == LaunchStyle.Ares) sb.Append("\n찌르기가 적중한 화살은 적에게 박히고(기본 줍기는 떨어짐), 빗나가면 화살통에 남습니다.");
            sb.Append("</color>");
            GUI.Label(new Rect(x, 185, w, 330), sb.ToString(), label);

            // 플레이어 체력
            float y = 530;
            GUI.Label(new Rect(x, y, w, 36), $"플레이어 체력 (기획 기본 {cfg.playerMaxHp:0})  ·  공격력 {cfg.playerAttack:0.##}", header);
            y += 44;
            if (GUI.Button(new Rect(x, y, 70, 44), " -10", button)) flow.SelectedMaxHp = Mathf.Max(1f, flow.SelectedMaxHp - 10f);
            if (GUI.GetNameOfFocusedControl() != "hp") hpField = flow.SelectedMaxHp.ToString("0");
            if (textField == null) textField = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.SetNextControlName("hp");
            string edited = GUI.TextField(new Rect(x + 80, y, 120, 44), hpField, 6, textField);
            if (edited != hpField)
            {
                hpField = edited;
                if (float.TryParse(edited, out float hpVal) && hpVal > 0f) flow.SelectedMaxHp = hpVal;
            }
            if (GUI.Button(new Rect(x + 210, y, 70, 44), " +10", button)) flow.SelectedMaxHp += 10f;
            if (GUI.Button(new Rect(x + 290, y, 150, 44), "기본값", button)) flow.SelectedMaxHp = cfg.playerMaxHp;

            // 화살통 구성
            y += 66;
            GUI.Label(new Rect(x, y, w, 36), "화살통 구성 (테스트 데이터)", header);
            y += 44;
            foreach (var def in cfg.arrows)
            {
                int n = flow.LoadoutCounts.TryGetValue(def.id, out int c) ? c : 0;
                string col = ColorUtility.ToHtmlStringRGB(def.tint);
                GUI.Label(new Rect(x, y + 6, 560, 36),
                    $"<color=#{col}>■</color> {def.displayName}  피해 {def.damage:0.#} · 활시위 {def.drawSeconds:0.##}초(×{cfg.DrawMultiplier(def):0.##}) · 무게 {def.weight:0.##} · {def.effectDescription}", labelSmall);
                if (GUI.Button(new Rect(x + 600, y, 50, 40), " -", button)) flow.LoadoutCounts[def.id] = Mathf.Max(0, n - 1);
                GUI.Label(new Rect(x + 660, y + 4, 50, 36), n.ToString(), countStyle);
                if (GUI.Button(new Rect(x + 715, y, 50, 40), " +", button)) flow.LoadoutCounts[def.id] = Mathf.Min(20, n + 1);
                y += 48;
            }
            if (GUI.Button(new Rect(x, y, 380, 42), flow.ShuffleLoadout ? "초기 순서: 무작위 섞기" : "초기 순서: 종류별 번갈아", flow.ShuffleLoadout ? buttonSel : button))
                flow.ShuffleLoadout = !flow.ShuffleLoadout;

            // 시작
            if (GUI.Button(new Rect(60, 610, 840, 90), "테스트 시작", bigButton))
            {
                flow.LoadoutOverride = null;
                flow.StartBattle();
            }
            if (GUI.Button(new Rect(60, 715, 840, 56), "자동 검증 실행 (30조합 + 규칙 검사, 약 1~2분)", button))
            {
                ArcherySelfTest.Launch(flow, false);
            }

            string summary = ArcherySelfTest.LastSummary;
            if (!string.IsNullOrEmpty(summary))
            {
                GUI.Label(new Rect(60, 785, 840, 270), summary, labelSmall);
            }
            else
            {
                GUI.Label(new Rect(60, 785, 840, 270),
                    "<color=#a0a8b8>조작: A/D 이동 · Space 점프(2단) · Shift 대시 · 좌클릭 발사/충전/찌르기 · R 회수 궁술\n" +
                    "전투 중: F5 초기화 · Esc 로비 · F1 범위 표시 · F2 번개 시험 화살 배치 · F3 적 체력 초기화</color>", labelSmall);
            }
        }

        // ───────────── 전투 HUD ─────────────

        private void DrawBattle()
        {
            var combat = flow.Combat;
            var ctx = flow.Ctx;
            if (combat == null || ctx == null) return;
            var cfg = flow.Config;
            Camera cam = Camera.main;

            // 적 라벨
            if (cam != null)
            {
                foreach (var e in ctx.Enemies)
                {
                    if (e == null) continue;
                    Vector3 top = e.transform.position + Vector3.up * ((e.Col != null ? e.Col.bounds.extents.y : 0.8f) + 0.25f);
                    Vector2 gp = WorldToGui(cam, top);
                    string hp = e.IsAlive ? $"HP {ArcheryCombatContext.FormatDamage(e.Hp)}/{e.maxHp:0}" : "<color=#ff7777>쓰러짐</color>";
                    string last = Time.time - e.LastDamageTime < 3f ? $"\n<color=#ffcc66>최근 피해 {ArcheryCombatContext.FormatDamage(e.LastDamage)} ({e.LastDamageSource})</color>" : "";
                    string stuck = e.StuckArrows.Count > 0 ? $"  <color=#ff9f7f>박힌 화살 {e.StuckArrows.Count}</color>" : "";
                    GUI.Label(new Rect(gp.x - 200, gp.y - 70, 400, 70), $"<b>{e.displayName}</b>  {hp}{stuck}{last}", enemyLabel);
                }

                // 피해 팝업
                for (int i = ctx.Popups.Count - 1; i >= 0; i--)
                {
                    var p = ctx.Popups[i];
                    float age = Time.time - p.time;
                    if (age > 1.1f) { ctx.Popups.RemoveAt(i); continue; }
                    Vector2 gp = WorldToGui(cam, p.worldPos + Vector3.up * (age * 1.2f));
                    var c = GUI.color;
                    GUI.color = new Color(1f, 0.85f, 0.3f, 1f - age / 1.1f);
                    GUI.Label(new Rect(gp.x - 80, gp.y - 25, 160, 50), "-" + ArcheryCombatContext.FormatDamage(p.amount), popup);
                    GUI.color = c;
                }
            }

            // 좌상단 상태 패널 (내용 높이에 맞춤)
            float w = 980;
            var sb = new StringBuilder();
            sb.Append($"<b><color=#9fd8ff>발사 {ArcheryConfig.LaunchName(combat.LaunchType)}</color>   <color=#ffd27f>회수 {ArcheryConfig.RetrievalName(combat.RetrievalType)}</color></b>\n");
            sb.Append($"체력 {combat.Hp:0}/{combat.MaxHp:0} · 공격력 {combat.Attack:0.##} · 공격속도 ×{combat.AttackSpeedMultiplier:0.00}\n");
            sb.Append(combat.Launcher.StatusText()).Append('\n');
            sb.Append(combat.Retriever.StatusText()).Append('\n');
            int total = combat.Arrows.Count;
            sb.Append($"화살통 <b>{combat.Quiver.Count}/{total}</b> · 비행 {combat.CountState(ArrowState.Flying)} · 낙하 {combat.CountState(ArrowState.Falling)} · " +
                      $"바닥 {combat.CountState(ArrowState.Grounded)} · 적에게 박힘 {combat.CountState(ArrowState.Stuck)} · <b>회수 중 {combat.CountState(ArrowState.Returning)}</b>\n");
            sb.Append(combat.Violations.Count == 0 ? "<color=#7fff9f>무결성 검사: 정상 (중복 회수/중복 화살 없음)</color>" : $"<color=#ff6666>무결성 위반 {combat.Violations.Count}건: {combat.Violations[combat.Violations.Count - 1]}</color>");
            if (!string.IsNullOrEmpty(combat.LastBatchDescription)) sb.Append($"\n<color=#c0c8d8>최근 회수: {combat.LastBatchDescription}</color>");
            string status = sb.ToString();
            float th = labelSmall.CalcHeight(new GUIContent(status), w - 30);
            GUI.Box(new Rect(12, 12, w, th + 16), GUIContent.none, panel);
            GUI.Label(new Rect(26, 18, w - 30, th + 4), status, labelSmall);

            // 화살통 순서
            float qy = 12 + th + 24;
            var order = combat.Quiver.Order;
            int shownCount = Mathf.Min(order.Count, 14);
            GUI.Box(new Rect(12, qy, w, 92), GUIContent.none, panel);
            GUI.Label(new Rect(26, qy + 4, 600, 28), "화살통 순서 (왼쪽이 다음 화살)", labelSmall);
            float bx = 26;
            for (int i = 0; i < shownCount; i++)
            {
                var a = order[i];
                Rect r = new Rect(bx, qy + 34, 64, 52);
                GUI.Box(r, GUIContent.none, panel);
                var prev = GUI.color;
                GUI.color = a.Def.tint;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 5), whiteTex);
                if (i == 0) GUI.DrawTexture(new Rect(r.x, r.yMax - 3, r.width, 3), whiteTex);
                GUI.color = prev;
                string col = ColorUtility.ToHtmlStringRGB(a.Def.tint);
                string text = (i == 0 ? "<b>다음</b>" : $"{i + 1}") + $"\n<color=#{col}>{a.Def.displayName.Replace(" 화살", "")}#{a.Id}</color>";
                GUI.Label(new Rect(r.x, r.y + 6, r.width, r.height - 6), text, quiverBox);
                bx += 68;
            }
            if (order.Count > shownCount) GUI.Label(new Rect(bx, qy + 46, 120, 30), $"+{order.Count - shownCount}", labelSmall);
            if (order.Count == 0) GUI.Label(new Rect(26, qy + 40, 700, 40), "<color=#ff8080>비어 있음 — 회수 궁술로 화살을 돌려받으세요</color>", label);

            // 우상단 버튼
            float bw = 260, bxr = VirtualWidth - bw - 16;
            battleButtonsRect = new Rect(bxr, 12, bw, 5 * 52);
            if (GUI.Button(new Rect(bxr, 12, bw, 46), "F5  전투 초기화", button)) flow.ResetBattle();
            if (GUI.Button(new Rect(bxr, 64, bw, 46), "Esc  로비로", button)) { flow.ReturnToLobby(); return; }
            if (GUI.Button(new Rect(bxr, 116, bw, 46), flow.ShowRanges ? "F1  범위 표시: 켜짐" : "F1  범위 표시: 꺼짐", button)) flow.ToggleRanges();
            if (GUI.Button(new Rect(bxr, 168, bw, 46), "F2  번개 시험 배치", button)) flow.PlaceLightningTestArrows();
            if (GUI.Button(new Rect(bxr, 220, bw, 46), "F3  적 체력 초기화", button)) flow.ResetEnemyStats();

            // 하단 로그
            float lh = 168, ly = VirtualHeight - lh - 12;
            GUI.Box(new Rect(12, ly, 900, lh), GUIContent.none, panel);
            GUI.Label(new Rect(26, ly + 2, 400, 26), "이벤트 로그", logStyle);
            var log = ctx.EventLog;
            int start = Mathf.Max(0, log.Count - 7);
            var lsb = new StringBuilder();
            for (int i = start; i < log.Count; i++) lsb.Append(log[i]).Append('\n');
            GUI.Label(new Rect(26, ly + 26, 880, lh - 28), lsb.ToString(), logStyle);

            // 우하단 조작 안내
            string controls = ControlsText(combat);
            float gw = 600, gh = labelSmall.CalcHeight(new GUIContent(controls), gw - 24) + 14;
            GUI.Box(new Rect(VirtualWidth - gw - 16, VirtualHeight - gh - 12, gw, gh), GUIContent.none, panel);
            GUI.Label(new Rect(VirtualWidth - gw - 4, VirtualHeight - gh - 6, gw - 24, gh), controls, labelSmall);
        }

        private string ControlsText(ArcheryCombat combat)
        {
            var sb = new StringBuilder("<b>조작</b>\nA/D 이동 · Space 점프(2단) · Shift 대시\n");
            switch (combat.LaunchType)
            {
                case LaunchStyle.Odysseus: sb.Append("좌클릭 누르고 있기 → 충전, 놓기 → 발사\n"); break;
                case LaunchStyle.Ares: sb.Append("좌클릭 → 마우스 방향 찌르기 (주황 상자 = 판정)\n"); break;
                default: sb.Append("좌클릭(누르고 있으면 연속) → 마우스 방향 발사\n"); break;
            }
            switch (combat.RetrievalType)
            {
                case RetrievalStyle.Orpheus: sb.Append("R 누르고 있기 → 모든 화살 부르기 (5초)\n"); break;
                case RetrievalStyle.Ares: sb.Append("화살이 박힌 적에게 다가가기 → 뽑기 + 재적중\n"); break;
                case RetrievalStyle.Demeter: sb.Append("R → 마우스 방향 낫 휘두르기 (노란 부채꼴)\n"); break;
                case RetrievalStyle.Hades: sb.Append("자동: 떨어지거나 박힌 화살이 5초에 걸쳐 귀환\n"); break;
                case RetrievalStyle.Zeus: sb.Append("R → 마우스 원 안의 화살 즉시 회수 + 번개\n"); break;
                case RetrievalStyle.Basic: sb.Append("바닥의 화살에 다가가 줍기 (적에게 박히지 않음)\n"); break;
            }
            sb.Append("공통: 바닥 화살 근처 자동 줍기 (초록 원)\n");
            sb.Append("F5 초기화 · Esc 로비 · F1 범위 · F2 번개 시험 배치 · F3 적 체력 초기화");
            return sb.ToString();
        }

        private void DrawSelfTestBanner()
        {
            Rect r = new Rect(1000, 12, Mathf.Max(300, VirtualWidth - 1000 - 300), 86);
            GUI.Box(r, GUIContent.none, panel);
            GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, r.height - 8), $"<b><color=#ffd27f>자동 검증 실행 중</color></b>\n{ArcherySelfTest.Progress}", labelSmall);
        }

        private Vector2 WorldToGui(Camera cam, Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            float s = Scale;
            return new Vector2(sp.x / s, (Screen.height - sp.y) / s);
        }
    }
}
