using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 범위·판정·번개선 표시용 LineRenderer 풀. 매 프레임 제출된 도형만 그린다.
    /// 일정 시간 유지되는 도형(찌르기 판정, 낫 궤적, 번개선)은 AddTimed 로 등록한다.
    /// </summary>
    public class ArcheryDebugDraw : MonoBehaviour
    {
        public Material lineMaterial;
        public bool showRanges = true;

        private struct Shape
        {
            public Vector3[] points;
            public bool loop;
            public Color color;
            public float width;
            public float expire;     // 0 = 이번 프레임만
            public float duration;
        }

        private readonly List<Shape> frameShapes = new List<Shape>();
        private readonly List<Shape> timedShapes = new List<Shape>();
        private readonly List<LineRenderer> pool = new List<LineRenderer>();

        public void Circle(Vector2 c, float r, Color col, float width = 0.05f, bool persistentRange = true)
        {
            if (persistentRange && !showRanges) return;
            frameShapes.Add(new Shape { points = CirclePoints(c, r, 48), loop = true, color = col, width = width });
        }

        public void Sector(Vector2 c, Vector2 dir, float r, float angleDeg, Color col, float width = 0.05f, float duration = 0f)
        {
            var pts = SectorPoints(c, dir, r, angleDeg);
            Add(new Shape { points = pts, loop = true, color = col, width = width }, duration);
        }

        public void Box(Vector2 center, Vector2 size, float angleDeg, Color col, float width = 0.05f, float duration = 0f)
        {
            Quaternion q = Quaternion.Euler(0f, 0f, angleDeg);
            Vector2 h = size * 0.5f;
            var pts = new Vector3[]
            {
                (Vector3)center + q * new Vector3(-h.x, -h.y),
                (Vector3)center + q * new Vector3(h.x, -h.y),
                (Vector3)center + q * new Vector3(h.x, h.y),
                (Vector3)center + q * new Vector3(-h.x, h.y),
            };
            Add(new Shape { points = pts, loop = true, color = col, width = width }, duration);
        }

        public void Polyline(List<Vector3> pts, Color col, float width, float duration)
        {
            Add(new Shape { points = pts.ToArray(), loop = false, color = col, width = width }, duration);
        }

        /// <summary>두 점 사이에 지그재그 번개선</summary>
        public void Lightning(Vector2 a, Vector2 b, Color col, float width, float duration)
        {
            var pts = new List<Vector3>();
            int segs = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a, b) / 0.45f), 3, 40);
            Vector2 perp = Vector2.Perpendicular((b - a).normalized);
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                Vector2 p = Vector2.Lerp(a, b, t);
                if (i != 0 && i != segs) p += perp * Random.Range(-0.28f, 0.28f);
                pts.Add(new Vector3(p.x, p.y, -0.1f));
            }
            Polyline(pts, col, width, duration);
        }

        private void Add(Shape s, float duration)
        {
            if (duration > 0f)
            {
                s.expire = Time.time + duration;
                s.duration = duration;
                timedShapes.Add(s);
            }
            else frameShapes.Add(s);
        }

        public void ClearAll()
        {
            frameShapes.Clear();
            timedShapes.Clear();
            for (int i = 0; i < pool.Count; i++) if (pool[i] != null) pool[i].enabled = false;
        }

        private void LateUpdate()
        {
            int used = 0;
            for (int i = timedShapes.Count - 1; i >= 0; i--)
            {
                if (Time.time >= timedShapes[i].expire) timedShapes.RemoveAt(i);
            }
            for (int i = 0; i < timedShapes.Count; i++)
            {
                var s = timedShapes[i];
                float a = Mathf.Clamp01((s.expire - Time.time) / Mathf.Max(0.01f, s.duration));
                s.color.a *= Mathf.Lerp(0.25f, 1f, a);
                Render(s, used++);
            }
            for (int i = 0; i < frameShapes.Count; i++) Render(frameShapes[i], used++);
            frameShapes.Clear();
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        private void Render(Shape s, int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("DebugLine");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.sortingOrder = 40;
                lr.numCapVertices = 2;
                lr.material = lineMaterial;
                pool.Add(lr);
            }
            var l = pool[index];
            l.enabled = true;
            l.loop = s.loop;
            l.startWidth = l.endWidth = s.width;
            l.startColor = l.endColor = s.color;
            l.positionCount = s.points.Length;
            l.SetPositions(s.points);
        }

        public static Vector3[] CirclePoints(Vector2 c, float r, int n)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n * Mathf.PI * 2f;
                pts[i] = new Vector3(c.x + Mathf.Cos(t) * r, c.y + Mathf.Sin(t) * r, -0.1f);
            }
            return pts;
        }

        public static Vector3[] SectorPoints(Vector2 c, Vector2 dir, float r, float angleDeg)
        {
            int n = 24;
            var pts = new Vector3[n + 2];
            pts[0] = new Vector3(c.x, c.y, -0.1f);
            float baseAngle = Mathf.Atan2(dir.y, dir.x);
            float half = angleDeg * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i <= n; i++)
            {
                float t = baseAngle - half + (2f * half) * i / n;
                pts[i + 1] = new Vector3(c.x + Mathf.Cos(t) * r, c.y + Mathf.Sin(t) * r, -0.1f);
            }
            return pts;
        }
    }
}
