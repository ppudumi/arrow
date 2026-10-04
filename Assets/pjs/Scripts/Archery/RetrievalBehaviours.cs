using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 회수 궁술의 공통 기반. 모든 회수 유형은 바닥(지형)에 떨어진 화살 근처에 가면 자동으로 줍는다.
    /// 화살통에 넣는 일은 반드시 ArcheryCombat.TryRecover 를 거친다 (중복 회수 방지).
    /// </summary>
    public abstract class RetrievalBehaviour
    {
        protected readonly ArcheryCombat combat;
        protected ArcheryConfig Cfg => combat.Config;
        protected readonly List<ArcheryArrow> scratch = new List<ArcheryArrow>();

        protected RetrievalBehaviour(ArcheryCombat c) { combat = c; }

        public abstract RetrievalStyle Style { get; }
        /// <summary>false면 화살이 적에게 박히지 않고 떨어진다 (기본 줍기)</summary>
        public virtual bool ArrowsStickToEnemies => true;
        public int AutoPickups { get; private set; }

        public virtual void Tick(ArcheryInput input, float dt)
        {
            AutoPickup();
        }

        /// <summary>공통: 바닥(지형)에 떨어진 화살 자동 줍기</summary>
        protected void AutoPickup()
        {
            Vector2 p = combat.PlayerCenter;
            float r = Cfg.autoPickupRadius;
            var arrows = combat.Arrows;
            for (int i = 0; i < arrows.Count; i++)
            {
                var a = arrows[i];
                if (a.State != ArrowState.Grounded) continue;
                if (Vector2.Distance(a.transform.position, p) <= r)
                {
                    if (combat.TryRecover(a, "줍기")) AutoPickups++;
                }
            }
        }

        public virtual void DrawGizmos(ArcheryDebugDraw draw)
        {
            draw.Circle(combat.PlayerCenter, Cfg.autoPickupRadius, new Color(0.4f, 1f, 0.6f, 0.35f), 0.03f);
        }

        public abstract string StatusText();

        /// <summary>회수 가능한 필드 상태(비행 중·귀환 중·화살통 제외)</summary>
        protected static bool IsOnField(ArcheryArrow a) =>
            a.State == ArrowState.Grounded || a.State == ArrowState.Stuck || a.State == ArrowState.Falling;

        protected int CountReturning(string source)
        {
            int n = 0;
            for (int i = 0; i < combat.Arrows.Count; i++)
            {
                var a = combat.Arrows[i];
                if (a.State == ArrowState.Returning && a.ReturnSource == source) n++;
            }
            return n;
        }

        protected float MaxReturnProgress(string source)
        {
            float m = 0f;
            for (int i = 0; i < combat.Arrows.Count; i++)
            {
                var a = combat.Arrows[i];
                if (a.State == ArrowState.Returning && a.ReturnSource == source) m = Mathf.Max(m, a.ReturnProgress);
            }
            return m;
        }

        public static RetrievalBehaviour Create(RetrievalStyle s, ArcheryCombat c)
        {
            switch (s)
            {
                case RetrievalStyle.Orpheus: return new OrpheusCall(c);
                case RetrievalStyle.Ares: return new AresPull(c);
                case RetrievalStyle.Demeter: return new DemeterHarvest(c);
                case RetrievalStyle.Hades: return new HadesSummon(c);
                case RetrievalStyle.Zeus: return new ZeusStorm(c);
                default: return new BasicPickup(c);
            }
        }
    }

    /// <summary>기본 — 줍기: 화살이 적에게 박히지 않는다. 바닥의 화살을 줍는다.</summary>
    public class BasicPickup : RetrievalBehaviour
    {
        public BasicPickup(ArcheryCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Basic;
        public override bool ArrowsStickToEnemies => false;
        public override string StatusText() => $"자동 줍기 반경 {Cfg.autoPickupRadius:0.#}  (주운 화살 {AutoPickups}발)";
    }

    /// <summary>
    /// 오르페우스 — 부르기: R을 누르고 있는 동안 필드의 모든 화살(비행 중 제외)을 5초에 걸쳐 불러온다.
    /// 귀환 곡선: 남은 거리 = 시작 거리 × (1-t)^3 → 멀수록 빠르고 가까울수록 느리다. 무게와 무관.
    /// [잠정] R을 떼면 귀환 중이던 화살은 그 자리에서 떨어진다(설정으로 '계속 귀환' 선택 가능).
    /// [잠정] R을 누르고 있는 동안 새로 떨어진/박힌 화살도 합류한다.
    /// </summary>
    public class OrpheusCall : RetrievalBehaviour
    {
        public const string Source = "부르기";
        public bool Holding { get; private set; }

        public OrpheusCall(ArcheryCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Orpheus;

        public override void Tick(ArcheryInput input, float dt)
        {
            base.Tick(input, dt);
            Holding = input.recallHeld;
            if (input.recallHeld)
            {
                int started = 0;
                for (int i = 0; i < combat.Arrows.Count; i++)
                {
                    var a = combat.Arrows[i];
                    if (!IsOnField(a)) continue;
                    if (a.BeginReturn(combat.PlayerCenter, Cfg.orpheusReturnDuration, Cfg.returnEaseExponent, Source)) started++;
                }
                if (started > 0)
                {
                    combat.Ctx.Log($"부르기: {started}발 귀환 시작 (5초)");
                    if (Procedural2D.AudioManager.Instance != null) Procedural2D.AudioManager.Instance.PlayArrowRecall(combat.PlayerCenter);
                }
            }
            else if (input.recallUp || (!input.recallHeld && CountReturning(Source) > 0 && Cfg.orpheusReleaseMode == OrpheusReleaseMode.DropInPlace))
            {
                if (Cfg.orpheusReleaseMode == OrpheusReleaseMode.DropInPlace)
                {
                    int dropped = 0;
                    for (int i = 0; i < combat.Arrows.Count; i++)
                    {
                        var a = combat.Arrows[i];
                        if (a.State == ArrowState.Returning && a.ReturnSource == Source) { a.Drop(Vector2.zero); dropped++; }
                    }
                    if (dropped > 0) combat.Ctx.Log($"부르기 중단: 귀환 중이던 {dropped}발이 그 자리에서 떨어짐");
                }
            }
        }

        public override string StatusText()
        {
            int n = CountReturning(Source);
            string s = Holding ? "R 누르는 중" : "R 누르고 있기";
            return $"부르기 ({s})  귀환 중 {n}발  진행 {MaxReturnProgress(Source) * 100f:0}% / {Cfg.orpheusReturnDuration:0.#}초" +
                   $"  [놓으면: {(Cfg.orpheusReleaseMode == OrpheusReleaseMode.DropInPlace ? "그 자리 낙하" : "계속 귀환")}]";
        }
    }

    /// <summary>
    /// 아레스 — 뽑기: 박힌 지 0.5초 지난 화살이 있는 적에게 다가가면 그 적의 화살을 모두 뽑는다.
    /// 뽑을 때 각 화살의 적중 판정(피해 + 적중 효과)을 ArrowHitResolver 로 한 번 더 실행한다.
    /// [잠정] 0.5초가 지나지 않은 화살은 남겨 두었다가 시간이 지나면 다음 접근 때 뽑는다.
    /// [잠정] 재적중 피해는 차지 배율 없이 '화살 피해 + 공격력', 넉백 방향은 원래 적중 방향.
    /// </summary>
    public class AresPull : RetrievalBehaviour
    {
        public int PulledTotal { get; private set; }
        public int RetriggerTotal { get; private set; }

        public AresPull(ArcheryCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Ares;

        public override void Tick(ArcheryInput input, float dt)
        {
            base.Tick(input, dt);
            Vector2 p = combat.PlayerCenter;
            var enemies = combat.Ctx.Enemies;
            for (int e = 0; e < enemies.Count; e++)
            {
                var enemy = enemies[e];
                if (enemy == null || enemy.StuckArrows.Count == 0) continue;
                if (enemy.DistanceFrom(p) > Cfg.aresPullRadius) continue;

                scratch.Clear();
                for (int i = 0; i < enemy.StuckArrows.Count; i++)
                {
                    var a = enemy.StuckArrows[i];
                    if (a.State == ArrowState.Stuck && a.StateTime >= Cfg.aresPullDelay) scratch.Add(a);
                }
                if (scratch.Count == 0) continue;

                var pulling = new List<ArcheryArrow>(scratch);
                combat.Ctx.Log($"뽑기: {enemy.displayName}에게서 {pulling.Count}발");
                for (int i = 0; i < pulling.Count; i++)
                {
                    var a = pulling[i];
                    float dmg = ArrowHitResolver.NormalDamage(a.Def, combat.Attack);
                    ArrowHitResolver.Resolve(combat, a, enemy, HitSource.Pull, dmg, a.LastDirection);
                    RetriggerTotal++;
                }
                for (int i = 0; i < pulling.Count; i++)
                {
                    if (combat.TryRecover(pulling[i], "뽑기")) PulledTotal++;
                }
            }
        }

        public override void DrawGizmos(ArcheryDebugDraw draw)
        {
            base.DrawGizmos(draw);
            draw.Circle(combat.PlayerCenter, Cfg.aresPullRadius, new Color(1f, 0.45f, 0.35f, 0.3f), 0.03f);
        }

        public override string StatusText()
        {
            int stuck = combat.CountState(ArrowState.Stuck);
            int ready = 0;
            for (int i = 0; i < combat.Arrows.Count; i++)
            {
                var a = combat.Arrows[i];
                if (a.State == ArrowState.Stuck && a.StateTime >= Cfg.aresPullDelay) ready++;
            }
            return $"박힌 화살 {stuck}발 (뽑기 가능 {ready})  뽑은 화살 {PulledTotal}  재적중 {RetriggerTotal}회  접근 반경 {Cfg.aresPullRadius:0.#}";
        }
    }

    /// <summary>
    /// 데메테르 — 수확: R로 마우스 방향 부채꼴에 낫을 휘둘러 공격력×2 피해, 범위 안 화살(비행 중 제외) 모두 회수.
    /// [잠정] 낫 간격 = 기준 간격(1초) × 0.5 ÷ 공속. '기본공격속도의 2배'를 2배 빠름으로 해석.
    /// </summary>
    public class DemeterHarvest : RetrievalBehaviour
    {
        private float cooldown;
        public int Swings { get; private set; }

        public DemeterHarvest(ArcheryCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Demeter;
        public float CooldownRemaining => cooldown;

        public float SwingInterval
        {
            get
            {
                float baseI = Cfg.demeterUseLaunchBaseInterval ? combat.Launcher.BaseInterval : Cfg.demeterReferenceInterval;
                float atk = Cfg.demeterApplyAttackSpeed ? combat.AttackSpeedMultiplier : 1f;
                return baseI * Cfg.demeterIntervalFactor / atk;
            }
        }

        public bool InSector(Vector2 origin, Vector2 dir, Vector2 point)
        {
            Vector2 d = point - origin;
            float dist = d.magnitude;
            if (dist > Cfg.demeterRadius) return false;
            if (dist < 0.35f) return true;
            return Vector2.Angle(dir, d) <= Cfg.demeterAngle * 0.5f;
        }

        public override void Tick(ArcheryInput input, float dt)
        {
            base.Tick(input, dt);
            if (cooldown > 0f) cooldown -= dt;
            if (!input.recallDown) return;
            if (cooldown > 0f)
            {
                combat.Ctx.Log($"수확 대기 중 ({cooldown:0.00}초)");
                return;
            }

            Vector2 origin = combat.PlayerCenter;
            Vector2 dir = combat.AimDirectionFrom(origin, input.aimWorld);
            cooldown = SwingInterval;
            Swings++;
            combat.PlayFireFeedback(dir);
            combat.Ctx.Draw?.Sector(origin, dir, Cfg.demeterRadius, Cfg.demeterAngle, new Color(1f, 0.9f, 0.35f, 1f), 0.08f, 0.25f);

            float damage = combat.Attack * Cfg.demeterDamageMultiplier;
            int hitEnemies = 0;
            foreach (var enemy in combat.Ctx.Enemies)
            {
                if (enemy == null || !enemy.IsAlive || enemy.Col == null) continue;
                Vector2 cp = enemy.Col.ClosestPoint(origin);
                if (InSector(origin, dir, cp) || InSector(origin, dir, enemy.transform.position))
                {
                    enemy.ApplyDamage(damage, "수확");
                    hitEnemies++;
                }
            }

            scratch.Clear();
            for (int i = 0; i < combat.Arrows.Count; i++)
            {
                var a = combat.Arrows[i];
                if (IsOnField(a) && InSector(origin, dir, a.transform.position)) scratch.Add(a);
            }
            int recovered = 0;
            for (int i = 0; i < scratch.Count; i++) if (combat.TryRecover(scratch[i], "수확")) recovered++;
            combat.Ctx.Log($"수확: 적 {hitEnemies}명에게 {ArcheryCombatContext.FormatDamage(damage)} 피해, 화살 {recovered}발 회수 (다음 {SwingInterval:0.00}초)");
        }

        public override void DrawGizmos(ArcheryDebugDraw draw)
        {
            base.DrawGizmos(draw);
            if (!draw.showRanges) return;
            Vector2 origin = combat.PlayerCenter;
            Vector2 dir = combat.AimDirectionFrom(origin, combat.CurrentInput.aimWorld);
            draw.Sector(origin, dir, Cfg.demeterRadius, Cfg.demeterAngle, new Color(1f, 0.85f, 0.3f, cooldown > 0f ? 0.15f : 0.4f), 0.03f);
        }

        public override string StatusText()
        {
            string cd = cooldown > 0f ? $"대기 {cooldown:0.00}초" : "준비됨";
            float baseI = Cfg.demeterUseLaunchBaseInterval ? combat.Launcher.BaseInterval : Cfg.demeterReferenceInterval;
            return $"수확 [{cd}]  간격 {SwingInterval:0.00}초 = {baseI:0.##} × {Cfg.demeterIntervalFactor:0.##} ÷ 공속  피해 {ArcheryCombatContext.FormatDamage(combat.Attack * Cfg.demeterDamageMultiplier)}  휘두름 {Swings}회";
        }
    }

    /// <summary>
    /// 하데스 — 부름: 바닥에 떨어졌거나(지형) 적에게 박힌 화살이 자동으로 5초에 걸쳐 돌아온다.
    /// 비행 중·낙하 중인 화살은 대상이 아니다. [잠정] 바닥/박힘 상태가 된 즉시(대기 0초) 귀환 시작.
    /// </summary>
    public class HadesSummon : RetrievalBehaviour
    {
        public const string Source = "부름";
        public HadesSummon(ArcheryCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Hades;

        public override void Tick(ArcheryInput input, float dt)
        {
            base.Tick(input, dt);
            for (int i = 0; i < combat.Arrows.Count; i++)
            {
                var a = combat.Arrows[i];
                if ((a.State == ArrowState.Grounded || a.State == ArrowState.Stuck) && a.StateTime >= Cfg.hadesStartDelay)
                {
                    if (a.BeginReturn(combat.PlayerCenter, Cfg.hadesReturnDuration, Cfg.returnEaseExponent, Source))
                        combat.Ctx.Log($"부름: {a.Label} 자동 귀환 시작");
                }
            }
        }

        public override string StatusText()
        {
            return $"자동 귀환 중 {CountReturning(Source)}발  최대 진행 {MaxReturnProgress(Source) * 100f:0}% / {Cfg.hadesReturnDuration:0.#}초 (입력 없음)";
        }
    }

    /// <summary>
    /// 제우스 — 뇌우: R로 마우스 주변 원 안의 화살(비행 중 제외)을 즉시 회수한다. 재사용 6초.
    /// [잠정] 화살 연결: 커서에 가장 가까운 화살부터 최근접 이웃 순으로 이은 선분들.
    /// [잠정] 선분과 겹친 적 + 화살이 박혀 있는 적이 번개 피해(공격력÷2). 한 번의 뇌우에서 적마다 1회.
    /// [잠정] 범위 안에 화살이 없으면 대기시간을 소모하지 않는다.
    /// </summary>
    public class ZeusStorm : RetrievalBehaviour
    {
        private float cooldown;
        private static readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        public float CooldownRemaining => cooldown;
        public int Casts { get; private set; }
        public int LastBoltHits { get; private set; }
        public int LastRecalled { get; private set; }

        public ZeusStorm(ArcheryCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Zeus;

        public override void Tick(ArcheryInput input, float dt)
        {
            base.Tick(input, dt);
            if (cooldown > 0f) cooldown = Mathf.Max(0f, cooldown - dt);
            if (!input.recallDown) return;
            if (cooldown > 0f)
            {
                combat.Ctx.Log($"뇌우 재사용 대기 중 ({cooldown:0.0}초)");
                return;
            }

            Vector2 center = input.aimWorld;
            scratch.Clear();
            for (int i = 0; i < combat.Arrows.Count; i++)
            {
                var a = combat.Arrows[i];
                if (IsOnField(a) && Vector2.Distance(a.transform.position, center) <= Cfg.zeusRadius) scratch.Add(a);
            }

            if (scratch.Count == 0)
            {
                combat.Ctx.Log("뇌우: 범위 안에 화살 없음" + (Cfg.zeusCooldownOnlyWhenRecalled ? " (대기시간 소모 안 함)" : ""));
                if (!Cfg.zeusCooldownOnlyWhenRecalled) cooldown = Cfg.zeusCooldown;
                return;
            }

            var chain = BuildChain(center, scratch);
            var damaged = new HashSet<ArcheryEnemy>();
            float damage = combat.Attack * Cfg.zeusDamageFactor;
            int boltHits = 0;
            var draw = combat.Ctx.Draw;

            // 1) 화살 사이 연결선
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                Vector2 a = chain[i].transform.position, b = chain[i + 1].transform.position;
                var between = EnemiesOnSegment(a, b);
                bool struck = false;
                foreach (var e in between)
                {
                    if (Cfg.zeusHitOncePerEnemy && damaged.Contains(e)) { struck = true; continue; }
                    e.ApplyDamage(damage, "뇌우");
                    damaged.Add(e);
                    boltHits++;
                    struck = true;
                }
                draw?.Lightning(a, b, struck ? new Color(1f, 0.95f, 0.4f, 1f) : new Color(0.5f, 0.75f, 1f, 0.6f), struck ? 0.12f : 0.05f, Cfg.zeusBoltVisibleTime);
            }

            // 2) 적에게 박힌 화살
            for (int i = 0; i < chain.Count; i++)
            {
                var arrow = chain[i];
                if (arrow.State != ArrowState.Stuck || arrow.StuckEnemy == null) continue;
                var e = arrow.StuckEnemy;
                Vector2 p = arrow.transform.position;
                draw?.Lightning(p + new Vector2(Random.Range(-0.6f, 0.6f), 4f), p, new Color(1f, 0.95f, 0.4f, 1f), 0.1f, Cfg.zeusBoltVisibleTime);
                if (Cfg.zeusHitOncePerEnemy && damaged.Contains(e)) continue;
                e.ApplyDamage(damage, "뇌우");
                damaged.Add(e);
                boltHits++;
            }

            // 3) 즉시 회수 (같은 프레임 → 동시 회수)
            int recalled = 0;
            for (int i = 0; i < chain.Count; i++) if (combat.TryRecover(chain[i], "뇌우")) recalled++;

            cooldown = Cfg.zeusCooldown;
            Casts++;
            LastBoltHits = boltHits;
            LastRecalled = recalled;
            combat.Ctx.Log($"뇌우: 화살 {recalled}발 즉시 회수, 번개 적중 {boltHits}회 × {ArcheryCombatContext.FormatDamage(damage)}");
            if (Procedural2D.AudioManager.Instance != null) Procedural2D.AudioManager.Instance.PlayArrowRecall(center);
        }

        private List<ArcheryArrow> BuildChain(Vector2 center, List<ArcheryArrow> src)
        {
            var remaining = new List<ArcheryArrow>(src);
            var chain = new List<ArcheryArrow>(src.Count);
            if (Cfg.zeusChainOrder == ZeusChainOrder.AngleAroundCursor)
            {
                remaining.Sort((x, y) =>
                {
                    Vector2 dx = (Vector2)x.transform.position - center, dy = (Vector2)y.transform.position - center;
                    return Mathf.Atan2(dx.y, dx.x).CompareTo(Mathf.Atan2(dy.y, dy.x));
                });
                return remaining;
            }
            Vector2 cur = center;
            while (remaining.Count > 0)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    float d = Vector2.Distance(cur, remaining[i].transform.position);
                    if (d < bd) { bd = d; best = i; }
                }
                chain.Add(remaining[best]);
                cur = remaining[best].transform.position;
                remaining.RemoveAt(best);
            }
            return chain;
        }

        private List<ArcheryEnemy> EnemiesOnSegment(Vector2 a, Vector2 b)
        {
            var result = new List<ArcheryEnemy>();
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return result;
            var filter = new ContactFilter2D { useTriggers = true };
            hits.Clear();
            Physics2D.CircleCast(a, Cfg.zeusLineThickness * 0.5f, d / len, filter, hits, len);
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].collider == null) continue;
                var e = hits[i].collider.GetComponentInParent<ArcheryEnemy>();
                if (e != null && e.IsAlive && !result.Contains(e)) result.Add(e);
            }
            return result;
        }

        public override void DrawGizmos(ArcheryDebugDraw draw)
        {
            base.DrawGizmos(draw);
            Color c = cooldown > 0f ? new Color(0.5f, 0.6f, 0.8f, 0.3f) : new Color(0.55f, 0.8f, 1f, 0.75f);
            draw.Circle(combat.CurrentInput.aimWorld, Cfg.zeusRadius, c, 0.05f, false);
        }

        public override string StatusText()
        {
            string cd = cooldown > 0f ? $"재사용 대기 {cooldown:0.0}초" : "준비됨";
            return $"뇌우 [{cd}]  반경 {Cfg.zeusRadius:0.#}  번개 {ArcheryCombatContext.FormatDamage(combat.Attack * Cfg.zeusDamageFactor)}" +
                   $"  사용 {Casts}회 (직전: 회수 {LastRecalled}발, 번개 적중 {LastBoltHits}회)";
        }
    }
}
