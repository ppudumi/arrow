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

        /// <summary>라운드가 끝나거나 회수 방식이 바뀌어 이 객체를 버릴 때 (이펙트 등 정리)</summary>
        public virtual void Dispose() { }

        protected void AutoPickup()
        {
            if (combat.Quiver != null && combat.Quiver.Count >= combat.MaxQuiverCapacity) return;
            Vector2 p = combat.PlayerCenter;
            float r = Cfg.autoPickupRadius;
            foreach (var a in combat.Bodies)
            {
                if (a.State != ArrowState.Grounded) continue;
                if (Vector2.Distance(a.transform.position, p) <= r) combat.TryRecover(a, "줍기");
            }
        }

        protected int PullAllFieldArrows(float speed, float exponent, string source, bool useSlerp = false)
        {
            int started = 0;
            Vector2 target = combat.PlayerCenter + Vector2.up * 0.2f;
            foreach (var a in combat.Bodies)
            {
                if (!a.IsOnField) continue;
                float dist = Vector2.Distance(a.transform.position, target);
                float dur = Mathf.Max(0.05f, dist / Mathf.Max(1f, speed));
                if (a.BeginReturn(combat.PlayerCenter, dur, exponent, source, useSlerp)) started++;
            }
            return started;
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

    /// <summary>기본 — 줍기: 화살이 적에게 박히지 않고 떨어진다. R을 누르면 모든 화살을 불러온다.</summary>
    public class BasicPickup : RetrievalBehaviour
    {
        public BasicPickup(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Basic;
        public override bool ArrowsStickToEnemies => false;
        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            if (input.recallDown) PullAllFieldArrows(24f, 1.5f, "줍기");
        }
        public override string StatusText() => "바닥의 화살 줍기 / R: 모든 화살 불러오기";
    }

    /// <summary>오르페우스 — 부르기: R을 누르고 있는 동안 필드의 모든 화살이 고정된 빠른 속도로 Slerp 호를 그리며 돌아온다.</summary>
    public class OrpheusCall : RetrievalBehaviour
    {
        public const string Source = "부르기";
        public const float FixedReturnSpeed = 28f;
        public bool Holding { get; private set; }
        public OrpheusCall(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Orpheus;

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            if (lyre == null) lyre = OrpheusLyreFx.Create(LyreAnchor);

            Holding = input.recallDown || input.recallHeld;
            if (input.recallDown)
            {
                int started = 0;
                foreach (var a in combat.Bodies)
                {
                    if (!a.IsOnField) continue;
                    float dist = Vector2.Distance(a.transform.position, combat.PlayerCenter + Vector2.up * 0.2f);
                    float dur = Mathf.Max(0.05f, dist / FixedReturnSpeed);
                    if (a.BeginReturn(combat.PlayerCenter, dur, 1f, Source, useSlerp: true)) started++;
                }
                if (started > 0)
                {
                    combat.Log($"부르기: {started}발 귀환 시작");
                    Sfx.Play("recall", 0.5f);
                    Vector2 emitPos = lyre != null ? (Vector2)lyre.transform.position : combat.PlayerCenter;
                    lyre?.TriggerPulse();
                    Fx.WaveRing(emitPos, 1f, 0.45f);
                    Fx.WaveRing(emitPos, 0.65f, 0.35f);
                    Fx.Burst(emitPos, new Color(1f, 0.88f, 0.45f), 8, 4.5f, 0.12f, 0.32f, 0f);
                }
            }

            // 부르기 이펙트: 부르기로 돌아오는 화살이 있는 동안 현악기 위치에 표시
            if (CountReturning(Source) > 0)
            {
                if (fx == null) fx = RecallFx.Create(() => lyre != null ? (Vector2)lyre.transform.position : combat.PlayerCenter);
                if (fx != null) fx.Pulse();
            }
            else if (fx != null) fx.Hide();
        }

        private Vector2 LyreAnchor()
        {
            float facing = combat.Aim != null && !combat.Aim.IsFacingRight ? -1f : 1f;
            return combat.PlayerCenter + new Vector2(-facing * 0.72f, 1.05f);
        }

        private RecallFx fx;
        private OrpheusLyreFx lyre;
        /// <summary>검증용: 지금 이펙트가 보이는지</summary>
        public bool FxVisible => fx != null && fx.Visible;

        public override void Dispose()
        {
            if (fx != null) Object.Destroy(fx.gameObject);
            fx = null;
            if (lyre != null) Object.Destroy(lyre.gameObject);
            lyre = null;
        }

        public override string StatusText() => $"R 누르기: 부르기 (귀환 중 {CountReturning(Source)}발)";
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
            if (input.recallDown) PullAllFieldArrows(26f, 1.5f, "뽑기");
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

    /// <summary>데메테르 — 수확: R 클릭 시 마우스 방향 부채꼴에 낫, R 0.5초 차징 시 3초간 날아가는 낫 투척(쿨타임 3초).</summary>
    public class DemeterHarvest : RetrievalBehaviour
    {
        public const float ChargeDuration = 0.5f;
        public const float ThrowCooldownDuration = 3f;
        public const float ProjectileLifetime = 3f;

        private float cooldown;
        private float throwCooldown;
        private bool charging;
        private float chargeElapsed;

        public int Swings { get; private set; }
        public bool IsCharging => charging && throwCooldown <= 0f;
        public float ChargeProgress => IsCharging ? Mathf.Clamp01(chargeElapsed / ChargeDuration) : 0f;
        public float ThrowCooldownRemaining => throwCooldown;

        public DemeterHarvest(PlayerCombat c) : base(c) { }
        public override RetrievalStyle Style => RetrievalStyle.Demeter;
        public override float CooldownRemaining => cooldown;
        public float SwingInterval => Cfg.apolloBaseInterval * Cfg.demeterIntervalFactor / combat.AttackSpeedMultiplier;
        public float SlashRadius => Cfg.demeterRadius * 1.35f;

        public bool InSector(Vector2 origin, Vector2 dir, Vector2 point)
        {
            Vector2 d = point - origin;
            float dist = d.magnitude;
            if (dist > SlashRadius) return false;
            if (dist < 0.35f) return true;
            return Vector2.Angle(dir, d) <= Cfg.demeterAngle * 0.5f;
        }

        public override void Tick(CombatInput input, float dt)
        {
            base.Tick(input, dt);
            if (cooldown > 0f) cooldown = Mathf.Max(0f, cooldown - dt);
            if (throwCooldown > 0f) throwCooldown = Mathf.Max(0f, throwCooldown - dt);

            if (!charging && input.recallDown)
            {
                charging = true;
                chargeElapsed = 0f;
            }

            if (!charging) return;

            if (input.recallHeld && !input.recallUp)
            {
                if (throwCooldown <= 0f)
                {
                    chargeElapsed += dt;
                    if (chargeElapsed >= ChargeDuration)
                    {
                        charging = false;
                        chargeElapsed = 0f;
                        ThrowScythe(input.aimWorld);
                    }
                }
                return;
            }

            charging = false;
            chargeElapsed = 0f;
            if (cooldown <= 0f) SwingScythe(input.aimWorld);
        }

        private void SwingScythe(Vector2 aimWorld)
        {
            Vector2 origin = combat.PlayerCenter;
            Vector2 dir = combat.AimDirectionFrom(origin, aimWorld);
            cooldown = SwingInterval;
            Swings++;
            combat.PlayFireFeedback(dir);
            Sfx.Play("swing");
            Fx.ScytheSlash(origin, dir, SlashRadius, Cfg.demeterAngle, 0.16f);
            Fx.Burst(origin + dir * (SlashRadius * 0.55f), new Color(0.35f, 1f, 0.5f), 10, 6.5f, 0.14f, 0.28f, 2f);
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
            int pulled = PullAllFieldArrows(26f, 1.5f, "수확");
            combat.Log($"수확: 피해 {damage:0.#}, 화살 {rec}발 회수 (추가 귀환 {pulled}발)");
        }

        private void ThrowScythe(Vector2 aimWorld)
        {
            Vector2 origin = combat.PlayerCenter;
            Vector2 dir = combat.AimDirectionFrom(origin, aimWorld);
            throwCooldown = ThrowCooldownDuration;
            Swings++;
            combat.PlayFireFeedback(dir);
            Sfx.Play("swing", 1f, 0.85f);
            Fx.Burst(origin + dir * 0.6f, new Color(0.45f, 1f, 0.6f), 10, 6f, 0.16f, 0.3f);

            var go = new GameObject("DemeterThrownScythe");
            go.transform.position = new Vector3(origin.x, origin.y, -0.15f);
            var proj = go.AddComponent<DemeterScytheProjectile>();
            proj.Init(combat, dir, ProjectileLifetime, combat.Attack * Cfg.demeterDamageMultiplier);
            combat.Log("수확 투척: 날아가는 낫 발사 (쿨타임 3초)");
        }

        public override string StatusText()
        {
            if (throwCooldown > 0f) return $"R: 수확 · 투척 대기 {throwCooldown:0.0}초";
            return cooldown > 0f ? $"R: 수확 (대기 {cooldown:0.0}초)" : "R: 수확 / 0.5초 차징: 낫 투척";
        }
    }

    /// <summary>데메테르 0.5초 차징으로 날아가는 낫 투사체 (demeter-scythe 사용, 회전 비행, 중앙이 벽에 닿으면 플레이어에게 복귀)</summary>
    public class DemeterScytheProjectile : MonoBehaviour
    {
        public const string ResourcePath = "LaurelFx/demeter-scythe";
        private const float Speed = 28f;
        private const float SpinSpeed = 1080f;
        private const float HitRadius = 2.775f;
        private const float AfterimageInterval = 0.025f;

        private static Sprite cachedScytheSprite;
        private static readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>(8);

        private PlayerCombat combat;
        private Vector2 dir;
        private float life;
        private float damage;
        private float angleZ;
        private float afterimageTimer;
        private bool returning;
        private SpriteRenderer sr;
        private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
        private readonly List<ArrowBody> arrowScratch = new List<ArrowBody>();

        private static Sprite LoadScytheSprite()
        {
            if (cachedScytheSprite != null) return cachedScytheSprite;
            var tex = Resources.Load<Texture2D>(ResourcePath);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point;
                cachedScytheSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                return cachedScytheSprite;
            }
            return ScytheSlashFx.LoadSprite();
        }

        public void Init(PlayerCombat c, Vector2 direction, float duration, float dmg)
        {
            combat = c;
            dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            life = duration;
            damage = dmg;
            angleZ = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            var sp = LoadScytheSprite();
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = 36;
            sr.color = Color.white;
            float spriteWidthUnits = sp != null ? sp.rect.width / sp.pixelsPerUnit : 2.56f;
            float sc = 3.8f / Mathf.Max(0.1f, spriteWidthUnits);
            transform.localScale = new Vector3(sc, sc, 1f);
            transform.rotation = Quaternion.Euler(0f, 0f, angleZ);
        }

        private void SpawnAfterimage()
        {
            if (sr == null || sr.sprite == null) return;
            var ghost = new GameObject("DemeterScytheGhost");
            ghost.transform.position = transform.position + new Vector3(0f, 0f, 0.01f);
            ghost.transform.rotation = transform.rotation;
            ghost.transform.localScale = transform.localScale;
            var gsr = ghost.AddComponent<SpriteRenderer>();
            gsr.sprite = sr.sprite;
            gsr.flipY = sr.flipY;
            gsr.sortingOrder = sr.sortingOrder - 1;
            gsr.color = new Color(0.28f, 0.95f, 0.45f, 0.55f);
            ghost.AddComponent<DemeterScytheAfterimage>().Init(gsr, 0.2f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f || combat == null || !combat.Active)
            {
                Destroy(gameObject);
                return;
            }

            Vector2 pos = transform.position;
            if (!returning)
            {
                Vector2 nextPos = pos + dir * (Speed * dt);
                var filter = new ContactFilter2D { useTriggers = false };
                filter.SetLayerMask(Layers.TerrainMask);
                wallHits.Clear();
                int hitCount = Physics2D.Linecast(pos, nextPos, filter, wallHits);
                bool hitWall = false;
                Vector2 hitPoint = nextPos;
                float bestDist = float.MaxValue;
                for (int i = 0; i < hitCount; i++)
                {
                    var h = wallHits[i];
                    if (h.collider == null || h.collider.isTrigger || h.collider.usedByEffector) continue;
                    if (h.distance < bestDist) { bestDist = h.distance; hitPoint = h.point; hitWall = true; }
                }
                if (hitWall)
                {
                    pos = hitPoint;
                    returning = true;
                    hitEnemies.Clear();
                    Fx.Burst(pos, new Color(0.35f, 1f, 0.55f), 8, 5f, 0.14f, 0.28f);
                    Sfx.Play("wall", 0.5f, 1.2f);
                }
                else
                {
                    pos = nextPos;
                }
            }
            else
            {
                Vector2 toPlayer = combat.PlayerCenter - pos;
                float dist = toPlayer.magnitude;
                float step = Speed * dt;
                if (dist <= Mathf.Max(0.8f, step))
                {
                    Destroy(gameObject);
                    return;
                }
                pos += (toPlayer / dist) * step;
            }

            transform.position = new Vector3(pos.x, pos.y, -0.15f);
            angleZ += (dir.x >= 0f ? -SpinSpeed : SpinSpeed) * dt;
            transform.rotation = Quaternion.Euler(0f, 0f, angleZ);

            afterimageTimer += dt;
            if (afterimageTimer >= AfterimageInterval)
            {
                afterimageTimer = 0f;
                SpawnAfterimage();
            }

            // 낫에 맞은 화살 회수
            arrowScratch.Clear();
            for (int i = 0; i < combat.Bodies.Count; i++)
            {
                var a = combat.Bodies[i];
                if (a != null && a.IsOnField && Vector2.Distance(a.transform.position, pos) <= HitRadius)
                    arrowScratch.Add(a);
            }
            for (int i = 0; i < arrowScratch.Count; i++)
                combat.TryRecover(arrowScratch[i], "수확");

            // 경로 상의 적 피해
            if (combat.Room != null)
            {
                foreach (var e in new List<Enemy>(combat.Room.Enemies))
                {
                    if (e == null || !e.IsAlive || hitEnemies.Contains(e)) continue;
                    if (e.DistanceFrom(pos) <= HitRadius)
                    {
                        hitEnemies.Add(e);
                        e.TakeDamage(damage, "수확", new Color(1f, 0.9f, 0.4f), false);
                    }
                }
            }

            if (sr != null && life < 0.35f)
            {
                sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(life / 0.35f));
            }
        }
    }

    public class DemeterScytheAfterimage : MonoBehaviour
    {
        private SpriteRenderer sr;
        private float life, maxLife;
        private Color baseCol;

        public void Init(SpriteRenderer r, float duration)
        {
            sr = r;
            maxLife = life = Mathf.Max(0.05f, duration);
            baseCol = r.color;
        }

        private void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f || sr == null) { Destroy(gameObject); return; }
            float t = life / maxLife;
            sr.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * t);
        }
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
                if (a.IsOnField)
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
            if (scratch.Count == 0)
            {
                int fallbackPulled = PullAllFieldArrows(28f, 1.4f, "뇌우");
                combat.Log(fallbackPulled > 0 ? $"뇌우: {fallbackPulled}발 귀환" : "뇌우: 범위 안에 화살 없음 (대기시간 소모 안 함)");
                return;
            }

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
            int pulled = PullAllFieldArrows(28f, 1.4f, "뇌우");
            cooldown = Cfg.zeusCooldown;
            Casts++;
            Sfx.Play("zap");
            combat.Log($"뇌우: {rec}발 즉시 회수, {pulled}발 귀환, 번개 적중 {damaged.Count}");
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
