using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 화살 선택 카드의 픽셀 아트 테두리. 금색 장식 테두리 + 어두운 바탕의 고풍스러운 세로형 카드.
    /// 플레이어 스프라이트와 같은 픽셀 아트 질감이 나도록 저해상도(Width×Height 픽셀)로 그리고
    /// Point 필터로 정수배 확대해 그린다. 참고 이미지(Docs/DesignReference/arrow_selection_reference.png)는
    /// 분위기 참고용이며 화면에 쓰지 않는다.
    /// </summary>
    public static class ArrowCardArt
    {
        public const int Width = 68;
        public const int Height = 100;

        // 카드 안쪽 영역 (아트 픽셀 단위, 위쪽 기준) — GameUI 가 글자 배치에 사용
        public static readonly RectInt NameArea = new RectInt(9, 21, 50, 11);
        public static readonly RectInt DescArea = new RectInt(12, 38, 44, 37);
        public static readonly RectInt CountArea = new RectInt(14, 81, 40, 11);

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Ink = new Color32(26, 16, 12, 255);
        private static readonly Color32 GoldHi = new Color32(244, 230, 186, 255);
        private static readonly Color32 Gold = new Color32(218, 190, 128, 255);
        private static readonly Color32 GoldLo = new Color32(150, 112, 58, 255);
        private static readonly Color32 GoldDeep = new Color32(92, 64, 32, 255);
        private static readonly Color32 Field = new Color32(32, 22, 24, 255);
        private static readonly Color32 FieldDark = new Color32(22, 15, 17, 255);
        private static readonly Color32 FieldLift = new Color32(42, 30, 32, 255);

        private static Texture2D frame;
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>테두리·장식만 그린 카드 (가운데 보석은 화살 색으로 따로 칠한다)</summary>
        public static Texture2D Frame => frame != null ? frame : (frame = Build());

        /// <summary>윗 장식의 화살촉 보석 위치 (아트 픽셀, 위쪽 기준)</summary>
        public static readonly RectInt GemArea = new RectInt(31, 4, 6, 7);

        private static Texture2D Build()
        {
            var px = new Color32[Width * Height];
            for (int i = 0; i < px.Length; i++) px[i] = Clear;

            void Set(int x, int y, Color32 c)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return;
                px[(Height - 1 - y) * Width + x] = c; // 텍스처는 아래쪽이 0
            }
            Color32 Get(int x, int y) => (x < 0 || y < 0 || x >= Width || y >= Height) ? Clear : px[(Height - 1 - y) * Width + x];

            // ── 1. 카드 몸체: 모서리를 깎고 옆구리가 살짝 들어간 실루엣 ──
            const int top = 12, bottom = Height - 5, left = 3, right = Width - 4;
            int Inset(int y)
            {
                // 모서리 깎기
                int dTop = y - top, dBot = bottom - y;
                int corner = 0;
                if (dTop < 5) corner = 5 - dTop;
                if (dBot < 5) corner = Mathf.Max(corner, 5 - dBot);
                // 옆구리 홈 (위 1/3, 아래 1/3 지점)
                int waist = 0;
                foreach (int wy in new[] { 40, 72 })
                {
                    int d = Mathf.Abs(y - wy);
                    if (d <= 3) waist = Mathf.Max(waist, d <= 1 ? 2 : 1);
                }
                return Mathf.Max(corner, waist);
            }
            bool Inside(int x, int y) => y >= top && y <= bottom && x >= left + Inset(y) && x <= right - Inset(y);
            int Depth(int x, int y)
            {
                // 바깥 경계까지의 체비셰프 거리 (0 = 경계 픽셀)
                if (!Inside(x, y)) return -1;
                for (int d = 0; d < 8; d++)
                    for (int dy = -d - 1; dy <= d + 1; dy++)
                        for (int dx = -d - 1; dx <= d + 1; dx++)
                            if ((Mathf.Abs(dx) == d + 1 || Mathf.Abs(dy) == d + 1) && !Inside(x + dx, y + dy)) return d;
                return 8;
            }

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int d = Depth(x, y);
                    if (d < 0) continue;
                    bool lit = (x - Width * 0.5f) * 0.6f + (y - Height * 0.5f) < 0f; // 왼쪽 위가 밝다
                    Color32 c;
                    if (d == 0) c = Ink;
                    else if (d == 1) c = lit ? GoldHi : Gold;
                    else if (d == 2) c = lit ? Gold : GoldLo;
                    else if (d == 3) c = GoldDeep;
                    else if (d == 4) c = Ink;
                    else if (d == 5) c = GoldLo;
                    else
                    {
                        // 바탕: 가장자리는 어둡게, 가운데는 살짝 밝게 (은은한 비네트)
                        float nx = (x - Width * 0.5f) / (Width * 0.5f), ny = (y - (top + bottom) * 0.5f) / ((bottom - top) * 0.5f);
                        float v = nx * nx + ny * ny;
                        // 4×4 순서 디더로 단계 경계를 부드럽게 (픽셀 아트식 그라데이션)
                        float th = Bayer[(y & 3) * 4 + (x & 3)] / 16f;
                        c = v > 0.62f + th * 0.3f ? FieldDark : Field;
                    }
                    Set(x, y, c);
                }

            // ── 2. 네 모서리의 소용돌이 장식 ──
            void Spiral(int cx, int cy, int sx, int sy)
            {
                // 지름 7의 둥근 금 장식 + 안쪽 소용돌이
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int r2 = dx * dx + dy * dy;
                        if (r2 > 11) continue;
                        Set(cx + dx, cy + dy, r2 >= 7 ? Ink : (r2 >= 4 ? ((dx * sx + dy * sy) < 0 ? GoldHi : Gold) : GoldLo));
                    }
                Set(cx, cy, GoldHi);
                Set(cx + sx, cy, Ink);
                Set(cx + sx, cy + sy, Ink);
                Set(cx, cy + sy, GoldDeep);
                // 안쪽으로 뻗는 잎 장식
                for (int k = 1; k <= 4; k++)
                {
                    Set(cx + sx * (3 + k), cy + sy * (2 + k / 2), k == 4 ? GoldLo : Gold);
                    Set(cx + sx * (2 + k / 2), cy + sy * (3 + k), k == 4 ? GoldLo : Gold);
                }
            }
            Spiral(left + 3, top + 3, 1, 1);
            Spiral(right - 3, top + 3, -1, 1);
            Spiral(left + 3, bottom - 3, 1, -1);
            Spiral(right - 3, bottom - 3, -1, -1);

            // ── 3. 옆구리 홈의 작은 금 구슬 ──
            foreach (int wy in new[] { 40, 72 })
                foreach (int sx in new[] { left + 2, right - 2 })
                {
                    Set(sx, wy - 1, Gold); Set(sx, wy, GoldHi); Set(sx, wy + 1, GoldLo);
                    Set(sx - 1, wy, Ink); Set(sx + 1, wy, Ink);
                }

            // ── 4. 윗 장식: 초승달 받침 + 화살촉 ──
            int mx = Width / 2;
            for (int dy = -9; dy <= 9; dy++)
                for (int dx = -9; dx <= 9; dx++)
                {
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    int x = mx + dx - 1, y = top + dy;
                    if (dy < -2) continue; // 아래쪽 반달만
                    if (r >= 5.5f && r < 7.5f) Set(x, y, r < 6.3f ? GoldHi : Gold);
                    else if (r >= 7.5f && r < 8.5f) Set(x, y, Ink);
                    else if (r < 5.5f && y >= top && Get(x, y).a > 0) Set(x, y, Field);
                }
            // 초승달 끝의 뿔 장식
            foreach (int s in new[] { -1, 1 })
            {
                int bx = mx - 1 + s * 7;
                Set(bx, top - 2, Gold); Set(bx + s, top - 3, GoldHi); Set(bx + s * 2, top - 4, Gold); Set(bx + s * 2, top - 3, Ink);
                Set(bx, top - 1, GoldLo); Set(bx + s, top - 2, Ink);
            }
            // 화살촉 (보석이 박힐 자리 테두리) + 화살대
            for (int y = 1; y <= 12; y++)
            {
                int half = y <= 9 ? (y + 1) / 3 + (y >= 7 ? 1 : 0) : 0;
                for (int dx = -half - 1; dx <= half + 1; dx++)
                {
                    bool edge = Mathf.Abs(dx) == half + 1;
                    int x = mx - 1 + dx;
                    if (y > 9) { if (Mathf.Abs(dx) <= 1) Set(x, y, dx == 0 ? GoldLo : Ink); continue; }
                    Set(x, y, edge ? Ink : (dx < 0 ? GoldHi : Gold));
                }
            }
            for (int y = 13; y <= 15; y++) { Set(mx - 1, y, Gold); Set(mx - 2, y, Ink); Set(mx, y, Ink); }

            // ── 5. 아래 장식: 마름모 ──
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int m = Mathf.Abs(dx) + Mathf.Abs(dy);
                    if (m > 3) continue;
                    Set(mx - 1 + dx, bottom + dy, m == 3 ? Ink : (m == 0 ? GoldDeep : (dx + dy < 0 ? GoldHi : Gold)));
                }

            // ── 6. 구분선: 이름 아래, 수량 위 (가운데 마름모) ──
            void Divider(int y, int x0, int x1)
            {
                for (int x = x0; x <= x1; x++)
                {
                    bool fade = x - x0 < 3 || x1 - x < 3;
                    Set(x, y, fade ? GoldLo : Gold);
                    Set(x, y + 1, GoldDeep);
                }
                Set(mx - 1, y - 2, Gold); Set(mx - 2, y - 1, Gold); Set(mx, y - 1, GoldHi); Set(mx - 1, y - 1, GoldHi);
                Set(mx - 3, y, GoldHi); Set(mx + 1, y, GoldHi); Set(mx - 2, y + 1, Gold); Set(mx, y + 1, Gold); Set(mx - 1, y + 2, GoldLo);
            }
            Divider(34, 13, 54);
            Divider(78, 17, 50);

            // ── 7. 능력 설명 칸: 한 단 낮은 어두운 판 + 모서리 금장 꺾쇠 ──
            var da = DescArea;
            for (int y = da.y; y < da.yMax; y++)
                for (int x = da.x; x < da.xMax; x++)
                {
                    bool edge = x == da.x || y == da.y || x == da.xMax - 1 || y == da.yMax - 1;
                    bool shade = x == da.x + 1 || y == da.y + 1; // 위·왼쪽 안쪽 그림자 = 파인 느낌
                    Set(x, y, edge ? GoldDeep : (shade ? Ink : FieldDark));
                }
            void Bracket(int x, int y, int sx, int sy)
            {
                for (int k = 0; k < 4; k++) { Set(x + sx * k, y, k == 3 ? GoldLo : Gold); Set(x, y + sy * k, k == 3 ? GoldLo : Gold); }
                Set(x, y, GoldHi);
            }
            Bracket(da.x, da.y, 1, 1);
            Bracket(da.xMax - 1, da.y, -1, 1);
            Bracket(da.x, da.yMax - 1, 1, -1);
            Bracket(da.xMax - 1, da.yMax - 1, -1, -1);

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "ArrowCardFrame"
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D gem;

        /// <summary>윗 장식 화살촉에 박는 보석 (흰색, GUI.color 로 화살 색을 입힌다)</summary>
        public static Texture2D Gem
        {
            get
            {
                if (gem != null) return gem;
                // GemArea 크기 6×7: 위가 뾰족한 화살촉 모양, 왼쪽 위 하이라이트
                string[] rows =
                {
                    "..##..",
                    "..#H..",
                    ".##H#.",
                    ".#H##.",
                    "######",
                    "#####s",
                    ".ssss.",
                };
                var px = new Color32[6 * 7];
                for (int y = 0; y < 7; y++)
                    for (int x = 0; x < 6; x++)
                    {
                        char ch = rows[y][x];
                        Color32 c = ch == '#' ? new Color32(220, 220, 220, 255) : ch == 'H' ? new Color32(255, 255, 255, 255) : ch == 's' ? new Color32(150, 150, 150, 255) : new Color32(0, 0, 0, 0);
                        px[(6 - y) * 6 + x] = c;
                    }
                gem = new Texture2D(6, 7, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "ArrowCardGem" };
                gem.SetPixels32(px);
                gem.Apply(false, true);
                return gem;
            }
        }
    }
}
