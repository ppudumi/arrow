using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 튜토리얼 보스전 맵 테스트 씬(TutorialBossMap_Test) 전용. 게임 흐름(GameRoot) 없이 맵·플레이어만 둔다.
    /// - 카메라: 플레이어를 따라가지 않고 전투 공간(arena) 전체를 고정 구도로 보여 준다.
    ///   화면이 arena 비율(참고 화면 ≈ 2.227:1)보다 좁으면 가로를 맞추고 위아래를 더 보여 주며, 넓으면 세로를 맞추고 좌우를 더 보여 준다.
    /// - 배경: 1스테이지 배경 이미지를 카메라가 보여 주는 범위 전체에 깔고, 이미지 위·아래 바깥은 하늘색·안개색으로 채운다.
    /// - 경계: arena 좌우·위에 보이지 않는 벽을 두어 플레이어가 화면 밖으로 나가지 않게 한다.
    /// 지형은 씬의 Tilemap(Rule Tile)으로 직접 편집한다. 1타일 = 32px = 1유닛.
    /// </summary>
    [ExecuteAlways]
    public class TutorialBossMapTest : MonoBehaviour
    {
        [Header("전투 공간 (참고 화면의 게임 영역에 해당, 월드 유닛)")]
        [Tooltip("카메라가 항상 다 보여 주는 범위. 기본: 폭 46.77 × 높이 21 (참고 화면 비율 2.227:1, 플레이어 키 ≈ 화면 높이의 4.6%)")]
        public Rect arena = new Rect(-0.1f, 0.37f, 46.77f, 21f);
        public Camera cam;

        [Header("배경 (1스테이지 배경)")]
        public SpriteRenderer backdropImage;
        public SpriteRenderer skyFill;
        public SpriteRenderer groundFill;
        [Tooltip("이미지 위 경계를 하늘색으로 섞는 그라데이션 (위로 갈수록 진함)")]
        public SpriteRenderer skyBlend;
        [Tooltip("이미지 아래 경계를 안개색으로 섞는 그라데이션 (flipY)")]
        public SpriteRenderer groundBlend;
        [Tooltip("위·아래 경계를 섞는 높이 (이미지 안쪽, 월드 유닛)")]
        public float skyFade = 1.5f, groundFade = 2f;
        [Tooltip("배경 이미지 폭 (월드 유닛). arena 폭보다 넓게")]
        public float backdropWidth = 56f;
        [Tooltip("arena 중심 기준 배경 이미지 중심 위치")]
        public Vector2 backdropOffset = Vector2.zero;

        [Header("보이지 않는 경계")]
        public BoxCollider2D wallLeft;
        public BoxCollider2D wallRight;
        public BoxCollider2D ceiling;
        [Tooltip("천장을 arena 위쪽 끝보다 이만큼 위에 둔다")]
        public float ceilingMargin = 2f;

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();
        private void LateUpdate() { if (Application.isPlaying) Apply(); } // 편집 중에는 OnValidate·OnEnable 때만 (씬이 계속 변경됨으로 표시되지 않게)

        /// <summary>현재 화면 비율에서 카메라가 보여 주는 월드 범위</summary>
        public Rect ViewRect
        {
            get
            {
                if (cam == null) return arena;
                float h = cam.orthographicSize, w = h * cam.aspect;
                Vector2 c = cam.transform.position;
                return new Rect(c.x - w, c.y - h, w * 2f, h * 2f);
            }
        }

        public void Apply()
        {
            if (cam != null)
            {
                cam.orthographic = true;
                float aspect = Mathf.Max(0.1f, cam.aspect);
                float arenaAspect = arena.width / arena.height;
                // arena 전체가 들어오는 최소 크기 (contain)
                cam.orthographicSize = aspect >= arenaAspect ? arena.height * 0.5f : arena.width / aspect * 0.5f;
                cam.transform.position = new Vector3(arena.center.x, arena.center.y, -10f);
            }
            LayoutBackdrop();
            LayoutBounds();
        }

        private void LayoutBackdrop()
        {
            if (backdropImage == null || backdropImage.sprite == null) return;
            var s = backdropImage.sprite;
            float spriteW = s.rect.width / s.pixelsPerUnit;
            float scale = backdropWidth / spriteW;
            float h = s.rect.height / s.pixelsPerUnit * scale;
            Vector2 c = arena.center + backdropOffset;
            var t = backdropImage.transform;
            t.localScale = new Vector3(scale, scale, 1f);
            // 피벗과 무관하게 가운데 정렬
            Vector2 pivotOff = (s.pivot - s.rect.size * 0.5f) / s.pixelsPerUnit * scale;
            t.position = new Vector3(c.x + pivotOff.x, c.y + pivotOff.y, 5f);

            const float far = 400f;
            if (skyFill != null) Fill(skyFill, new Vector2(c.x, c.y + h * 0.5f + far * 0.5f - 0.01f), far);
            if (groundFill != null) Fill(groundFill, new Vector2(c.x, c.y - h * 0.5f - far * 0.5f + 0.01f), far);
            // 이미지 맨 윗줄·아랫줄은 색이 고르지 않아 단색 채움과 경계선이 생긴다 → 이미지 안쪽 띠를 채움색으로 서서히 덮는다
            float sf = Mathf.Clamp(skyFade, 0f, h * 0.5f), gf = Mathf.Clamp(groundFade, 0f, h * 0.5f);
            if (skyBlend != null) { skyBlend.enabled = sf > 0.001f; if (skyFill != null) skyBlend.color = skyFill.color; Fill(skyBlend, new Vector2(c.x, c.y + h * 0.5f - sf * 0.5f), new Vector2(backdropWidth + 0.02f, sf)); }
            if (groundBlend != null) { groundBlend.enabled = gf > 0.001f; groundBlend.flipY = true; if (groundFill != null) groundBlend.color = groundFill.color; Fill(groundBlend, new Vector2(c.x, c.y - h * 0.5f + gf * 0.5f), new Vector2(backdropWidth + 0.02f, gf)); }
        }

        private static void Fill(SpriteRenderer r, Vector2 center, float size) => Fill(r, center, new Vector2(size, size));

        private static void Fill(SpriteRenderer r, Vector2 center, Vector2 size)
        {
            if (r.sprite == null) return;
            var s = r.sprite;
            float sw = s.rect.width / s.pixelsPerUnit, sh = s.rect.height / s.pixelsPerUnit;
            r.transform.localScale = new Vector3(size.x / sw, size.y / sh, 1f);
            Vector2 pivotOff = (s.pivot - s.rect.size * 0.5f) / s.pixelsPerUnit;
            r.transform.position = new Vector3(center.x + pivotOff.x * size.x / sw, center.y + pivotOff.y * size.y / sh, 5f);
        }

        private void LayoutBounds()
        {
            const float t = 2f, tall = 200f;
            if (wallLeft != null) { wallLeft.transform.position = new Vector3(arena.xMin - t * 0.5f, arena.center.y, 0f); wallLeft.size = new Vector2(t, tall); }
            if (wallRight != null) { wallRight.transform.position = new Vector3(arena.xMax + t * 0.5f, arena.center.y, 0f); wallRight.size = new Vector2(t, tall); }
            if (ceiling != null) { ceiling.transform.position = new Vector3(arena.center.x, arena.yMax + ceilingMargin + t * 0.5f, 0f); ceiling.size = new Vector2(arena.width + t * 2f, t); }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(arena.center, arena.size);
        }
    }
}
