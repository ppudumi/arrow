using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 레이저 사격 테스트용 허수아비 (기존 Enemy·TrainingDummy 와 무관한 새 대상).
    /// - 체력 무한 (받은 피해 합계만 기록), 움직이지 않음. 판정은 트리거 상자라 플레이어 이동을 막지 않는다
    /// - 맞으면: 하얗게 두 번 깜빡임 + 사격 방향으로 살짝 흔들림 + 맞은 자리에 작고 얇은 화살이 박힘 + 데미지 숫자 + 타격음
    /// - 그림은 도형으로 실행 중에 만든다. 위치(transform)가 발밑(땅)
    /// </summary>
    public class LaserTestDummy : MonoBehaviour, ILaserTarget
    {
        [Tooltip("박히는 화살 그림 (기존 Assets/pjs/Sprites/Character/Arrow.png)")]
        public Sprite arrowSprite;
        [Tooltip("박힌 화살 크기 배율 (원본 1 × 0.22 유닛 기준: 길이, 두께)")]
        public Vector2 arrowScale = new Vector2(0.45f, 0.35f);
        [Tooltip("화살 끝이 대상 안으로 들어가는 깊이 (유닛)")]
        public float arrowEmbed = 0.1f;
        [Tooltip("한 번에 박혀 있는 화살 최대 수 (넘으면 오래된 것부터 지움)")]
        public int maxArrows = 30;
        [Tooltip("판정 상자 (발밑 기준)")]
        public Vector2 hitboxCenter = new Vector2(0f, 1.05f), hitboxSize = new Vector2(0.7f, 1.3f);

        public int TotalDamage { get; private set; }
        public int Hits { get; private set; }
        public int StuckArrows => arrows.Count;
        public bool Flashing => flashT >= 0f;

        private Transform visual;
        private readonly List<SpriteRenderer> parts = new List<SpriteRenderer>();
        private readonly List<Color> partColors = new List<Color>();
        private readonly Queue<GameObject> arrows = new Queue<GameObject>();
        private float flashT = -1f;
        private float shakeT = -1f;
        private Vector2 shakeDir;
        private static readonly float[] Blink = { 0.045f, 0.035f, 0.035f }; // 흰색 → 원래 → 흰색 → 원래

        private void Awake()
        {
            BuildVisual();
            var box = GetComponent<BoxCollider2D>();
            if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = hitboxCenter;
            box.size = hitboxSize;
        }

        private void BuildVisual()
        {
            var old = transform.Find("Visual");
            if (old != null) Destroy(old.gameObject);
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            var wood = new Color(0.42f, 0.29f, 0.17f);
            var sack = new Color(0.79f, 0.67f, 0.46f);
            var straw = new Color(0.93f, 0.80f, 0.40f);
            var rope = new Color(0.36f, 0.25f, 0.15f);
            Add("Base", Art.Square, wood * 0.85f, new Vector2(0.55f, 0.08f), 5, new Vector3(0f, 0.04f));
            Add("Post", Art.Square, wood, new Vector2(0.12f, 1.55f), 5, new Vector3(0f, 0.78f));
            Add("Bar", Art.Square, wood, new Vector2(1.0f, 0.09f), 6, new Vector3(0f, 1.18f));
            Add("StrawL", Art.Diamond, straw, new Vector2(0.18f, 0.16f), 7, new Vector3(-0.52f, 1.18f));
            Add("StrawR", Art.Diamond, straw, new Vector2(0.18f, 0.16f), 7, new Vector3(0.52f, 1.18f));
            Add("Body", Art.Circle, sack, new Vector2(0.64f, 0.82f), 8, new Vector3(0f, 0.95f));
            Add("Belt", Art.Square, rope, new Vector2(0.58f, 0.06f), 9, new Vector3(0f, 0.82f));
            Add("Head", Art.Circle, sack * 1.06f, new Vector2(0.42f, 0.42f), 8, new Vector3(0f, 1.52f));
            Add("Tie", Art.Square, rope, new Vector2(0.2f, 0.05f), 9, new Vector3(0f, 1.33f));
            Add("EyeL", Art.Square, rope, new Vector2(0.06f, 0.06f), 9, new Vector3(-0.08f, 1.55f));
            Add("EyeR", Art.Square, rope, new Vector2(0.06f, 0.06f), 9, new Vector3(0.08f, 1.55f));
        }

        private void Add(string n, Sprite s, Color c, Vector2 size, int order, Vector3 pos)
        {
            c.a = 1f;
            var sr = Art.Quad(visual, n, s, c, size, order, pos);
            parts.Add(sr);
            partColors.Add(c);
        }

        public void TakeLaserHit(int damage, bool critical, Vector2 point, Vector2 dir)
        {
            TotalDamage += damage;
            Hits++;
            flashT = 0f;
            shakeT = 0f;
            shakeDir = dir;
            StickArrow(point, dir);
            DamagePopup.Spawn(point + new Vector2(Random.Range(-0.2f, 0.2f), 0.3f + Random.Range(0f, 0.15f)), damage, critical);
            Sfx.Play("hit", critical ? 0.9f : 0.7f, critical ? 0.8f : Random.Range(0.95f, 1.15f));
        }

        private void StickArrow(Vector2 point, Vector2 dir)
        {
            var go = new GameObject("StuckArrow");
            go.transform.SetParent(visual, true);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = arrowSprite != null ? arrowSprite : Art.Diamond;
            sr.sortingOrder = 12;
            go.transform.localScale = new Vector3(arrowScale.x, arrowScale.y, 1f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            // 피벗(꼬리 쪽 10%) → 촉까지 거리만큼 뒤로 물려, 촉이 맞은 지점에서 arrowEmbed 만큼 안쪽에 오게
            float pivotToTip = sr.sprite != null ? (sr.sprite.rect.width - sr.sprite.pivot.x) / sr.sprite.pixelsPerUnit * arrowScale.x : 0.4f;
            go.transform.position = (Vector3)(point - dir * (pivotToTip - arrowEmbed));
            arrows.Enqueue(go);
            while (arrows.Count > maxArrows) { var o = arrows.Dequeue(); if (o != null) Destroy(o); }
        }

        /// <summary>박힌 화살 모두 제거 (테스트용)</summary>
        [ContextMenu("박힌 화살 지우기")]
        public void ClearArrows()
        {
            while (arrows.Count > 0) { var o = arrows.Dequeue(); if (o != null) Destroy(o); }
        }

        private void Update()
        {
            // 깜빡임: 흰색 → 원래 → 흰색 → 원래
            if (flashT >= 0f)
            {
                flashT += Time.deltaTime;
                bool white = flashT < Blink[0] || (flashT >= Blink[0] + Blink[1] && flashT < Blink[0] + Blink[1] + Blink[2]);
                for (int i = 0; i < parts.Count; i++) parts[i].color = white ? Color.white : partColors[i];
                if (flashT >= Blink[0] + Blink[1] + Blink[2]) { flashT = -1f; for (int i = 0; i < parts.Count; i++) parts[i].color = partColors[i]; }
            }
            // 흔들림: 사격 방향으로 밀렸다 돌아옴 (그림만)
            if (shakeT >= 0f)
            {
                shakeT += Time.deltaTime;
                float k = Mathf.Clamp01(shakeT / 0.12f);
                float a = (1f - k) * 0.07f;
                visual.localPosition = (Vector3)(shakeDir * a * Mathf.Cos(k * Mathf.PI * 3f));
                if (k >= 1f) { shakeT = -1f; visual.localPosition = Vector3.zero; }
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(transform.position + (Vector3)hitboxCenter, hitboxSize);
        }
    }
}
