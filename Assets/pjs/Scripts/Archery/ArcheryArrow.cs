using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 화살 한 발. 전투 동안 계속 존재하며, 화살통 안에 있을 때는 화면에서 숨겨진다.
    /// 상태 전환은 모두 이 클래스의 메서드를 통해서만 일어나고, 허용되지 않은 전환은 거부된다.
    /// 물리 엔진 강체를 쓰지 않고 직접 적분 + Linecast 로 충돌을 판정한다 (터널링/중복 충돌 방지).
    /// </summary>
    public class ArcheryArrow : MonoBehaviour
    {
        public int Id { get; private set; }
        public ArrowDefinition Def { get; private set; }
        public ArrowState State { get; private set; } = ArrowState.InQuiver;
        public float StateTime { get; private set; }

        /// <summary>발사될 때마다 1씩 증가. 한 번의 비행에서 적중은 최대 1회.</summary>
        public int FlightSerial { get; private set; }
        /// <summary>현재 비행에서 이미 적중 판정을 했는지</summary>
        public bool HitResolvedThisFlight { get; private set; }
        /// <summary>발사 시점에 확정된 피해량 (차지샷 배율 포함)</summary>
        public float ShotDamage { get; private set; }
        /// <summary>마지막 비행/찌르기 방향 (넉백 방향 등)</summary>
        public Vector2 LastDirection { get; private set; } = Vector2.right;

        /// <summary>검증용: 마지막 발사 위치/시각/초기 속도/중력</summary>
        public Vector2 LaunchOrigin { get; private set; }
        public float LaunchTime { get; private set; }
        public Vector2 LaunchVelocity { get; private set; }
        public float LaunchGravity { get; private set; }

        public ArcheryEnemy StuckEnemy { get; private set; }
        public string ReturnSource { get; private set; }

        /// <summary>디버그/검증용 상태 전환 기록</summary>
        public readonly List<ArrowState> History = new List<ArrowState>();
        /// <summary>디버그/검증용 적중 판정 기록 (출처별)</summary>
        public readonly List<HitSource> HitHistory = new List<HitSource>();

        private ArcheryCombat owner;
        private SpriteRenderer sr;
        private Vector2 velocity;
        private float gravity;
        private float traveled;
        private float maxRange;

        private Vector2 stuckLocalPos;
        private float stuckLocalAngle;

        private Vector2 returnStartOffset;
        private float returnDuration;
        private float returnElapsed;
        private float returnExponent = 3f;

        private static readonly List<RaycastHit2D> castResults = new List<RaycastHit2D>(16);

        public void Setup(ArcheryCombat combat, int id, ArrowDefinition def, Sprite sprite)
        {
            owner = combat;
            Id = id;
            Def = def;
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = def.tint;
            sr.sortingOrder = 20;
            float scale = Mathf.Lerp(0.85f, 1.25f, Mathf.InverseLerp(0.5f, 1.5f, def.weight));
            transform.localScale = new Vector3(scale, scale, 1f);
            gameObject.name = $"Arrow#{id}_{def.id}";
            SetState(ArrowState.InQuiver);
        }

        public string Label => $"{Def.displayName}#{Id}";

        private void SetState(ArrowState s)
        {
            State = s;
            StateTime = 0f;
            History.Add(s);
            if (sr != null) sr.enabled = s != ArrowState.InQuiver;
        }

        // ───────────── 화살통 ↔ 필드 전환 ─────────────

        /// <summary>화살통에서 꺼내 발사. ArrowQuiver.TakeNext 로 꺼낸 직후에만 호출된다.</summary>
        public void Launch(Vector2 origin, Vector2 direction, float speed, float gravityAccel, float range, float damage)
        {
            if (State != ArrowState.InQuiver) { owner.ReportViolation($"{Label}: {State} 상태에서 발사 시도"); return; }
            transform.position = origin;
            velocity = direction.normalized * speed;
            gravity = gravityAccel;
            maxRange = range;
            traveled = 0f;
            LaunchOrigin = origin;
            LaunchTime = Time.time;
            LaunchVelocity = velocity;
            LaunchGravity = gravityAccel;
            ShotDamage = damage;
            LastDirection = direction.normalized;
            FlightSerial++;
            HitResolvedThisFlight = false;
            StuckEnemy = null;
            SetState(ArrowState.Flying);
            AlignTo(velocity);
        }

        /// <summary>찌르기 적중 시: 화살통에서 꺼내 바로 적에게 박거나(뽑기 계열) 떨군다(줍기).</summary>
        public void PlaceFromThrust(Vector2 point, Vector2 direction, float damage)
        {
            if (State != ArrowState.InQuiver) { owner.ReportViolation($"{Label}: {State} 상태에서 찌르기 시도"); return; }
            transform.position = point;
            LastDirection = direction.normalized;
            ShotDamage = damage;
            FlightSerial++;
            HitResolvedThisFlight = false;
            AlignTo(direction);
            SetState(ArrowState.Flying); // 찰나의 '사용 중' 상태. 곧바로 MarkHitResolved 후 Stick/Drop 된다.
        }

        /// <summary>적중 판정 직후 호출. 같은 비행에서 두 번째 적중을 막는다.</summary>
        public bool MarkHitResolved()
        {
            if (HitResolvedThisFlight) return false;
            HitResolvedThisFlight = true;
            return true;
        }

        public void RecordHit(HitSource source) => HitHistory.Add(source);

        /// <summary>회수 완료. ArcheryCombat.TryRecover 만 호출한다.</summary>
        public void EnterQuiver()
        {
            DetachFromEnemy();
            velocity = Vector2.zero;
            SetState(ArrowState.InQuiver);
        }

        /// <summary>테스트 보조: 화살통의 화살을 지정 위치 바닥에 내려놓는다.</summary>
        public void DebugPlaceOnGround(Vector2 point)
        {
            if (State != ArrowState.InQuiver) return;
            transform.position = point;
            transform.rotation = Quaternion.Euler(0f, 0f, 180f + 20f);
            SetState(ArrowState.Grounded);
        }

        // ───────────── 필드 상태 전환 ─────────────

        public void StickTo(ArcheryEnemy enemy, Vector2 point)
        {
            if (State != ArrowState.Flying) { owner.ReportViolation($"{Label}: {State} 상태에서 박힘 시도"); return; }
            StuckEnemy = enemy;
            Vector2 embed = point + LastDirection * 0.15f;
            transform.position = embed;
            stuckLocalPos = enemy.transform.InverseTransformPoint(embed);
            stuckLocalAngle = transform.eulerAngles.z - enemy.transform.eulerAngles.z;
            enemy.RegisterStuck(this);
            SetState(ArrowState.Stuck);
        }

        /// <summary>추진력을 잃고 떨어지게 한다 (적에게서 튕김, 사거리 초과, 적 사망, 부르기 중단).</summary>
        public void Drop(Vector2 initialVelocity)
        {
            if (State != ArrowState.Flying && State != ArrowState.Stuck && State != ArrowState.Returning && State != ArrowState.Falling)
            {
                owner.ReportViolation($"{Label}: {State} 상태에서 낙하 시도");
                return;
            }
            DetachFromEnemy();
            velocity = initialVelocity;
            gravity = owner.Config.fallGravity;
            SetState(ArrowState.Falling);
        }

        private void LandOnTerrain(Vector2 point, Vector2 normal, bool lodged)
        {
            transform.position = point;
            if (!lodged)
            {
                // 바닥에 눕힌다: 바닥 접선 방향 + 약간 기울임
                float tangent = Mathf.Atan2(-normal.x, normal.y) * Mathf.Rad2Deg;
                float facing = velocity.x >= 0f ? 0f : 180f;
                transform.rotation = Quaternion.Euler(0f, 0f, tangent + facing + (facing == 0f ? -12f : 12f));
            }
            velocity = Vector2.zero;
            SetState(ArrowState.Grounded);
        }

        /// <summary>회수 시작: 지정 시간 동안 플레이어에게 돌아온다.</summary>
        public bool BeginReturn(Vector2 playerPos, float duration, float exponent, string source)
        {
            if (State != ArrowState.Grounded && State != ArrowState.Stuck && State != ArrowState.Falling) return false;
            DetachFromEnemy();
            returnStartOffset = (Vector2)transform.position - playerPos;
            returnDuration = Mathf.Max(0.01f, duration);
            returnElapsed = 0f;
            returnExponent = Mathf.Max(1f, exponent);
            ReturnSource = source;
            SetState(ArrowState.Returning);
            return true;
        }

        public float ReturnProgress => State == ArrowState.Returning ? Mathf.Clamp01(returnElapsed / returnDuration) : 0f;

        private void DetachFromEnemy()
        {
            if (StuckEnemy != null)
            {
                StuckEnemy.UnregisterStuck(this);
                StuckEnemy = null;
            }
        }

        /// <summary>적이 죽거나 사라졌을 때 박힌 화살을 떨어뜨린다.</summary>
        public void OnEnemyLost()
        {
            if (State == ArrowState.Stuck) Drop(new Vector2(Random.Range(-1.5f, 1.5f), 3f));
        }

        // ───────────── 시뮬레이션 (ArcheryCombat 이 호출) ─────────────

        public void SimulateFixed(float dt)
        {
            StateTime += dt;
            switch (State)
            {
                case ArrowState.Flying: SimulateFlight(dt); break;
                case ArrowState.Falling: SimulateFalling(dt); break;
            }
        }

        public void SimulateFrame(float dt, Vector2 playerPos)
        {
            switch (State)
            {
                case ArrowState.Stuck:
                    if (StuckEnemy == null || !StuckEnemy.IsAlive)
                    {
                        OnEnemyLost();
                        break;
                    }
                    transform.position = StuckEnemy.transform.TransformPoint(stuckLocalPos);
                    transform.rotation = Quaternion.Euler(0f, 0f, StuckEnemy.transform.eulerAngles.z + stuckLocalAngle);
                    break;

                case ArrowState.Returning:
                    returnElapsed += dt;
                    float t = Mathf.Clamp01(returnElapsed / returnDuration);
                    // 남은 거리 비율 = (1-t)^n → 속도는 남은 거리에 비례해 멀수록 빠르고 가까울수록 느리다
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
            velocity.y -= gravity * dt;
            Vector2 next = pos + velocity * dt;

            if (CastSegment(pos, next, true, out RaycastHit2D hit, out ArcheryEnemy enemy))
            {
                if (enemy != null)
                {
                    transform.position = hit.point;
                    owner.OnArrowStruckEnemy(this, enemy, hit.point, HitSource.Shot);
                }
                else
                {
                    // 지형(바닥·발판·벽)에 꽂힘 → 바닥에 떨어진 화살로 취급
                    LandOnTerrain(hit.point - velocity.normalized * 0.05f, hit.normal, true);
                }
                return;
            }

            traveled += (next - pos).magnitude;
            transform.position = next;
            AlignTo(velocity);

            if (gravity <= 0f && traveled >= maxRange)
            {
                Drop(velocity * 0.15f);
            }
        }

        private void SimulateFalling(float dt)
        {
            Vector2 pos = transform.position;
            velocity.y -= gravity * dt;
            velocity.x = Mathf.MoveTowards(velocity.x, 0f, 2f * dt);
            Vector2 next = pos + velocity * dt;

            if (CastSegment(pos, next, false, out RaycastHit2D hit, out _))
            {
                LandOnTerrain(hit.point + hit.normal * 0.05f, hit.normal, false);
                return;
            }
            transform.position = next;
            transform.Rotate(0f, 0f, -velocity.x * 40f * dt);

            if (next.y < -30f)
            {
                // 안전장치: 월드 밖으로 떨어지면 플레이어 근처 바닥에 놓는다
                transform.position = owner.SafeGroundPoint();
                SetState(ArrowState.Grounded);
            }
        }

        /// <summary>선분과 겹치는 첫 지형/적을 찾는다. 플레이어와 다른 트리거는 무시.</summary>
        private bool CastSegment(Vector2 from, Vector2 to, bool includeEnemies, out RaycastHit2D best, out ArcheryEnemy bestEnemy)
        {
            best = default;
            bestEnemy = null;
            var filter = new ContactFilter2D();
            filter.useTriggers = true;
            castResults.Clear();
            int n = Physics2D.Linecast(from, to, filter, castResults);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = castResults[i];
                if (h.collider == null) continue;
                if (owner.IsPlayerCollider(h.collider)) continue;

                ArcheryEnemy e = h.collider.GetComponentInParent<ArcheryEnemy>();
                if (e != null)
                {
                    if (!includeEnemies || !e.IsAlive) continue;
                }
                else if (h.collider.isTrigger)
                {
                    continue;
                }

                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    bestEnemy = e;
                    found = true;
                }
            }
            return found;
        }

        private void AlignTo(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
