using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 발사 궁술의 공통 기반. 화살통의 '다음 화살' 데이터를 읽어 간격·피해를 계산한다.
    /// 화살 고유의 동작은 ArrowDefinition 데이터(피해, 활시위, 무게, 적중 효과)로 연결된다.
    /// </summary>
    public abstract class LaunchBehaviour
    {
        protected readonly ArcheryCombat combat;
        protected ArcheryConfig Cfg => combat.Config;

        protected LaunchBehaviour(ArcheryCombat c) { combat = c; }

        public abstract LaunchStyle Style { get; }
        /// <summary>이 궁술의 기본 발사 간격 (초)</summary>
        public abstract float BaseInterval { get; }
        /// <summary>공격속도 증가 합 (0.1 = +10%)</summary>
        public virtual float AttackSpeedBonus => 0f;

        public abstract void Tick(ArcheryInput input, float dt);
        public virtual void DrawGizmos(ArcheryDebugDraw draw) { }
        public abstract string StatusText();

        /// <summary>
        /// 발사 간격 진행도. 마지막 발사 이후 (dt × 공격속도 배율)을 누적하고,
        /// 누적값이 '기본 간격 × 다음 화살의 활시위 배율'에 도달하면 발사 가능.
        /// → 실제 간격 = 기본 간격 × 활시위 배율 ÷ 공격속도 배율
        /// </summary>
        protected float drawProgress = 9999f;

        public float RequiredDraw(ArrowDefinition next) => combat.BaseIntervalFor(BaseInterval, next);
        public float EffectiveInterval(ArrowDefinition next) => RequiredDraw(next) / combat.AttackSpeedMultiplier;

        public bool IsReady
        {
            get
            {
                var next = combat.Quiver.Peek();
                return next != null && drawProgress >= RequiredDraw(next.Def);
            }
        }

        public float ReadyRatio
        {
            get
            {
                var next = combat.Quiver.Peek();
                if (next == null) return 0f;
                return Mathf.Clamp01(drawProgress / Mathf.Max(0.0001f, RequiredDraw(next.Def)));
            }
        }

        public float CooldownRemaining
        {
            get
            {
                var next = combat.Quiver.Peek();
                if (next == null) return 0f;
                return Mathf.Max(0f, RequiredDraw(next.Def) - drawProgress) / combat.AttackSpeedMultiplier;
            }
        }

        protected void AdvanceDraw(float dt)
        {
            drawProgress = Mathf.Min(drawProgress + dt * combat.AttackSpeedMultiplier, 9999f);
        }

        protected string IntervalLine()
        {
            var next = combat.Quiver.Peek();
            if (next == null) return "다음 화살 없음 (화살통 비어 있음)";
            float draw = Cfg.DrawMultiplier(next.Def);
            string state = IsReady ? "준비됨" : $"대기 {CooldownRemaining:0.00}초";
            return $"발사 간격 {EffectiveInterval(next.Def):0.00}초 = 기본 {BaseInterval:0.##} × 활시위 {draw:0.##} ÷ 공속 {combat.AttackSpeedMultiplier:0.00}  [{state}]";
        }

        public static LaunchBehaviour Create(LaunchStyle s, ArcheryCombat c)
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

    /// <summary>직사/곡사 투사체 발사 공통: 좌클릭 누르고 있으면 간격마다 연속 발사</summary>
    public abstract class ProjectileLaunch : LaunchBehaviour
    {
        protected ProjectileLaunch(ArcheryCombat c) : base(c) { }

        protected virtual float GravityFor(ArrowDefinition def) => 0f;
        protected virtual void OnFired(ArcheryArrow arrow) { }

        public override void Tick(ArcheryInput input, float dt)
        {
            AdvanceDraw(dt);
            if (!(input.fireDown || input.fireHeld)) return;

            var next = combat.Quiver.Peek();
            if (next == null)
            {
                if (input.fireDown) combat.EmptyQuiverFeedback();
                return;
            }
            if (!IsReady) return;

            Vector2 dir = combat.AimDirectionFrom(combat.MuzzlePosition, input.aimWorld);
            float damage = ArrowHitResolver.NormalDamage(next.Def, combat.Attack);
            float interval = EffectiveInterval(next.Def);
            var fired = combat.FireNext(dir, GravityFor(next.Def), damage);
            drawProgress = 0f;
            combat.Ctx.Log($"발사: {fired.Label} (피해 {ArcheryCombatContext.FormatDamage(damage)}, 간격 {interval:0.00}초)");
            OnFired(fired);
        }

        public override string StatusText() => IntervalLine();
    }

    /// <summary>아폴론 — 기본사격: 기본 간격 1초, 마우스 방향 직사</summary>
    public class ApolloLaunch : ProjectileLaunch
    {
        public ApolloLaunch(ArcheryCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Apollo;
        public override float BaseInterval => Cfg.apolloBaseInterval;
    }

    /// <summary>헤르메스 — 연사: 기본 간격 1.25초, 발사마다 공속 +10% (5초, 최대 5스택)</summary>
    public class HermesLaunch : ProjectileLaunch
    {
        private readonly List<float> stackExpiry = new List<float>();
        private float now;

        public HermesLaunch(ArcheryCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Hermes;
        public override float BaseInterval => Cfg.hermesBaseInterval;

        public int Stacks
        {
            get
            {
                int n = 0;
                for (int i = 0; i < stackExpiry.Count; i++) if (stackExpiry[i] > now) n++;
                return n;
            }
        }

        public float BuffRemaining
        {
            get
            {
                float r = 0f;
                for (int i = 0; i < stackExpiry.Count; i++) r = Mathf.Max(r, stackExpiry[i] - now);
                return r;
            }
        }

        public override float AttackSpeedBonus => Stacks * Cfg.hermesAttackSpeedPerStack;

        public override void Tick(ArcheryInput input, float dt)
        {
            now += dt;
            stackExpiry.RemoveAll(t => t <= now);
            base.Tick(input, dt);
        }

        protected override void OnFired(ArcheryArrow arrow)
        {
            // 발사 후 스택 획득 → 다음 발사부터 빨라진다
            float expiry = now + Cfg.hermesBuffDuration;
            if (Cfg.hermesRefreshMode == HermesStackRefreshMode.RefreshAllOnShot)
            {
                if (stackExpiry.Count < Cfg.hermesMaxStacks) stackExpiry.Add(expiry);
                for (int i = 0; i < stackExpiry.Count; i++) stackExpiry[i] = expiry;
            }
            else
            {
                if (stackExpiry.Count >= Cfg.hermesMaxStacks)
                {
                    stackExpiry.Sort();
                    stackExpiry.RemoveAt(0);
                }
                stackExpiry.Add(expiry);
            }
            combat.Ctx.Log($"연사 버프 {Stacks}/{Cfg.hermesMaxStacks}스택 (공속 +{AttackSpeedBonus * 100f:0}%)");
        }

        public override string StatusText()
        {
            return $"연사 버프 {Stacks}/{Cfg.hermesMaxStacks}스택  공속 +{AttackSpeedBonus * 100f:0}%  남은 시간 {BuffRemaining:0.0}초\n" + IntervalLine();
        }
    }

    /// <summary>아테나 — 곡사: 기본 간격 0.6초, 무게에 비례한 중력으로 떨어진다</summary>
    public class AthenaLaunch : ProjectileLaunch
    {
        public AthenaLaunch(ArcheryCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Athena;
        public override float BaseInterval => Cfg.athenaBaseInterval;
        protected override float GravityFor(ArrowDefinition def) => Mathf.Max(0f, def.weight) * Cfg.athenaGravityPerWeight;

        public override void DrawGizmos(ArcheryDebugDraw draw)
        {
            if (!draw.showRanges) return;
            var next = combat.Quiver.Peek();
            if (next == null) return;
            // 다음 화살의 예상 궤적
            Vector2 p = combat.MuzzlePosition;
            Vector2 v = combat.AimDirectionFrom(p, combat.CurrentInput.aimWorld) * Cfg.arrowSpeed;
            float g = GravityFor(next.Def);
            var pts = new List<Vector3>();
            for (int i = 0; i < 40; i++)
            {
                pts.Add(new Vector3(p.x, p.y, -0.1f));
                float step = 0.05f;
                v.y -= g * step;
                p += v * step;
                if (p.y < -1f) break;
            }
            var c = next.Def.tint;
            draw.Polyline(pts, new Color(c.r, c.g, c.b, 0.35f), 0.04f, 0f);
        }

        public override string StatusText()
        {
            var next = combat.Quiver.Peek();
            string g = next != null ? $"다음 화살 무게 {next.Def.weight:0.##} → 낙하 가속 {GravityFor(next.Def):0.#}" : "";
            return g + "\n" + IntervalLine();
        }
    }

    /// <summary>
    /// 오디세우스 — 차지샷. 충전(입력·시간)과 피해 계산(ChargeDamage)을 분리했다.
    /// [잠정] 정규화 충전시간 = 실제 충전시간 × 공속 ÷ 활시위 배율, [1.2, 1.5]로 제한.
    /// [잠정] 배율 = 정규화 충전시간 ÷ 1초 → 기본 화살(6) 1.2초=7.2, 1.3초=7.8, 1.4초=8.4, 1.5초=9.0 (+공격력)
    /// [잠정] 최소 충전 전에 놓으면 발사를 취소하고 화살을 소모하지 않는다.
    /// </summary>
    public class OdysseusLaunch : LaunchBehaviour
    {
        public bool Charging { get; private set; }
        public float ChargeElapsed { get; private set; }

        public OdysseusLaunch(ArcheryCombat c) : base(c) { }
        public override LaunchStyle Style => LaunchStyle.Odysseus;
        public override float BaseInterval => Cfg.odysseusMinCharge;

        private float DrawScale(ArrowDefinition def) => Cfg.odysseusChargeScalesWithDraw ? Cfg.DrawMultiplier(def) : 1f;

        /// <summary>실제 필요한 충전시간(초)</summary>
        public float MinChargeFor(ArrowDefinition def) => Cfg.odysseusMinCharge * DrawScale(def) / combat.AttackSpeedMultiplier;
        public float MaxChargeFor(ArrowDefinition def) => Cfg.odysseusMaxCharge * DrawScale(def) / combat.AttackSpeedMultiplier;

        /// <summary>실제 충전시간 → 기본 화살 기준 충전시간(초)</summary>
        public float NormalizedCharge(ArrowDefinition def, float elapsed)
        {
            float n = elapsed * combat.AttackSpeedMultiplier / DrawScale(def);
            return Mathf.Clamp(n, Cfg.odysseusMinCharge, Cfg.odysseusMaxCharge);
        }

        /// <summary>피해 계산 (충전 입력과 분리). 정규화 충전시간(초)을 받는다.</summary>
        public static float ChargeDamage(ArcheryConfig cfg, ArrowDefinition def, float attack, float normalizedChargeSeconds)
        {
            float mult = normalizedChargeSeconds / Mathf.Max(0.01f, cfg.odysseusDamageReferenceSeconds);
            return cfg.odysseusMultiplierIncludesAttack ? (def.damage + attack) * mult : def.damage * mult + attack;
        }

        public override void Tick(ArcheryInput input, float dt)
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
                ChargeElapsed = Mathf.Min(ChargeElapsed + dt, MaxChargeFor(next.Def));
                return;
            }

            // 놓음
            Charging = false;
            if (ChargeElapsed + 0.0001f < MinChargeFor(next.Def))
            {
                combat.Ctx.Log($"차지 취소: {ChargeElapsed:0.00}초 < 최소 {MinChargeFor(next.Def):0.00}초 (화살 소모 없음)");
                ChargeElapsed = 0f;
                return;
            }

            float norm = NormalizedCharge(next.Def, ChargeElapsed);
            float damage = ChargeDamage(Cfg, next.Def, combat.Attack, norm);
            Vector2 dir = combat.AimDirectionFrom(combat.MuzzlePosition, input.aimWorld);
            var fired = combat.FireNext(dir, 0f, damage);
            combat.Ctx.Log($"차지샷: {fired.Label} 실제 {ChargeElapsed:0.00}초 (기준 {norm:0.00}초) → 피해 {ArcheryCombatContext.FormatDamage(damage)}");
            LastChargeSeconds = ChargeElapsed;
            LastDamage = damage;
            ChargeElapsed = 0f;
        }

        public float LastChargeSeconds { get; private set; }
        public float LastDamage { get; private set; }

        public override string StatusText()
        {
            var next = combat.Quiver.Peek();
            if (next == null) return "다음 화살 없음";
            float min = MinChargeFor(next.Def), max = MaxChargeFor(next.Def);
            float progress = Mathf.Clamp01(ChargeElapsed / Mathf.Max(0.001f, max));
            int bars = Mathf.RoundToInt(progress * 20f);
            int minMark = Mathf.RoundToInt(min / max * 20f);
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < 20; i++) sb.Append(i == minMark ? '|' : (i < bars ? '■' : '·'));
            sb.Append(']');
            float norm = NormalizedCharge(next.Def, Mathf.Max(ChargeElapsed, min));
            float dmg = ChargeDamage(Cfg, next.Def, combat.Attack, norm);
            string state = Charging ? (ChargeElapsed >= min ? "발사 가능" : "충전 중") : "좌클릭 누르기로 충전";
            return $"충전 {sb} {ChargeElapsed:0.00}초 / 최소 {min:0.00} 최대 {max:0.00}  ({state})\n" +
                   $"기준 충전 {norm:0.00}초 → 최종 피해 {ArcheryCombatContext.FormatDamage(dmg)}" +
                   (LastDamage > 0f ? $"   (직전 발사: {LastChargeSeconds:0.00}초, {ArcheryCombatContext.FormatDamage(LastDamage)})" : "");
        }
    }

    /// <summary>
    /// 아레스 — 찌르기: 기본 간격 0.5초. 플레이어 앞 직사각형 판정에서 가장 가까운 적 1명을 찌른다.
    /// 적중하면 화살이 소모되어 그 적에게 박히고(기본 줍기는 떨어짐), 빗나가면 화살을 소모하지 않는다.
    /// </summary>
    public class AresThrustLaunch : LaunchBehaviour
    {
        private static readonly List<Collider2D> overlap = new List<Collider2D>(16);
        public int Hits { get; private set; }
        public int Misses { get; private set; }

        public AresThrustLaunch(ArcheryCombat c) : base(c) { }
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

        public override void Tick(ArcheryInput input, float dt)
        {
            AdvanceDraw(dt);
            if (!(input.fireDown || input.fireHeld)) return;
            var next = combat.Quiver.Peek();
            if (next == null)
            {
                if (input.fireDown) combat.EmptyQuiverFeedback();
                return;
            }
            if (!IsReady) return;

            GetHitbox(input.aimWorld, out var center, out var size, out var angle, out var dir);
            var filter = new ContactFilter2D { useTriggers = true };
            overlap.Clear();
            Physics2D.OverlapBox(center, size, angle, filter, overlap);

            ArcheryEnemy best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < overlap.Count; i++)
            {
                var e = overlap[i].GetComponentInParent<ArcheryEnemy>();
                if (e == null || !e.IsAlive) continue;
                float d = e.DistanceFrom(combat.PlayerCenter);
                if (d < bestDist) { bestDist = d; best = e; }
            }

            combat.PlayFireFeedback(dir);
            if (best != null)
            {
                var arrow = combat.Quiver.TakeNext();
                Vector2 point = best.Col.ClosestPoint(combat.PlayerCenter);
                float damage = ArrowHitResolver.NormalDamage(arrow.Def, combat.Attack);
                arrow.PlaceFromThrust(point, dir, damage);
                combat.OnArrowStruckEnemy(arrow, best, point, HitSource.Thrust);
                Hits++;
                combat.Ctx.Draw?.Box(center, size, angle, new Color(1f, 0.35f, 0.25f, 1f), 0.07f, 0.18f);
                drawProgress = 0f;
            }
            else
            {
                Misses++;
                combat.Ctx.Log($"찌르기 빗나감: {next.Label} 소모 안 됨 (화살통 {combat.Quiver.Count}발 유지)");
                combat.Ctx.Draw?.Box(center, size, angle, new Color(0.7f, 0.7f, 0.7f, 1f), 0.05f, 0.18f);
                if (Cfg.aresMissConsumesInterval) drawProgress = 0f;
            }
        }

        public override void DrawGizmos(ArcheryDebugDraw draw)
        {
            if (!draw.showRanges) return;
            GetHitbox(combat.CurrentInput.aimWorld, out var center, out var size, out var angle, out _);
            draw.Box(center, size, angle, new Color(1f, 0.55f, 0.35f, IsReady ? 0.5f : 0.2f), 0.03f);
        }

        public override string StatusText()
        {
            return $"찌르기 범위 {Cfg.aresThrustRange:0.#}×{Cfg.aresThrustWidth:0.#}  적중 {Hits} / 빗나감 {Misses}\n" + IntervalLine();
        }
    }
}
