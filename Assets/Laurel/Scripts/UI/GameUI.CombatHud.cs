using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 전투 HUD (튜토리얼 · 모든 전투 방 공용). 아트 콘셉트(raurel-hud-concept.png)의 체력 용기·금괴를 참고한 픽셀 아트를 런타임에 만든다.
    /// 왼쪽 위, 위에서 아래로: 맵 이름(패널 없음) → 체력 용기 · 골드 · 넥타르 → 퀘스트(목표가 없으면 숨김).
    /// 하단 화살통·보스 체력·유물 목록은 기존 그대로. 오른쪽 위 발사·회수 상태 패널은 표시하지 않는다(기능은 그대로).
    /// 로비는 전투 맵이 아니므로 HUD 를 그리지 않는다 (중앙 메뉴·수련장 안내만, DrawLobby).
    /// </summary>
    public partial class GameUI
    {
        // ── 배치 (1920×1080 가상 좌표) ──
        private const float HudX = 24f;
        private const float MapNameY = 12f;
        private const float FlaskY = 52f;
        private const int FlaskPx = 48;            // 용기 원본 픽셀 크기
        private const float PixelScale = 3f;       // 모든 HUD 픽셀 아트 공통 배율 (용기·금괴·암포라 같은 픽셀 크기)
        private float FlaskSize => FlaskPx * PixelScale;

        // 용기 안쪽(액체가 차는 원)의 위·아래 경계 (원본 픽셀, 위쪽 기준)
        private const int FlaskCx = 24, FlaskCy = 26, FlaskInnerR = 17, FlaskOuterR = 20;

        private Texture2D flaskBack, flaskLiquid, flaskSurface, flaskGlass, goldIcon, nectarIcon;
        private float hpShown = -1f;

        /// <summary>체력 비율(0~1)을 용기 텍스처의 세로 좌표(아래=0)로 바꾼다. 0 이면 안쪽 바닥, 1 이면 안쪽 꼭대기.</summary>
        public static float FlaskFillV(float ratio)
        {
            float v0 = (FlaskPx - (FlaskCy + FlaskInnerR)) / (float)FlaskPx;
            float v1 = (FlaskPx - (FlaskCy - FlaskInnerR)) / (float)FlaskPx;
            return Mathf.Lerp(v0, v1, Mathf.Clamp01(ratio));
        }

        /// <summary>지금 표시 중인(부드럽게 따라가는) 체력 비율. 검증용.</summary>
        public float HudHpShown => hpShown;
        /// <summary>현재 HUD 가 보여 주는 맵 이름·부제·퀘스트 (검증용, 그리는 것과 같은 함수)</summary>
        public string HudMapName => CurrentMapName(out _);
        public List<(string text, bool done)> HudQuest => CurrentQuest();

        private static Texture2D PixelTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }

        /// <summary>
        /// 이 프로젝트는 Linear 색 공간이라 IMGUI 가 텍스처·색 값을 한 번 더 밝게(감마 인코딩) 내보낸다.
        /// 의도한 화면색(sRGB)이 그대로 보이도록 픽셀 값을 미리 선형값으로 바꿔 넣는다. Gamma 프로젝트면 그대로.
        /// </summary>
        private static Color32[] ForScreen(Color32[] px)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return px;
            var o = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) { var c = ((Color)px[i]).linear; c.a = px[i].a / 255f; o[i] = c; }
            return o;
        }

        /// <summary>IMGUI 색(GUI.color·Rect)용 같은 보정</summary>
        private static Color ScreenColor(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            var l = c.linear; l.a = c.a; return l;
        }

        private void InitHudArt()
        {
            if (flaskBack != null) return;
            var outline = new Color32(0x1A, 0x12, 0x10, 255);
            var glass = new Color32(0x8C, 0x86, 0x82, 255);
            var glassHi = new Color32(0xE6, 0xE2, 0xDA, 255);
            var inside = new Color32(0x0C, 0x08, 0x0A, 255);
            var red = new Color32(0x9C, 0x14, 0x1E, 255);
            var redDark = new Color32(0x6A, 0x0C, 0x14, 255);
            var redLight = new Color32(0xC8, 0x2A, 0x30, 255);
            var cork = new Color32(0x8A, 0x7A, 0x66, 255);
            var corkDark = new Color32(0x5E, 0x52, 0x44, 255);
            var clear = new Color32(0, 0, 0, 0);
            int n = FlaskPx;
            flaskBack = PixelTexture(n, n); flaskLiquid = PixelTexture(n, n); flaskSurface = PixelTexture(n, n); flaskGlass = PixelTexture(n, n);
            var back = new Color32[n * n]; var liq = new Color32[n * n]; var surf = new Color32[n * n]; var gl = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = (n - 1 - y) * n + x; // 위쪽 기준 y → 텍스처 행
                    float dx = x + 0.5f - FlaskCx, dy = y + 0.5f - FlaskCy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    back[i] = liq[i] = surf[i] = gl[i] = clear;
                    if (d <= FlaskInnerR)
                    {
                        back[i] = inside;
                        // 액체: 아래로 갈수록 진하고, 왼쪽 위에 밝은 결
                        float t = (dy + FlaskInnerR) / (2f * FlaskInnerR);
                        liq[i] = t > 0.72f ? redDark : red;
                        if (dx < -6f && dy > -4f && dy < 6f && ((x + y) % 5 == 0)) liq[i] = redLight;
                        surf[i] = redLight;
                    }
                    else if (d <= FlaskInnerR + 1.6f) gl[i] = glass;                 // 유리 테
                    else if (d <= FlaskOuterR) gl[i] = outline;                        // 바깥 윤곽
                    // 유리 반사 (왼쪽 위 호)
                    if (d > FlaskInnerR - 3.2f && d <= FlaskInnerR - 1.6f && dx < -2f && dy < -2f && dx + dy < -10f) gl[i] = glassHi;
                }
            // 왼쪽 위로 비스듬히 뻗은 목 + 코르크 (콘셉트처럼). 몸통 테두리에서 바깥으로 7px, 폭 8px
            var u = new Vector2(-0.7071f, -0.7071f);                  // 왼쪽 위 방향 (위쪽 기준 좌표)
            var perp = new Vector2(0.7071f, -0.7071f);
            var anchor = new Vector2(FlaskCx, FlaskCy) + u * (FlaskOuterR - 1.5f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float along = Vector2.Dot(p - anchor, u), across = Mathf.Abs(Vector2.Dot(p - anchor, perp));
                    if (along < 0f || along > 8f || across > 4.2f) continue;
                    float ddx = p.x - FlaskCx, ddy = p.y - FlaskCy;
                    if (Mathf.Sqrt(ddx * ddx + ddy * ddy) <= FlaskOuterR - 0.3f) continue; // 몸통 위는 그대로
                    int i = (n - 1 - y) * n + x;
                    if (across > 3.0f || along > 7f) gl[i] = outline;
                    else if (along > 3.5f) gl[i] = across < 1f ? cork : corkDark;   // 코르크
                    else gl[i] = across < 1.2f ? glassHi : glass;                   // 유리 목
                }
            flaskBack.SetPixels32(ForScreen(back)); flaskBack.Apply();
            flaskLiquid.SetPixels32(ForScreen(liq)); flaskLiquid.Apply();
            flaskSurface.SetPixels32(ForScreen(surf)); flaskSurface.Apply();
            flaskGlass.SetPixels32(ForScreen(gl)); flaskGlass.Apply();

            // 금괴 (콘셉트의 금괴 더미) — 아이콘 둘 다 같은 윤곽색·같은 픽셀 배율
            var iconOutline = new Color32(0x2A, 0x1A, 0x0A, 255);
            goldIcon = FromRows(new[]
            {
                ".....##########.....",
                ".....#hhhhhhhh#.....",
                ".....#yyyyyyyy#.....",
                ".....#dddddddd#.....",
                "#########.#########.",
                "#hhhhhhh#.#hhhhhhh#.",
                "#yyyyyyy#.#yyyyyyy#.",
                "#ddddddd#.#ddddddd#.",
                "#########.#########.",
            }, new Dictionary<char, Color32>
            {
                { '#', iconOutline }, { 'h', new Color32(0xFF, 0xF2, 0xA8, 255) }, { 'y', new Color32(0xF2, 0xC2, 0x30, 255) }, { 'd', new Color32(0xB0, 0x7A, 0x18, 255) },
            });
            // 넥타르: 보랏빛 작은 암포라
            nectarIcon = FromRows(new[]
            {
                "...######...",
                "...#hhhh#...",
                "..#.#pp#.#..",
                "..##pppp##..",
                ".#hppppppp#.",
                "#hppppppppd#",
                "#hppppppppd#",
                "#pppppppppd#",
                ".#pppppppd#.",
                "..#pppppd#..",
                "...#dddd#...",
                "...######...",
            }, new Dictionary<char, Color32>
            {
                { '#', iconOutline }, { 'h', new Color32(0xD8, 0xBC, 0xFF, 255) }, { 'p', new Color32(0x8E, 0x5B, 0xD6, 255) }, { 'd', new Color32(0x58, 0x30, 0x8E, 255) },
            });
        }

        private static Texture2D FromRows(string[] rows, Dictionary<char, Color32> pal)
        {
            int h = rows.Length, w = rows[0].Length;
            var t = PixelTexture(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char ch = x < rows[y].Length ? rows[y][x] : '.';
                    px[(h - 1 - y) * w + x] = pal.TryGetValue(ch, out var c) ? c : new Color32(0, 0, 0, 0);
                }
            t.SetPixels32(ForScreen(px));
            t.Apply();
            return t;
        }

        // ── 내용 ──

        /// <summary>현재 방의 실제 이름 (방 테마 이름). 부제는 진행 위치.</summary>
        private string CurrentMapName(out string sub)
        {
            sub = "";
            var room = G.Room;
            if (room == null || room.Theme == null) return "";
            if (G.Run != null && G.Run.isTutorial) { sub = "튜토리얼"; return room.Theme.name; }
            var node = G.CurrentNode;
            if (node != null && G.Run != null && G.Run.map != null)
                sub = $"{G.Run.stage}스테이지 · {MapNode.TypeName(node.type)} · {node.layer + 1}/{G.Run.map.LayerCount}층";
            return room.Theme.name;
        }

        /// <summary>현재 목표와 체크 상태. 목표가 없으면 빈 목록(패널 숨김).</summary>
        private List<(string text, bool done)> CurrentQuest()
        {
            var q = new List<(string, bool)>();
            var room = G.Room;
            if (room == null) return q;
            if (G.State == GameState.Tutorial || (G.Run != null && G.Run.isTutorial))
            {
                switch (G.TutorialStep)
                {
                    case 0:
                        q.Add(("A / D 로 이동", G.tutMoved > 6f));
                        q.Add(("Space 로 점프", G.tutJumped));
                        q.Add(("공중에서 Space — 더블점프", G.tutDoubleJumped));
                        q.Add(("Shift — 대쉬", G.tutDashed));
                        break;
                    case 1: q.Add(($"좌클릭으로 과녁 3개 쓰러뜨리기 (남은 과녁 {room.AliveCount})", room.AliveCount == 0)); break;
                    case 2: q.Add(($"쏜 화살 모두 회수 — R 을 누르고 있으면 부르기 ({G.Combat?.Quiver.Count}/{G.Run?.deck.Count})", G.Combat != null && G.Run != null && G.Combat.Quiver.Count >= G.Run.deck.Count)); break;
                    default:
                        bool bossDown = room.Boss != null && !room.Boss.IsAlive || room.Cleared;
                        q.Add(("거대한 뱀 파에톤 쓰러뜨리기", bossDown));
                        break;
                }
                return q;
            }
            if (!room.CombatRoom) return q;
            if (room.Boss != null) q.Add(($"{room.Boss.DisplayName} 쓰러뜨리기", !room.Boss.IsAlive || room.Cleared));
            else
            {
                string wave = room.WaveCount > 1 ? $" · 웨이브 {Mathf.Clamp(room.WaveIndex + 1, 1, room.WaveCount)}/{room.WaveCount}" : "";
                bool waiting = room.WaveIndex < 0; // 첫 웨이브가 나오기 전(입장 직후)에는 '남은 적 0' 대신 숫자를 빼고 보여 준다
                q.Add((room.Cleared || waiting ? "모든 적 처치" : $"모든 적 처치 (남은 적 {room.AliveCount}{wave})", room.Cleared));
            }
            return q;
        }

        // ── 그리기 ──

        private void OutlinedText(Rect r, string t, GUIStyle st, Color c, float o = 2f)
        {
            // 외곽선은 불투명 검정 (반투명이면 Linear 공간 합성에서 회색으로 바래 밝은 배경에서 안 읽힘)
            var shadow = new Color(0.02f, 0.015f, 0.01f, 1f);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) Text(new Rect(r.x + dx * o, r.y + dy * o, r.width, r.height), t, st, shadow);
            Text(r, t, st, ScreenColor(c));
        }

        private void DrawCombatHud()
        {
            var av = G.Avatar;
            if (av == null) return;
            InitHudArt();

            // 1) 맵 이름 (패널 없음)
            string mapName = CurrentMapName(out string sub);
            if (mapName.Length > 0)
            {
                var nameStyle = new GUIStyle(label) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = false };
                OutlinedText(new Rect(HudX, MapNameY, 700, 38), mapName, nameStyle, new Color(1f, 0.93f, 0.75f));
                if (sub.Length > 0)
                {
                    var subStyle = new GUIStyle(label) { fontSize = 18, wordWrap = false };
                    float nw = nameStyle.CalcSize(new GUIContent(mapName)).x;
                    OutlinedText(new Rect(HudX + nw + 14, MapNameY + 8, 500, 28), sub, subStyle, new Color(0.9f, 0.87f, 0.8f), 1f);
                }
            }

            // 2) 체력 용기: 붉은 액체가 아래부터 현재 체력 비율만큼 찬다 (피해·회복 시 부드럽게 따라감)
            float ratio = av.Hp / Mathf.Max(1f, av.MaxHp);
            if (hpShown < 0f) hpShown = ratio;
            hpShown = Mathf.MoveTowards(hpShown, ratio, Time.unscaledDeltaTime * 1.5f);
            float s = FlaskSize;
            var fr = new Rect(HudX, FlaskY, s, s);
            GUI.DrawTexture(fr, flaskBack);
            float v = FlaskFillV(hpShown);
            if (hpShown > 0.0001f)
            {
                GUI.DrawTextureWithTexCoords(new Rect(fr.x, fr.y + (1f - v) * s, s, v * s), flaskLiquid, new Rect(0f, 0f, 1f, v));
                if (hpShown < 0.999f)
                {
                    float band = 2f / FlaskPx; // 액면: 원본 2픽셀 밝은 띠
                    float b0 = Mathf.Max(0f, v - band);
                    GUI.DrawTextureWithTexCoords(new Rect(fr.x, fr.y + (1f - v) * s, s, (v - b0) * s), flaskSurface, new Rect(0f, b0, 1f, v - b0));
                }
            }
            GUI.DrawTexture(fr, flaskGlass);
            var hpStyle = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            float cy = fr.y + (FlaskCy / (float)FlaskPx) * s;
            OutlinedText(new Rect(fr.x + (FlaskCx / (float)FlaskPx) * s - 80, cy - 18, 160, 36), $"{av.Hp:0} / {av.MaxHp:0}", hpStyle, Color.white);

            // 3) 골드 · 넥타르 (체력 오른쪽 가로 배치, 같은 픽셀 배율)
            var numStyle = new GUIStyle(label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft };
            float x = fr.xMax + 18f;
            int gold = G.Run != null ? G.Run.gold : 0;
            x = DrawCurrency(x, cy, goldIcon, gold.ToString(), numStyle, new Color(1f, 0.9f, 0.55f));
            x += 26f;
            DrawCurrency(x, cy, nectarIcon, G.Profile.nectar.ToString(), numStyle, new Color(0.86f, 0.76f, 1f));

            // 4) 퀘스트 (목표가 없으면 숨김)
            float qy = fr.yMax + 10f;
            var quest = CurrentQuest();
            if (quest.Count > 0) qy = DrawQuestPanel(quest, qy) + 10f;

            // 보스 체력 패널: ShowEnemyHealth 가 꺼져 있으면 배경·테두리째 그리지 않음
            var room = G.Room;
            if (ShowEnemyHealth && room != null && room.Boss != null && room.Boss.IsAlive)
            {
                var b = room.Boss;
                Panel(new Rect(VW * 0.5f - 420, 160, 840, 64));
                Text(new Rect(VW * 0.5f - 410, 162, 600, 30), $"<b>{b.DisplayName}</b>  <color=#BBBBBB>{(b.Phase == 2 ? "2페이즈 · " : "")}{b.PatternName}</color>  {b.Status.Badges()}", small);
                Bar(new Rect(VW * 0.5f - 410, 194, 820, 20), b.Hp / b.MaxHp, new Color(0.9f, 0.3f, 0.35f), new Color(0.15f, 0.05f, 0.05f));
            }

            // 화살통(하단)·오디세우스 충전 막대: 기존 그대로. 오른쪽 위 발사·회수 상태 패널은 그리지 않는다
            var c = G.Combat;
            if (c != null && c.Active && c.Launcher != null)
            {
                if (c.Launcher is OdysseusLaunch od && od.Charging) Bar(new Rect(VW * 0.5f - 120, VH * 0.5f - 120, 240, 12), od.ChargeRatio, new Color(1f, 0.85f, 0.3f), new Color(0, 0, 0, 0.6f));
                DrawQuiver(c);
            }
            else if (G.Run != null) DrawDeckStrip(G.Run);

            // 유물: 오른쪽 위 (상태 패널이 빠진 자리)
            if (G.Run != null && G.Run.relics.Count > 0)
            {
                float y = 20;
                Text(new Rect(VW - 300, y, 280, 26), "<b>유물</b>", tiny);
                foreach (var id in G.Run.relics)
                {
                    y += 24;
                    var r = DB.Relics[id];
                    Text(new Rect(VW - 300, y, 290, 26), $"<color=#{Hex(DB.RarityColors[r.rarity])}>◆</color> {r.name}", tiny);
                }
            }

        }

        // ── 양피지 퀘스트 패널 ──
        // 양피지 PNG(GameRoot.questParchment, 512×183 으로 임포트)를 9-slice 로 그린다: 말린 모서리·올리브 장식이 있는 가장자리는 늘리지 않고
        // 가운데만 늘려 목표 수에 따라 높이가 바뀌어도 장식이 찌그러지지 않는다. 아래 값은 512×183 기준 픽셀.
        // 왼쪽 위 말림(~45), 오른쪽 위 올리브(x438~, y~57), 오른쪽 아래 말림(y139~). RectOffset 은 Unity 객체라 정적 초기화에서 만들 수 없어 처음 쓸 때 만든다
        private const int ParchL = 50, ParchR = 80, ParchT = 62, ParchB = 46;
        private RectOffset parchmentBorder;
        private RectOffset ParchmentBorder => parchmentBorder ?? (parchmentBorder = new RectOffset(ParchL, ParchR, ParchT, ParchB));
        private const float QuestPanelW = 520f;
        private const float QuestTextLeft = 54f, QuestTextRight = 86f, QuestTitleTop = 24f, QuestItemsTop = 58f, QuestBottom = 34f; // 장식을 피한 글자 여백
        private Texture2D parchmentTex; private Texture2D parchmentSrc; private GUIStyle parchmentStyle;

        private GUIStyle ParchmentStyle()
        {
            var src = G != null ? G.questParchment : null;
            if (src == null) return null;
            if (parchmentStyle != null && parchmentSrc == src) return parchmentStyle;
            parchmentSrc = src;
            // Linear 색 공간 보정을 위해 픽셀을 다시 만든다 (원본은 읽기 가능하게 임포트)
            if (src.isReadable)
            {
                parchmentTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                parchmentTex.SetPixels32(ForScreen(src.GetPixels32()));
                parchmentTex.Apply();
            }
            else parchmentTex = src;
            parchmentStyle = new GUIStyle { border = ParchmentBorder };
            parchmentStyle.normal.background = parchmentTex;
            return parchmentStyle;
        }

        /// <summary>퀘스트 패널을 그리고 아래끝 y 를 돌려준다. 제목·체크리스트는 짙은 갈색.</summary>
        private float DrawQuestPanel(List<(string text, bool done)> quest, float y)
        {
            var st = ParchmentStyle();
            float w = QuestPanelW, textW = w - QuestTextLeft - QuestTextRight;
            var itemStyle = new GUIStyle(small) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
            var lines = new List<(string s, float h)>();
            float itemsH = 0f;
            string brown = Hex(ScreenColor(new Color32(0x3B, 0x24, 0x10, 255)));       // 본문: 짙은 갈색
            string doneCol = Hex(ScreenColor(new Color32(0x3E, 0x62, 0x22, 255)));     // 완료 체크: 짙은 올리브
            string doneTxt = Hex(ScreenColor(new Color32(0x6E, 0x58, 0x40, 255)));     // 완료 문구: 흐린 갈색
            string boxCol = Hex(ScreenColor(new Color32(0x5A, 0x3A, 0x1C, 255)));
            foreach (var q in quest)
            {
                string mark = q.done ? $"<color=#{doneCol}>✔</color>" : $"<color=#{boxCol}>□</color>";
                string s = $"{mark} <color=#{(q.done ? doneTxt : brown)}>{q.text}</color>";
                float h = Mathf.Max(26f, itemStyle.CalcHeight(new GUIContent(s), textW));
                lines.Add((s, h));
                itemsH += h + 2f;
            }
            float panelH = Mathf.Max(ParchmentBorder.vertical + 40f, QuestItemsTop + itemsH + QuestBottom);
            var r = new Rect(HudX - 8f, y, w, panelH);
            if (st != null) { if (Event.current.type == EventType.Repaint) st.Draw(r, false, false, false, false); } // Repaint 에서만 (예외 방지)
            else { Rect(r, ScreenColor(new Color(0.07f, 0.06f, 0.08f, 0.93f))); }
            var titleStyle = new GUIStyle(small) { fontSize = 22, fontStyle = FontStyle.Bold };
            InkText(new Rect(r.x + QuestTextLeft, r.y + QuestTitleTop, textW, 32), $"<color=#{Hex(ScreenColor(st != null ? (Color)new Color32(0x5A, 0x30, 0x10, 255) : new Color(1f, 0.85f, 0.45f)))}>퀘스트</color>", titleStyle);
            float ly = r.y + QuestItemsTop;
            foreach (var (s, h) in lines) { InkText(new Rect(r.x + QuestTextLeft, ly, textW, h), s, itemStyle); ly += h + 2f; }
            return r.yMax;
        }

        /// <summary>
        /// 밝은 양피지 위의 어두운 글자. Linear 색 공간 합성에서는 가는 획의 가장자리가 밝게 섞여 글자가 흐려지므로,
        /// 같은 자리에 세 번 + 오른쪽으로 0.6 만큼 한 번 더 겹쳐 그려 획을 진하고 조금 굵게 만든다.
        /// </summary>
        private void InkText(Rect r, string s, GUIStyle st)
        {
            for (int i = 0; i < 3; i++) GUI.Label(r, s, st);
            GUI.Label(new Rect(r.x + 0.6f, r.y, r.width, r.height), s, st);
        }

        private float DrawCurrency(float x, float cy, Texture2D icon, string amount, GUIStyle st, Color color)
        {
            float iw = icon.width * PixelScale, ih = icon.height * PixelScale;
            GUI.DrawTexture(new Rect(x, cy - ih * 0.5f, iw, ih), icon);
            x += iw + 8f;
            float tw = st.CalcSize(new GUIContent(amount)).x;
            OutlinedText(new Rect(x, cy - 18, tw + 10, 36), amount, st, color);
            return x + tw;
        }
    }
}
