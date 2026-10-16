using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 적 상태이상. [임시 결정] 같은 상태이상은 중첩하지 않고 지속시간을 더 긴 쪽으로 갱신한다(재앙·스태틱만 횟수/스택 누적).
    /// 보스에게는 지속시간·감속·끌어당김이 절반, 넉백은 15%만 적용한다.
    /// </summary>
    public class StatusSet
    {
        private readonly Enemy owner;
        public float poisonT, poisonDps; private float poisonTick;
        public float burnT, burnDps; private float burnTick;
        public float midasT; public float midasGoldCd;
        public int calamity;
        public float virusT;
        public int staticStacks; public float staticTimer;
        public float resonanceDamage;
        public float sisyT, sisyMul = 1f;
        public float stunT;
        public float webSlow;       // 거미줄 위에 있을 때 Room 이 매 프레임 설정
        public float webTime;
        public bool inOil;

        public StatusSet(Enemy e) { owner = e; }
        private StatusTuning St => DB.Balance.status;
        private float DurMul => owner.IsBoss ? St.bossDurationMul : 1f;

        public void ApplyPoison(float dur, float dps) { poisonT = Mathf.Max(poisonT, dur * DurMul); poisonDps = Mathf.Max(poisonDps, dps); }

        public void ApplyBurn(float dur, float dps)
        {
            float mul = inOil ? St.oilBurnMultiplier : 1f;
            burnT = Mathf.Max(burnT, dur * DurMul);
            burnDps = Mathf.Max(burnDps, dps * mul);
            if (inOil) owner.Room?.IgniteOilAt(owner.FeetPosition);
        }

        public void ApplyMidas(float dur) { midasT = Mathf.Max(midasT, dur * DurMul); }
        public void ApplyVirus(float dur) { virusT = Mathf.Max(virusT, dur * DurMul); }

        public void AddStatic(int n)
        {
            staticStacks = Mathf.Min(St.staticMaxStacks, staticStacks + n);
            staticTimer = St.staticDelay;
        }

        public float SpeedMul
        {
            get
            {
                if (stunT > 0f) return 0f;
                float m = 1f;
                float cc = owner.IsBoss ? St.bossCrowdControlMul : 1f;
                if (midasT > 0f) m *= 1f - St.midasSlow * cc;
                if (webSlow > 0f) m *= 1f - webSlow * cc;
                return m;
            }
        }

        public float DamageTakenMul => sisyT > 0f ? sisyMul : 1f;

        public void Tick(float dt)
        {
            if (poisonT > 0f)
            {
                poisonT -= dt; poisonTick += dt;
                if (poisonTick >= St.poisonTick) { poisonTick = 0f; owner.TakeDamage(poisonDps, "독", new Color(0.5f, 0.9f, 0.3f), false); }
            }
            if (burnT > 0f)
            {
                burnT -= dt; burnTick += dt;
                if (burnTick >= 0.5f) { burnTick = 0f; owner.TakeDamage(burnDps * 0.5f, "화염", new Color(1f, 0.5f, 0.2f), false); }
                if (Random.value < 0.3f) Fx.Burst(owner.Center, new Color(1f, 0.5f, 0.1f), 1, 2f, 0.12f, 0.3f, -4f);
            }
            if (midasT > 0f) midasT -= dt;
            if (midasGoldCd > 0f) midasGoldCd -= dt;
            if (virusT > 0f) virusT -= dt;
            if (sisyT > 0f) sisyT -= dt;
            if (stunT > 0f) stunT -= dt;
            if (staticStacks > 0)
            {
                staticTimer -= dt;
                if (staticTimer <= 0f)
                {
                    float dmg = St.staticPerStack * staticStacks;
                    Fx.Lightning((Vector2)owner.Center + Vector2.up * 3f, owner.Center, new Color(1f, 0.95f, 0.5f), 0.09f, 0.3f);
                    staticStacks = 0;
                    owner.TakeDamage(dmg, "스태틱", new Color(1f, 0.95f, 0.5f), false);
                }
            }
        }

        public string Badges()
        {
            var s = "";
            if (poisonT > 0) s += "독 ";
            if (burnT > 0) s += "화염 ";
            if (midasT > 0) s += "미다스 ";
            if (calamity > 0) s += $"재앙{calamity} ";
            if (virusT > 0) s += "바이러스 ";
            if (staticStacks > 0) s += $"스태틱{staticStacks} ";
            if (resonanceDamage > 0) s += "공명 ";
            if (sisyT > 0) s += "형벌 ";
            if (webSlow > 0) s += "거미줄 ";
            if (stunT > 0) s += "기절 ";
            return s.Trim();
        }
    }

    /// <summary>
    /// 적 기반. 콜라이더는 트리거(플레이어를 밀지 않음), 이동은 직접 적분 + 지형 BoxCast.
    /// 화살은 Linecast(트리거 포함)로 맞춘다.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public EnemyDef Def { get; private set; }
        public string DisplayName { get; protected set; }
        public float Hp { get; protected set; }
        public float MaxHp { get; protected set; }
        public float ContactDamage { get; protected set; }
        public float Damage { get; protected set; }
        public bool IsBoss { get; protected set; }
        public bool IsElite { get; protected set; }
        public bool IsAlive => !dead && Hp > 0f;
        public StatusSet Status { get; private set; }
        public Room Room { get; private set; }
        public BoxCollider2D Col { get; protected set; }
        public readonly List<ArrowBody> StuckArrows = new List<ArrowBody>();
        public Vector2 spawnPos;
        public ArrowDef lastArrowForm;
        public float lastArrowDamage;
        public int lastHitCardUid;
        public int goldValue;
        public float speed;
        /// <summary>검증용: 받은 피해 누적</summary>
        public float DamageTaken { get; private set; }
        public int HitsTaken { get; private set; }

        protected SpriteRenderer bodySr;
        protected Color baseColor;
        protected Vector2 halfSize = new Vector2(0.5f, 0.5f);
        protected Vector2 velocity;
        protected Vector2 kb;
        protected bool grounded, hitWall;
        protected float gravity = 30f;
        protected bool usesGravity = true;
        protected bool dead;
        protected float flash;
        protected float facing = 1f;
        protected float aiTimer;
        protected int state;
        private float contactCd;

        public Vector3 Center => transform.position;
        public Vector2 FeetPosition => (Vector2)transform.position - new Vector2(0f, halfSize.y);
        public float Radius => Mathf.Max(halfSize.x, halfSize.y);
        public Vector2 HalfSize => halfSize;

        public virtual void Init(Room room, EnemyDef def, float hpMul, float dmgMul, bool elite)
        {
            Room = room;
            Def = def;
            Status = new StatusSet(this);
            IsElite = elite;
            float sizeMul = elite ? 1.3f : 1f;
            DisplayName = (elite ? "정예 " : "") + def.name;
            MaxHp = def.hp * hpMul * (elite ? 2.6f : 1f);
            Hp = MaxHp;
            Damage = def.damage * dmgMul * (elite ? 1.25f : 1f);
            ContactDamage = Damage;
            speed = def.speed * (elite ? 1.1f : 1f);
            goldValue = Mathf.RoundToInt(def.gold * (elite ? 3f : 1f));
            halfSize = new Vector2(def.w, def.h) * 0.5f * sizeMul;
            baseColor = def.tint;
            BuildVisual(def.tint, elite);
            usesGravity = !(def.ai == "flyer" || def.fly);
            spawnPos = transform.position;
            facing = Random.value < 0.5f ? -1f : 1f;
            aiTimer = Random.Range(0.3f, 1.2f);
        }

        protected void SetupBoss(Room room, BossDef def, Vector2 size)
        {
            Room = room;
            Status = new StatusSet(this);
            IsBoss = true;
            DisplayName = def.name;
            MaxHp = def.hp;
            Hp = MaxHp;
            Damage = def.damage;
            ContactDamage = def.damage;
            halfSize = size * 0.5f;
            baseColor = def.tint;
            spawnPos = transform.position;
        }

        protected virtual void BuildVisual(Color c, bool elite)
        {
            gameObject.layer = Layers.Enemy;
            Col = gameObject.AddComponent<BoxCollider2D>();
            Col.isTrigger = true;
            Col.size = halfSize * 2f;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            bodySr = Art.Quad(transform, "Body", Def.ai == "flyer" || Def.fly ? Art.Circle : Art.Square, c, halfSize * 2f, 10);
            if (elite)
            {
                var aura = Art.Quad(transform, "Aura", Art.Soft, new Color(1f, 0.4f, 0.2f, 0.45f), halfSize * 3.4f, 9);
                aura.transform.localPosition = Vector3.zero;
            }
            // 눈 (방향 표시)
            var eye = Art.Quad(bodySr.transform, "Eye", Art.Circle, Color.white, new Vector2(0.22f, 0.22f) / Mathf.Max(0.2f, halfSize.x * 2f), 11, new Vector3(0.25f, 0.2f, 0f));
            Art.Quad(eye.transform, "Pupil", Art.Circle, Color.black, Vector2.one * 0.5f, 12);
        }

        // ───────────── 루프 ─────────────

        protected virtual void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            Status.Tick(dt);
            if (dead) return;
            if (Status.stunT <= 0f) Think(dt);
            ApplyMotion(dt);
            ContactCheck(dt);
            if (flash > 0f) flash -= dt;
            if (bodySr != null)
            {
                Color c = flash > 0f ? Color.white : baseColor;
                if (Status.midasT > 0f) c = Color.Lerp(c, new Color(1f, 0.85f, 0.2f), 0.5f);
                if (Status.sisyT > 0f) c = Color.Lerp(c, new Color(0.5f, 0.4f, 0.3f), 0.4f);
                bodySr.color = c;
                var s = bodySr.transform.localScale;
                s.x = Mathf.Abs(s.x) * (facing >= 0 ? 1f : -1f);
                bodySr.transform.localScale = s;
            }
        }

        protected virtual void Think(float dt) { }

        protected void ApplyMotion(float dt)
        {
            if (usesGravity) velocity.y -= gravity * dt;
            Vector2 v = velocity;
            v.x *= Status.SpeedMul;
            if (!usesGravity) v.y *= Status.SpeedMul;
            v += kb;
            kb = Vector2.MoveTowards(kb, Vector2.zero, 30f * dt);
            MoveKinematic(v * dt);
        }

        private static readonly List<RaycastHit2D> boxHits = new List<RaycastHit2D>(8);
        private static ContactFilter2D TerrainFilter { get { var f = new ContactFilter2D { useTriggers = false }; f.SetLayerMask(Layers.TerrainMask); return f; } }

        protected void MoveKinematic(Vector2 delta)
        {
            hitWall = false;
            Vector2 pos = transform.position;
            Vector2 size = halfSize * 2f * 0.96f;
            if (Mathf.Abs(delta.x) > 0.0001f)
            {
                float dir = Mathf.Sign(delta.x);
                int n = Physics2D.BoxCast(pos, size, 0f, new Vector2(dir, 0f), TerrainFilter, boxHits, Mathf.Abs(delta.x) + 0.02f);
                float allowed = Mathf.Abs(delta.x);
                for (int i = 0; i < n; i++) { if (boxHits[i].collider.isTrigger) continue; allowed = Mathf.Min(allowed, Mathf.Max(0f, boxHits[i].distance - 0.02f)); hitWall = true; }
                pos.x += dir * allowed;
            }
            grounded = false;
            if (Mathf.Abs(delta.y) > 0.0001f)
            {
                float dir = Mathf.Sign(delta.y);
                int n = Physics2D.BoxCast(pos, size, 0f, new Vector2(0f, dir), TerrainFilter, boxHits, Mathf.Abs(delta.y) + 0.02f);
                float allowed = Mathf.Abs(delta.y);
                bool blocked = false;
                for (int i = 0; i < n; i++) { if (boxHits[i].collider.isTrigger) continue; allowed = Mathf.Min(allowed, Mathf.Max(0f, boxHits[i].distance - 0.02f)); blocked = true; }
                pos.y += dir * allowed;
                if (blocked)
                {
                    if (dir < 0f) grounded = true;
                    velocity.y = 0f;
                }
            }
            if (Room != null)
            {
                var b = Room.InnerBounds;
                pos.x = Mathf.Clamp(pos.x, b.xMin + halfSize.x, b.xMax - halfSize.x);
                pos.y = Mathf.Clamp(pos.y, b.yMin + halfSize.y, b.yMax - halfSize.y);
            }
            transform.position = pos;
        }

        protected bool GroundAhead(float dir)
        {
            Vector2 probe = (Vector2)transform.position + new Vector2(dir * (halfSize.x + 0.15f), -halfSize.y);
            return Physics2D.Raycast(probe, Vector2.down, 0.6f, Layers.TerrainMask).collider != null;
        }

        protected PlayerAvatar Player => Room != null ? Room.Player : null;
        protected Vector2 PlayerPos => Player != null ? (Vector2)Player.transform.position + Vector2.up * 0.1f : (Vector2)transform.position;

        private void ContactCheck(float dt)
        {
            if (contactCd > 0f) { contactCd -= dt; return; }
            var p = Player;
            if (p == null || ContactDamage <= 0f || Status.stunT > 0f) return;
            Vector2 d = PlayerPos - (Vector2)transform.position;
            if (Mathf.Abs(d.x) < halfSize.x + 0.25f && Mathf.Abs(d.y) < halfSize.y + 0.45f)
            {
                if (p.Hurt(ContactDamage, DisplayName)) contactCd = 0.5f;
            }
        }

        // ───────────── 피해 ─────────────

        public virtual void TakeDamage(float amount, string label, Color color, bool isArrowHit)
        {
            if (dead || amount <= 0f) return;
            if (Status.calamity > 0) { amount *= 2f; Status.calamity--; Fx.Text(Center + Vector3.up * 0.8f, "재앙!", new Color(0.7f, 0.3f, 1f), 20, 0.6f); }
            amount *= Status.DamageTakenMul;
            Hp -= amount;
            DamageTaken += amount;
            if (isArrowHit) HitsTaken++;
            flash = 0.08f;
            Fx.Text(Center + Vector3.up * (halfSize.y + 0.2f), amount.ToString("0.#"), color, isArrowHit ? 28 : 22);
            if (isArrowHit && Status.resonanceDamage > 0f && Room != null)
            {
                float rd = Status.resonanceDamage;
                Status.resonanceDamage = 0f;
                Fx.Circle(Center, 2.6f, new Color(0.5f, 0.9f, 1f), 0.08f, 0.3f);
                Room.Explode(Center, 2.6f, rd, null, new Color(0.5f, 0.9f, 1f), "공명");
            }
            if (Status.midasT > 0f) DropMidasGold(false);
            OnDamaged(amount);
            if (Hp <= 0f) Die();
        }

        public void TakeTrueDamage(float amount, string label)
        {
            if (dead) return;
            Hp -= amount;
            DamageTaken += amount;
            Fx.Text(Center + Vector3.up * (halfSize.y + 0.5f), "-" + amount.ToString("0"), new Color(0.75f, 0.85f, 1f), 22);
            if (Hp <= 0f) Die();
        }

        protected virtual void OnDamaged(float amount) { }

        public void DropMidasGold(bool force)
        {
            if (Room == null) return;
            if (!force && Status.midasGoldCd > 0f) return;
            Status.midasGoldCd = DB.Balance.status.midasGoldCooldown;
            int g = Random.Range(DB.Balance.status.midasGoldMin, DB.Balance.status.midasGoldMax + 1);
            Room.SpawnGold(Center, g);
        }

        public void Knockback(Vector2 v)
        {
            if (IsBoss) v *= DB.Balance.status.bossKnockbackMul;
            kb += v;
        }

        public void ApplySisyphus()
        {
            var st = DB.Balance.status;
            if (IsBoss)
            {
                Status.sisyT = st.bossSisyphusDuration;
                Status.sisyMul = st.bossSisyphusMul;
                Fx.Text(Center + Vector3.up, "시시포스의 형벌", new Color(0.8f, 0.6f, 0.4f), 22);
                return;
            }
            transform.position = spawnPos;
            Hp = MaxHp;
            Status.sisyT = 8f;
            Status.sisyMul = st.sisyphusVulnerableMul;
            Status.stunT = st.sisyphusStun;
            velocity = Vector2.zero;
            kb = Vector2.zero;
            Fx.Text(Center + Vector3.up, "처음 자리로!", new Color(0.8f, 0.6f, 0.4f), 22);
        }

        protected virtual void Die()
        {
            if (dead) return;
            dead = true;
            Hp = 0f;
            foreach (var a in new List<ArrowBody>(StuckArrows)) a.OnEnemyLost();
            StuckArrows.Clear();
            Fx.Burst(Center, baseColor, 16, 7f, 0.22f, 0.6f);
            Sfx.Play("hit", 0.9f, 0.7f);
            if (Status.virusT > 0f && Room != null) Room.VirusBurst(this);
            Room?.OnEnemyDied(this);
            if (Col != null) Col.enabled = false;
            OnDeathVisual();
        }

        protected virtual void OnDeathVisual() { Destroy(gameObject, 0.05f); }

        public float DistanceFrom(Vector2 p)
        {
            if (Col != null && Col.enabled) return Vector2.Distance(Col.ClosestPoint(p), p);
            return Mathf.Max(0f, Vector2.Distance(Center, p) - Radius);
        }

        public Vector2 ClosestPoint(Vector2 p) => Col != null && Col.enabled ? Col.ClosestPoint(p) : (Vector2)Center;

        public void RegisterStuck(ArrowBody a) { if (!StuckArrows.Contains(a)) StuckArrows.Add(a); }
        public void UnregisterStuck(ArrowBody a) { StuckArrows.Remove(a); }

        protected void Shoot(Vector2 dir, float spd, float grav, float dmg, Color c, float radius = 0.22f, float life = 5f)
        {
            if (Room == null) return;
            EnemyProjectile.Spawn(Room, (Vector2)Center + dir.normalized * (Radius * 0.6f), dir.normalized * spd, grav, dmg, c, radius, life);
            Sfx.Play("enemyshot", 0.35f);
        }

        public void ForceKill() { if (!dead) { Hp = 0f; Die(); } }
    }

    // ───────────────────────── 일반 적 AI ─────────────────────────

    /// <summary>순찰: 벽이나 낭떠러지에서 돌아선다</summary>
    public class WalkerEnemy : Enemy
    {
        protected override void Think(float dt)
        {
            if (grounded && (hitWall || !GroundAhead(facing))) facing = -facing;
            velocity.x = facing * speed;
        }
    }

    /// <summary>돌진: 같은 높이에서 플레이어를 보면 예비 동작 후 빠르게 돌진</summary>
    public class ChargerEnemy : Enemy
    {
        private float t;
        protected override void Think(float dt)
        {
            float range = Def.p1 > 0 ? Def.p1 : 12f;
            float windup = Def.p2 > 0 ? Def.p2 : 0.55f;
            Vector2 d = PlayerPos - (Vector2)Center;
            switch (state)
            {
                case 0:
                    if (grounded && (hitWall || !GroundAhead(facing))) facing = -facing;
                    velocity.x = facing * speed;
                    if (grounded && Mathf.Abs(d.y) < 1.4f && Mathf.Abs(d.x) < range) { state = 1; t = windup; facing = Mathf.Sign(d.x); velocity.x = 0f; }
                    break;
                case 1:
                    t -= dt; velocity.x = 0f;
                    flash = Mathf.PingPong(t * 8f, 1f) > 0.5f ? 0.05f : 0f;
                    if (t <= 0f) { state = 2; t = 1.3f; }
                    break;
                case 2:
                    t -= dt; velocity.x = facing * speed * 4.2f;
                    if (hitWall || t <= 0f || (grounded && !GroundAhead(facing))) { state = 3; t = 0.8f; velocity.x = 0f; if (hitWall) CameraRig.Shake(0.1f); }
                    break;
                case 3:
                    t -= dt; velocity.x = 0f;
                    if (t <= 0f) state = 0;
                    break;
            }
        }
    }

    /// <summary>비행: 사인파로 흔들리며 플레이어에게 다가온다</summary>
    public class FlyerEnemy : Enemy
    {
        private float phase;
        protected override void Think(float dt)
        {
            phase += dt * 2.4f;
            Vector2 d = PlayerPos + Vector2.up * 0.6f - (Vector2)Center;
            Vector2 want = d.normalized * speed + new Vector2(0f, Mathf.Sin(phase) * 1.6f);
            velocity = Vector2.MoveTowards(velocity, want, 10f * dt);
            if (Mathf.Abs(d.x) > 0.1f) facing = Mathf.Sign(d.x);
        }
    }

    /// <summary>원거리: 일정 간격으로 플레이어를 향해 쏜다 (fly=true 면 공중에 떠 있음)</summary>
    public class ShooterEnemy : Enemy
    {
        private float phase;
        protected override void Think(float dt)
        {
            float interval = Def.p1 > 0 ? Def.p1 : 2.4f;
            float pspeed = Def.p2 > 0 ? Def.p2 : 9f;
            Vector2 d = PlayerPos - (Vector2)Center;
            facing = Mathf.Sign(d.x == 0 ? 1 : d.x);
            if (Def.fly)
            {
                phase += dt;
                Vector2 hover = PlayerPos + new Vector2(-Mathf.Sign(d.x) * 5f, 4f + Mathf.Sin(phase * 1.5f));
                velocity = Vector2.MoveTowards(velocity, (hover - (Vector2)Center).normalized * speed, 8f * dt);
            }
            else
            {
                float keep = d.magnitude < 4f ? -1f : (d.magnitude > 9f ? 1f : 0f);
                float dir = Mathf.Sign(d.x) * keep;
                if (grounded && dir != 0 && !GroundAhead(dir)) dir = 0;
                velocity.x = dir * speed;
            }
            aiTimer -= dt;
            if (aiTimer <= 0f && d.magnitude < 18f)
            {
                aiTimer = interval * Random.Range(0.85f, 1.15f);
                if (Def.fly) Shoot(d, pspeed, 0f, Damage, baseColor.WithAlpha(1f));
                else
                {
                    // 포물선 투척: 거리에 맞춘 상향 각도
                    Vector2 v = new Vector2(d.x, d.y + Mathf.Abs(d.x) * 0.45f).normalized;
                    Shoot(v, pspeed, 9f, Damage, new Color(0.7f, 0.6f, 0.5f));
                }
                flash = 0.1f;
            }
        }
    }

    /// <summary>도약: 땅에서 잠시 기다렸다가 플레이어 쪽으로 크게 뛴다</summary>
    public class HopperEnemy : Enemy
    {
        protected override void Think(float dt)
        {
            float wait = Def.p1 > 0 ? Def.p1 : 1.6f;
            float jump = Def.p2 > 0 ? Def.p2 : 13f;
            if (grounded)
            {
                velocity.x = Mathf.MoveTowards(velocity.x, 0f, 30f * dt);
                aiTimer -= dt;
                if (aiTimer <= 0f)
                {
                    aiTimer = wait * Random.Range(0.8f, 1.2f);
                    float dx = PlayerPos.x - Center.x;
                    facing = Mathf.Sign(dx == 0 ? 1 : dx);
                    velocity = new Vector2(facing * speed * Mathf.Clamp(Mathf.Abs(dx) / 5f, 0.5f, 1.4f), jump);
                }
            }
        }
    }

    /// <summary>포탑: 제자리에서 여러 갈래로 쏜다</summary>
    public class TurretEnemy : Enemy
    {
        protected override void Think(float dt)
        {
            velocity.x = 0f;
            float interval = Def.p1 > 0 ? Def.p1 : 2.6f;
            float pspeed = Def.p2 > 0 ? Def.p2 : 8f;
            int count = Mathf.Max(1, Mathf.RoundToInt(Def.p3 > 0 ? Def.p3 : 3));
            Vector2 d = PlayerPos - (Vector2)Center;
            facing = Mathf.Sign(d.x == 0 ? 1 : d.x);
            aiTimer -= dt;
            if (aiTimer <= 0f)
            {
                aiTimer = interval * Random.Range(0.9f, 1.1f);
                float spread = 14f;
                for (int i = 0; i < count; i++)
                {
                    float a = (i - (count - 1) * 0.5f) * spread;
                    Shoot(Quaternion.Euler(0, 0, a) * d, pspeed, 0f, Damage, baseColor);
                }
                flash = 0.1f;
            }
        }
    }

    /// <summary>적 투사체. 지형에 닿으면 사라지고 플레이어에게 닿으면 피해.</summary>
    public class EnemyProjectile : MonoBehaviour
    {
        public Vector2 velocity;
        public float gravity, damage, radius, life;
        private Room room;

        public static EnemyProjectile Spawn(Room room, Vector2 pos, Vector2 vel, float grav, float dmg, Color c, float radius, float life)
        {
            var go = new GameObject("EnemyShot");
            go.transform.SetParent(room.transform, false);
            go.transform.position = pos;
            var p = go.AddComponent<EnemyProjectile>();
            p.room = room;
            p.velocity = vel;
            p.gravity = grav;
            p.damage = dmg;
            p.radius = radius;
            p.life = life;
            Art.Quad(go.transform, "glow", Art.Soft, c.WithAlpha(0.5f), Vector2.one * radius * 4f, 14);
            Art.Quad(go.transform, "core", Art.Circle, c, Vector2.one * radius * 2f, 15);
            room.RegisterProjectile(p);
            return p;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f) { Destroy(gameObject); return; }
            velocity.y -= gravity * dt;
            Vector2 pos = transform.position;
            Vector2 next = pos + velocity * dt;
            var hit = Physics2D.Linecast(pos, next, Layers.TerrainMask);
            if (hit.collider != null && !hit.collider.isTrigger && !hit.collider.usedByEffector)
            {
                Fx.Burst(hit.point, new Color(0.8f, 0.8f, 0.8f), 3, 2f, 0.1f, 0.2f);
                Destroy(gameObject);
                return;
            }
            transform.position = next;
            var pl = room != null ? room.Player : null;
            if (pl != null)
            {
                Vector2 pp = (Vector2)pl.transform.position + Vector2.up * 0.05f;
                Vector2 dd = next - pp;
                if (Mathf.Abs(dd.x) < 0.3f + radius && Mathf.Abs(dd.y) < 0.5f + radius)
                {
                    if (pl.Hurt(damage, "투사체")) { Destroy(gameObject); }
                }
            }
        }

        private void OnDestroy() { room?.UnregisterProjectile(this); }
    }
}
