using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public enum ArrowState { InQuiver, Flying, Delayed, Falling, Grounded, Stuck, Returning, Consumed }
    public enum HitSource { Shot, Thrust, Pull, Discard, Phantom, Hitscan }

    /// <summary>발사 순간에 정해지는 한 번의 비행 정보</summary>
    public class ShotInfo
    {
        public ArrowDef form;          // 이번 비행의 형태 (에코는 복사한 화살, 이카루스↔밀랍은 비행 중 변함)
        public float damage;           // 최종 피해 (화살 피해 + 공격력, 차지 배율 반영)
        public float speedMul = 1f;
        public int pierce;             // 남은 관통 횟수
        public int bounces;            // 볼링 남은 꺾임
        public int redirects;          // 망령 남은 방향 전환
        public bool isEcho;
        public float launchY;
    }

    /// <summary>
    /// 화살 카드 한 장의 실체. 노드(라운드) 동안 존재하며 화살통에 있을 때는 숨겨진다.
    /// 상태 전환은 이 클래스의 메서드로만 일어나고 허용되지 않은 전환은 위반으로 기록한다.
    /// 물리 엔진 강체 대신 직접 적분 + Linecast 로 충돌을 판정한다(터널링·중복 충돌 방지).
    /// </summary>
    public class ArrowBody : MonoBehaviour
    {
        public ArrowCard Card { get; private set; }
        public ArrowState State { get; private set; } = ArrowState.InQuiver;
        public float StateTime { get; private set; }
        public int FlightSerial { get; private set; }
        public ShotInfo Shot { get; private set; }
        public Vector2 LastDirection { get; private set; } = Vector2.right;
        public Enemy StuckEnemy { get; private set; }
        public string ReturnSource { get; private set; }
        public bool AnyEnemyHitThisFlight { get; private set; }
        public readonly HashSet<Enemy> hitThisFlight = new HashSet<Enemy>();
        public int TotalHitsResolved { get; private set; }

        private PlayerCombat owner;
        private SpriteRenderer sr;
        private Vector2 velocity;
        private float gravity;
        private float traveled, maxRange;
        private Transform stuckTo;
        private Vector2 stuckLocalPos;
        private float stuckLocalAngle;
        private Vector2 returnStartOffset;
        private float returnDuration, returnElapsed, returnExponent = 3f;
        private float delayTimer;
        private Vector2 delayedDir;
        private float delayedSpeed;
        private int formChanges;
        private float waxPeakY;
        private float oilAccum;
        private readonly HashSet<Enemy> grazed = new HashSet<Enemy>();

        public void Setup(PlayerCombat combat, ArrowCard card)
        {
            owner = combat;
            Card = card;
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Art.ArrowSprite != null ? Art.ArrowSprite : Art.Diamond;
            sr.sortingOrder = 20;
            ApplyLook(card.Def);
            gameObject.name = "Arrow_" + card.Label;
            SetState(ArrowState.InQuiver);
        }

        public string Label => Card.Label;
        public ArrowDef Form => Shot != null ? Shot.form : Card.Def;
        public bool IsOnField => State == ArrowState.Grounded || State == ArrowState.Stuck || State == ArrowState.Falling;

        private void ApplyLook(ArrowDef def)
        {
            sr.color = def.tint;
            float s = Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(0.5f, 1.5f, def.weight));
            if (Art.ArrowSprite == null) transform.localScale = new Vector3(0.9f * s, 0.25f * s, 1f);
            else transform.localScale = new Vector3(s, s, 1f);
        }

        private void SetState(ArrowState s)
        {
            State = s;
            StateTime = 0f;
            if (sr != null) sr.enabled = s != ArrowState.InQuiver && s != ArrowState.Consumed;
        }

        // ───────────── 화살통 ↔ 필드 ─────────────

        public void Launch(Vector2 origin, Vector2 dir, float speed, float grav, float range, ShotInfo shot, float delay = 0f)
        {
            if (State != ArrowState.InQuiver) { owner.ReportViolation($"{Label}: {State} 상태에서 발사 시도"); return; }
            transform.position = origin;
            Shot = shot;
            ApplyLook(shot.form);
            velocity = dir.normalized * speed;
            gravity = grav;
            maxRange = range;
            traveled = 0f;
            LastDirection = dir.normalized;
            BeginFlight();
            shot.launchY = origin.y;
            AlignTo(velocity);
            if (delay > 0f)
            {
                delayTimer = delay;
                delayedDir = dir.normalized;
                delayedSpeed = speed;
                velocity = Vector2.zero;
                SetState(ArrowState.Delayed);
            }
            else SetState(ArrowState.Flying);
        }

        private void BeginFlight()
        {
            FlightSerial++;
            hitThisFlight.Clear();
            grazed.Clear();
            AnyEnemyHitThisFlight = false;
            StuckEnemy = null;
            formChanges = 0;
            oilAccum = 0f;
        }

        /// <summary>찌르기·즉발(헤르메스 화살)처럼 날아가지 않고 바로 적중 지점에 놓일 때</summary>
        public void PlaceInstant(Vector2 point, Vector2 dir, ShotInfo shot)
        {
            if (State != ArrowState.InQuiver) { owner.ReportViolation($"{Label}: {State} 상태에서 즉시 배치 시도"); return; }
            transform.position = point;
            Shot = shot;
            ApplyLook(shot.form);
            LastDirection = dir.normalized;
            BeginFlight();
            shot.launchY = point.y;
            AlignTo(dir);
            SetState(ArrowState.Flying);
        }

        public void MarkEnemyHit(Enemy e)
        {
            hitThisFlight.Add(e);
            AnyEnemyHitThisFlight = true;
            TotalHitsResolved++;
        }

        /// <summary>회수 완료. PlayerCombat.TryRecover 만 호출한다.</summary>
        public void EnterQuiver()
        {
            Detach();
            velocity = Vector2.zero;
            Shot = null;
            ApplyLook(Card.Def);
            SetState(ArrowState.InQuiver);
        }

        /// <summary>이번 라운드 동안 소멸</summary>
        public void Consume()
        {
            Detach();
            velocity = Vector2.zero;
            Shot = null;
            SetState(ArrowState.Consumed);
        }

        /// <summary>라운드가 끝나 소멸이 풀릴 때 (화살통 복귀는 PlayerCombat 이 처리)</summary>
        public void RestoreFromConsumed()
        {
            if (State == ArrowState.Consumed) SetState(ArrowState.InQuiver);
        }

        /// <summary>버리기: 화살통에서 꺼내 앞쪽 바닥으로 던진다</summary>
        public void Toss(Vector2 from, Vector2 vel)
        {
            if (State != ArrowState.InQuiver) { owner.ReportViolation($"{Label}: {State} 상태에서 버리기 시도"); return; }
            transform.position = from;
            Shot = null;
            ApplyLook(Card.Def);
            velocity = vel;
            gravity = owner.Tuning.fallGravity;
            SetState(ArrowState.Falling);
        }

        public void StickTo(Enemy enemy, Transform part, Vector2 point)
        {
            if (State != ArrowState.Flying) { owner.ReportViolation($"{Label}: {State} 상태에서 박힘 시도"); return; }
            StuckEnemy = enemy;
            stuckTo = part != null ? part : enemy.transform;
            Vector2 embed = point + LastDirection * 0.15f;
            transform.position = embed;
            stuckLocalPos = stuckTo.InverseTransformPoint(embed);
            stuckLocalAngle = transform.eulerAngles.z - stuckTo.eulerAngles.z;
            enemy.RegisterStuck(this);
            SetState(ArrowState.Stuck);
        }

        public void Drop(Vector2 initialVelocity)
        {
            if (State != ArrowState.Flying && State != ArrowState.Stuck && State != ArrowState.Returning && State != ArrowState.Falling && State != ArrowState.Delayed)
            {
                owner.ReportViolation($"{Label}: {State} 상태에서 낙하 시도");
                return;
            }
            Detach();
            velocity = initialVelocity;
            gravity = owner.Tuning.fallGravity;
            SetState(ArrowState.Falling);
        }

        private void LandOnTerrain(Vector2 point, Vector2 normal, bool lodged)
        {
            transform.position = point;
            if (!lodged)
            {
                float tangent = Mathf.Atan2(-normal.x, normal.y) * Mathf.Rad2Deg;
                float facing = velocity.x >= 0f ? 0f : 180f;
                transform.rotation = Quaternion.Euler(0f, 0f, tangent + facing + (facing == 0f ? -12f : 12f));
            }
            velocity = Vector2.zero;
            bool wasFlight = State == ArrowState.Flying;
            SetState(ArrowState.Grounded);
            if (wasFlight) owner.OnFlightEndedOnTerrain(this, point);
        }

        public bool BeginReturn(Vector2 playerPos, float duration, float exponent, string source)
        {
            if (State != ArrowState.Grounded && State != ArrowState.Stuck && State != ArrowState.Falling) return false;
            Detach();
            returnStartOffset = (Vector2)transform.position - playerPos;
            returnDuration = Mathf.Max(0.01f, duration);
            returnElapsed = 0f;
            returnExponent = Mathf.Max(1f, exponent);
            ReturnSource = source;
            SetState(ArrowState.Returning);
            return true;
        }

        public float ReturnProgress => State == ArrowState.Returning ? Mathf.Clamp01(returnElapsed / returnDuration) : 0f;

        private void Detach()
        {
            if (StuckEnemy != null)
            {
                StuckEnemy.UnregisterStuck(this);
                StuckEnemy = null;
            }
            stuckTo = null;
        }

        public void OnEnemyLost()
        {
            if (State == ArrowState.Stuck) Drop(new Vector2(Random.Range(-1.5f, 1.5f), 3f));
        }

        // ───────────── 시뮬레이션 ─────────────

        public void SimulateFixed(float dt)
        {
            StateTime += dt;
            switch (State)
            {
                case ArrowState.Flying: SimulateFlight(dt); break;
                case ArrowState.Falling: SimulateFalling(dt); break;
                case ArrowState.Delayed:
                    delayTimer -= dt;
                    AlignTo(delayedDir);
                    if (delayTimer <= 0f)
                    {
                        velocity = delayedDir * delayedSpeed;
                        SetState(ArrowState.Flying);
                    }
                    break;
            }
        }

        public void SimulateFrame(float dt, Vector2 playerPos)
        {
            switch (State)
            {
                case ArrowState.Stuck:
                    if (StuckEnemy == null || !StuckEnemy.IsAlive || stuckTo == null) { OnEnemyLost(); break; }
                    transform.position = stuckTo.TransformPoint(stuckLocalPos);
                    transform.rotation = Quaternion.Euler(0f, 0f, stuckTo.eulerAngles.z + stuckLocalAngle);
                    break;
                case ArrowState.Returning:
                    returnElapsed += dt;
                    float t = Mathf.Clamp01(returnElapsed / returnDuration);
                    float remain = Mathf.Pow(1f - t, returnExponent);
                    Vector2 target = playerPos + Vector2.up * 0.2f;
                    Vector2 newPos = target + returnStartOffset * remain;
                    Vector2 move = newPos - (Vector2)transform.position;
                    transform.position = newPos;
                    if (move.sqrMagnitude > 0.000001f) AlignTo(move);
                    if (t >= 1f) owner.NotifyReturnArrived(this);
                    break;
            }
        }

        private void SimulateFlight(float dt)
        {
            Vector2 pos = transform.position;
            var form = Shot != null ? Shot.form : Card.Def;

            // 유도(??? 화살)
            if (form.effect == "homing")
            {
                var target = owner.Room != null ? owner.Room.NearestEnemy(pos, form.p2 > 0 ? form.p2 : 10f, hitThisFlight) : null;
                if (target != null)
                {
                    Vector2 want = ((Vector2)target.Center - pos).normalized;
                    float maxTurn = (form.p1 > 0 ? form.p1 : 260f) * dt;
                    float ang = Vector2.SignedAngle(velocity, want);
                    velocity = (Vector2)(Quaternion.Euler(0, 0, Mathf.Clamp(ang, -maxTurn, maxTurn)) * velocity);
                }
            }

            // 이카루스 ↔ 밀랍 (비행 중 변형, 무한 반복 방지로 최대 4회)
            if (Shot != null && formChanges < 4)
            {
                if (form.effect == "icarus" && pos.y > Shot.launchY + (form.p3 > 0 ? form.p3 : 2.5f))
                {
                    var wax = DB.Arrow(form.variantTo);
                    Shot.form = wax;
                    formChanges++;
                    waxPeakY = pos.y;
                    velocity = new Vector2(velocity.x * 0.35f, Mathf.Min(velocity.y, 0f) * 0.2f);
                    gravity = wax.p1 > 0 ? wax.p1 : 2f;
                    ApplyLook(wax);
                    Fx.Burst(pos, wax.tint, 6, 3f, 0.12f);
                    owner.Log($"{Label}: 이카루스 → 녹아내린 밀랍");
                }
                else if (form.effect == "wax" && pos.y < Shot.launchY - 0.5f)
                {
                    var ic = DB.Arrow(form.variantOf);
                    Shot.form = ic;
                    formChanges++;
                    float sx = Mathf.Abs(velocity.x) > 0.01f ? Mathf.Sign(velocity.x) : Mathf.Sign(LastDirection.x);
                    velocity = new Vector2(sx, 0f) * owner.Tuning.arrowSpeed * (ic.p1 > 0 ? ic.p1 : 1.7f);
                    gravity = 0f;
                    traveled = 0f;
                    ApplyLook(ic);
                    owner.Log($"{Label}: 밀랍 → 이카루스");
                }
                if (Shot.form.effect == "wax") waxPeakY = Mathf.Max(waxPeakY, pos.y);
            }

            velocity.y -= gravity * dt;
            Vector2 next = pos + velocity * dt;

            // 기름 화살: 적에게 맞기 전까지 기름을 남김
            if (form.effect == "oil" && !AnyEnemyHitThisFlight)
            {
                oilAccum += (next - pos).magnitude;
                if (oilAccum >= 1.2f) { oilAccum = 0f; owner.Room?.DropOil(next, form.p1 > 0 ? form.p1 : 10f); }
            }

            // 마찰 화살: 스치는 적에게 피해
            if (form.effect == "friction" && owner.Room != null)
            {
                foreach (var e in owner.Room.Enemies)
                {
                    if (e == null || !e.IsAlive || grazed.Contains(e) || hitThisFlight.Contains(e)) continue;
                    if (e.DistanceFrom(next) <= (form.p2 > 0 ? form.p2 : 0.9f))
                    {
                        grazed.Add(e);
                        e.TakeDamage(form.p1 > 0 ? form.p1 : 5f, "마찰", new Color(0.9f, 0.7f, 0.5f), false);
                    }
                }
            }

            if (owner.CastSegment(pos, next, true, hitThisFlight, out RaycastHit2D hit, out Enemy enemy))
            {
                if (enemy != null)
                {
                    transform.position = hit.point;
                    LastDirection = velocity.normalized;
                    var outcome = owner.OnArrowHitEnemy(this, enemy, hit.collider.transform, hit.point, HitSource.Shot);
                    if (outcome.continueFlight && State == ArrowState.Flying)
                    {
                        if (outcome.newDirection.sqrMagnitude > 0.01f)
                            velocity = outcome.newDirection.normalized * velocity.magnitude;
                        transform.position = hit.point + velocity.normalized * 0.05f;
                        AlignTo(velocity);
                    }
                }
                else
                {
                    LandOnTerrain(hit.point - velocity.normalized * 0.05f, hit.normal, true);
                }
                return;
            }

            traveled += (next - pos).magnitude;
            transform.position = next;
            AlignTo(velocity);

            if (gravity <= 0f && traveled >= maxRange) { Drop(velocity * 0.15f); owner.OnFlightEnded(this, next); }
            if (next.y < -40f || Mathf.Abs(next.x) > 80f) { transform.position = owner.SafeGroundPoint(); SetState(ArrowState.Grounded); }
        }

        /// <summary>밀랍 화살의 높이 비례 피해</summary>
        public float WaxDamage(float baseDamage, ArrowDef wax)
        {
            float fall = Mathf.Max(0f, waxPeakY - transform.position.y);
            float cap = wax.p2 > 0 ? wax.p2 : 36f;
            return Mathf.Min(cap, baseDamage + fall * 2.5f);
        }

        private void SimulateFalling(float dt)
        {
            Vector2 pos = transform.position;
            velocity.y -= gravity * dt;
            velocity.x = Mathf.MoveTowards(velocity.x, 0f, 2f * dt);
            Vector2 next = pos + velocity * dt;
            if (owner.CastSegment(pos, next, false, null, out RaycastHit2D hit, out _))
            {
                LandOnTerrain(hit.point + hit.normal * 0.05f, hit.normal, false);
                return;
            }
            transform.position = next;
            transform.Rotate(0f, 0f, -velocity.x * 40f * dt);
            if (next.y < -40f)
            {
                transform.position = owner.SafeGroundPoint();
                SetState(ArrowState.Grounded);
            }
        }

        private void AlignTo(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    /// <summary>
    /// 임시 투사체(카드가 아님): 삼살 옆 화살, 거울 화살, 분열체, 파열 파편, 바이러스 8방향, 하데스 재발사.
    /// 화살통에 들어오지 않고, 다른 임시 투사체를 만들지 않는다(무한 복제 방지).
    /// </summary>
    public class Phantom : MonoBehaviour
    {
        public ArrowDef form;
        public float damage;
        public Vector2 velocity;
        public float gravity;
        public float life;
        public float range;
        public int pierce;
        public string label;
        private float traveled;
        private PlayerCombat owner;
        private readonly HashSet<Enemy> hit = new HashSet<Enemy>();

        public static Phantom Spawn(PlayerCombat owner, ArrowDef form, Vector2 pos, Vector2 dir, float speed, float damage, float range, string label, float scale = 0.75f)
        {
            if (owner.Room == null || owner.PhantomCount >= owner.Tuning.phantomCap) return null;
            var go = new GameObject("Phantom_" + label);
            go.transform.SetParent(owner.Room.transform, false);
            go.transform.position = pos;
            var p = go.AddComponent<Phantom>();
            p.owner = owner;
            p.form = form;
            p.damage = damage;
            p.velocity = dir.normalized * speed;
            p.range = range;
            p.life = owner.Tuning.phantomLifetime;
            p.label = label;
            p.pierce = form.effect == "pierce" || form.effect == "ares" ? Mathf.RoundToInt(form.p1) : 0;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Art.ArrowSprite != null ? Art.ArrowSprite : Art.Diamond;
            sr.color = form.tint.WithAlpha(0.75f);
            sr.sortingOrder = 19;
            go.transform.localScale = Art.ArrowSprite != null ? Vector3.one * scale : new Vector3(0.7f * scale, 0.2f * scale, 1f);
            owner.PhantomCount++;
            owner.PhantomsSpawnedTotal++;
            return p;
        }

        private void OnDestroy()
        {
            if (owner != null) owner.PhantomCount--;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            life -= dt;
            if (life <= 0f) { Destroy(gameObject); return; }
            Vector2 pos = transform.position;
            velocity.y -= gravity * dt;
            Vector2 next = pos + velocity * dt;
            if (owner.CastSegment(pos, next, true, hit, out RaycastHit2D h, out Enemy enemy))
            {
                if (enemy != null)
                {
                    hit.Add(enemy);
                    HitResolver.ResolvePhantom(owner, this, enemy, h.point, velocity.normalized);
                    if (pierce > 0) { pierce--; transform.position = h.point + velocity.normalized * 0.05f; return; }
                }
                Fx.Burst(h.point, form.tint, 3, 2f, 0.08f, 0.25f);
                Destroy(gameObject);
                return;
            }
            traveled += (next - pos).magnitude;
            transform.position = next;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);
            if (range > 0f && traveled >= range) Destroy(gameObject);
        }
    }

    /// <summary>순서가 있는 화살통. 맨 앞 화살부터 사용한다. 들어오는 길은 AddRecoveredBatch 하나뿐.</summary>
    public class ArrowQuiver
    {
        private readonly List<ArrowBody> order = new List<ArrowBody>();
        private readonly System.Random rng;

        public ArrowQuiver(int seed) { rng = new System.Random(seed); }

        public int Count => order.Count;
        public IReadOnlyList<ArrowBody> Order => order;
        public ArrowBody Peek(int i = 0) => i < order.Count ? order[i] : null;
        public bool Contains(ArrowBody a) => order.Contains(a);

        public void InitialAdd(ArrowBody a) { if (!order.Contains(a)) order.Add(a); }

        public ArrowBody TakeNext()
        {
            if (order.Count == 0) return null;
            var a = order[0];
            order.RemoveAt(0);
            return a;
        }

        public bool Remove(ArrowBody a) => order.Remove(a);

        /// <summary>같은 프레임에 회수된 화살은 '동시 회수'로 보고 무작위로 섞어 뒤에 붙인다. 1발이면 순서대로(순차 회수).</summary>
        public List<ArrowBody> AddRecoveredBatch(List<ArrowBody> batch)
        {
            var added = new List<ArrowBody>(batch.Count);
            foreach (var b in batch) if (b != null && !order.Contains(b) && !added.Contains(b)) added.Add(b);
            if (added.Count > 1)
            {
                for (int i = added.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var t = added[i]; added[i] = added[j]; added[j] = t;
                }
            }
            order.AddRange(added);
            return added;
        }

        public bool HasDuplicates()
        {
            var set = new HashSet<ArrowBody>();
            foreach (var a in order) if (!set.Add(a)) return true;
            return false;
        }
    }
}
