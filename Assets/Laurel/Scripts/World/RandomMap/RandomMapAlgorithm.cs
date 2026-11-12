using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>생성 결과. solid[x,y] = 지형 칸. 칸 (x,y) 는 월드 [x,x+1]×[y,y+1].</summary>
    public class RandomMapData
    {
        public int seed, usedSeed, width, height, attempts;
        public bool[,] solid;
        /// <summary>플레이어가 서는 칸 (발 아래 칸이 지형, 이 칸과 위 headroom 칸이 비어 있음)</summary>
        public Vector2Int spawn, portal;
        public readonly List<RectInt> platforms = new List<RectInt>();
        public int removedPlatforms;
        public int reachableStandCount, standCount;

        public bool Solid(int x, int y) => x < 0 || x >= width || y < 0 || (y < height && solid[x, y]);

        /// <summary>같은 시드 재현 확인용 지문</summary>
        public int Hash()
        {
            unchecked
            {
                int h = 17;
                for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) if (solid[x, y]) h = h * 31 + x * 977 + y;
                return h * 31 + spawn.GetHashCode() * 7 + portal.GetHashCode();
            }
        }
    }

    /// <summary>
    /// 옆에서 보는 2D 플랫포머용 랜덤 맵 생성.
    /// 1) 지면: 좌→우로 폭이 정해진 구간마다 높이를 랜덤 워크 (오르막은 한 번 점프로 오를 수 있는 높이까지만)
    /// 2) 공중 발판: 아래 지형에서 점프로 닿는 높이에 무작위 배치, 다른 지형과 여백을 둔다 (Rule Tile 모서리가 서로 붙지 않게)
    /// 3) 도달 판정: '설 수 있는 칸'을 노드로 걷기·점프(위로 reachUp, 옆으로 reachAcross)·낙하 간선을 이은 그래프에서 BFS.
    ///    점프는 '제자리에서 올라간 뒤 옆으로', 낙하는 '옆으로 나간 뒤 내려가기' 경로가 막히지 않을 때만 인정 (보수적).
    ///    시작 지점에서 닿지 않는 발판은 지운다 → 남은 모든 지형 위는 시작 지점에서 갈 수 있다.
    /// 4) 복귀 포탈: 시작 지점에서 갈 수 있고 포탈에서 시작 지점으로도 돌아올 수 있는 칸 중 가장 먼 평지.
    /// 조건을 못 맞추면 시드에서 유도한 다음 시드로 다시 만든다 (같은 시드 → 항상 같은 결과).
    /// </summary>
    public static class RandomMapAlgorithm
    {
        public static RandomMapData Generate(RandomMapSettings s, int seed)
        {
            RandomMapData last = null;
            for (int attempt = 0; attempt < Mathf.Max(1, s.maxRetries); attempt++)
            {
                int used = attempt == 0 ? seed : unchecked(seed * 486187739 + attempt * 16777619);
                var d = TryGenerate(s, used);
                d.seed = seed;
                d.attempts = attempt + 1;
                last = d;
                if (d.portal.x >= 0) return d;
            }
            return last;
        }

        private static RandomMapData TryGenerate(RandomMapSettings s, int seed)
        {
            var rng = new System.Random(seed);
            int W = s.width, H = s.height, wall = s.wallThickness;
            var d = new RandomMapData { usedSeed = seed, width = W, height = H, solid = new bool[W, H], portal = new Vector2Int(-1, -1) };

            // 외벽
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (x < wall || x >= W - wall) d.solid[x, y] = true;

            // 1) 지면 높이 랜덤 워크
            int gMin = Mathf.Max(2, s.groundMin), gMax = Mathf.Clamp(s.groundMax, gMin, H - s.reachUp - 6);
            var ground = new int[W];
            int xCur = wall, h = Mathf.Clamp(gMin + 1, gMin, gMax);
            int segMin = Mathf.Max(2, s.segmentWidth.x), segMax = Mathf.Max(segMin, s.segmentWidth.y);
            bool first = true;
            while (xCur < W - wall)
            {
                int seg = first ? Mathf.Max(s.spawnFlatWidth, segMin) : rng.Next(segMin, segMax + 1);
                if (!first) h = Mathf.Clamp(h + rng.Next(-s.maxStepDown, s.maxStepUp + 1), gMin, gMax);
                int end = Mathf.Min(W - wall, xCur + seg);
                if (W - wall - end < segMin) end = W - wall; // 마지막에 2칸 미만 기둥이 남지 않게 흡수
                for (int x = xCur; x < end; x++) ground[x] = h;
                xCur = end;
                first = false;
            }
            for (int x = wall; x < W - wall; x++) for (int y = 0; y < ground[x]; y++) d.solid[x, y] = true;

            // 2) 공중 발판
            int target = rng.Next(s.platformCount.x, s.platformCount.y + 1);
            int pwMin = Mathf.Max(3, s.platformWidth.x), pwMax = Mathf.Max(pwMin, s.platformWidth.y);
            int thick = Mathf.Max(2, s.platformThickness), margin = Mathf.Max(1, s.platformMargin);
            for (int tries = 0; tries < s.platformAttempts && d.platforms.Count < target; tries++)
            {
                int pw = rng.Next(pwMin, pwMax + 1);
                int px = rng.Next(wall + margin + 1, W - wall - margin - pw);
                if (px < wall + 1) continue;
                // 발판 아래에서 가장 높은 지형 윗면 (지면 또는 다른 발판)
                int below = 0;
                for (int x = px - 1; x <= px + pw; x++) below = Mathf.Max(below, TopUnder(d, x, H - 1));
                int bottom = below + Mathf.Max(s.platformClearance, s.headroom + 1) + rng.Next(0, Mathf.Max(1, s.reachUp - s.platformClearance - thick + 2));
                int top = bottom + thick; // 윗면 (서는 칸 y = top)
                if (top - below > s.reachUp + thick + 2) continue;
                if (top + s.headroom + 2 >= H) continue;
                var r = new RectInt(px, bottom, pw, thick);
                // 아래로는 지나갈 공간, 위로는 그 위에 설 공간(먼저 놓인 발판과의 간격)까지 비어 있어야 한다
                int above = Mathf.Max(margin, s.platformClearance);
                if (!Free(d, new RectInt(r.x - margin, r.y - s.platformClearance, r.width + margin * 2, r.height + s.platformClearance + above))) continue;
                for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) d.solid[x, y] = true;
                d.platforms.Add(r);
            }

            // 3) 도달 판정
            d.spawn = new Vector2Int(wall + 2, ground[wall + 2]);
            var reach = Reachable(d, s, d.spawn, false);
            for (int i = d.platforms.Count - 1; i >= 0; i--)
            {
                var r = d.platforms[i];
                bool ok = false;
                for (int x = r.xMin; x < r.xMax && !ok; x++) ok = reach.ContainsKey(new Vector2Int(x, r.yMax));
                if (ok) continue;
                for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) d.solid[x, y] = false;
                d.platforms.RemoveAt(i);
                d.removedPlatforms++;
            }
            if (d.removedPlatforms > 0) reach = Reachable(d, s, d.spawn, false);
            d.reachableStandCount = reach.Count;
            d.standCount = CountStand(d, s);

            // 4) 복귀 포탈: 갈 수 있고 돌아올 수 있는 가장 먼 평지 (좌우 1칸도 같은 높이로 설 수 있음, 머리 위 3칸 비어 있음)
            var back = Reachable(d, s, d.spawn, true); // 역방향: 그 칸에서 시작 지점으로 갈 수 있는가
            int best = -1;
            foreach (var kv in reach)
            {
                var c = kv.Key;
                if (!back.ContainsKey(c)) continue;
                if (Mathf.Abs(c.x - d.spawn.x) < s.minPortalDistance) continue;
                if (!Stand(d, s, c + Vector2Int.left) || !Stand(d, s, c + Vector2Int.right)) continue;
                if (d.Solid(c.x, c.y + 3) || d.Solid(c.x - 1, c.y + 2) || d.Solid(c.x + 1, c.y + 2)) continue;
                int score = kv.Value * 4 + Mathf.Abs(c.x - d.spawn.x);
                if (score > best) { best = score; d.portal = c; }
            }
            return d;
        }

        private static int TopUnder(RandomMapData d, int x, int fromY)
        {
            for (int y = fromY; y >= 0; y--) if (d.Solid(x, y)) return y + 1;
            return 0;
        }

        private static bool Free(RandomMapData d, RectInt r)
        {
            for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++)
                if (x >= 0 && x < d.width && y >= 0 && y < d.height && d.solid[x, y]) return false;
            return r.yMin >= 0;
        }

        public static bool Stand(RandomMapData d, RandomMapSettings s, Vector2Int c)
        {
            if (c.x < 0 || c.x >= d.width || c.y < 1 || c.y >= d.height) return false;
            if (!d.Solid(c.x, c.y - 1)) return false;
            for (int k = 0; k < s.headroom; k++) if (d.Solid(c.x, c.y + k)) return false;
            return true;
        }

        private static int CountStand(RandomMapData d, RandomMapSettings s)
        {
            int n = 0;
            for (int x = 0; x < d.width; x++) for (int y = 1; y < d.height; y++) if (Stand(d, s, new Vector2Int(x, y))) n++;
            return n;
        }

        /// <summary>
        /// from 에서 갈 수 있는 설 수 있는 칸 → BFS 거리. reverse 면 '그 칸에서 from 으로 갈 수 있는' 칸.
        /// </summary>
        public static Dictionary<Vector2Int, int> Reachable(RandomMapData d, RandomMapSettings s, Vector2Int from, bool reverse)
        {
            var stands = new List<Vector2Int>();
            for (int x = 0; x < d.width; x++) for (int y = 1; y < d.height; y++) { var c = new Vector2Int(x, y); if (Stand(d, s, c)) stands.Add(c); }
            var dist = new Dictionary<Vector2Int, int>();
            if (!Stand(d, s, from)) return dist;
            var q = new Queue<Vector2Int>();
            dist[from] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                var a = q.Dequeue();
                foreach (var b in stands)
                {
                    if (dist.ContainsKey(b)) continue;
                    if (Mathf.Abs(b.x - a.x) > s.reachAcross) continue;
                    bool ok = reverse ? CanMove(d, s, b, a) : CanMove(d, s, a, b);
                    if (!ok) continue;
                    dist[b] = dist[a] + 1;
                    q.Enqueue(b);
                }
            }
            return dist;
        }

        /// <summary>a 에서 b 로 걷기·점프·낙하로 갈 수 있는가 (보수적인 L자 경로)</summary>
        public static bool CanMove(RandomMapData d, RandomMapSettings s, Vector2Int a, Vector2Int b)
        {
            int dx = b.x - a.x, dy = b.y - a.y;
            if (dx == 0 && dy == 0) return false;
            if (Mathf.Abs(dx) > s.reachAcross) return false;
            int step = dx > 0 ? 1 : -1;
            int hr = s.headroom;
            if (dy == 0 && Mathf.Abs(dx) == 1) return true; // 옆 칸으로 걷기 (둘 다 설 수 있는 칸)
            if (dy > 0)
            {
                if (dy > s.reachUp) return false;
                // 제자리에서 b 높이 + 머리 공간까지 올라간 뒤 옆으로 이동
                for (int y = a.y; y < b.y + hr; y++) if (d.Solid(a.x, y)) return false;
                for (int x = a.x; x != b.x + step && dx != 0; x += step) for (int y = b.y; y < b.y + hr; y++) if (d.Solid(x, y)) return false;
                return true;
            }
            // 같은 높이로 건너기 또는 낙하: 현재 높이에서 b 열까지 옆으로 이동한 뒤 수직으로 내려간다
            if (dx == 0) return false;
            int jumpY = dy == 0 ? 1 : 0; // 같은 높이 사이 틈은 살짝 뛰어 넘는다 (1칸 위로)
            for (int x = a.x; x != b.x + step; x += step) for (int y = a.y; y < a.y + hr + jumpY; y++) if (d.Solid(x, y)) return false;
            for (int y = b.y; y < a.y; y++) if (d.Solid(b.x, y)) return false;
            return true;
        }

        /// <summary>
        /// Rule Tile 로 그릴 수 있는지 검사: 두께 1칸(위아래 또는 좌우가 동시에 빈) 지형 칸이 없어야 한다.
        /// 맵 밖은 지형으로 친다(화면 밖까지 채워 그린다).
        /// </summary>
        public static int CountThinCells(RandomMapData d)
        {
            int n = 0;
            for (int x = 0; x < d.width; x++)
                for (int y = 0; y < d.height; y++)
                {
                    if (!d.solid[x, y]) continue;
                    bool up = Above(d, x, y + 1), down = d.Solid(x, y - 1), left = d.Solid(x - 1, y), right = d.Solid(x + 1, y);
                    if ((!up && !down) || (!left && !right)) n++;
                }
            return n;
        }

        // 맵 위쪽 바깥은 비어 있다고 본다 (외벽은 생성기에서 위로 더 이어 그린다)
        private static bool Above(RandomMapData d, int x, int y) => y >= d.height ? false : d.Solid(x, y);
    }
}
