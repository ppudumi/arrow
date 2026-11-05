using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 로비(지하 신전) 장식. 씬 오브젝트에 붙여 Inspector 에서 그림·위치를 바꾸고, GameRoot.GoLobby 가 로비를 만들 때 Apply 한다.
    /// - 도리아식 기둥·그리스 문양 띠를 배경 앞(지형·플레이어 뒤)에 세운다. 충돌 없음.
    /// - 기존 제단(Room 의 "Altar")·불빛("Flame") 오브젝트는 그대로 두고 그림과 색만 바꾼다.
    /// - 로비의 임시 기둥 도형("Pillar")은 숨긴다.
    /// 위치는 로비 방 중심 기준 월드 유닛. 기본값은 콘셉트아트(54.3px = 1유닛, 그림 바닥선 = 로비 바닥 y -7)에 맞춘 값이라
    /// 배경 이미지(Backdrop_Lobby)의 같은 자리에 정확히 겹친다.
    /// </summary>
    public class LobbyDecor : MonoBehaviour
    {
        [Header("도리아식 기둥")]
        public Sprite pillar;
        [Tooltip("기둥 중심 x 위치들 (콘셉트아트 기둥 자리)")]
        public float[] pillarX = { -9.733f, -1.777f, 12.827f };
        [Tooltip("기둥 중심 y (아래끝이 바닥선에 닿는 값)")]
        public float pillarY = -0.628f;
        public int pillarOrder = -20;

        [Header("그리스 문양 띠")]
        public Sprite frieze;
        public Vector2 friezePosition = new Vector2(-1.215f, 6.389f);
        public int friezeOrder = -19;

        [Header("제단 (기존 Altar 오브젝트에 적용)")]
        [Tooltip("올리브 문양 제단 + 화로 그림")]
        public Sprite altar;
        [Tooltip("제단 중심 (아래끝이 바닥 y -7 에 닿는 값). x 는 기존 제단 자리 0")]
        public Vector2 altarPosition = new Vector2(0f, -4.845f);

        [Header("제단 불빛 (기존 Flame 오브젝트에 적용)")]
        public Color flameGlow = new Color(1f, 0.72f, 0.32f, 0.5f);
        public Vector2 flameGlowSize = new Vector2(3.4f, 3.8f);
        public Vector2 flameGlowPosition = new Vector2(0f, -3.7f);
        [Tooltip("불빛 깜빡임 세기 (0 이면 고정)")]
        [Range(0f, 0.5f)] public float flicker = 0.12f;

        public void Apply(Room room)
        {
            if (room == null) return;
            var t = room.transform;
            foreach (Transform c in t) if (c.name == "Pillar") c.gameObject.SetActive(false); // 임시 기둥 도형

            if (pillar != null && pillarX != null)
                foreach (var x in pillarX)
                    Art.Quad(t, "Decor_Pillar", pillar, Color.white, Vector2.one, pillarOrder, new Vector3(x, pillarY, 2f));
            if (frieze != null)
                Art.Quad(t, "Decor_Frieze", frieze, Color.white, Vector2.one, friezeOrder, new Vector3(friezePosition.x, friezePosition.y, 2f));

            var altarT = t.Find("Altar");
            if (altarT != null && altar != null)
            {
                var sr = altarT.GetComponent<SpriteRenderer>();
                sr.sprite = altar;
                sr.color = Color.white;
                altarT.localScale = Vector3.one;
                altarT.localPosition = new Vector3(altarPosition.x, altarPosition.y, altarT.localPosition.z);
            }
            var flameT = t.Find("Flame");
            if (flameT != null)
            {
                var sr = flameT.GetComponent<SpriteRenderer>();
                sr.color = flameGlow;
                sr.sortingOrder = (altarT != null ? altarT.GetComponent<SpriteRenderer>().sortingOrder : -5) - 1; // 제단 뒤에서 비치는 빛
                flameT.localScale = new Vector3(flameGlowSize.x, flameGlowSize.y, 1f);
                flameT.localPosition = new Vector3(flameGlowPosition.x, flameGlowPosition.y, flameT.localPosition.z);
                if (flicker > 0f)
                {
                    var f = flameT.gameObject.AddComponent<GlowFlicker>();
                    f.baseColor = flameGlow;
                    f.baseScale = flameT.localScale;
                    f.amount = flicker;
                }
            }
        }
    }

    /// <summary>불빛이 은은하게 일렁이도록 밝기·크기를 조금씩 바꾼다.</summary>
    public class GlowFlicker : MonoBehaviour
    {
        public Color baseColor;
        public Vector3 baseScale;
        public float amount = 0.12f;
        private SpriteRenderer sr;
        private float seed;

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            seed = Random.value * 10f;
        }

        private void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * 2.3f) - 0.5f;
            float k = 1f + n * 2f * amount;
            if (sr != null) sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(baseColor.a * k));
            transform.localScale = new Vector3(baseScale.x * (1f + n * amount), baseScale.y * k, 1f);
        }
    }
}
