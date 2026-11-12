using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 레이저 사격 이펙트 (참고 이미지: 하얀 레이저 여러 줄 + 쏘는 쪽의 스케치풍 원형 충격파 + 맞은 곳의 하얀 파편).
    /// 이펙트마다 짧게 살다가 사라지는 오브젝트를 만든다 (선은 LineRenderer, 모두 그림만이고 판정 없음).
    /// </summary>
    public class LaserFx : MonoBehaviour
    {
        public static Color Core = new Color(1f, 1f, 1f, 1f);
        public static Color Soft = new Color(1f, 0.97f, 0.9f, 0.85f);
        const int Order = 30;

        private struct Item { public LineRenderer lr; public float w0, w1; public Color c; public Vector3[] basePts; public Vector2 center; public float grow; }
        private readonly List<Item> items = new List<Item>();
        private float life, t;

        /// <summary>레이저: 활에서 끝점까지 굵은 선 1 + 살짝 벌어진 가는 선 2~3, 활 앞 원형 충격파 2개</summary>
        public static void Shot(Vector2 from, Vector2 to, Vector2 dir)
        {
            var fx = New("LaserShotFx", 0.11f);
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float len = Vector2.Distance(from, to);
            fx.AddLine(new[] { (Vector3)from, (Vector3)to }, 0.075f, 0.03f, Core);
            int extra = Random.Range(2, 4);
            for (int i = 0; i < extra; i++)
            {
                float side = (i % 2 == 0 ? 1f : -1f) * Random.Range(0.06f, 0.22f) * Mathf.Clamp01(len / 6f);
                float startBack = Random.Range(0f, 0.3f);
                Vector2 a = from + dir * startBack;
                Vector2 b = from + dir * len * Random.Range(0.75f, 1.02f) + perp * side;
                fx.AddLine(new[] { (Vector3)a, (Vector3)b }, Random.Range(0.02f, 0.035f), 0.006f, Soft);
            }

            var ring = New("LaserMuzzleRing", 0.18f);
            ring.AddEllipse(from - dir * 0.1f, dir, 0.16f, 0.62f, 2, 0.035f, Soft, 1.6f);
            ring.AddEllipse(from + dir * 0.55f, dir, 0.05f, 0.38f, 1, 0.02f, Soft.WithAlpha(0.7f), 1.2f);
        }

        /// <summary>맞은 곳: 사격 반대쪽으로 튀는 하얀 파편. 대상이면 더 크게</summary>
        public static void Impact(Vector2 point, Vector2 dir, bool onTarget)
        {
            var fx = New("LaserImpactFx", onTarget ? 0.13f : 0.09f);
            int n = onTarget ? Random.Range(7, 10) : 4;
            float baseAng = Mathf.Atan2(-dir.y, -dir.x);
            for (int i = 0; i < n; i++)
            {
                float a = baseAng + Random.Range(-1.3f, 1.3f);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float r0 = Random.Range(0.03f, 0.12f), r1 = r0 + Random.Range(onTarget ? 0.25f : 0.12f, onTarget ? 0.6f : 0.3f);
                fx.AddLine(new[] { (Vector3)(point + d * r0), (Vector3)(point + d * r1) }, 0.03f, 0.008f, Core, point, 1.8f);
            }
            // 앞쪽(관통 방향)으로도 짧게 몇 개
            for (int i = 0; i < (onTarget ? 3 : 1); i++)
            {
                float a = Mathf.Atan2(dir.y, dir.x) + Random.Range(-0.5f, 0.5f);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                fx.AddLine(new[] { (Vector3)(point + d * 0.05f), (Vector3)(point + d * Random.Range(0.2f, 0.4f)) }, 0.025f, 0.006f, Soft, point, 1.5f);
            }
        }

        private static LaserFx New(string name, float life)
        {
            var go = new GameObject(name);
            var fx = go.AddComponent<LaserFx>();
            fx.life = life;
            return fx;
        }

        private void AddLine(Vector3[] pts, float w0, float w1, Color c, Vector2 center = default, float grow = 1f)
        {
            var lr = Art.Line(transform, "l", c, w0, Order);
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth = w0; lr.endWidth = w1;
            lr.numCapVertices = 0;
            items.Add(new Item { lr = lr, w0 = w0, w1 = w1, c = c, basePts = pts, center = center, grow = grow });
        }

        /// <summary>사격 방향에 수직인 타원 (반지름 along × across), 획마다 반지름을 조금씩 흔들어 손그림처럼</summary>
        private void AddEllipse(Vector2 center, Vector2 dir, float along, float across, int strokes, float width, Color c, float grow)
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);
            for (int s = 0; s < strokes; s++)
            {
                const int N = 26;
                var pts = new Vector3[N + 1];
                float phase = Random.Range(0f, 6.28f);
                for (int i = 0; i <= N; i++)
                {
                    float a = i / (float)N * Mathf.PI * 2f + phase;
                    float j = 1f + Random.Range(-0.12f, 0.12f);
                    pts[i] = center + dir * Mathf.Cos(a) * along * j + perp * Mathf.Sin(a) * across * j;
                }
                AddLine(pts, width, width, c, center, grow);
            }
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float fade = 1f - k * k;
            foreach (var it in items)
            {
                if (it.lr == null) continue;
                it.lr.startWidth = it.w0 * (1f - 0.6f * k);
                it.lr.endWidth = it.w1 * (1f - 0.6f * k);
                var c = it.c.WithAlpha(it.c.a * fade);
                it.lr.startColor = c; it.lr.endColor = c;
                if (it.grow != 1f)
                {
                    float g = Mathf.Lerp(1f, it.grow, 1f - (1f - k) * (1f - k));
                    for (int i = 0; i < it.basePts.Length; i++)
                        it.lr.SetPosition(i, (Vector3)it.center + (it.basePts[i] - (Vector3)it.center) * g);
                }
            }
            if (t >= life) Destroy(gameObject);
        }
    }
}
