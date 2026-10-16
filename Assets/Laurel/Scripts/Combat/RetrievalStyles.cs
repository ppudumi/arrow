using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 회수 궁술 공통. 모든 회수 유형은 바닥(지형)에 떨어진 화살 근처에 가면 자동으로 줍는다.
    /// 화살통에 넣는 일은 반드시 PlayerCombat.TryRecover 를 거친다(중복 회수 방지).
    /// </summary>
    public abstract class RetrievalBehaviour
    {
        protected readonly PlayerCombat combat;
        protected ArcheryTuning Cfg => combat.Tuning;
        protected readonly List<ArrowBody> scratch = new List<ArrowBody>();

        protected RetrievalBehaviour(PlayerCombat c) { combat = c; }
        public abstract RetrievalStyle Style { get; }
        public virtual bool ArrowsStickToEnemies => true;
        public virtual float CooldownRemaining => 0f;

        public virtual void Tick(CombatInput input, float dt) { AutoPickup(); }

        protected void AutoPickup()
        {
            Vector2 p = combat.PlayerCenter;
            float r = Cfg.autoPickupRadius;
            foreach (var a in combat.Bodies)
            {
                if (a.State != ArrowState.Grounded) continue;
                if (Vector2.Distance(a.transform.position, p) <= r) combat.TryRecover(a, "줍기");
            }
        }

        public abstract string StatusText();

        protected int CountReturning(string source)
        {
            int n = 0;
            foreach (var a in combat.Bodies) if (a.State == ArrowState.Returning && a.ReturnSource == source) n++;
            return n;
        }

        public static RetrievalBehaviour Create(RetrievalStyle s, PlayerCombat c)
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

    /// <summary>기본 — 줍기: 화살이 적에게 박히지 않고 떨어진다.</summary>
    public class BasicPickup : RetrievalBehaviour
    {
        public BasicPickup(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Basic;
        public override bool ArrowsStickToEnemies => false;
        public override string StatusText() => "바닥의 화살 근처로 가면 자동으로 줍기";
    }

    /// <summary>오르페우스 — 부르기: R을 누르고 있는 동안 필드의 모든 화살이 5초에 걸쳐 돌아온다. 놓으면 그 자리에서 떨어진다.</summary>
    public class OrpheusCall : RetrievalBehaviour
    {
        public const string Source = "부르기";
        public bool Holding { get; private set; }
        public OrpheusCall(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Orpheus;

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            Holding = input.recallHeld;
            if (input.recallHeld)
            {
                int started = 0;
                foreach (var a in combat.Bodies)
                    if (a.IsOnField && a.BeginReturn(combat.PlayerCenter, Cfg.orpheusReturnDuration, Cfg.returnEaseExponent, Source)) started++;
                if (started > 0) { combat.Log($"부르기: {started}발 귀환 시작"); Sfx.Play("recall", 0.5f); }
            }
            else if (CountReturning(Source) > 0)
            {
                int dropped = 0;
                foreach (var a in combat.Bodies)
                    if (a.State == ArrowState.Returning && a.ReturnSource == Source) { a.Drop(Vector2.zero); dropped++; }
                if (dropped > 0) combat.Log($"부르기 중단: {dropped}발이 그 자리에서 떨어짐");
            }
        }

        public override string StatusText() => $"R 누르고 있기: 부르기 (귀환 중 {CountReturning(Source)}발)";
    }

    /// <summary>아레스 — 뽑기: 박힌 지 0.5초 지난 화살이 있는 적에게 다가가면 모두 뽑으며 적중 판정을 한 번 더 한다.</summary>
    public class AresPull : RetrievalBehaviour
    {
        public int PulledTotal { get; private set; }
        public int RetriggerTotal { get; private set; }
        public AresPull(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Ares;

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            if (combat.Room == null) return;
            Vector2 p = combat.PlayerCenter;
            foreach (var enemy in combat.Room.Enemies)
            {
                if (enemy == null || enemy.StuckArrows.Count == 0 || enemy.DistanceFrom(p) > Cfg.aresPullRadius) continue;
                scratch.Clear();
                foreach (var a in enemy.StuckArrows) if (a.State == ArrowState.Stuck && a.StateTime >= Cfg.aresPullDelay) scratch.Add(a);
                if (scratch.Count == 0) continue;
                var pulling = new List<ArrowBody>(scratch);
                combat.Log($"뽑기: {enemy.DisplayName}에게서 {pulling.Count}발");
                foreach (var a in pulling)
                {
                    if (!enemy.IsAlive) break;
                    HitResolver.Resolve(combat, a, enemy, null, a.transform.position, HitSource.Pull);
                    RetriggerTotal++;
                }
                foreach (var a in pulling)
                {
                    if (combat.ShouldConsumeNow(a)) { combat.ConsumeNow(a); continue; }
                    if (combat.TryRecover(a, "뽑기")) PulledTotal++;
                }
                return; // 한 프레임에 한 적만 (같은 프레임 회수 = 동시 회수 묶음)
            }
        }

        public override string StatusText() => $"박힌 화살에 다가가 뽑기 (뽑은 화살 {PulledTotal}, 재적중 {RetriggerTotal})";
    }

    /// <summary>데메테르 — 수확: R로 마우스 방향 부채꼴에 낫. 공격력×2 피해, 범위 안 화살(비행 중 제외) 모두 회수. 간격 = 1초 × 0.5 ÷ 공속.</summary>
    public class DemeterHarvest : RetrievalBehaviour
    {
        private float cooldown;
        public int Swings { get; private set; }
        public DemeterHarvest(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Demeter;
        public override float CooldownRemaining => cooldown;
        public float SwingInterval => Cfg.apolloBaseInterval * Cfg.demeterIntervalFactor / combat.AttackSpeedMultiplier;

        public bool InSector(Vector2 origin, Vector2 dir, Vector2 point)
        {
            Vector2 d = point - origin;
            float dist = d.magnitude;
            if (dist > Cfg.demeterRadius) return false;
            if (dist < 0.35f) return true;
            return Vector2.Angle(dir, d) <= Cfg.demeterAngle * 0.5f;
        }

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            if (cooldown > 0f) cooldown -= dt;
            if (!input.recallDown || cooldown > 0f) return;
            Vector2 origin = combat.PlayerCenter;
            Vector2 dir = combat.AimDirectionFrom(origin, input.aimWorld);
            cooldown = SwingInterval;
            Swings++;
            combat.PlayFireFeedback(dir);
            Sfx.Play("swing");
            Fx.Sector(origin, dir, Cfg.demeterRadius, Cfg.demeterAngle, new Color(1f, 0.9f, 0.35f), 0.08f, 0.25f);
            float damage = combat.Attack * Cfg.demeterDamageMultiplier;
            if (combat.Room != null)
            {
                foreach (var e in new List<Enemy>(combat.Room.Enemies))
                {
                    if (e == null || !e.IsAlive) continue;
                    if (InSector(origin, dir, e.ClosestPoint(origin)) || InSector(origin, dir, e.Center)) e.TakeDamage(damage, "수확", new Color(1f, 0.9f, 0.4f), false);
                }
            }
            scratch.Clear();
            foreach (var a in combat.Bodies) if (a.IsOnField && InSector(origin, dir, a.transform.position)) scratch.Add(a);
            int rec = 0;
            foreach (var a in scratch) if (combat.TryRecover(a, "수확")) rec++;
            combat.Log($"수확: 피해 {damage:0.#}, 화살 {rec}발 회수");
        }

        public override string StatusText() => cooldown > 0f ? $"R: 수확 (대기 {cooldown:0.0}초)" : "R: 수확 준비";
    }

    /// <summary>하데스 — 부름: 땅에 떨어졌거나 적에게 박힌 화살이 입력 없이 5초에 걸쳐 돌아온다.</summary>
    public class HadesSummon : RetrievalBehaviour
    {
        public const string Source = "부름";
        public HadesSummon(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Hades;

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            foreach (var a in combat.Bodies)
                if (a.State == ArrowState.Grounded || a.State == ArrowState.Stuck)
                    a.BeginReturn(combat.PlayerCenter, Cfg.hadesReturnDuration, Cfg.returnEaseExponent, Source);
        }

        public override string StatusText() => $"자동 귀환 중 {CountReturning(Source)}발";
    }

    /// <summary>제우스 — 뇌우: R로 커서 주변 원 안의 화살을 즉시 회수(6초). 화살 사이 적·박힌 적에게 공격력÷2 번개.</summary>
    public class ZeusStorm : RetrievalBehaviour
    {
        private float cooldown;
        private static readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        public int Casts { get; private set; }
        public ZeusStorm(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Zeus;
        public override float CooldownRemaining => cooldown;

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            if (cooldown > 0f) cooldown = Mathf.Max(0f, cooldown - dt);
            if (!input.recallDown || cooldown > 0f) return;
            Vector2 center = input.aimWorld;
            scratch.Clear();
            foreach (var a in combat.Bodies) if (a.IsOnField && Vector2.Distance(a.transform.position, center) <= Cfg.zeusRadius) scratch.Add(a);
            Fx.Circle(center, Cfg.zeusRadius, new Color(0.6f, 0.85f, 1f, 0.6f), 0.05f, 0.3f);
            if (scratch.Count == 0) { combat.Log("뇌우: 범위 안에 화살 없음 (대기시간 소모 안 함)"); return; }

            var chain = new List<ArrowBody>();
            var remaining = new List<ArrowBody>(scratch);
            Vector2 cur = center;
            while (remaining.Count > 0)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++) { float d = Vector2.Distance(cur, remaining[i].transform.position); if (d < bd) { bd = d; best = i; } }
                chain.Add(remaining[best]);
                cur = remaining[best].transform.position;
                remaining.RemoveAt(best);
            }
            var damaged = new HashSet<Enemy>();
            float damage = combat.Attack * Cfg.zeusDamageFactor;
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                Vector2 a = chain[i].transform.position, b = chain[i + 1].transform.position;
                bool struck = false;
                foreach (var e in EnemiesOnSegment(a, b))
                {
                    struck = true;
                    if (damaged.Add(e)) e.TakeDamage(damage, "뇌우", new Color(1f, 0.95f, 0.4f), false);
                }
                Fx.Lightning(a, b, struck ? new Color(1f, 0.95f, 0.4f) : new Color(0.5f, 0.75f, 1f, 0.7f), struck ? 0.12f : 0.05f, 0.45f);
            }
            foreach (var arrow in chain)
            {
                if (arrow.State != ArrowState.Stuck || arrow.StuckEnemy == null) continue;
                var e = arrow.StuckEnemy;
                Vector2 p = arrow.transform.position;
                Fx.Lightning(p + new Vector2(Random.Range(-0.6f, 0.6f), 4f), p, new Color(1f, 0.95f, 0.4f), 0.1f, 0.45f);
                if (damaged.Add(e)) e.TakeDamage(damage, "뇌우", new Color(1f, 0.95f, 0.4f), false);
            }
            int rec = 0;
            foreach (var a in chain) if (combat.TryRecover(a, "뇌우")) rec++;
            cooldown = Cfg.zeusCooldown;
            Casts++;
            Sfx.Play("zap");
            combat.Log($"뇌우: {rec}발 즉시 회수, 번개 적중 {damaged.Count}");
        }

        private List<Enemy> EnemiesOnSegment(Vector2 a, Vector2 b)
        {
            var result = new List<Enemy>();
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return result;
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Layers.EnemyMask);
            hits.Clear();
            Physics2D.CircleCast(a, Cfg.zeusLineThickness * 0.5f, d / len, filter, hits, len);
            foreach (var h in hits)
            {
                var e = h.collider != null ? h.collider.GetComponentInParent<Enemy>() : null;
                if (e != null && e.IsAlive && !result.Contains(e)) result.Add(e);
            }
            return result;
        }

        public override string StatusText() => cooldown > 0f ? $"R: 뇌우 (대기 {cooldown:0.0}초)" : "R: 뇌우 준비 (커서 주변 화살)";
    }
}
