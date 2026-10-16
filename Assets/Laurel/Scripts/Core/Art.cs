using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// [임시 결정] 적·지형·이펙트용 그래픽은 아트 자산이 없어 런타임에 단순 도형 텍스처로 만든다.
    /// 플레이어 외형은 기존 프리팹·스프라이트를 그대로 사용한다.
    /// </summary>
    public static class Art
    {
        private static Sprite square, circle, ring, diamond, triangle, soft;
        private static Material lineMat;

        public static Sprite Square => square != null ? square : (square = Make(16, 16, (x, y) => 1f));
        public static Sprite Circle => circle != null ? circle : (circle = Make(64, 64, (x, y) => Disk(x, y, 64, 0.5f)));
        public static Sprite Ring => ring != null ? ring : (ring = Make(64, 64, (x, y) => Disk(x, y, 64, 0.5f) * (1f - Disk(x, y, 64, 0.40f))));
        public static Sprite Soft => soft != null ? soft : (soft = Make(64, 64, (x, y) => SoftDisk(x, y, 64)));
        public static Sprite Diamond => diamond != null ? diamond : (diamond = Make(64, 64, (x, y) =>
        {
            float u = Mathf.Abs((x + 0.5f) / 64f - 0.5f), v = Mathf.Abs((y + 0.5f) / 64f - 0.5f);
            return u + v <= 0.5f ? 1f : 0f;
        }));
        public static Sprite Triangle => triangle != null ? triangle : (triangle = Make(64, 64, (x, y) =>
        {
            float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
            return v <= 1f - Mathf.Abs(u - 0.5f) * 2f ? 1f : 0f;
        }));

        /// <summary>기존 캐릭터 화살 스프라이트 (GameRoot 가 연결). 없으면 도형으로 대체.</summary>
        public static Sprite ArrowSprite;

        public static Material LineMaterial
        {
            get
            {
                if (lineMat == null)
                {
                    var sh = Shader.Find("Sprites/Default");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    lineMat = new Material(sh);
                }
                return lineMat;
            }
        }

        private static float Disk(int x, int y, int size, float r)
        {
            float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01((r - d) * size);
        }

        private static float SoftDisk(int x, int y, int size)
        {
            float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
        }

        private static Sprite Make(int w, int h, System.Func<int, int, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        }

        public static SpriteRenderer Quad(Transform parent, string name, Sprite sprite, Color color, Vector2 size, int order, Vector3 localPos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public static LineRenderer Line(Transform parent, string name, Color color, float width, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = LineMaterial;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.useWorldSpace = true;
            lr.sortingOrder = order;
            lr.numCapVertices = 2;
            return lr;
        }

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
