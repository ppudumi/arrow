using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 데미지 숫자 (참고 이미지처럼 어두운 작은 상자 안의 픽셀 숫자, 치명타는 노란색 + '!').
    /// 숫자는 3×5 픽셀 글꼴을 코드로 그려 쓰고(폰트 에셋 불필요), 튀어 오르며 커졌다가 위로 떠오르며 사라진다.
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        [Tooltip("글꼴 1픽셀의 크기 (유닛).")]
        public static float PixelSize = 4f / (1080f / 21f);
        public static Color BoxColor = new Color(0.10f, 0.08f, 0.08f, 0.88f);
        public static Color BorderColor = new Color(0.55f, 0.52f, 0.5f, 0.9f);
        public static Color NormalColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        public static Color CritColor = new Color(1f, 0.82f, 0.25f, 1f);
        const int Order = 40;

        // 3×5 글꼴 (위 줄부터, 1 = 칠함)
        private static readonly string[] Glyphs =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
        };
        private static readonly string Bang = "11101"; // 1×5 '!'
        private static readonly Dictionary<char, string> LetterGlyphs = new Dictionary<char, string>
        {
            ['D'] = "110101101101110",
            ['O'] = "111101101101111",
            ['G'] = "111100101101111",
            ['E'] = "111100111100111",
        };
        private static Sprite[] digitSprites;
        private static Sprite bangSprite;
        private static Dictionary<char, Sprite> letterSprites;

        private float t;
        private Vector3 start;
        private Vector3 drift;
        private float baseScale = 1f;
        private SpriteRenderer[] renderers;
        private Color[] baseColors;

        public static void Spawn(Vector2 pos, int value, bool critical, Color? overrideColor = null, float scaleMultiplier = 1f)
        {
            Build();
            var go = new GameObject("DamagePopup");
            var p = go.AddComponent<DamagePopup>();
            string s = value.ToString();
            float px = PixelSize;
            int glyphW = 3, gap = 1, pad = 1;
            int textW = s.Length * glyphW + (s.Length - 1) * gap + (critical ? gap + 1 : 0);
            int boxW = textW + pad * 2 + 2, boxH = 5 + pad * 2 + 2;   // +2 = 테두리
            var root = go.transform;
            Art.Quad(root, "Border", Art.Square, BorderColor, new Vector2(boxW * px, boxH * px), Order);
            Art.Quad(root, "Box", Art.Square, BoxColor, new Vector2((boxW - 2) * px, (boxH - 2) * px), Order + 1);
            var col = overrideColor ?? (critical ? CritColor : NormalColor);
            float x = -textW * px * 0.5f;
            foreach (char ch in s)
            {
                Glyph(root, digitSprites[ch - '0'], col, new Vector3(x + glyphW * px * 0.5f, 0f, 0f));
                x += (glyphW + gap) * px;
            }
            if (critical) Glyph(root, bangSprite, col, new Vector3(x + px * 0.5f, 0f, 0f));
            p.baseScale = (critical ? 1.25f : 1f) * Mathf.Max(0.5f, scaleMultiplier);

            p.start = (Vector3)pos;
            p.drift = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.45f, 0.65f), 0f);
            go.transform.position = p.start;
            p.renderers = go.GetComponentsInChildren<SpriteRenderer>();
            p.baseColors = new Color[p.renderers.Length];
            for (int i = 0; i < p.renderers.Length; i++) p.baseColors[i] = p.renderers[i].color;
        }

        /// <summary>데미지 숫자와 동일한 박스 팝업 형태로 짧은 영문 텍스트(예: "DODGE!")를 띄운다.</summary>
        public static void SpawnText(Vector2 pos, string text, Color color)
        {
            Build();
            string upper = (text ?? "").ToUpperInvariant();
            if (upper.Length == 0) return;

            var go = new GameObject("DamagePopup_Text");
            var p = go.AddComponent<DamagePopup>();
            float px = PixelSize;
            int glyphW = 3, gap = 1, pad = 1;
            int textW = 0;
            for (int i = 0; i < upper.Length; i++)
            {
                if (i > 0) textW += gap;
                textW += upper[i] == '!' ? 1 : glyphW;
            }
            int boxW = textW + pad * 2 + 2, boxH = 5 + pad * 2 + 2;
            var root = go.transform;
            Art.Quad(root, "Border", Art.Square, BorderColor, new Vector2(boxW * px, boxH * px), Order);
            Art.Quad(root, "Box", Art.Square, BoxColor, new Vector2((boxW - 2) * px, (boxH - 2) * px), Order + 1);

            float x = -textW * px * 0.5f;
            foreach (char ch in upper)
            {
                if (ch == '!')
                {
                    Glyph(root, bangSprite, color, new Vector3(x + px * 0.5f, 0f, 0f));
                    x += (1 + gap) * px;
                }
                else
                {
                    Sprite sp = null;
                    if (ch >= '0' && ch <= '9') sp = digitSprites[ch - '0'];
                    else letterSprites.TryGetValue(ch, out sp);
                    if (sp != null) Glyph(root, sp, color, new Vector3(x + glyphW * px * 0.5f, 0f, 0f));
                    x += (glyphW + gap) * px;
                }
            }

            p.baseScale = 1.15f;
            p.start = (Vector3)pos;
            p.drift = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.45f, 0.65f), 0f);
            go.transform.position = p.start;
            p.renderers = go.GetComponentsInChildren<SpriteRenderer>();
            p.baseColors = new Color[p.renderers.Length];
            for (int i = 0; i < p.renderers.Length; i++) p.baseColors[i] = p.renderers[i].color;
        }

        private static SpriteRenderer Glyph(Transform parent, Sprite s, Color c, Vector3 pos)
        {
            var go = new GameObject("g");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * PixelSize; // 글꼴 스프라이트는 1픽셀 = 1유닛
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.color = c; sr.sortingOrder = Order + 2;
            return sr;
        }

        private static void Build()
        {
            if (digitSprites != null) return;
            digitSprites = new Sprite[10];
            for (int d = 0; d < 10; d++) digitSprites[d] = Make(Glyphs[d], 3);
            bangSprite = Make(Bang, 1);
            letterSprites = new Dictionary<char, Sprite>();
            foreach (var kv in LetterGlyphs) letterSprites[kv.Key] = Make(kv.Value, 3);
        }

        private static Sprite Make(string bits, int w)
        {
            int h = 5;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, h - 1 - y, bits[y * w + x] == '1' ? Color.white : Color.clear);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        }

        private void Update()
        {
            t += Time.deltaTime;
            const float life = 0.75f;
            // 0~0.08초 튀어 오르며 커짐 → 위로 떠오름 → 마지막 0.25초 사라짐
            float pop = t < 0.08f ? Mathf.Lerp(0.4f, 1.25f, t / 0.08f) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((t - 0.08f) / 0.1f));
            float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / life), 3f);
            transform.position = start + drift * rise;
            transform.localScale = Vector3.one * pop * baseScale;
            float a = t > life - 0.25f ? Mathf.Clamp01((life - t) / 0.25f) : 1f;
            for (int i = 0; i < renderers.Length; i++) renderers[i].color = baseColors[i].WithAlpha(baseColors[i].a * a);
            if (t >= life) Destroy(gameObject);
        }
    }
}
