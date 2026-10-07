using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 발사 궁술 공통. 발사 간격 = 기본 간격 × 다음 화살의 활시위 배율 ÷ 공격속도 배율 (최소 minInterval).
    /// 마지막 발사 이후 (dt × 공격속도 배율)을 누적해 '기본 간격 × 활시위 배율'에 닿으면 발사 가능.
    /// </summary>
    public abstract class LaunchBehaviour
    {
        protected readonly PlayerCombat combat;
        protected ArcheryTuning Cfg => combat.Tuning;
        protected float drawProgress = 9999f;

        protected LaunchBehaviour(PlayerCombat c) { combat = c; }

        public abstract LaunchStyle Style { get; }
        public abstract float BaseInterval { get; }
        public virtual float AttackSpeedBonus => 0f;
        public abstract void Tick(CombatInput input, float dt);
        public virtual string StatusText() => IntervalLine();

        public float RequiredDraw() => BaseInterval * combat.NextDrawMultiplier();
        public float EffectiveInterval() => Mathf.Max(Cfg.minInterval, RequiredDraw() / combat.AttackSpeedMultiplier);

        public bool IsReady
        {
            get
            {
                if (combat.Quiver.Peek() == null) return false;
                return drawProgress >= Mathf.Max(Cfg.minInterval * combat.AttackSpeedMultiplier, RequiredDraw());
            }
        }

        public float ReadyRatio
        {
            get
            {
                if (combat.Quiver.Peek() == null) return 0f;
                return Mathf.Clamp01(drawProgress / Mathf.Max(0.0001f, Mathf.Max(Cfg.minInterval * combat.AttackSpeedMultiplier, RequiredDraw())));
            }
        }

        protected void AdvanceDraw(float dt) { drawProgress = Mathf.Min(drawProgress + dt * combat.AttackSpeedMultiplier, 9999f); }
        protected void ResetDraw() { drawProgress = 0f; }

        protected string IntervalLine()
        {
            if (combat.Quiver.Peek() == null) return "화살통이 비어 있음";
            string state = IsReady ? "준비" : $"{(1f - ReadyRatio) * EffectiveInterval():0.00}초";
            return $"간격 {EffectiveInterval():0.00}초 (기본 {BaseInterval:0.##} × 활시위 {combat.NextDrawMultiplier():0.##} ÷ 공속 {combat.AttackSpeedMultiplier:0.00}) [{state}]";
        }

        public static LaunchBehaviour Create(LaunchStyle s, PlayerCombat c)
        {
            switch (s)
            {
                case LaunchStyle.Hermes: return new HermesLaunch(c);
                case LaunchStyle.Athena: return new AthenaLaunch(c);
                case LaunchStyle.Odysseus: return new OdysseusLaunch(c);
                case LaunchStyle.Ares: return new AresThrustLaunch(c);
                default: return new ApolloLaunch(c);
            }
        }
    }

    /// <summary>직사/곡사 발사 공통: 좌클릭을 누르고 있으면 간격마다 연속 발사</summary>
    public abstract class ProjectileLaunch : LaunchBehaviour
    {
        protected ProjectileLaunch(PlayerCombat c) : base(c) { }
        protected virtual float GravityFor(ArrowDef def) => 0f;
        protected virtual void OnFired() { }

        public override void Tick(CombatInput input, float dt)
        {
            AdvanceDraw(dt);
            if (!(input.fireDown || input.fireHeld)) return;
            var next = combat.Quiver.Peek();
            if (next == null) { if (input.fireDown) combat.EmptyQuiverFeedback(); return; }
            if (!IsReady) return;
            Vector2 dir = combat.AimDirectionFrom(combat.MuzzlePosition, input.aimWorld);
            var fired = combat.FireFront(dir, GravityFor(next.Card.Def));
            if (fired.Count > 0) { ResetDraw(); OnFired(); }
        }
    }

    /// <summary>아폴론 — 기본사격: 기본 간격 1초, 마우스 방향 직사</summary>
    public class ApolloLaunch : ProjectileLaunch
    {
        public ApolloLaunch(PlayerCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Apollo;
        public override float BaseInterval => Cfg.apolloBaseInterval;
    }

    /// <summary>헤르메스 — 연사: 기본 간격 1.25초, 발사마다 공속 +10% (5초, 최대 5스택, 쏠 때 전체 갱신)</summary>
    public class HermesLaunch : ProjectileLaunch
    {
        private readonly List<float> stackExpiry = new List<float>();
        private float now;
        public HermesLaunch(PlayerCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Hermes;
        public override float BaseInterval => Cfg.hermesBaseInterval;
        public int Stacks { get { int n = 0; foreach (var t in stackExpiry) if (t > now) n++; return n; } }
        public override float AttackSpeedBonus => Stacks * Cfg.hermesAttackSpeedPerStack;

        public override void Tick(CombatInput input, float dt)
        {
            now += dt;
            stackExpiry.RemoveAll(t => t <= now);
            base.Tick(input, dt);
        }

        protected override void OnFired()
        {
            float expiry = now + Cfg.hermesBuffDuration;
            if (stackExpiry.Count < Cfg.hermesMaxStacks) stackExpiry.Add(expiry);
            for (int i = 0; i < stackExpiry.Count; i++) stackExpiry[i] = expiry;
        }

        public override string StatusText() => $"연사 {Stacks}/{Cfg.hermesMaxStacks}스택 (공속 +{AttackSpeedBonus * 100f:0}%)\n" + IntervalLine();
    }

    /// <summary>아테나 — 곡사: 기본 간격 0.6초, 화살 무게 × 18 의 중력으로 떨어진다</summary>
    public class AthenaLaunch : ProjectileLaunch
    {
        public AthenaLaunch(PlayerCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Athena;
        public override float BaseInterval => Cfg.athenaBaseInterval;
        protected override float GravityFor(ArrowDef def) => Mathf.Max(0f, def.weight) * Cfg.athenaGravityPerWeight;
        public float PreviewGravity => combat.Quiver.Peek() != null ? GravityFor(combat.Quiver.Peek().Card.Def) : 0f;
    }

    /// <summary>
    /// 오디세우스 — 차지샷. 충전시간 = 실제 충전 × 공속 ÷ 활시위 배율, [1.2, 1.5]초.
    /// 피해 = 화살 피해 × (충전시간 ÷ 1초) + 공격력 → 기본 화살 6: 1.2초=7.2, 1.3초=7.8, 1.4초=8.4, 1.5초=9.0 (+공격력).
    /// 최소 충전 전에 놓으면 취소(화살 소모 없음).
    /// </summary>
    public class OdysseusLaunch : LaunchBehaviour
    {
        public bool Charging { get; private set; }
        public float ChargeElapsed { get; private set; }
        public float LastNormalizedCharge { get; private set; }
        public OdysseusLaunch(PlayerCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Odysseus;
        public override float BaseInterval => Cfg.odysseusMinCharge;

        private float DrawScale() => Mathf.Max(0.05f, combat.NextDrawMultiplier());
        public float MinCharge() => Cfg.odysseusMinCharge * DrawScale() / combat.AttackSpeedMultiplier;
        public float MaxCharge() => Cfg.odysseusMaxCharge * DrawScale() / combat.AttackSpeedMultiplier;

        public float NormalizedCharge(float elapsed)
        {
            float n = elapsed * combat.AttackSpeedMultiplier / DrawScale();
            return Mathf.Clamp(n, Cfg.odysseusMinCharge, Cfg.odysseusMaxCharge);
        }

        public static float ChargeDamage(float cardDamage, float attack, float normalizedSeconds, float referenceSeconds)
        {
            return cardDamage * (normalizedSeconds / Mathf.Max(0.01f, referenceSeconds)) + attack;
        }

        public float ChargeRatio => Charging ? Mathf.Clamp01(ChargeElapsed / Mathf.Max(0.001f, MaxCharge())) : 0f;

        public override void Tick(CombatInput input, float dt)
        {
            var next = combat.Quiver.Peek();
            if (!Charging)
            {
                if (input.fireDown)
                {
                    if (next == null) { combat.EmptyQuiverFeedback(); return; }
                    Charging = true;
                    ChargeElapsed = 0f;
                }
                return;
            }
            if (next == null) { Charging = false; ChargeElapsed = 0f; return; }
            if (input.fireHeld && !input.fireUp)
            {
                ChargeElapsed = Mathf.Min(ChargeElapsed + dt, MaxCharge());
                return;
            }
            Charging = false;
            if (ChargeElapsed + 0.0001f < MinCharge())
            {
                combat.Log($"차지 취소: {ChargeElapsed:0.00}초 < 최소 {MinCharge():0.00}초 (화살 소모 없음)");
                ChargeElapsed = 0f;
                return;
            }
            float norm = NormalizedCharge(ChargeElapsed);
            LastNormalizedCharge = norm;
            float atk = combat.Attack;
            Vector2 dir = combat.AimDirectionFrom(combat.MuzzlePosition, input.aimWorld);
            combat.FireFront(dir, 0f, cd => ChargeDamage(cd, atk, norm, Cfg.odysseusDamageReferenceSeconds));
            combat.Log($"차지샷: 실제 {ChargeElapsed:0.00}초 → 기준 {norm:0.00}초");
            ChargeElapsed = 0f;
        }

        public override string StatusText()
        {
            if (combat.Quiver.Peek() == null) return "화살통이 비어 있음";
            string s = Charging ? (ChargeElapsed >= MinCharge() ? "발사 가능 — 놓으세요" : "충전 중") : "좌클릭을 누르고 있으면 충전";
            return $"충전 {ChargeElapsed:0.00} / 최소 {MinCharge():0.00} 최대 {MaxCharge():0.00}초 ({s})";
        }
    }

    /// <summary>아레스 — 찌르기: 기본 간격 0.5초, 앞쪽 상자 판정의 가장 가까운 적 1명. 빗나가면 화살을 쓰지 않는다.</summary>
    public class AresThrustLaunch : LaunchBehaviour
    {
        private static readonly List<Collider2D> overlap = new List<Collider2D>(16);
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public AresThrustLaunch(PlayerCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Ares;
        public override float BaseInterval => Cfg.aresThrustBaseInterval;

        public void GetHitbox(Vector2 aimWorld, out Vector2 center, out Vector2 size, out float angle, out Vector2 dir)
        {
            Vector2 origin = combat.PlayerCenter;
            dir = combat.AimDirectionFrom(origin, aimWorld);
            center = origin + dir * (Cfg.aresThrustRange * 0.5f);
            size = new Vector2(Cfg.aresThrustRange, Cfg.aresThrustWidth);
            angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }

        public override void Tick(CombatInput input, float dt)
        {
            AdvanceDraw(dt);
            if (!(input.fireDown || input.fireHeld)) return;
            if (combat.Quiver.Peek() == null) { if (input.fireDown) combat.EmptyQuiverFeedback(); return; }
            if (!IsReady) return;

            GetHitbox(input.aimWorld, out var center, out var size, out var angle, out var dir);
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Layers.EnemyMask);
            overlap.Clear();
            Physics2D.OverlapBox(center, size, angle, filter, overlap);
            Enemy best = null;
            Collider2D bestCol = null;
            float bestDist = float.MaxValue;
            foreach (var col in overlap)
            {
                var e = col.GetComponentInParent<Enemy>();
                if (e == null || !e.IsAlive) continue;
                float d = e.DistanceFrom(combat.PlayerCenter);
                if (d < bestDist) { bestDist = d; best = e; bestCol = col; }
            }
            ResetDraw();
            if (best != null)
            {
                Vector2 point = bestCol.ClosestPoint(combat.PlayerCenter);
                combat.ThrustHit(best, bestCol.transform, point, dir);
                Hits++;
                Fx.Box(center, size, angle, new Color(1f, 0.4f, 0.3f), 0.07f, 0.15f);
            }
            else
            {
                Misses++;
                combat.Stats.thrustMisses++;
                combat.PlayFireFeedback(dir);
                combat.Log("찌르기 빗나감: 화살 소모 없음");
                Fx.Box(center, size, angle, new Color(0.7f, 0.7f, 0.7f, 0.6f), 0.05f, 0.15f);
            }
        }

        public override string StatusText() => $"찌르기 적중 {Hits} / 빗나감 {Misses}\n" + IntervalLine();
    }
}
