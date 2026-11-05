using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public class RoomTheme
    {
        public Color bg, bgFar, terrain, platform, accent;
        public string name;
        /// <summary>지형 모양(두께·색·테두리). null 이면 예전 단색 사각형으로 그린다.</summary>
        public TerrainStyle style;

        public static RoomTheme FromStage(StageDef s) => new RoomTheme
        {
            name = s.name, bg = DB.Hex(s.bg), bgFar = DB.Hex(s.bgFar), terrain = DB.Hex(s.terrain), platform = DB.Hex(s.platform), accent = DB.Hex(s.accent)
        };

        public static RoomTheme Tutorial => new RoomTheme
        {
            name = "델포이 신전 앞", bg = new Color(0.16f, 0.2f, 0.28f), bgFar = new Color(0.3f, 0.36f, 0.48f), terrain = new Color(0.55f, 0.52f, 0.45f), platform = new Color(0.75f, 0.7f, 0.6f), accent = new Color(1f, 0.85f, 0.45f)
        };

        public static RoomTheme Lobby => new RoomTheme
        {
            name = "델포이 성소", bg = new Color(0.12f, 0.1f, 0.16f), bgFar = new Color(0.26f, 0.2f, 0.3f), terrain = new Color(0.45f, 0.38f, 0.32f), platform = new Color(0.7f, 0.6f, 0.45f), accent = new Color(1f, 0.8f, 0.4f)
        };
    }

    public enum ZoneKind { Web, Oil, Fire, Wave }

    public class Zone
    {
        public ZoneKind kind;
        public Vector2 center;
        public float radius, life, maxLife;
        public SpriteRenderer sr;
    }

    /// <summary>골드 줍기</summary>
    public class GoldPickup : MonoBehaviour
    {
        public int value;
        public Room room;
        private Vector2 vel;
        private bool magnet;
        private float age;

        public void Init(Room r, int v, Vector2 velocity) { room = r; value = v; vel = velocity; }
        public void Magnet() { magnet = true; }

        private void Update()
        {
            if (room == null || room.Player == null) return;
            float dt = Time.deltaTime;
            age += dt;
            Vector2 pos = transform.position;
            Vector2 pp = (Vector2)room.Player.transform.position + Vector2.up * 0.1f;
            float d = Vector2.Distance(pos, pp);
            if ((magnet || d < 2.6f) && age > 0.25f)
            {
                vel = Vector2.MoveTowards(vel, (pp - pos).normalized * 16f, 60f * dt);
                transform.position = pos + vel * dt;
                if (d < 0.45f) { room.CollectGold(this); }
                return;
            }
            vel.y -= 25f * dt;
            Vector2 next = pos + vel * dt;
            var hit = Physics2D.Linecast(pos, next, Layers.TerrainMask);
            if (hit.collider != null && !hit.collider.isTrigger) { transform.position = hit.point + hit.normal * 0.12f; vel = new Vector2(vel.x * 0.4f, Mathf.Abs(vel.y) * 0.3f); if (vel.y < 1f) vel = Vector2.zero; }
            else transform.position = next;
        }
    }

    /// <summary>
    /// 노드 하나의 맵(작은 정사각형 방) 또는 로비. 지형·적·장판·골드를 관리하고 '마지막 적 처치'를 알린다.
    /// </summary>
    public class Room : MonoBehaviour
    {
        public RoomTheme Theme { get; private set; }
        public Rect InnerBounds { get; private set; }
        public Vector2 PlayerSpawn { get; private set; }
        public PlayerAvatar Player { get; set; }
        public PlayerCombat Combat { get; set; }
        public float HpMul { get; private set; } = 1f;
        public float DmgMul { get; private set; } = 1f;
        public readonly List<Enemy> Enemies = new List<Enemy>();
        public readonly List<Zone> Zones = new List<Zone>();
        public readonly List<Rect> Platforms = new List<Rect>();
        private readonly List<EnemyProjectile> projectiles = new List<EnemyProjectile>();
        private readonly List<GoldPickup> golds = new List<GoldPickup>();

        public bool Cleared { get; private set; }
        public bool CombatRoom { get; private set; }
        public System.Action OnCleared;
        public System.Action<Enemy> OnEnemyKilled;
        public System.Action<int> OnGoldCollected;
        public int KillCount { get; private set; }

        private readonly List<List<SpawnSpec>> waves = new List<List<SpawnSpec>>();
        private int waveIndex = -1;
        private float waveDelay;
        public int WaveIndex => waveIndex;
        public int WaveCount => waves.Count;
        public Boss Boss { get; private set; }

        public struct SpawnSpec { public EnemyDef def; public bool elite; }

        // ───────────── 생성 ─────────────

        public static Room Create(string name, RoomTheme theme, float size, int layout, int seed)
        {
            var go = new GameObject("Room_" + name);
            var room = go.AddComponent<Room>();
            room.Build(theme, size, size, layout, seed);
            return room;
        }

        public static Room CreateLobby(float width, TerrainStyle style = null)
        {
            var go = new GameObject("Room_Lobby");
            var room = go.AddComponent<Room>();
            var theme = RoomTheme.Lobby;
            theme.style = style;
            room.Build(theme, width, 14f, -1, 1);
            return room;
        }

        // [임시 결정] 정사각형 방의 발판 배치 템플릿 (방 크기 22 기준 좌표: x, y, 길이)
        private static readonly float[][][] Layouts =
        {
            new[] { new[] { -6f, -6f, 5f }, new[] { 6f, -6f, 5f }, new[] { 0f, -1.5f, 6f }, new[] { -7f, 3f, 4f }, new[] { 7f, 3f, 4f }, new[] { 0f, 7f, 5f } },
            new[] { new[] { -5f, -7f, 6f }, new[] { 5f, -3f, 6f }, new[] { -5f, 1f, 6f }, new[] { 5f, 5f, 6f } },
            new[] { new[] { 0f, -7f, 8f }, new[] { -8f, -3f, 3f }, new[] { 8f, -3f, 3f }, new[] { -4f, 2f, 4f }, new[] { 4f, 2f, 4f }, new[] { 0f, 6.5f, 3f } },
            new[] { new[] { -7.5f, -6.5f, 4f }, new[] { 0f, -4f, 4f }, new[] { 7.5f, -1.5f, 4f }, new[] { 0f, 2.5f, 4f }, new[] { -7.5f, 5f, 4f } },
            new[] { new[] { -6.5f, -5.5f, 5f }, new[] { 6.5f, -5.5f, 5f }, new[] { 0f, 0f, 5f } },           // 보스용 (넓은 공간)
        };
        public const int BossLayout = 4;

        private void Build(RoomTheme theme, float width, float height, int layout, int seed)
        {
            Theme = theme;
            var rng = new System.Random(seed);
            float hw = width * 0.5f, hh = height * 0.5f;
            InnerBounds = new Rect(-hw, -hh, width, height);
            float wall = DB.Balance.world.wallThickness;

            // 임시 배경 (단색 + 도형). RoomBackdrop 이 붙으면 SetTempBackgroundVisible(false) 로 숨긴다
            tempBackground.Add(Art.Quad(transform, "BgFar", Art.Square, theme.bgFar, new Vector2(width + 40f, height + 30f), -30, new Vector3(0, 0, 5)).gameObject);
            tempBackground.Add(Art.Quad(transform, "Bg", Art.Square, theme.bg, new Vector2(width, height), -25, new Vector3(0, 0, 4)).gameObject);
            for (int i = 0; i < 9; i++)
            {
                float x = Mathf.Lerp(-hw + 1f, hw - 1f, (float)rng.NextDouble());
                float s = 1.5f + (float)rng.NextDouble() * 3f;
                var deco = Art.Quad(transform, "Deco", layout < 0 ? Art.Square : (i % 2 == 0 ? Art.Triangle : Art.Circle), theme.bgFar.WithAlpha(0.55f), new Vector2(s * 0.8f, s * 1.6f), -24, new Vector3(x, -hh + s * 0.8f, 3));
                deco.color = Color.Lerp(theme.bg, theme.bgFar, 0.6f);
                tempBackground.Add(deco.gameObject);
            }

            // 외벽 (바닥·천장·좌우). 스타일이 있으면 플레이 공간 쪽 면에만, 방 안쪽 범위(-hw~hw, -hh~hh)만큼 테두리를 그린다
            var outer = theme.style != null && theme.style.styleOuterBlocks ? theme.style : null;
            var floor = Solid("Floor", new Vector2(0, -hh - wall * 0.5f), new Vector2(width + wall * 2f, wall), theme.terrain, outer);
            var ceil = Solid("Ceil", new Vector2(0, hh + wall * 0.5f), new Vector2(width + wall * 2f, wall), theme.terrain, outer);
            var wallL = Solid("WallL", new Vector2(-hw - wall * 0.5f, 0), new Vector2(wall, height + wall * 2f), theme.terrain, outer);
            var wallR = Solid("WallR", new Vector2(hw + wall * 0.5f, 0), new Vector2(wall, height + wall * 2f), theme.terrain, outer);
            if (outer != null)
            {
                // 바닥 띠·천장 테두리는 벽 테두리 두께만큼 양옆으로 늘려 안쪽 모서리를 채운다 (벽 테두리는 바닥 윗면~천장 아랫면까지라 겹치지 않음)
                float cornerFill = Mathf.Max(0f, outer.borderWidth);
                StyledFace(floor, new Vector2(width + wall * 2f, wall), Vector2.up, -hw - cornerFill, hw + cornerFill, outer, true);
                StyledFace(ceil, new Vector2(width + wall * 2f, wall), Vector2.down, -hw - cornerFill, hw + cornerFill, outer, false);
                StyledFace(wallL, new Vector2(wall, height + wall * 2f), Vector2.right, -hh, hh, outer, false);
                StyledFace(wallR, new Vector2(wall, height + wall * 2f), Vector2.left, -hh, hh, outer, false);

                if (outer.maskOutsideWalls)
                {
                    // 좌우 외벽 바깥 가림 (시각용, 충돌체 없음). 외벽 안으로 0.1 겹쳐 이음매가 생기지 않게 하고,
                    // 정렬 6(외벽 몸체와 같음)으로 배경(-30)보다 앞에 그린다. 위아래로도 크게 늘려 모서리 바깥까지 덮는다
                    float m = Mathf.Max(outer.outsideMaskSize, wall * 4f);
                    const float overlap = 0.1f;
                    float edge = hw + wall; // 외벽 바깥면
                    Art.Quad(transform, "OutsideMaskL", Art.Square, outer.body, new Vector2(m, m), 6, new Vector3(-edge - m * 0.5f + overlap, 0f, 0f));
                    Art.Quad(transform, "OutsideMaskR", Art.Square, outer.body, new Vector2(m, m), 6, new Vector3(edge + m * 0.5f - overlap, 0f, 0f));
                }
            }
            else Art.Quad(transform, "FloorEdge", Art.Square, theme.accent.WithAlpha(0.6f), new Vector2(width, 0.12f), 7, new Vector3(0, -hh - 0.06f, 0));

            if (layout >= 0)
            {
                var plats = Layouts[Mathf.Clamp(layout, 0, Layouts.Length - 1)];
                float scale = width / 22f;
                foreach (var p in plats) Platform(new Vector2(p[0] * scale, p[1] * scale), p[2] * scale, theme);
            }
            else
            {
                // 로비: 제단과 단상
                Platform(new Vector2(-8f, -3.5f), 5f, theme);
                Platform(new Vector2(8f, -3.5f), 5f, theme);
                Platform(new Vector2(0f, 0.5f), 6f, theme);
                Art.Quad(transform, "Altar", Art.Square, theme.accent.WithAlpha(0.8f), new Vector2(2.2f, 1.4f), -5, new Vector3(0, -hh + 0.7f, 0));
                Art.Quad(transform, "Flame", Art.Soft, new Color(1f, 0.75f, 0.3f, 0.9f), new Vector2(2.2f, 2.6f), -4, new Vector3(0, -hh + 2.3f, 0));
                for (int i = -2; i <= 2; i++)
                    if (i != 0) tempBackground.Add(Art.Quad(transform, "Pillar", Art.Square, theme.platform.WithAlpha(0.5f), new Vector2(1.1f, height), -20, new Vector3(i * 6f, 0, 2)).gameObject); // 임시 기둥: 배경 이미지가 붙으면 숨김
            }
            PlayerSpawn = new Vector2(-hw + 2.2f, -hh + 1.2f);
        }

        private readonly List<GameObject> tempBackground = new List<GameObject>();

        /// <summary>코드로 만든 임시 배경(BgFar·Bg·Deco)을 켜고 끈다. 배경 이미지(RoomBackdrop)를 쓸 때 끈다.</summary>
        public void SetTempBackgroundVisible(bool visible)
        {
            foreach (var go in tempBackground) if (go != null) go.SetActive(visible);
        }

        private Transform Solid(string name, Vector2 pos, Vector2 size, Color c, TerrainStyle style = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.layer = Layers.Terrain;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            // 지형은 플레이어 리본보다 앞에 그려 바닥 아래로 늘어진 끝을 가린다
            Art.Quad(go.transform, "v", Art.Square, style != null ? style.body : c, size, 6);
            return go.transform;
        }

        // 지형 테두리 정렬: 몸체 6 위에 7. 선(띠)과 안쪽 그라데이션은 서로 겹치지 않게 놓아 같은 정렬값을 쓴다
        private const int TerrainEdgeOrder = 7;

        /// <summary>
        /// 블록(로컬 원점 = 가운데, 크기 size)의 한 면에 테두리를 그린다. outward = 면이 바라보는 방향(플레이 공간 쪽).
        /// spanMin~spanMax 는 면을 따라 그릴 범위(블록 로컬 좌표). 블록 안쪽으로 그려 블록 경계·충돌체는 그대로다.
        /// landing 이면 흙색 띠(topBand), 아니면 테두리 선(border). 그 안쪽에 몸체로 섞이는 그라데이션.
        /// </summary>
        private static void StyledFace(Transform block, Vector2 size, Vector2 outward, float spanMin, float spanMax, TerrainStyle s, bool landing)
        {
            float line = Mathf.Max(0f, landing ? s.topBandWidth : s.borderWidth);
            float shade = Mathf.Max(0f, s.innerShade);
            Color lineColor = landing ? s.topBand : s.border;
            float span = spanMax - spanMin, mid = (spanMin + spanMax) * 0.5f;
            if (span <= 0f) return;
            bool horizontal = outward.y != 0f; // 윗면·아랫면
            float half = horizontal ? size.y * 0.5f : size.x * 0.5f;
            float sign = horizontal ? Mathf.Sign(outward.y) : Mathf.Sign(outward.x);
            float lineCenter = sign * (half - line * 0.5f);
            float shadeCenter = sign * (half - line - shade * 0.5f);
            if (line > 0f)
                Art.Quad(block, landing ? "TopBand" : "Border", Art.Square, lineColor,
                    horizontal ? new Vector2(span, line) : new Vector2(line, span), TerrainEdgeOrder,
                    horizontal ? new Vector3(mid, lineCenter, 0f) : new Vector3(lineCenter, mid, 0f));
            if (shade > 0.001f)
            {
                // Art.Gradient 는 아래가 진하다. 진한 쪽이 테두리 선에 닿도록 돌린다
                var g = Art.Quad(block, "Shade", Art.Gradient, (landing ? s.topBand : s.border).WithAlpha(Mathf.Clamp01(s.innerShadeStrength)), new Vector2(span, shade / 64f), TerrainEdgeOrder,
                    horizontal ? new Vector3(mid, shadeCenter, 0f) : new Vector3(shadeCenter, mid, 0f));
                float rot = horizontal ? (sign > 0f ? 180f : 0f) : (sign > 0f ? 90f : -90f);
                g.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            }
        }

        private void Platform(Vector2 center, float length, RoomTheme theme)
        {
            var go = new GameObject("Platform");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center;
            go.layer = Layers.Terrain;
            var s = theme.style;
            var col = go.AddComponent<BoxCollider2D>();
            bool solid = s != null && s.solidPlatforms;
            float colThick = 0.6f;
            if (solid)
            {
                // 막힌 발판: 충돌체를 보이는 몸체와 똑같이 (윗면 +0.3 고정, 아래로 두께만큼). 한쪽 통과 효과는 붙이지 않는다
                colThick = Mathf.Max(0.6f, s.platformThickness);
                col.size = new Vector2(length, colThick);
                col.offset = new Vector2(0f, 0.3f - colThick * 0.5f);
            }
            else
            {
                // 한쪽 통과 발판: 위에서만 착지, 아래·옆에서는 통과
                col.size = new Vector2(length, 0.6f);
                col.usedByEffector = true;
                var eff = go.AddComponent<PlatformEffector2D>();
                eff.useOneWay = true;
                eff.surfaceArc = 170f;
            }
            if (s == null)
            {
                Art.Quad(go.transform, "v", Art.Square, theme.platform, new Vector2(length, 0.6f), 6);
                Art.Quad(go.transform, "edge", Art.Square, theme.accent.WithAlpha(0.7f), new Vector2(length, 0.1f), 7, new Vector3(0, 0.25f, 0));
            }
            else
            {
                // 보이는 몸체만 두껍게: 윗면(충돌체 윗면 = +0.3)은 고정, 아래로 늘린다. 충돌체는 위의 0.6 그대로
                float thick = Mathf.Max(0.6f, s.platformThickness);
                var vis = new GameObject("Visual").transform;
                vis.SetParent(go.transform, false);
                vis.localPosition = new Vector3(0f, 0.3f - thick * 0.5f, 0f);
                var size = new Vector2(length, thick);
                Art.Quad(vis, "Body", Art.Square, s.body, size, 6);
                float hw = length * 0.5f, hh = thick * 0.5f;
                float topInner = hh - s.topBandWidth; // 옆 테두리는 흙색 띠 아래부터 그려 모서리에서 겹치지 않게 한다
                StyledFace(vis, size, Vector2.up, -hw, hw, s, true);
                StyledFace(vis, size, Vector2.down, -hw, hw, s, false);
                StyledFace(vis, size, Vector2.left, -hh, topInner, s, false);
                StyledFace(vis, size, Vector2.right, -hh, topInner, s, false);
            }
            Platforms.Add(new Rect(center.x - length * 0.5f, center.y + 0.3f - colThick, length, colThick)); // 충돌 범위 (윗면 yMax 는 항상 center.y+0.3)
        }

        // ───────────── 적·웨이브 ─────────────

        public void SetupCombat(List<List<SpawnSpec>> waveList, float hpMul, float dmgMul)
        {
            CombatRoom = true;
            HpMul = hpMul;
            DmgMul = dmgMul;
            waves.Clear();
            waves.AddRange(waveList);
            waveIndex = -1;
            waveDelay = 0.9f;
        }

        public void SetupBoss(Boss boss, float hpMul, float dmgMul)
        {
            CombatRoom = true;
            HpMul = hpMul;
            DmgMul = dmgMul;
            Boss = boss;
            Enemies.Add(boss);
            waves.Clear();
            waveIndex = 0;
        }

        public Vector2 RandomSpawnPoint(System.Random rng, bool air)
        {
            var b = InnerBounds;
            for (int tries = 0; tries < 20; tries++)
            {
                float x = Mathf.Lerp(b.xMin + 2f, b.xMax - 2f, (float)rng.NextDouble());
                if (Player != null && Mathf.Abs(x - Player.transform.position.x) < 5f) continue;
                if (air) return new Vector2(x, Mathf.Lerp(b.center.y, b.yMax - 2f, (float)rng.NextDouble()));
                // 바닥 또는 발판 위
                if (Platforms.Count > 0 && rng.NextDouble() < 0.45)
                {
                    var p = Platforms[rng.Next(Platforms.Count)];
                    if (Player != null && Mathf.Abs(p.center.x - Player.transform.position.x) < 4f) continue;
                    return new Vector2(p.center.x, p.yMax + 1.0f);
                }
                return new Vector2(x, b.yMin + 1.2f);
            }
            return new Vector2(b.xMax - 3f, b.yMin + 1.2f);
        }

        public Enemy SpawnEnemy(EnemyDef def, Vector2 pos, float hpMul, float dmgMul, bool elite, bool minion = false)
        {
            var go = new GameObject((elite ? "Elite_" : "Enemy_") + def.id);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            Enemy e;
            switch (def.ai)
            {
                case "charger": e = go.AddComponent<ChargerEnemy>(); break;
                case "flyer": e = go.AddComponent<FlyerEnemy>(); break;
                case "shooter": e = go.AddComponent<ShooterEnemy>(); break;
                case "hopper": e = go.AddComponent<HopperEnemy>(); break;
                case "turret": e = go.AddComponent<TurretEnemy>(); break;
                default: e = go.AddComponent<WalkerEnemy>(); break;
            }
            e.Init(this, def, hpMul, dmgMul, elite);
            Enemies.Add(e);
            Fx.Burst(pos, def.tint, 10, 4f, 0.15f, 0.4f, 0f);
            return e;
        }

        private void StartNextWave()
        {
            waveIndex++;
            if (waveIndex >= waves.Count) return;
            var rng = new System.Random(GetInstanceID() + waveIndex * 977);
            foreach (var s in waves[waveIndex])
            {
                bool air = s.def.ai == "flyer" || s.def.fly;
                SpawnEnemy(s.def, RandomSpawnPoint(rng, air), HpMul, DmgMul, s.elite);
            }
        }

        public int AliveCount
        {
            get { int n = 0; foreach (var e in Enemies) if (e != null && e.IsAlive) n++; return n; }
        }

        public void OnEnemyDied(Enemy e)
        {
            KillCount++;
            if (e.goldValue > 0 && !e.IsBoss) SpawnGold(e.Center, Mathf.RoundToInt(e.goldValue * DB.Balance.economy.enemyGoldMul));
            OnEnemyKilled?.Invoke(e);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Enemies.RemoveAll(e => e == null);
            if (CombatRoom && !Cleared)
            {
                if (Boss != null)
                {
                    if (!Boss.IsAlive) ClearRoom();
                }
                else if (AliveCount == 0)
                {
                    if (waveIndex + 1 < waves.Count)
                    {
                        waveDelay -= dt;
                        if (waveDelay <= 0f) { StartNextWave(); waveDelay = 0.8f; }
                    }
                    else if (waveIndex >= 0 || waves.Count == 0) ClearRoom();
                }
            }
            UpdateZones(dt);
        }

        public void ClearRoom()
        {
            if (Cleared) return;
            Cleared = true;
            foreach (var p in new List<EnemyProjectile>(projectiles)) if (p != null) Destroy(p.gameObject);
            foreach (var g in golds) if (g != null) g.Magnet();
            OnCleared?.Invoke();
        }

        /// <summary>테스트·디버그용: 남은 적과 웨이브를 즉시 정리</summary>
        public void DebugKillAll()
        {
            waveIndex = waves.Count;
            foreach (var e in new List<Enemy>(Enemies)) if (e != null && e.IsAlive) e.ForceKill();
        }

        // ───────────── 투사체·골드 ─────────────

        public void RegisterProjectile(EnemyProjectile p) => projectiles.Add(p);
        public void UnregisterProjectile(EnemyProjectile p) => projectiles.Remove(p);

        public void SpawnGold(Vector2 pos, int amount)
        {
            int coins = Mathf.Clamp(amount, 1, 6);
            int per = Mathf.Max(1, amount / coins);
            int rest = amount;
            for (int i = 0; i < coins && rest > 0; i++)
            {
                int v = i == coins - 1 ? rest : Mathf.Min(per, rest);
                rest -= v;
                var go = new GameObject("Gold");
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
                Art.Quad(go.transform, "c", Art.Circle, new Color(1f, 0.82f, 0.25f), Vector2.one * 0.3f, 18);
                var g = go.AddComponent<GoldPickup>();
                g.Init(this, v, new Vector2(Random.Range(-3f, 3f), Random.Range(4f, 8f)));
                if (Cleared) g.Magnet();
                golds.Add(g);
            }
        }

        public void CollectGold(GoldPickup g)
        {
            golds.Remove(g);
            OnGoldCollected?.Invoke(g.value);
            Sfx.Play("coin", 0.4f);
            Destroy(g.gameObject);
        }

        public int PendingGold { get { int s = 0; foreach (var g in golds) if (g != null) s += g.value; return s; } }

        public void CollectAllGoldNow()
        {
            foreach (var g in new List<GoldPickup>(golds)) if (g != null) CollectGold(g);
        }

        // ───────────── 광역 효과 ─────────────

        public Enemy NearestEnemy(Vector2 p, float maxDist, HashSet<Enemy> exclude)
        {
            Enemy best = null;
            float bd = maxDist;
            foreach (var e in Enemies)
            {
                if (e == null || !e.IsAlive || (exclude != null && exclude.Contains(e))) continue;
                float d = Vector2.Distance(e.Center, p);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        /// <summary>광역 피해. '화살 적중'이 아니므로 적중 효과를 다시 일으키지 않는다.</summary>
        public void Explode(Vector2 center, float radius, float damage, Enemy exclude, Color color, string label)
        {
            Fx.Burst(center, color, 18, 8f, 0.2f, 0.4f, 2f);
            Fx.Circle(center, radius, color, 0.1f, 0.25f);
            CameraRig.Shake(0.12f);
            Sfx.Play("boom", 0.5f);
            foreach (var e in new List<Enemy>(Enemies))
            {
                if (e == null || !e.IsAlive || e == exclude) continue;
                if (e.DistanceFrom(center) <= radius) e.TakeDamage(damage, label, color, false);
            }
        }

        /// <summary>바이러스를 가진 채 죽으면 마지막으로 맞은 화살을 8방향으로 (임시 투사체, 재복제 없음)</summary>
        public void VirusBurst(Enemy e)
        {
            if (Combat == null) return;
            var def = e.lastArrowForm ?? DB.Arrow(0);
            int n = 8;
            float dmg = e.lastArrowDamage > 0 ? e.lastArrowDamage : 7f;
            for (int i = 0; i < n; i++)
            {
                float a = i * 360f / n;
                Phantom.Spawn(Combat, def, e.Center, new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)), 20f, dmg, 14f, "바이러스: " + def.name, 0.6f);
            }
            Fx.Text(e.Center + Vector3.up, "감염 폭발!", new Color(0.4f, 1f, 0.7f), 22);
        }

        public void SpawnWeb(Vector2 at, float radius, float life)
        {
            var hit = Physics2D.Raycast(at + Vector2.up * 0.5f, Vector2.down, 12f, Layers.TerrainMask);
            Vector2 c = hit.collider != null ? hit.point : at;
            AddZone(ZoneKind.Web, c, radius, life, new Color(0.9f, 0.9f, 1f, 0.35f));
        }

        public void SpawnWave(Vector2 at, float radius, float life) => AddZone(ZoneKind.Wave, at, radius, life, new Color(0.2f, 0.55f, 1f, 0.35f));

        public void DropOil(Vector2 at, float life)
        {
            var hit = Physics2D.Raycast(at, Vector2.down, 25f, Layers.TerrainMask);
            if (hit.collider == null) return;
            foreach (var z in Zones) if (z.kind == ZoneKind.Oil && Vector2.Distance(z.center, hit.point) < 0.8f) { z.life = Mathf.Max(z.life, life); return; }
            AddZone(ZoneKind.Oil, hit.point, 0.9f, life, new Color(0.25f, 0.2f, 0.08f, 0.75f));
        }

        public void IgniteOilAt(Vector2 at)
        {
            var st = DB.Balance.status;
            foreach (var z in Zones)
            {
                if (z.kind != ZoneKind.Oil || Vector2.Distance(z.center, at) > z.radius + 1.2f) continue;
                z.kind = ZoneKind.Fire;
                z.life = z.maxLife = st.oilFireDuration;
                z.radius *= 1.4f;
                z.sr.color = new Color(1f, 0.45f, 0.1f, 0.55f);
                z.sr.transform.localScale = new Vector3(z.radius * 2f, z.radius * 0.8f, 1f);
            }
        }

        private void AddZone(ZoneKind kind, Vector2 c, float radius, float life, Color color)
        {
            if (Zones.Count > 60) { var old = Zones[0]; if (old.sr != null) Destroy(old.sr.gameObject); Zones.RemoveAt(0); }
            var sr = Art.Quad(transform, "Zone_" + kind, Art.Soft, color, kind == ZoneKind.Wave ? new Vector2(1.2f, radius * 2f) : new Vector2(radius * 2f, radius * 0.8f), 5, new Vector3(c.x, c.y, 0));
            Zones.Add(new Zone { kind = kind, center = c, radius = radius, life = life, maxLife = life, sr = sr });
        }

        private void UpdateZones(float dt)
        {
            var st = DB.Balance.status;
            foreach (var e in Enemies)
            {
                if (e == null || !e.IsAlive) continue;
                bool onWeb = false;
                e.Status.inOil = false;
                foreach (var z in Zones)
                {
                    float d = Vector2.Distance(e.FeetPosition, z.center);
                    switch (z.kind)
                    {
                        case ZoneKind.Web:
                            if (d <= z.radius) onWeb = true;
                            break;
                        case ZoneKind.Oil:
                            if (d <= z.radius) e.Status.inOil = true;
                            break;
                        case ZoneKind.Fire:
                            if (d <= z.radius && Random.value < dt * 2f) e.TakeDamage(st.oilFireDps * 0.5f, "불꽃 장판", new Color(1f, 0.5f, 0.1f), false);
                            break;
                        case ZoneKind.Wave:
                            if (Mathf.Abs(e.Center.x - z.center.x) <= z.radius && Mathf.Abs(e.Center.y - z.center.y) <= 4f)
                            {
                                float pull = (z.center.x - e.Center.x) * 3f * (e.IsBoss ? st.bossCrowdControlMul : 1f);
                                e.Knockback(new Vector2(pull * dt * 4f, 0f));
                            }
                            break;
                    }
                }
                if (onWeb)
                {
                    e.Status.webTime += dt;
                    e.Status.webSlow = Mathf.Min(st.webSlowMax, st.webSlowStart + st.webSlowPerSecond * e.Status.webTime);
                }
                else { e.Status.webTime = 0f; e.Status.webSlow = 0f; }
            }
            for (int i = Zones.Count - 1; i >= 0; i--)
            {
                var z = Zones[i];
                z.life -= dt;
                if (z.sr != null)
                {
                    var c = z.sr.color;
                    c.a = Mathf.Clamp01(z.life / Mathf.Max(0.01f, z.maxLife)) * (z.kind == ZoneKind.Oil ? 0.75f : 0.5f) + 0.05f;
                    z.sr.color = c;
                }
                if (z.life <= 0f) { if (z.sr != null) Destroy(z.sr.gameObject); Zones.RemoveAt(i); }
            }
        }
    }
}
