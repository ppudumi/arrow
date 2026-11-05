using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// [임시 결정] UI 는 빠른 반복을 위해 IMGUI(1920×1080 기준 비율 스케일)로 그린다. 한글은 OS 폰트를 동적으로 사용.
    /// </summary>
    public partial class GameUI : MonoBehaviour
    {
        private GameRoot G => GameRoot.I;
        private Font font;
        private GUIStyle label, small, title, header, button, bigButton, box, center, tiny, rich;
        private Texture2D white;
        private float scale = 1f;
        private float VW => Screen.width / scale;
        private const float VH = 1080f;
        private bool showDeck;
        /// <summary>
        /// 적 체력 표시(머리 위 체력바, 화면 위 보스 체력 패널) 여부. 사용자 지시로 꺼 둠: 적 체력은 플레이어 화면에 보이지 않는다.
        /// 체력 데이터·피해 계산·페이즈 전환·사망 처리, 플레이어 체력 HUD, 타격 피해 숫자는 영향 없음. 다시 보이게 하려면 true.
        /// </summary>
        public const bool ShowEnemyHealth = false;

        private static readonly string[] FontCandidates = { "Apple SD Gothic Neo", "AppleSDGothicNeo-Regular", "AppleGothic", "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans CJK KR", "Noto Sans KR", "Arial Unicode MS" };

        private void Init()
        {
            if (label != null) return;
            try
            {
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (var c in FontCandidates) if (installed.Contains(c)) { font = Font.CreateDynamicFontFromOSFont(c, 24); break; }
                if (font == null) font = Font.CreateDynamicFontFromOSFont(FontCandidates, 24);
            }
            catch { font = null; }
            white = Texture2D.whiteTexture;
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 22, richText = true, wordWrap = true };
            label.normal.textColor = new Color(0.95f, 0.93f, 0.88f);
            small = new GUIStyle(label) { fontSize = 18 };
            tiny = new GUIStyle(label) { fontSize = 15 };
            rich = new GUIStyle(label) { fontSize = 20 };
            title = new GUIStyle(label) { fontSize = 76, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            header = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22, richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 6, 6) };
            bigButton = new GUIStyle(button) { fontSize = 32, fontStyle = FontStyle.Bold };
            box = new GUIStyle(GUI.skin.box);
        }

        // ───────────── 그리기 도구 ─────────────

        private void Rect(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }

        private void Panel(Rect r, float alpha = 0.88f)
        {
            Rect(r, new Color(0.06f, 0.06f, 0.09f, alpha));
            Rect(new Rect(r.x, r.y, r.width, 2), new Color(1f, 0.85f, 0.45f, 0.5f));
        }

        private void Line(Vector2 a, Vector2 b, Color c, float w)
        {
            var m = GUI.matrix;
            Vector2 d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            // 해상도 스케일 행렬 위에 이동·회전을 곱한다 (RotateAroundPivot 은 스케일과 섞이면 어긋남)
            GUI.matrix = m * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f), Quaternion.Euler(0f, 0f, ang), Vector3.one);
            Rect(new Rect(0f, -w * 0.5f, d.magnitude, w), c);
            GUI.matrix = m;
        }

        private void Bar(Rect r, float t, Color fg, Color bg)
        {
            Rect(r, bg);
            Rect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), fg);
        }

        private bool Btn(Rect r, string text, bool enabled = true, GUIStyle st = null)
        {
            bool old = GUI.enabled;
            GUI.enabled = enabled;
            bool c = GUI.Button(r, text, st ?? button);
            GUI.enabled = old;
            if (c) Sfx.Play("ui", 0.5f);
            return c;
        }

        private void Text(Rect r, string t, GUIStyle st = null, Color? color = null)
        {
            var s = st ?? label;
            if (color.HasValue) { var old = s.normal.textColor; s.normal.textColor = color.Value; GUI.Label(r, t, s); s.normal.textColor = old; }
            else GUI.Label(r, t, s);
        }

        private Vector2 WorldToGui(Vector3 w)
        {
            var cam = Camera.main;
            if (cam == null) return Vector2.zero;
            var sp = cam.WorldToScreenPoint(w);
            return new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // ───────────── 메인 ─────────────

        private void OnGUI()
        {
            if (G == null) return;
            Init();
            scale = Screen.height / VH;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            switch (G.State)
            {
                case GameState.Title: DrawTitle(); break;
                case GameState.Tutorial: DrawWorldOverlay(); DrawCombatHud(); break; // 전투 HUD(공용): GameUI.CombatHud.cs
                case GameState.Node: DrawWorldOverlay(); DrawCombatHud(); break;
                case GameState.Map: DrawMap(); break;
                case GameState.Reward: DrawWorldOverlay(); DrawReward(); break;
                case GameState.Shop: DrawWorldOverlay(); DrawShop(); break;
                case GameState.Rest: DrawWorldOverlay(); DrawRest(); break;
                case GameState.Result: DrawResult(); break;
                case GameState.Death: DrawDeath(); break;
                case GameState.Lobby: DrawWorldOverlay(); DrawLobby(); break; // 로비는 중앙 메뉴·안내만 (HUD 패널·화살통 없음)
                case GameState.NectarTree: DrawNectarTree(); break;
                case GameState.ArcherySelect: DrawArcherySelect(); break;
                case GameState.Records: DrawRecords(); break;
                case GameState.Story: DrawStoryBackdrop(); DrawStory(); break;
            }
            if (G.Paused) DrawPause();
            DrawToast();
            if (G.DevMode) Text(new Rect(VW - 90, VH - 34, 80, 30), "<b>DEV</b>", small, new Color(1f, 0.4f, 0.4f));
        }

        private void DrawStoryBackdrop()
        {
            var back = G.StoryReturnState;
            if (back == GameState.Node || back == GameState.Tutorial) { DrawWorldOverlay(); DrawCombatHud(); }
            else if (back == GameState.Lobby) DrawWorldOverlay();
            else if (back == GameState.Map) DrawMap();
            else Rect(new Rect(0, 0, VW, VH), new Color(0.05f, 0.05f, 0.08f, 1f));
        }

        // ───────────── 타이틀 ─────────────

        private void DrawTitle()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.07f, 0.07f, 0.11f));
            for (int i = 0; i < 18; i++)
            {
                float x = (i * 137.5f + Time.time * 6f) % VW;
                Rect(new Rect(x, 700 + Mathf.Sin(i + Time.time * 0.5f) * 30f, 3, 380), new Color(0.9f, 0.75f, 0.35f, 0.06f));
            }
            Text(new Rect(0, 210, VW, 110), "<color=#F5D27A>라 우 랄</color>", title);
            Text(new Rect(0, 320, VW, 50), "LAUREL — 월계수가 된 님프와 저주받은 궁술의 신", center, new Color(0.85f, 0.8f, 0.7f));
            float bx = VW * 0.5f - 200;
            if (G.ConfirmNewGame)
            {
                Panel(new Rect(bx - 60, 450, 520, 260));
                Text(new Rect(bx - 40, 470, 480, 120), "저장된 진행(넥타르·강화·스킬)이 모두 지워집니다.\n새 게임을 시작할까요?", center);
                if (Btn(new Rect(bx - 20, 610, 200, 70), "새로 시작")) { G.ConfirmNewGame = false; G.NewGame(); }
                if (Btn(new Rect(bx + 220, 610, 200, 70), "취소")) G.ConfirmNewGame = false;
                return;
            }
            float y = 460;
            if (G.CanContinue)
            {
                if (Btn(new Rect(bx, y, 400, 80), "이어하기 (로비)", true, bigButton)) G.Continue();
                y += 100;
            }
            if (Btn(new Rect(bx, y, 400, 80), "새 게임", true, bigButton))
            {
                if (SaveStore.Exists() && G.Profile.tutorialDone) G.ConfirmNewGame = true; else G.NewGame();
            }
            y += 100;
            if (Btn(new Rect(bx, y, 400, 70), "종료")) Application.Quit();
            Text(new Rect(20, VH - 80, VW - 40, 70), $"저장 위치: {SaveStore.FilePath}" + (SaveStore.LastLoadSource == "backup" ? "  (백업에서 복구됨)" : ""), tiny, new Color(0.6f, 0.6f, 0.6f));
            Text(new Rect(VW - 520, VH - 50, 500, 40), "조작: A/D 이동 · Space 점프 · Shift 대쉬 · 좌클릭 발사 · R 회수 · 우클릭/Q 버리기 · Esc 일시정지", tiny, new Color(0.7f, 0.7f, 0.7f));
        }

        // ───────────── 대화 ─────────────

        // 대사창(DrawStory)·안내 메시지(DrawToast)는 GameUI.Dialogue.cs (양피지 공용 대화창)

        // ───────────── 월드 오버레이 (적 체력바·피해 숫자) ─────────────

        private void DrawWorldOverlay()
        {
            var room = G.Room;
            if (room != null)
            {
                foreach (var e in room.Enemies)
                {
                    if (e == null || !e.IsAlive || e.IsBoss) continue;
                    var p = WorldToGui(e.Center + Vector3.up * (e.HalfSize.y + 0.45f));
                    if (ShowEnemyHealth) Bar(new Rect(p.x - 36, p.y, 72, 7), e.Hp / Mathf.Max(1f, e.MaxHp), e.IsElite ? new Color(1f, 0.5f, 0.2f) : new Color(0.9f, 0.25f, 0.25f), new Color(0, 0, 0, 0.6f));
                    string badges = e.Status.Badges();
                    if (e.IsElite || badges.Length > 0) Text(new Rect(p.x - 120, p.y - 26, 240, 24), (e.IsElite ? "<b>정예</b> " : "") + badges, new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.9f, 0.7f));
                    if (e.StuckArrows.Count > 0) Text(new Rect(p.x + 38, p.y - 8, 60, 24), "↘" + e.StuckArrows.Count, tiny, new Color(0.8f, 0.9f, 1f));
                }
            }
            if (Fx.I != null)
            {
                foreach (var t in Fx.I.texts)
                {
                    var p = WorldToGui(t.pos);
                    var st = new GUIStyle(label) { fontSize = t.size, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                    float a = Mathf.Clamp01(t.life / t.maxLife * 1.5f);
                    Text(new Rect(p.x - 150 + 2, p.y - 20 + 2, 300, 40), t.text, st, new Color(0, 0, 0, a * 0.7f));
                    Text(new Rect(p.x - 150, p.y - 20, 300, 40), t.text, st, t.color.WithAlpha(a));
                }
            }
        }

        // ───────────── HUD ─────────────

        // 로비 전용이던 예전 HUD(DrawHud)는 삭제: 로비는 메뉴·안내만, 전투는 GameUI.CombatHud.cs

        // 하단 화살통 HUD(DrawQuiver)는 GameUI.Quiver.cs

        private void DrawDeckStrip(RunState run)
        {
            var r = new Rect(VW * 0.5f - 500, VH - 70, 1000, 50);
            Panel(r, 0.7f);
            Text(new Rect(r.x + 14, r.y + 10, 970, 30), $"덱 {run.deck.Count}장 · " + DeckSummary(run), tiny);
        }

        private static string DeckSummary(RunState run)
        {
            var counts = new Dictionary<string, int>();
            foreach (var c in run.deck) { var n = c.Def.name; counts[n] = counts.TryGetValue(n, out var v) ? v + 1 : 1; }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Key}×{kv.Value}");
            return string.Join(", ", parts);
        }

        // ───────────── 튜토리얼 ─────────────

        // ───────────── 지도 ─────────────

        // 노드 선택 맵(DrawMap·MapLayout)은 GameUI.Map.cs

        // ───────────── 보상 ─────────────

        private void DrawReward()
        {
            var rw = G.Reward;
            if (rw == null) return;
            // 세로형 화살 카드가 들어가도록 패널을 위아래로 넓게 쓴다
            var r = new Rect(VW * 0.5f - 700, 40, 1400, 1000);
            Panel(r, 0.95f);
            Text(new Rect(r.x + 30, r.y + 20, 1000, 50), $"<b>{rw.title}</b>", header);
            string earned = "";
            if (rw.gold > 0) earned += $"골드 +{rw.gold}   ";
            if (rw.nectar > 0) earned += $"넥타르 +{rw.nectar} (저장됨)";
            Text(new Rect(r.x + 30, r.y + 74, 1000, 30), earned, small, new Color(1f, 0.85f, 0.5f));

            float y = r.y + 120;
            if (rw.relics.Count > 0)
            {
                Text(new Rect(r.x + 30, y, 900, 34), rw.relicTaken ? "<color=#888888>유물 선택 완료</color>" : "<b>유물 하나를 고르세요</b>", small);
                y += 40;
                for (int i = 0; i < rw.relics.Count; i++)
                {
                    var rd = rw.relics[i];
                    var cell = new Rect(r.x + 30 + i * 450, y, 430, 170);
                    Rect(cell, new Color(0.13f, 0.12f, 0.16f));
                    Rect(new Rect(cell.x, cell.y, cell.width, 5), DB.RarityColors[rd.rarity]);
                    Text(new Rect(cell.x + 14, cell.y + 12, 400, 30), $"<b>{rd.name}</b>  <color=#{Hex(DB.RarityColors[rd.rarity])}>{rd.grade}</color>", small);
                    Text(new Rect(cell.x + 14, cell.y + 46, 400, 80), rd.desc, tiny);
                    if (Btn(new Rect(cell.x + 14, cell.y + 124, 160, 38), "가져가기", !rw.relicTaken)) G.TakeRewardRelic(rd);
                }
                if (!rw.relicTaken && rw.title.StartsWith("보상방") && Btn(new Rect(r.x + 1380 - 280, y - 44, 250, 40), $"대신 골드 {rw.skipGold}")) G.TakeRewardGold();
                y += 180;
            }
            if (rw.arrows.Count > 0)
            {
                bool full = G.Run.NonRelicCardCount >= DB.Balance.player.quiverMax;
                Text(new Rect(r.x + 30, y, 1300, 34), rw.arrowTaken ? "<color=#888888>화살 선택 완료</color>" : (full ? "<color=#FF9080>화살통이 가득 차 더 받을 수 없습니다</color>" : "<b>화살 하나를 고르세요</b> <color=#AAAAAA>(덱 = 화살통에 1장 추가)</color>"), small);
                y += 40;
                y += 52;
                cardZoom = rw.relics.Count > 0 ? 4 : 5;
                float cx = CardRowStart(r.center.x, rw.arrows.Count, 48f);
                for (int i = 0; i < rw.arrows.Count; i++) DrawArrowCard(new Vector2(cx + i * (CardSize.x + 48f), y), rw.arrows[i], rw.arrowTaken || full ? null : "가져가기", () => G.TakeRewardArrow(rw.arrows[i]));
                y += CardSize.y + CardButtonH + 16f;
                cardZoom = 4;
            }
            bool nothingTaken = !rw.arrowTaken && (rw.relics.Count == 0 || !rw.relicTaken) && rw.skipGold > 0;
            if (Btn(new Rect(r.x + r.width - 330, r.y + r.height - 80, 300, 60), nothingTaken ? $"건너뛰기 (골드 +{rw.skipGold})" : "지도로", true, bigButton)) G.FinishReward();
        }

        // ───────────── 화살 선택 카드 ─────────────

        private static readonly Color CardGold = new Color32(244, 230, 186, 255);
        private static readonly Color CardText = new Color32(230, 220, 203, 255);
        private static readonly Color CardShadow = new Color32(12, 7, 6, 230);

        /// <summary>카드 아트 확대 배율 (1080p 기준 화면 픽셀). 상점처럼 카드가 많은 화면은 4, 보상 화면은 5.</summary>
        private int cardZoom = 4;
        /// <summary>카드 아트 1픽셀이 차지하는 GUI 단위. 화면 픽셀로 정수배가 되게 맞춰 픽셀이 흐려지거나 뭉개지지 않게 한다.</summary>
        private float CardPixel => Mathf.Max(1f, Mathf.Round(cardZoom * scale)) / scale;
        private Vector2 CardSize => new Vector2(ArrowCardArt.Width, ArrowCardArt.Height) * CardPixel;
        private const float CardButtonH = 44f;

        private float Snap(float v) => Mathf.Round(v * scale) / scale;

        private Rect ArtRect(Rect card, RectInt a)
        {
            float p = CardPixel;
            return new Rect(card.x + a.x * p, card.y + a.y * p, a.width * p, a.height * p);
        }

        private void ShadowText(Rect r, string t, GUIStyle st, Color color)
        {
            float o = Mathf.Max(1f, CardPixel * 0.5f);
            Text(new Rect(r.x + o, r.y + o, r.width, r.height), t, st, CardShadow);
            Text(r, t, st, color);
        }

        /// <summary>현재 덱(화살통)에 들어 있는 같은 화살 수</summary>
        private int OwnedCount(ArrowDef d)
        {
            var run = G.Run;
            if (run == null) return 0;
            int n = 0;
            foreach (var c in run.deck) if (c.baseCode == d.code) n++;
            return n;
        }

        /// <summary>
        /// 금색 장식 테두리의 세로형 화살 카드. 위: 화살 이름 / 가운데: 능력 설명 / 아래: 보유 수량.
        /// 카드 전체 또는 아래 버튼을 눌러 선택한다.
        /// </summary>
        private void DrawArrowCard(Vector2 pos, ArrowDef d, string buttonText, System.Func<bool> onClick, string price = null)
        {
            var size = CardSize;
            var card = new Rect(Snap(pos.x), Snap(pos.y), size.x, size.y);
            bool selectable = buttonText != null;
            bool hover = selectable && card.Contains(Event.current.mousePosition);
            if (hover) card.y -= CardPixel * 2f; // 살짝 떠오름

            var oldColor = GUI.color;
            if (!selectable) GUI.color = new Color(0.72f, 0.7f, 0.68f, 1f);
            if (hover)
            {
                // 뒤쪽 은은한 금빛
                var glow = Art.Soft.texture;
                GUI.color = new Color(1f, 0.82f, 0.45f, 0.35f);
                GUI.DrawTexture(new Rect(card.x - size.x * 0.25f, card.y - size.y * 0.15f, size.x * 1.5f, size.y * 1.3f), glow);
                GUI.color = oldColor;
            }
            GUI.DrawTexture(card, ArrowCardArt.Frame);
            // 윗 장식 화살촉 보석 = 화살 고유색
            var gc = GUI.color;
            GUI.color = new Color(d.tint.r, d.tint.g, d.tint.b, 1f) * gc;
            GUI.DrawTexture(ArtRect(card, ArrowCardArt.GemArea), ArrowCardArt.Gem);
            GUI.color = gc;

            // 위: 이름 (길면 줄여서 한 줄에)
            var nameRect = ArtRect(card, ArrowCardArt.NameArea);
            var nameStyle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(CardPixel * 6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            while (nameStyle.fontSize > 14 && nameStyle.CalcSize(new GUIContent(d.name)).x > nameRect.width) nameStyle.fontSize--;
            ShadowText(nameRect, d.name, nameStyle, CardGold);

            // 가운데: 능력 설명 + 수치
            var desc = ArtRect(card, ArrowCardArt.DescArea);
            float pad = CardPixel * 1.5f;
            var inner = new Rect(desc.x + pad, desc.y + pad, desc.width - pad * 2f, desc.height - pad * 2f);
            string ability = string.IsNullOrEmpty(d.special) || d.special == "없음" ? "특수 능력 없음" : d.special;
            var descStyle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(CardPixel * 4.25f), alignment = TextAnchor.UpperCenter, wordWrap = true };
            var statStyle = new GUIStyle(tiny) { fontSize = Mathf.RoundToInt(CardPixel * 3.25f), alignment = TextAnchor.LowerCenter, wordWrap = true };
            float statH = CardPixel * 9f;
            while (descStyle.fontSize > 12 && descStyle.CalcHeight(new GUIContent(ability), inner.width) > inner.height - statH) descStyle.fontSize--;
            ShadowText(new Rect(inner.x, inner.y, inner.width, inner.height - statH), ability, descStyle, CardText);
            string stats = $"<color=#{Hex(DB.RarityColors[d.rarity])}>{d.grade}</color> · 피해 {d.DamageLabel}{(d.damageFilled ? "*" : "")}\n활시위 {d.draw:0.##}초{(d.drawFilled ? "*" : "")} · 무게 {d.weight:0.##}{(d.weightFilled ? "*" : "")}";
            Text(new Rect(inner.x, inner.yMax - statH, inner.width, statH), stats, statStyle, new Color(0.78f, 0.66f, 0.45f));

            // 아래: 수량 (현재 화살통에 있는 같은 화살 수)
            var countRect = ArtRect(card, ArrowCardArt.CountArea);
            int owned = OwnedCount(d);
            var countStyle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(CardPixel * 7.5f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            string countText = $"× {owned}";
            float countW = countStyle.CalcSize(new GUIContent(countText)).x;
            var capStyle = new GUIStyle(tiny) { fontSize = 14, alignment = TextAnchor.MiddleRight, wordWrap = false };
            Text(new Rect(countRect.x, countRect.y + 2f, countRect.width * 0.5f - countW * 0.5f - 6f, countRect.height), "보유", capStyle, new Color32(184, 154, 102, 255));
            ShadowText(countRect, countText, countStyle, CardGold);
            GUI.color = oldColor;

            // 카드 아래: 가격·선택 버튼
            var below = new Rect(card.x, Snap(pos.y) + size.y + 8f, size.x, CardButtonH);
            if (price != null) Text(new Rect(below.x, below.y - 2f, below.width, 22f), price, new GUIStyle(small) { fontSize = 17, alignment = TextAnchor.UpperCenter }, new Color(1f, 0.85f, 0.3f));
            if (selectable)
            {
                var btnRect = price != null ? new Rect(below.x + 20f, below.y + 20f, below.width - 40f, CardButtonH - 6f) : new Rect(below.x + 20f, below.y, below.width - 40f, CardButtonH);
                bool clicked = Btn(btnRect, buttonText);
                if (!clicked && GUI.Button(card, GUIContent.none, GUIStyle.none)) { clicked = true; Sfx.Play("ui", 0.5f); }
                if (clicked) onClick();
            }
        }

        /// <summary>카드 n장을 가로 가운데 정렬로 놓을 때 첫 카드의 x</summary>
        private float CardRowStart(float centerX, int n, float gap) => centerX - (n * CardSize.x + (n - 1) * gap) * 0.5f;

        // ───────────── 상점 ─────────────

        private void DrawShop()
        {
            var r = new Rect(VW * 0.5f - 900, 40, 1800, 1000);
            Panel(r, 0.95f);
            var run = G.Run;
            Text(new Rect(r.x + 30, r.y + 18, 900, 50), "<b>상점 — 떠돌이 대장장이 헤파이스토스의 제자</b>", header);
            Text(new Rect(r.x + 30, r.y + 70, 900, 30), $"골드 <b>{run.gold}</b>   넥타르 <b>{G.Profile.nectar}</b>   덱 {run.deck.Count}장   체력 {G.Avatar.Hp:0}/{G.Avatar.MaxHp:0}", small, new Color(1f, 0.85f, 0.5f));
            if (G.ShopRemoveMode) { DrawRemoveList(r); return; }
            float y = r.y + 112;
            int i = 0, arrowOffers = 0;
            foreach (var o in G.ShopOffers) if (o.kind == "arrow") arrowOffers++;
            float ax = CardRowStart(r.center.x, arrowOffers, 36f);
            foreach (var o in G.ShopOffers)
            {
                if (o.kind != "arrow") continue;
                DrawArrowCard(new Vector2(ax + i * (CardSize.x + 36f), y + 8f), DB.Arrow(o.arrowCode), o.sold ? null : "구매", () => G.Buy(o), o.sold ? "판매됨" : $"{o.price} G");
                i++;
            }
            y += CardSize.y + CardButtonH + 30f;
            i = 0;
            foreach (var o in G.ShopOffers)
            {
                if (o.kind != "relic") continue;
                var rd = DB.Relics[o.relicId];
                var cell = new Rect(r.x + 30 + i * 450, y, 430, 170);
                Rect(cell, new Color(0.13f, 0.12f, 0.16f));
                Rect(new Rect(cell.x, cell.y, cell.width, 5), DB.RarityColors[rd.rarity]);
                Text(new Rect(cell.x + 14, cell.y + 12, 400, 30), $"<b>{rd.name}</b>  <color=#{Hex(DB.RarityColors[rd.rarity])}>{rd.grade}</color>", small);
                Text(new Rect(cell.x + 14, cell.y + 46, 400, 80), rd.desc, tiny);
                Text(new Rect(cell.x + 280, cell.y + 124, 140, 36), o.sold ? "판매됨" : $"{o.price} G", small, new Color(1f, 0.85f, 0.3f));
                if (!o.sold && Btn(new Rect(cell.x + 14, cell.y + 124, 140, 38), "구매", run.gold >= o.price)) G.Buy(o);
                i++;
            }
            // 소모품·서비스
            float sx = r.x + 30 + 3 * 450;
            foreach (var o in G.ShopOffers)
            {
                if (o.kind != "heal" && o.kind != "ambrosia") continue;
                string desc = o.kind == "heal" ? $"체력 {DB.Balance.economy.healPotionAmount} 회복" : $"이번 도전 최대 체력 +{DB.Balance.economy.ambrosiaMaxHp}";
                if (Btn(new Rect(sx, y, 360, 78), $"{o.Title} — {o.price} G\n<size=16>{desc}{(o.sold ? " (판매됨)" : "")}</size>", !o.sold && run.gold >= o.price)) G.Buy(o);
                y += 88;
            }
            int rc = Loot.RemoveCost(run);
            if (Btn(new Rect(sx, y, 360, 78), $"화살 제거 — {(rc == 0 ? "무료(첫 회)" : rc + " G")}\n<size=16>덱에서 화살 1장 빼기</size>", run.gold >= rc && run.NonRelicCardCount > 1)) G.ShopRemoveMode = true;

            float by = r.y + r.height - 80;
            if (Btn(new Rect(r.x + 30, by, 300, 56), $"리롤 ({G.RerollGoldCost} G)", run.gold >= G.RerollGoldCost)) G.RerollShop(false);
            if (Btn(new Rect(r.x + 350, by, 300, 56), $"리롤 (넥타르 {DB.Balance.economy.rerollNectar})", G.Profile.nectar >= DB.Balance.economy.rerollNectar)) G.RerollShop(true);
            if (Btn(new Rect(r.x + r.width - 330, by - 4, 300, 60), "지도로", true, bigButton)) G.LeaveNode();
        }

        private void DrawRemoveList(Rect r)
        {
            var run = G.Run;
            Text(new Rect(r.x + 30, r.y + 110, 1200, 34), $"<b>제거할 화살을 고르세요</b> (비용 {Loot.RemoveCost(run)} G, 유물로 받은 조약돌은 제외)", small);
            float x = r.x + 30, y = r.y + 160;
            foreach (var c in new List<ArrowCard>(run.deck))
            {
                if (c.fromRelic) continue;
                if (Btn(new Rect(x, y, 260, 56), $"{c.Def.name}\n<size=14>피해 {c.Damage:0.#}</size>")) { G.RemoveCard(c); return; }
                x += 270;
                if (x > r.xMax - 280) { x = r.x + 30; y += 64; }
            }
            if (Btn(new Rect(r.x + r.width - 330, r.y + r.height - 84, 300, 60), "취소")) G.ShopRemoveMode = false;
        }

        // ───────────── 샘 ─────────────

        private void DrawRest()
        {
            var r = new Rect(VW * 0.5f - 450, 260, 900, 420);
            Panel(r, 0.95f);
            Text(new Rect(r.x + 30, r.y + 20, 800, 50), "<b>샘 — 카스탈리아의 샘물</b>", header);
            Text(new Rect(r.x + 30, r.y + 80, 840, 60), $"맑은 샘물이 솟는다. 한 가지를 고르세요.   체력 {G.Avatar.Hp:0}/{G.Avatar.MaxHp:0}", label);
            if (Btn(new Rect(r.x + 30, r.y + 160, 400, 110), $"샘물을 마신다\n<size=18>체력 {DB.Balance.economy.restHealRatio * 100:0}% 회복</size>", !G.RestUsed)) G.RestHeal();
            if (Btn(new Rect(r.x + 470, r.y + 160, 400, 110), "명상한다\n<size=18>이번 도전 최대 체력 +6</size>", !G.RestUsed)) G.RestMeditate();
            if (Btn(new Rect(r.x + r.width - 330, r.y + r.height - 80, 300, 60), "지도로", true, bigButton)) G.LeaveNode();
        }

        // ───────────── 결과·사망 ─────────────

        private void DrawResult()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.08f, 0.07f, 0.04f));
            Text(new Rect(0, 160, VW, 100), "<color=#F5D27A>3스테이지 클리어</color>", title);
            Text(new Rect(0, 280, VW, 60), "현재 개발 범위(1~3스테이지)의 끝입니다. 이야기는 다음 장에서 이어집니다.", center);
            var p = G.Profile;
            Text(new Rect(VW * 0.5f - 400, 380, 800, 260),
                $"이번 도전에서 얻은 넥타르: <b>{G.LastRunNectar}</b>\n보유 넥타르: <b>{p.nectar}</b>\n총 클리어: {p.clears}회 · 총 도전: {p.runs}회 · 사망: {p.deaths}회\n\n보스 처치로 해금된 스킬은 로비의 넥타르 강화에서 구매할 수 있습니다.", center);
            if (Btn(new Rect(VW * 0.5f - 200, 720, 400, 80), "로비로 돌아가기", true, bigButton)) G.GoLobby();
        }

        private void DrawDeath()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.1f, 0.03f, 0.04f));
            Text(new Rect(0, 180, VW, 100), "<color=#E06060>아폴론이 쓰러졌다</color>", title);
            Text(new Rect(0, 300, VW, 50), $"원인: {G.LastDeathCause} · {G.LastRunStage}스테이지", center);
            Text(new Rect(VW * 0.5f - 520, 380, 1040, 250),
                "<b>초기화</b>: 돈 · 화살 · 유물 · 도전 중 얻은 능력치와 일시 효과\n" +
                $"<b>유지</b>: 넥타르 <b>{G.Profile.nectar}</b> (이번 도전 +{G.LastRunNectar}) · 넥타르 강화 · 보스 해금 자격 · 구매한 스킬\n\n다음 도전은 1스테이지 처음부터 시작합니다.", center);
            if (Btn(new Rect(VW * 0.5f - 200, 700, 400, 80), "로비로", true, bigButton)) G.GoLobby();
        }

        // ───────────── 로비 ─────────────

        private void DrawLobby()
        {
            float y = 168;
            var r = new Rect(VW * 0.5f - 640, y, 1280, 90);
            Panel(r, 0.9f);
            float bw = 240, gap = 12, x = r.x + 16;
            if (Btn(new Rect(x, y + 14, bw, 62), "<b>출정</b> (1스테이지)", true, new GUIStyle(button) { fontSize = 26 })) { G.StartRun(); return; }
            x += bw + gap;
            if (Btn(new Rect(x, y + 14, bw, 62), $"넥타르 강화 ({G.Profile.nectar})")) G.OpenPanel(GameState.NectarTree);
            x += bw + gap;
            if (Btn(new Rect(x, y + 14, bw, 62), "궁술 선택")) G.OpenPanel(GameState.ArcherySelect);
            x += bw + gap;
            if (Btn(new Rect(x, y + 14, bw, 62), "기록")) G.OpenPanel(GameState.Records);
            x += bw + gap;
            if (Btn(new Rect(x, y + 14, bw - 20, 62), "타이틀로")) G.GoTitle();
            Text(new Rect(r.x, y + 94, 1280, 36), "로비 수련장: 오른쪽 허수아비에게 고른 궁술 조합을 시험해 볼 수 있습니다.", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, new Color(0.85f, 0.8f, 0.7f));
        }

        private void DrawNectarTree()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.09f, 0.07f, 0.05f));
            var p = G.Profile;
            Text(new Rect(50, 24, 1200, 56), $"<b>넥타르 강화 — 개미굴</b>   <color=#C59BFF>◆ 넥타르 {p.nectar}</color>", header);
            Text(new Rect(50, 80, 1400, 30), "영구 강화입니다. 사망해도 유지됩니다. 유니크 스킬은 해당 보스를 쓰러뜨린 뒤에만 넥타르로 구매할 수 있습니다.", small, new Color(0.8f, 0.75f, 0.65f));
            var area = new Rect(60, 130, VW - 640, VH - 180);
            Rect(area, new Color(0.16f, 0.12f, 0.08f, 0.9f));
            // 흙 질감
            for (int i = 0; i < 40; i++)
            {
                float rx = area.x + (i * 97 % (int)area.width), ry = area.y + (i * 53 % (int)area.height);
                Rect(new Rect(rx, ry, 6 + i % 5, 4 + i % 3), new Color(0.25f, 0.18f, 0.1f, 0.6f));
            }
            Vector2 P(NectarNodeDef n) => new Vector2(area.x + 40 + n.x / 100f * (area.width - 80), area.y + 40 + n.y / 100f * (area.height - 80));
            // 굴(연결 통로)
            foreach (var n in DB.NectarNodes)
                foreach (var req in n.requires)
                    if (DB.NectarById.TryGetValue(req, out var rn))
                    {
                        bool open = p.Level(req) > 0;
                        Line(P(rn), P(n), open ? new Color(0.55f, 0.4f, 0.22f) : new Color(0.3f, 0.22f, 0.14f), 22);
                        Line(P(rn), P(n), open ? new Color(0.85f, 0.65f, 0.3f, 0.6f) : new Color(0.2f, 0.15f, 0.1f), 6);
                    }
            foreach (var n in DB.NectarNodes)
            {
                var c = P(n);
                var st = NectarShop.StateOf(p, n, out _);
                int lv = p.Level(n.id);
                Color col;
                string tag;
                switch (st)
                {
                    case NectarNodeState.Maxed: col = new Color(1f, 0.8f, 0.3f); tag = n.kind == "skill" ? "보유" : "MAX"; break;
                    case NectarNodeState.Available: col = new Color(0.45f, 0.9f, 0.5f); tag = $"{NectarShop.NextCost(p, n)}"; break;
                    case NectarNodeState.TooExpensive: col = new Color(0.55f, 0.6f, 0.45f); tag = $"{NectarShop.NextCost(p, n)}"; break;
                    case NectarNodeState.BossLocked: col = new Color(0.6f, 0.3f, 0.35f); tag = "보스"; break;
                    default: col = new Color(0.35f, 0.32f, 0.3f); tag = "잠김"; break;
                }
                float rad = n.kind == "skill" ? 48 : 38;
                bool sel = G.SelectedNectarNode == n;
                if (sel) Rect(new Rect(c.x - rad - 6, c.y - rad - 6, rad * 2 + 12, rad * 2 + 12), Color.white);
                Rect(new Rect(c.x - rad, c.y - rad, rad * 2, rad * 2), col);
                if (n.kind == "skill") Rect(new Rect(c.x - rad, c.y - rad, rad * 2, 6), new Color(0.9f, 0.5f, 1f));
                Text(new Rect(c.x - 90, c.y - rad - 30, 180, 28), n.name, new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter, fontSize = 15, wordWrap = false }, Color.white);
                Text(new Rect(c.x - rad, c.y - 14, rad * 2, 28), $"<b>{tag}</b>", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }, Color.black);
                if (n.MaxLevel > 1) Text(new Rect(c.x - rad, c.y + 10, rad * 2, 24), $"{lv}/{n.MaxLevel}", new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter }, new Color(0, 0, 0, 0.8f));
                if (GUI.Button(new Rect(c.x - rad, c.y - rad, rad * 2, rad * 2), GUIContent.none, GUIStyle.none)) { G.SelectedNectarNode = n; Sfx.Play("ui", 0.4f); }
            }
            // 상세
            var d = new Rect(VW - 560, 130, 520, VH - 180);
            Panel(d);
            var selN = G.SelectedNectarNode;
            if (selN == null) Text(new Rect(d.x + 20, d.y + 20, 480, 400), "노드를 눌러 자세히 보세요.\n\n<color=#7FE07F>초록</color> 구매 가능\n<color=#9AA080>올리브</color> 넥타르 부족\n<color=#A04C58>붉은색</color> 보스 처치 필요\n<color=#666666>회색</color> 선행 강화 필요\n<color=#FFD050>금색</color> 보유 / 최대\n\n보라색 띠가 있는 큰 노드는 유니크 스킬입니다.", label);
            else
            {
                var st = NectarShop.StateOf(p, selN, out string reason);
                int lv = p.Level(selN.id);
                Text(new Rect(d.x + 20, d.y + 18, 480, 40), $"<b>{selN.name}</b>", header);
                string kind = selN.kind == "skill" ? "유니크 스킬" : selN.kind == "launch" ? "발사 궁술 해금" : selN.kind == "retrieval" ? "회수 궁술 해금" : "능력치";
                Text(new Rect(d.x + 20, d.y + 64, 480, 30), $"{kind} · 레벨 {lv}/{selN.MaxLevel}", small, new Color(0.8f, 0.8f, 0.8f));
                Text(new Rect(d.x + 20, d.y + 100, 480, 120), selN.desc, label);
                string cond = selN.bossUnlock > 0 ? $"구매 조건: {selN.bossUnlock}스테이지 보스 처치 " + (p.BossDefeated(selN.bossUnlock) ? "<color=#7FE07F>(달성 — 구매 자격 있음)</color>" : "<color=#E07070>(미달성)</color>") : "";
                if (selN.requires.Count > 0) cond += (cond.Length > 0 ? "\n" : "") + "선행: " + string.Join(", ", selN.requires.ConvertAll(x => DB.NectarById[x].name));
                Text(new Rect(d.x + 20, d.y + 230, 480, 90), cond, small);
                string costs = "가격: " + string.Join(" → ", System.Array.ConvertAll(selN.cost, x => x.ToString()));
                Text(new Rect(d.x + 20, d.y + 320, 480, 30), costs, small, new Color(0.8f, 0.7f, 1f));
                Text(new Rect(d.x + 20, d.y + 360, 480, 60), reason, small, st == NectarNodeState.Available ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.7f, 0.6f));
                if (Btn(new Rect(d.x + 20, d.y + 430, 480, 70), st == NectarNodeState.Maxed ? "구매 완료" : $"넥타르 {NectarShop.NextCost(p, selN)} 로 구매", st == NectarNodeState.Available, bigButton)) G.BuyNectarNode(selN);
            }
            if (Btn(new Rect(d.x + 20, d.yMax - 80, 480, 60), "닫기 (Esc)")) G.ClosePanel();
        }

        private void DrawArcherySelect()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.06f, 0.07f, 0.1f, 0.97f));
            var p = G.Profile;
            Text(new Rect(60, 30, 1400, 56), "<b>궁술 선택</b>  <color=#AAAAAA>발사 1종 + 회수 1종 — 모든 조합 가능 (5 × 6 = 30)</color>", header);
            Text(new Rect(60, 90, 1400, 30), "잠긴 궁술은 넥타르 강화(개미굴 오른쪽 가지)에서 해금합니다. 선택은 저장되고 다음 출정에 적용됩니다.", small, new Color(0.8f, 0.8f, 0.8f));
            float y = 150;
            Text(new Rect(60, y, 600, 40), "<b>발사 궁술</b>", small);
            foreach (var s in StyleNames.AllLaunch)
            {
                y += 70;
                bool unlocked = p.StyleUnlocked("launch", s.ToString());
                bool sel = p.launch == s.ToString();
                if (Btn(new Rect(60, y, 360, 60), (sel ? "▶ " : "") + StyleNames.Launch(s) + (unlocked ? "" : "  <color=#E07070>잠김</color>"), unlocked)) G.SelectLaunch(s);
                Text(new Rect(440, y + 4, 520, 60), StyleNames.LaunchDesc(s), tiny);
            }
            y = 150;
            Text(new Rect(1000, y, 600, 40), "<b>회수 궁술</b>", small);
            foreach (var s in StyleNames.AllRetrieval)
            {
                y += 70;
                bool unlocked = p.StyleUnlocked("retrieval", s.ToString());
                bool sel = p.retrieval == s.ToString();
                if (Btn(new Rect(1000, y, 360, 60), (sel ? "▶ " : "") + StyleNames.Retrieval(s) + (unlocked ? "" : "  <color=#E07070>잠김</color>"), unlocked)) G.SelectRetrieval(s);
                Text(new Rect(1380, y + 4, Mathf.Max(300, VW - 1420), 60), StyleNames.RetrievalDesc(s), tiny);
            }
            int l = 0, rr = 0;
            foreach (var s in StyleNames.AllLaunch) if (p.StyleUnlocked("launch", s.ToString())) l++;
            foreach (var s in StyleNames.AllRetrieval) if (p.StyleUnlocked("retrieval", s.ToString())) rr++;
            Text(new Rect(60, 700, 1200, 40), $"사용 가능한 조합: {l} × {rr} = <b>{l * rr}</b> / 30", small);
            if (Btn(new Rect(60, VH - 120, 360, 70), "닫기 (Esc)", true, bigButton)) G.ClosePanel();
        }

        private void DrawRecords()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0.06f, 0.06f, 0.08f, 0.97f));
            var p = G.Profile;
            Text(new Rect(60, 30, 1200, 56), "<b>기록</b>", header);
            string bosses = p.bossesDefeated.Count == 0 ? "없음" : string.Join(", ", p.bossesDefeated.ConvertAll(s => $"{s}스테이지 ({DB.Bosses[DB.Stage(s).boss].name})"));
            Text(new Rect(60, 110, 1400, 600),
                $"도전 {p.runs}회 · 사망 {p.deaths}회 · 3스테이지 클리어 {p.clears}회 · 최고 도달 {p.bestStage}스테이지 · 처치 {p.kills}\n" +
                $"넥타르: 보유 {p.nectar} · 누적 획득 {p.nectarEarnedTotal} · 사용 {p.nectarSpentTotal}\n" +
                $"보스 처치(스킬 구매 자격): {bosses}\n" +
                $"구매한 유니크 스킬: {(p.HasSkill("doubleJump") ? "더블점프 " : "")}{(p.HasSkill("dash") ? "대쉬 " : "")}{(p.HasSkill("tripleJump") ? "에로스의 날개" : "")}\n\n" +
                $"저장 파일: {SaveStore.FilePath}\n마지막 저장: {p.lastSavedUtc}\n" + (SaveStore.LastError != null ? $"<color=#FF8080>{SaveStore.LastError}</color>" : ""), label);
            if (Btn(new Rect(60, VH - 120, 360, 70), "닫기 (Esc)", true, bigButton)) G.ClosePanel();
        }

        // ───────────── 일시정지·토스트 ─────────────

        private void DrawPause()
        {
            Rect(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.6f));
            var r = new Rect(VW * 0.5f - 420, 220, 840, 600);
            Panel(r, 0.96f);
            Text(new Rect(r.x, r.y + 20, r.width, 60), "<b>일시정지</b>", new GUIStyle(header) { alignment = TextAnchor.MiddleCenter });
            Text(new Rect(r.x + 40, r.y + 90, 760, 220),
                "A / D 이동 · Space 점프 (보유 시 공중 Space 더블점프) · Shift 대쉬 (보유 시)\n좌클릭 발사 (궁술에 따라 연사·충전·찌르기) · R 회수 궁술 · 우클릭 / Q 버리기\n마우스 조준 · Esc 일시정지\n\n개발자 모드 F12 (F9 넥타르, F10 방 정리, F11 보스 해금)", small);
            if (Btn(new Rect(r.x + 220, r.y + 330, 400, 66), "계속하기", true, bigButton)) G.SetPaused(false);
            bool inRun = G.Run != null && !G.Run.isTutorial;
            if (G.State != GameState.Lobby && Btn(new Rect(r.x + 220, r.y + 410, 400, 60), inRun ? "도전 포기 (사망 처리 → 로비)" : "튜토리얼 그만두기 (타이틀)")) G.AbandonRun();
            if (Btn(new Rect(r.x + 220, r.y + 486, 400, 60), "타이틀로" + (inRun ? " (도전 포기)" : ""))) { if (inRun) G.AbandonRun(); G.GoTitle(); }
        }
    }
}
