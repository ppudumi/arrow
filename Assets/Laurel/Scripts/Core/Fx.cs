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
        private float shake;
        private Vector3 basePos;

        private void Awake()
        {
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
            basePos = Clamp(target != null ? target.position : (Vector3)bounds.center);
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
            Vector3 want = target != null ? target.position + Vector3.up * 1.2f : (Vector3)bounds.center;
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
