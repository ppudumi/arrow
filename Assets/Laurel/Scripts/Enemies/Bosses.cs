using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 보스 공통. [임시 결정] 엑셀 보스 시트에는 파이톤 체력(20000)만 있고 패턴이 없어, 패턴·체력은 개발상 임시 설계.
    /// 체력 50% 이하에서 2페이즈(패턴 강화). 패턴은 코루틴으로 순환한다.
    /// </summary>
    public abstract class Boss : Enemy
    {
        public BossDef BDef { get; private set; }
        public int Phase => Hp <= MaxHp * 0.5f ? 2 : 1;
        public string PatternName { get; protected set; } = "";
        protected float t;
        protected readonly List<Enemy> minions = new List<Enemy>();

        public void InitBoss(Room room, BossDef def, Vector2 size)
        {
            SetupBoss(room, def, size);
            gameObject.layer = Layers.Enemy;
            Col = gameObject.AddComponent<BoxCollider2D>();
            Col.isTrigger = true;
            Col.size = size;
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            BuildBossVisual();
            usesGravity = false;
            StartCoroutine(Run());
        }

        protected abstract void BuildBossVisual();
        protected abstract IEnumerator Brain();

        private IEnumerator Run()
        {
            PatternName = "등장";
            Sfx.Play("roar", 0.8f);
            CameraRig.Shake(0.25f);
            yield return Wait(1.4f);
            while (IsAlive) yield return Brain();
        }

        protected IEnumerator Wait(float s)
        {
            float e = 0f;
            while (e < s && IsAlive) { e += Time.deltaTime; yield return null; }
        }

        protected IEnumerator Telegraph(float s, string name)
        {
            PatternName = name;
            float e = 0f;
            while (e < s && IsAlive) { e += Time.deltaTime; flash = Mathf.PingPong(e * 10f, 1f) > 0.5f ? 0.04f : 0f; yield return null; }
        }

        protected Enemy SpawnMinion(string id, Vector2 pos)
        {
            minions.RemoveAll(m => m == null || !m.IsAlive);
            if (minions.Count >= 4 || Room == null || !DB.Enemies.ContainsKey(id)) return null;
            var m = Room.SpawnEnemy(DB.Enemies[id], pos, Room.HpMul, Room.DmgMul, false, true);
            if (m != null) minions.Add(m);
            return m;
        }

        protected override void Die()
        {
            foreach (var m in minions) if (m != null && m.IsAlive) m.ForceKill();
            CameraRig.Shake(0.5f);
            Sfx.Play("boom", 1f, 0.6f);
            base.Die();
        }

        protected override void OnDeathVisual()
        {
            StopAllCoroutines();
            Fx.Burst(Center, baseColor, 40, 10f, 0.35f, 1.0f);
            Destroy(gameObject, 0.1f);
        }

        protected float Floor => Room != null ? Room.InnerBounds.yMin : -11f;
        protected float Ceil => Room != null ? Room.InnerBounds.yMax : 11f;
        protected float Left => Room != null ? Room.InnerBounds.xMin : -11f;
        protected float Right => Room != null ? Room.InnerBounds.xMax : 11f;
    }

    // ───────────────────────── 튜토리얼: 거대한 뱀 파에톤 ─────────────────────────

    public class PhaetonBoss : Boss
    {
        private readonly List<Transform> segs = new List<Transform>();
        private readonly List<Vector2> trail = new List<Vector2>();
        private Vector2 moveTarget;
        private float swim;

        protected override void BuildBossVisual()
        {
            bodySr = Art.Quad(transform, "Head", Art.Circle, baseColor, halfSize * 2f, 12);
            var eyeL = Art.Quad(transform, "Eye", Art.Circle, new Color(1f, 0.85f, 0.2f), Vector2.one * 0.35f, 13, new Vector3(0.35f, 0.25f, 0));
            Art.Quad(eyeL.transform, "Pupil", Art.Circle, Color.black, new Vector2(0.35f, 0.8f), 14);
            for (int i = 0; i < 12; i++)
            {
                float s = Mathf.Lerp(1.5f, 0.6f, i / 11f);
                var seg = new GameObject("Seg" + i);
                seg.transform.SetParent(transform, false);
                seg.layer = Layers.Enemy;
                var c = seg.AddComponent<CircleCollider2D>();
                c.isTrigger = true;
                c.radius = 0.5f;
                seg.transform.localScale = Vector3.one * s;
                Art.Quad(seg.transform, "s", Art.Circle, Color.Lerp(baseColor, new Color(0.2f, 0.35f, 0.15f), i / 14f), Vector2.one, 11 - i / 4);
                segs.Add(seg.transform);
            }
            moveTarget = transform.position;
        }

        protected override void Update()
        {
            base.Update();
            if (!IsAlive) return;
            trail.Insert(0, transform.position);
            if (trail.Count > 200) trail.RemoveAt(trail.Count - 1);
            for (int i = 0; i < segs.Count; i++)
            {
                int k = Mathf.Min(trail.Count - 1, (i + 1) * 5);
                segs[i].position = trail[k];
            }
        }

        private IEnumerator SwimTo(Vector2 p, float spd, float maxTime)
        {
            float e = 0f;
            while (IsAlive && e < maxTime && Vector2.Distance(Center, p) > 0.4f)
            {
                e += Time.deltaTime;
                swim += Time.deltaTime * 3f;
                Vector2 d = (p - (Vector2)Center).normalized;
                Vector2 wobble = Vector2.Perpendicular(d) * Mathf.Sin(swim) * 2f;
                velocity = d * spd * Status.SpeedMul + wobble;
                facing = Mathf.Sign(d.x == 0 ? 1 : d.x);
                yield return null;
            }
            velocity = Vector2.zero;
        }

        protected override IEnumerator Brain()
        {
            PatternName = "유영";
            Vector2 a = new Vector2(Random.Range(Left + 3, Right - 3), Random.Range(Floor + 3, Ceil - 4));
            yield return SwimTo(a, 6f + Phase, 2.5f);

            yield return Telegraph(0.6f, "독 뱉기");
            for (int i = 0; i < (Phase == 2 ? 5 : 3); i++)
            {
                Vector2 d = PlayerPos - (Vector2)Center;
                Vector2 v = new Vector2(d.x, d.y + Mathf.Abs(d.x) * 0.5f).normalized;
                Shoot(Quaternion.Euler(0, 0, (i - 1) * 9f) * v, 11f, 10f, Damage * 0.7f, new Color(0.6f, 1f, 0.3f), 0.32f);
            }
            yield return Wait(1.0f);

            yield return Telegraph(0.7f, "돌진");
            Vector2 target = PlayerPos;
            yield return SwimTo(target, 15f, 1.2f);
            yield return Wait(0.6f);
        }
    }

    // ───────────────────────── 1스테이지: 칼리돈의 멧돼지 ─────────────────────────

    public class BoarBoss : Boss
    {
        protected override void BuildBossVisual()
        {
            usesGravity = true;
            bodySr = Art.Quad(transform, "Body", Art.Square, baseColor, halfSize * 2f, 10);
            Art.Quad(bodySr.transform, "Tusk", Art.Triangle, new Color(0.95f, 0.9f, 0.8f), new Vector2(0.25f, 0.35f), 11, new Vector3(0.48f, -0.15f, 0));
            Art.Quad(bodySr.transform, "Eye", Art.Circle, new Color(1f, 0.3f, 0.2f), new Vector2(0.09f, 0.12f), 11, new Vector3(0.35f, 0.2f, 0));
            Art.Quad(bodySr.transform, "Mane", Art.Square, new Color(0.25f, 0.15f, 0.1f), new Vector2(0.7f, 0.15f), 11, new Vector3(-0.05f, 0.45f, 0));
        }

        protected override void Update()
        {
            usesGravity = true;
            base.Update();
        }

        protected override IEnumerator Brain()
        {
            // 1. 돌진 (2페이즈: 두 번)
            for (int n = 0; n < (Phase == 2 ? 2 : 1) && IsAlive; n++)
            {
                facing = Mathf.Sign(PlayerPos.x - Center.x);
                yield return Telegraph(Phase == 2 ? 0.55f : 0.8f, "돌진");
                PatternName = "돌진";
                float e = 0f;
                while (IsAlive && e < 2.5f)
                {
                    e += Time.deltaTime;
                    velocity.x = facing * (Phase == 2 ? 17f : 14f);
                    if (hitWall) break;
                    yield return null;
                }
                velocity.x = 0f;
                if (hitWall)
                {
                    CameraRig.Shake(0.35f);
                    Sfx.Play("boom", 0.7f);
                    for (int i = 0; i < 3 + Phase; i++)
                        EnemyProjectile.Spawn(Room, new Vector2(Random.Range(Left + 1, Right - 1), Ceil - 0.5f), Vector2.zero, 14f, Damage * 0.7f, new Color(0.6f, 0.5f, 0.4f), 0.4f, 4f);
                    PatternName = "기절";
                    yield return Wait(1.1f);
                }
                facing = -facing;
            }
            yield return Wait(0.5f);

            // 2. 도약 내려찍기 → 바닥 충격파
            yield return Telegraph(0.6f, "도약");
            float tx = Mathf.Clamp(PlayerPos.x, Left + 2, Right - 2);
            velocity = new Vector2((tx - Center.x) / 1.1f, 19f);
            yield return null;
            float air = 0f;
            while (IsAlive && (!grounded || air < 0.2f) && air < 2.5f) { air += Time.deltaTime; yield return null; }
            velocity.x = 0f;
            CameraRig.Shake(0.4f);
            Sfx.Play("boom", 0.8f);
            float sw = Phase == 2 ? 11f : 8.5f;
            Vector2 foot = new Vector2(Center.x, Floor + 0.45f);
            EnemyProjectile.Spawn(Room, foot + Vector2.right * 1.2f, Vector2.right * sw, 0f, Damage * 0.8f, new Color(0.8f, 0.6f, 0.3f), 0.45f, 3f);
            EnemyProjectile.Spawn(Room, foot + Vector2.left * 1.2f, Vector2.left * sw, 0f, Damage * 0.8f, new Color(0.8f, 0.6f, 0.3f), 0.45f, 3f);
            yield return Wait(0.9f);

            // 3. 엄니 흩뿌리기
            yield return Telegraph(0.5f, "돌 튕기기");
            int rocks = Phase == 2 ? 7 : 5;
            for (int i = 0; i < rocks; i++)
            {
                float ang = Mathf.Lerp(40f, 140f, i / (rocks - 1f));
                Vector2 v = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                Shoot(v, Random.Range(9f, 13f), 12f, Damage * 0.6f, new Color(0.55f, 0.45f, 0.35f), 0.3f);
            }
            yield return Wait(1.2f);
        }
    }

    // ───────────────────────── 2스테이지: 강의 신 아켈로오스 ─────────────────────────

    public class AchelousBoss : Boss
    {
        private float bob;
        protected override void BuildBossVisual()
        {
            bodySr = Art.Quad(transform, "Body", Art.Circle, baseColor, halfSize * 2f, 10);
            Art.Quad(bodySr.transform, "HornL", Art.Triangle, new Color(0.9f, 0.9f, 0.8f), new Vector2(0.25f, 0.45f), 9, new Vector3(-0.3f, 0.5f, 0));
            Art.Quad(bodySr.transform, "HornR", Art.Triangle, new Color(0.9f, 0.9f, 0.8f), new Vector2(0.25f, 0.45f), 9, new Vector3(0.3f, 0.5f, 0));
            Art.Quad(bodySr.transform, "Beard", Art.Triangle, new Color(0.6f, 0.85f, 1f), new Vector2(0.5f, -0.45f), 11, new Vector3(0f, -0.35f, 0));
            Art.Quad(bodySr.transform, "Eye", Art.Circle, Color.white, new Vector2(0.12f, 0.12f), 11, new Vector3(0.18f, 0.12f, 0));
        }

        private IEnumerator Drift(Vector2 p, float time)
        {
            float e = 0f;
            Vector2 start = Center;
            while (IsAlive && e < time)
            {
                e += Time.deltaTime;
                bob += Time.deltaTime;
                transform.position = Vector2.Lerp(start, p, Mathf.SmoothStep(0, 1, e / time)) + new Vector2(0, Mathf.Sin(bob * 2f) * 0.3f);
                facing = Mathf.Sign(PlayerPos.x - Center.x);
                yield return null;
            }
        }

        protected override IEnumerator Brain()
        {
            Vector2 hover = new Vector2(Random.Range(Left + 4, Right - 4), Ceil - 3.5f);
            PatternName = "이동";
            yield return Drift(hover, 1.2f);

            yield return Telegraph(0.5f, "물줄기 연사");
            for (int b = 0; b < 3 && IsAlive; b++)
            {
                Vector2 d = PlayerPos - (Vector2)Center;
                for (int i = -1; i <= 1; i++) Shoot(Quaternion.Euler(0, 0, i * 12f) * d, Phase == 2 ? 12f : 10f, 0f, Damage * 0.7f, new Color(0.4f, 0.8f, 1f), 0.28f);
                yield return Wait(0.45f);
            }
            yield return Wait(0.6f);

            yield return Telegraph(0.8f, "해일");
            float side = Random.value < 0.5f ? -1f : 1f;
            float x0 = side < 0 ? Left + 0.6f : Right - 0.6f;
            int rows = Phase == 2 ? 3 : 2;
            for (int r = 0; r < rows; r++)
                EnemyProjectile.Spawn(Room, new Vector2(x0, Floor + 0.5f + r * 0.95f), new Vector2(-side * (Phase == 2 ? 9f : 7.5f), 0f), 0f, Damage, new Color(0.3f, 0.6f, 1f), 0.48f, 4.5f);
            yield return Wait(1.4f);

            if (Random.value < 0.6f)
            {
                yield return Telegraph(0.5f, "님프 소환");
                SpawnMinion("naiad", (Vector2)Center + new Vector2(-2f, -1f));
                SpawnMinion("naiad", (Vector2)Center + new Vector2(2f, -1f));
                yield return Wait(0.8f);
            }

            if (Phase == 2)
            {
                yield return Telegraph(0.5f, "폭우");
                for (int i = 0; i < 14 && IsAlive; i++)
                {
                    EnemyProjectile.Spawn(Room, new Vector2(Random.Range(Left + 1, Right - 1), Ceil - 0.4f), new Vector2(0, -2f), 9f, Damage * 0.55f, new Color(0.5f, 0.8f, 1f), 0.25f, 4f);
                    yield return Wait(0.18f);
                }
                yield return Wait(0.6f);
            }
        }
    }

    // ───────────────────────── 3스테이지: 사랑의 신 에로스 ─────────────────────────

    public class ErosBoss : Boss
    {
        private SpriteRenderer wingL, wingR;
        private float flap;

        protected override void BuildBossVisual()
        {
            bodySr = Art.Quad(transform, "Body", Art.Circle, baseColor, halfSize * 2f, 10);
            wingL = Art.Quad(transform, "WingL", Art.Diamond, new Color(1f, 1f, 1f, 0.85f), new Vector2(1.2f, 0.7f), 9, new Vector3(-0.9f, 0.3f, 0));
            wingR = Art.Quad(transform, "WingR", Art.Diamond, new Color(1f, 1f, 1f, 0.85f), new Vector2(1.2f, 0.7f), 9, new Vector3(0.9f, 0.3f, 0));
            Art.Quad(bodySr.transform, "Hair", Art.Circle, new Color(1f, 0.85f, 0.4f), new Vector2(0.9f, 0.45f), 11, new Vector3(0, 0.35f, 0));
            Art.Quad(bodySr.transform, "Bow", Art.Ring, new Color(1f, 0.8f, 0.3f), new Vector2(0.5f, 0.8f), 12, new Vector3(0.45f, -0.1f, 0));
        }

        protected override void Update()
        {
            base.Update();
            flap += Time.deltaTime * 12f;
            if (wingL != null)
            {
                wingL.transform.localRotation = Quaternion.Euler(0, 0, 20f + Mathf.Sin(flap) * 25f);
                wingR.transform.localRotation = Quaternion.Euler(0, 0, -20f - Mathf.Sin(flap) * 25f);
            }
        }

        private IEnumerator FlyTo(Vector2 p, float spd)
        {
            float e = 0f;
            while (IsAlive && e < 2.5f && Vector2.Distance(Center, p) > 0.3f)
            {
                e += Time.deltaTime;
                transform.position = Vector2.MoveTowards(Center, p, spd * Status.SpeedMul * Time.deltaTime);
                facing = Mathf.Sign(PlayerPos.x - Center.x);
                yield return null;
            }
        }

        protected override IEnumerator Brain()
        {
            Vector2 p = new Vector2(Random.Range(Left + 3, Right - 3), Random.Range(Floor + 6, Ceil - 2));
            PatternName = "비행";
            yield return FlyTo(p, Phase == 2 ? 14f : 10f);

            yield return Telegraph(0.5f, "황금 화살 부채");
            int n = Phase == 2 ? 14 : 10;
            float off = Random.Range(0f, 360f / n);
            for (int i = 0; i < n; i++)
            {
                float a = off + i * 360f / n;
                Shoot(new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)), 8.5f, 0f, Damage * 0.6f, new Color(1f, 0.85f, 0.3f), 0.22f);
            }
            yield return Wait(0.9f);

            yield return Telegraph(0.5f, "납 화살");
            for (int i = 0; i < 3 && IsAlive; i++)
            {
                Shoot(PlayerPos - (Vector2)Center, 7f, 2f, Damage * 1.1f, new Color(0.45f, 0.45f, 0.5f), 0.38f, 6f);
                yield return Wait(0.35f);
            }
            yield return Wait(0.6f);

            yield return Telegraph(0.6f, "급강하");
            Vector2 target = PlayerPos;
            Vector2 dir = (target - (Vector2)Center).normalized;
            yield return FlyTo(target + dir * 3f, Phase == 2 ? 22f : 18f);
            yield return Wait(0.5f);

            if (Phase == 2)
            {
                yield return Telegraph(0.5f, "사랑의 추적");
                for (int i = 0; i < 4; i++)
                {
                    var hp = EnemyProjectile.Spawn(Room, (Vector2)Center + Random.insideUnitCircle, Random.insideUnitCircle.normalized * 4f, 0f, Damage * 0.6f, new Color(1f, 0.4f, 0.6f), 0.3f, 3.2f);
                    hp.gameObject.AddComponent<Homing>().room = Room;
                }
                yield return Wait(0.8f);
                if (Random.value < 0.5f)
                {
                    SpawnMinion("cupid", (Vector2)Center + Vector2.left * 2f);
                    SpawnMinion("cupid", (Vector2)Center + Vector2.right * 2f);
                }
            }
        }
    }

    /// <summary>투사체가 플레이어를 천천히 쫓게 만드는 부품</summary>
    public class Homing : MonoBehaviour
    {
        public Room room;
        private EnemyProjectile p;
        private void Awake() { p = GetComponent<EnemyProjectile>(); }
        private void Update()
        {
            if (p == null || room == null || room.Player == null) return;
            Vector2 want = ((Vector2)room.Player.transform.position - (Vector2)transform.position).normalized * 6.5f;
            p.velocity = Vector2.MoveTowards(p.velocity, want, 6f * Time.deltaTime);
        }
    }
}
