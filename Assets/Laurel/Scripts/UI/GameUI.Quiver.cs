using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 하단 화살통 HUD (raurel-quiver-concept.png 시안). 짙은 갈색 패널 + 회색 카드 + 대각선 도트 화살 아이콘 + 왼쪽 아래 피해 숫자.
    /// 다음에 발사할 맨 왼쪽 카드만 탁한 노란색. 왼쪽 띠 = 화살 고유색, 아래 띠 = 희귀도 색.
    /// 기본 화살(0번)은 시안 색 그대로, 특수 화살은 같은 도트 화살을 그 화살 고유색으로 칠해 쓴다(전용 그림 없음).
    /// 순서·수량·피해는 매 프레임 PlayerCombat.Quiver(실제 화살통)를 그대로 읽는다.
    /// </summary>
    public partial class GameUI
    {
        // 패널은 예전 화살통과 같은 자리·크기(가로 최대 1400, 아래에서 128 위, 높이 108) — 플레이 화면을 더 가리지 않게
        private const float QuiverPanelY = 128f, QuiverPanelH = 108f;
        private const float QuiverCardW = 74f, QuiverCardH = 68f, QuiverGap = 10f, QuiverPad = 14f, QuiverTitleH = 32f;
        private const int ArrowIconPx = 16;
        private const float ArrowIconScale = 2.5f;

        // 시안 색 (화면색)
        private static readonly Color QuiverPanelFill = new Color32(0x2A, 0x23, 0x20, 0xF2);
        private static readonly Color QuiverPanelEdge = new Color32(0x74, 0x68, 0x5E, 0xFF);
        private static readonly Color QuiverCardGrey = new Color32(0x5C, 0x5A, 0x63, 0xFF);
        private static readonly Color QuiverCardNext = new Color32(0x7A, 0x6E, 0x48, 0xFF);   // 다음 발사: 탁한 노랑
        private static readonly Color QuiverText = new Color32(0xEC, 0xE4, 0xD4, 0xFF);
        private static readonly Color32 BasicShaft = new Color32(0x8A, 0x5A, 0x34, 255), BasicShaftDark = new Color32(0x5E, 0x3C, 0x22, 255), BasicTip = new Color32(0xEF, 0xE6, 0xD2, 255);

        private readonly Dictionary<int, Texture2D> arrowIcons = new Dictionary<int, Texture2D>();

        /// <summary>대각선(왼쪽 아래 → 오른쪽 위) 도트 화살. 0번(기본 화살)은 시안 색, 나머지는 화살 고유색으로 칠한다.</summary>
        private Texture2D ArrowIcon(ArrowDef d)
        {
            if (arrowIcons.TryGetValue(d.code, out var cached) && cached != null) return cached;
            Color32 tip, shaft, shaftDark;
            if (d.code == 0) { tip = BasicTip; shaft = BasicShaft; shaftDark = BasicShaftDark; }
            else
            {
                Color t = d.tint;
                tip = (Color32)Color.Lerp(t, Color.white, 0.15f);
                shaft = (Color32)new Color(t.r * 0.62f, t.g * 0.62f, t.b * 0.62f, 1f);
                shaftDark = (Color32)new Color(t.r * 0.4f, t.g * 0.4f, t.b * 0.4f, 1f);
            }
            int n = ArrowIconPx;
            var px = new Color32[n * n];
            void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < n && y < n) px[(n - 1 - y) * n + x] = c; } // 위쪽 기준 좌표
            // 화살대: 2픽셀 두께 대각선
            for (int i = 4; i <= 11; i++) { Set(i, 15 - i, shaft); Set(i + 1, 15 - i, shaftDark); }
            // 화살촉: 오른쪽 위를 향한 삼각형
            for (int y = 1; y <= 4; y++) for (int x = 10; x <= 14; x++) if (x - 10 >= y - 1 && (x - 10) + (4 - y) >= 3) Set(x, y, tip);
            // 깃: 화살대 끝(왼쪽 아래)에 V 모양
            Set(2, 11, tip); Set(3, 11, tip); Set(3, 12, tip);
            Set(4, 13, tip); Set(4, 14, tip); Set(3, 13, tip);
            Set(1, 12, tip); Set(4, 15, tip);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(ForScreen(px));
            tex.Apply();
            arrowIcons[d.code] = tex;
            return tex;
        }

        /// <summary>검증용: 화면에 그리는 순서대로 (이름, 카드 피해, 고유색, 희귀도 색, 다음 발사 강조)</summary>
        public List<(string name, float damage, Color stripe, Color rarity, bool next)> QuiverCardsShown()
        {
            var list = new List<(string, float, Color, Color, bool)>();
            var c = G != null ? G.Combat : null;
            if (c == null || c.Quiver == null) return list;
            for (int i = 0; i < c.Quiver.Count; i++)
            {
                var b = c.Quiver.Order[i]; var d = b.Card.Def;
                list.Add((d.name, b.Card.Damage, d.tint, DB.RarityColors[d.rarity], i == 0));
            }
            return list;
        }

        private void DrawQuiver(PlayerCombat c)
        {
            var q = c.Quiver;
            float w = Mathf.Min(1400f, VW - 80f);
            var r = new Rect(VW * 0.5f - w * 0.5f, VH - QuiverPanelY, w, QuiverPanelH);
            // 패널: 짙은 갈색 + 탁한 갈색 테두리 (Linear 보정)
            Rect(r, ScreenColor(QuiverPanelEdge));
            Rect(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), ScreenColor(QuiverPanelFill));
            OutlinedText(new Rect(r.x + QuiverPad + 4, r.y + 3, 400, 28), $"화살통  {q.Count}/{c.Bodies.Count}", new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false }, QuiverText, 1f);

            float x = r.x + QuiverPad, y = r.y + QuiverTitleH;
            int maxShow = Mathf.Max(1, (int)((w - QuiverPad * 2 + QuiverGap) / (QuiverCardW + QuiverGap)));
            var numStyle = new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.LowerLeft };
            for (int i = 0; i < q.Count && i < maxShow; i++)
            {
                var card = q.Order[i].Card; var d = card.Def;
                var cell = new Rect(x, y, QuiverCardW, QuiverCardH);
                Rect(cell, ScreenColor(i == 0 ? QuiverCardNext : QuiverCardGrey));
                Rect(new Rect(cell.x, cell.y, 5, cell.height), ScreenColor(d.tint));                                  // 왼쪽 띠: 고유색
                Rect(new Rect(cell.x, cell.yMax - 4, cell.width, 4), ScreenColor(DB.RarityColors[d.rarity]));          // 아래 띠: 희귀도
                float s = ArrowIconPx * ArrowIconScale;
                GUI.DrawTexture(new Rect(cell.x + (cell.width - s) * 0.5f + 6f, cell.y + 4f, s, s), ArrowIcon(d));
                OutlinedText(new Rect(cell.x + 9, cell.yMax - 27, 60, 22), $"{card.Damage:0.#}", numStyle, QuiverText, 1f);
                x += QuiverCardW + QuiverGap;
            }
            if (q.Count > maxShow) OutlinedText(new Rect(x, y + 18, 80, 30), $"+{q.Count - maxShow}", small, QuiverText, 1f);
            if (q.Count == 0) OutlinedText(new Rect(r.x + QuiverPad, y + 18, 700, 30), "화살통이 비었습니다 — 화살을 회수하세요", small, new Color(1f, 0.6f, 0.5f), 1f);
        }
    }
}
