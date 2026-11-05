using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>떠오르는 숫자, 파편, 번개·범위 표시 같은 임시 연출. 모두 수명이 있고 자동 정리된다.</summary>
    public class Fx : MonoBehaviour
    {
        public static Fx I { get; private set; }

        public class FloatText
        {
            public Vector3 pos;
            public string text;
            public Color color;
            public float life, maxLife;
            public int size;
        }

        private class Particle
        {
            public SpriteRenderer sr;
            public Vector2 vel;
            public float life, maxLife, gravity, startScale;
        }

        private class TimedLine
        {
            public LineRenderer lr;
            public float life, maxLife;
            public Color color;
        }

        public readonly List<FloatText> texts = new List<FloatText>();
        private readonly List<Particle> particles = new List<Particle>();
        private readonly Stack<SpriteRenderer> particlePool = new Stack<SpriteRenderer>();
        private readonly List<TimedLine> lines = new List<TimedLine>();
        private readonly Stack<LineRenderer> linePool = new Stack<LineRenderer>();

        private void Awake() { I = this; }

        public static void Text(Vector3 pos, string text, Color color, int size = 26, float life = 0.9f)
        {
            if (I == null) return;
            if (I.texts.Count > 80) I.texts.RemoveAt(0);
            I.texts.Add(new FloatText { pos = pos + new Vector3(Random.Range(-0.2f, 0.2f), 0.2f, 0), text = text, color = color, life = life, maxLife = life, size = size });
        }

        public static void Burst(Vector2 pos, Color color, int count, float speed = 6f, float size = 0.18f, float life = 0.45f, float gravity = 12f)
        {
            if (I == null) return;
            for (int i = 0; i < count; i++)
            {
                if (I.particles.Count > 400) break;
                var sr = I.particlePool.Count > 0 ? I.particlePool.Pop() : MakeParticle();
                sr.gameObject.SetActive(true);
                sr.transform.position = pos;
                sr.color = color;
                float s = size * Random.Range(0.6f, 1.3f);
                sr.transform.localScale = new Vector3(s, s, 1f);
                var dir = Random.insideUnitCircle.normalized * speed * Random.Range(0.3f, 1f);
                I.particles.Add(new Particle { sr = sr, vel = dir, life = life, maxLife = life, gravity = gravity, startScale = s });
            }
        }

        private static SpriteRenderer MakeParticle()
        {
            var sr = Art.Quad(I.transform, "p", Art.Square, Color.white, Vector2.one * 0.2f, 60);
            return sr;
        }

        /// <summary>점들을 잇는 선을 잠시 보여준다</summary>
        public static void Polyline(IList<Vector3> pts, Color color, float width, float life, int order = 55)
        {
            if (I == null || pts.Count < 2) return;
            var lr = I.linePool.Count > 0 ? I.linePool.Pop() : Art.Line(I.transform, "fxline", color, width, order);
            lr.gameObject.SetActive(true);
            lr.startWidth = lr.endWidth = width;
            lr.sortingOrder = order;
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) lr.SetPosition(i, pts[i]);
            lr.startColor = lr.endColor = color;
            I.lines.Add(new TimedLine { lr = lr, life = life, maxLife = life, color = color });
        }

        public static void Lightning(Vector2 a, Vector2 b, Color color, float width, float life)
        {
            var pts = new List<Vector3>();
            int seg = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a, b) * 2f), 3, 24);
            Vector2 n = Vector2.Perpendicular((b - a).normalized);
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                Vector2 p = Vector2.Lerp(a, b, t);
                if (i > 0 && i < seg) p += n * Random.Range(-0.3f, 0.3f);
                pts.Add(new Vector3(p.x, p.y, -0.2f));
            }
            Polyline(pts, color, width, life, 70);
        }

        public static void Circle(Vector2 c, float r, Color color, float width, float life)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                pts.Add(new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, -0.1f));
            }
            Polyline(pts, color, width, life);
        }

        public static void Sector(Vector2 o, Vector2 dir, float r, float angle, Color color, float width, float life)
        {
            var pts = new List<Vector3> { o };
            float baseA = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            for (int i = 0; i <= 20; i++)
            {
                float a = (baseA - angle * 0.5f + angle * i / 20f) * Mathf.Deg2Rad;
                pts.Add(new Vector3(o.x + Mathf.Cos(a) * r, o.y + Mathf.Sin(a) * r, -0.1f));
            }
            pts.Add(o);
            Polyline(pts, color, width, life);
        }

        public static void Box(Vector2 center, Vector2 size, float angle, Color color, float width, float life)
        {
            var q = Quaternion.Euler(0, 0, angle);
            Vector2 hx = q * new Vector3(size.x * 0.5f, 0), hy = q * new Vector3(0, size.y * 0.5f);
            var pts = new List<Vector3>
            {
                center - hx - hy, center + hx - hy, center + hx + hy, center - hx + hy, center - hx - hy
            };
            Polyline(pts, color, width, life);
        }

        public void ClearAll()
        {
            texts.Clear();
            foreach (var p in particles) { p.sr.gameObject.SetActive(false); particlePool.Push(p.sr); }
            particles.Clear();
            foreach (var l in lines) { l.lr.gameObject.SetActive(false); linePool.Push(l.lr); }
            lines.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                var t = texts[i];
                t.life -= dt;
                t.pos += Vector3.up * dt * 1.4f;
                if (t.life <= 0f) texts.RemoveAt(i);
            }
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.life -= dt;
                p.vel.y -= p.gravity * dt;
                p.sr.transform.position += (Vector3)(p.vel * dt);
                float k = Mathf.Clamp01(p.life / p.maxLife);
                p.sr.transform.localScale = Vector3.one * p.startScale * k;
                if (p.life <= 0f)
                {
                    p.sr.gameObject.SetActive(false);
                    particlePool.Push(p.sr);
                    particles.RemoveAt(i);
                }
            }
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i];
                l.life -= dt;
                float k = Mathf.Clamp01(l.life / l.maxLife);
                l.lr.startColor = l.lr.endColor = l.color.WithAlpha(l.color.a * k);
                if (l.life <= 0f)
                {
                    l.lr.gameObject.SetActive(false);
                    linePool.Push(l.lr);
                    lines.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>플레이어를 따라가되 방(정사각형 맵) 경계를 넘지 않는 카메라. 흔들림 지원.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam { get; private set; }
        public Transform target;
        public Rect bounds = new Rect(-11, -11, 22, 22);
        public bool followY = true;
        /// <summary>보스처럼 함께 비춰야 하는 대상. 플레이어와의 사이를 비춘다.</summary>
        public Transform focus2;
        public float focus2Weight = 0.4f;

        [Header("플레이 카메라 (Inspector 에서 조정)")]
        [Tooltip("튜토리얼·전투방·로비의 줌 = 화면 세로 절반 (월드 유닛). 클수록 넓게 보이고 캐릭터가 작아진다. 0 이면 balance.json 의 world.cameraSize. 보스전은 world.bossCameraSize")]
        public float playSize = 7f;
        [Tooltip("플레이어보다 이만큼 위를 비춘다. 위쪽 발판이 먼저 보이게 (이전 1.2)")]
        public float lookUp = 2f;
        [Tooltip("달리는 방향 앞쪽을 미리 비추는 최대 거리 (방이 화면보다 넓을 때만 효과)")]
        public float lookAhead = 2.5f;
        [Tooltip("빠르게 떨어질 때 아래쪽 착지점을 미리 비추는 거리")]
        public float fallLookDown = 2.5f;
        [Tooltip("이 속도(유닛/초)보다 빨리 떨어지면 아래를 비추기 시작")]
        public float fallSpeedThreshold = 8f;
        [Tooltip("앞·아래 미리보기가 바뀌는 빠르기 (클수록 즉각)")]
        public float lookSmoothing = 3f;

        /// <summary>일반 방에 쓸 줌</summary>
        public float PlaySize => playSize > 0f ? playSize : DB.Balance.world.cameraSize;

        private float shake;
        private Vector3 basePos;
        private Vector2 look;
        private Rigidbody2D targetBody;

        private void Awake() => Init();

        /// <summary>씬에 미리 붙어 있으면 GameRoot.Awake 가 먼저 돌 수 있어, GameRoot 가 직접 불러 준다.</summary>
        public void Init()
        {
            if (I == this && Cam != null) return;
            I = this;
            Cam = GetComponent<Camera>();
            basePos = transform.position;
        }

        public void Setup(Rect worldBounds, float orthoSize, Color bg)
        {
            bounds = worldBounds;
            Cam.orthographicSize = orthoSize;
            Cam.backgroundColor = bg;
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            look = Vector2.zero;
            basePos = Clamp(target != null ? target.position + Vector3.up * lookUp : (Vector3)bounds.center);
            transform.position = basePos;
        }

        public static void Shake(float amount)
        {
            if (I != null) I.shake = Mathf.Max(I.shake, amount);
        }

        private Vector3 Clamp(Vector3 want)
        {
            float h = Cam.orthographicSize, w = h * Cam.aspect;
            float x = bounds.width <= w * 2f ? bounds.center.x : Mathf.Clamp(want.x, bounds.xMin + w, bounds.xMax - w);
            float y = bounds.height <= h * 2f ? bounds.center.y : Mathf.Clamp(want.y, bounds.yMin + h, bounds.yMax - h);
            if (!followY) y = bounds.center.y;
            return new Vector3(x, y, -10f);
        }

        private void LateUpdate()
        {
            // 앞(달리는 방향)·아래(빠른 낙하) 미리보기
            Vector2 lookWant = Vector2.zero;
            if (target != null)
            {
                if (targetBody == null || targetBody.transform != target) targetBody = target.GetComponent<Rigidbody2D>();
                if (targetBody != null)
                {
                    Vector2 v = targetBody.linearVelocity;
                    lookWant.x = Mathf.Clamp(v.x / 10f, -1f, 1f) * lookAhead;
                    if (v.y < -fallSpeedThreshold) lookWant.y = -fallLookDown;
                }
            }
            look = Vector2.Lerp(look, lookWant, 1f - Mathf.Exp(-lookSmoothing * Time.unscaledDeltaTime));
            Vector3 want = target != null ? target.position + Vector3.up * lookUp + (Vector3)look : (Vector3)bounds.center;
            if (focus2 != null && target != null) want = Vector3.Lerp(want, focus2.position, focus2Weight);
            basePos = Vector3.Lerp(basePos, Clamp(want), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            Vector3 off = Vector3.zero;
            if (shake > 0f)
            {
                off = (Vector3)Random.insideUnitCircle * shake;
                shake = Mathf.Max(0f, shake - Time.unscaledDeltaTime * 2.5f);
            }
            transform.position = new Vector3(basePos.x, basePos.y, -10f) + off;
        }
    }
}
