using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 방 뒤에 까는 배경 이미지 한 장 (하늘·먼 유적·큰 나무가 한 그림에 들어간 것).
    /// 씬의 오브젝트에 붙여 두고 Inspector 에서 이미지·위치·크기를 바꾼다. GameRoot 가 방을 만들 때 Attach 한다.
    /// - 붙는 순간 그 방의 임시 배경(BgFar·Bg·Deco 도형)은 숨긴다.
    /// - 카메라를 일부(parallax)만 따라가 멀리 있는 느낌을 준다.
    /// - 이미지 위·아래 바깥은 하늘색·땅 안개색으로 채우고, 경계는 그라데이션으로 섞어 카메라가 움직여도 끝이 드러나지 않게 한다.
    /// 플레이 중에 값을 바꿔도 바로 반영된다.
    /// </summary>
    [DefaultExecutionOrder(100)] // CameraRig(카메라 이동) 다음에 위치를 맞춘다
    public class RoomBackdrop : MonoBehaviour
    {
        [Header("배경 이미지")]
        [Tooltip("배경 PNG (Sprite). 교체하면 플레이 중에도 바로 바뀐다")]
        public Sprite image;
        [Tooltip("이미지 가로 폭 (월드 유닛). 세로는 이미지 비율로 정해진다. 화면 폭(줌 7 ≈ 25, 보스 줌 9 ≈ 32)보다 넓어야 좌우 끝이 보이지 않는다")]
        public float width = 34f;
        [Tooltip("카메라가 방 가운데 있을 때 이미지 중심의 위치 (방 중심 기준, 월드 유닛)")]
        public Vector2 offset = Vector2.zero;
        [Tooltip("카메라를 따라가는 정도. 0 = 방에 고정, 1 = 화면에 고정. 클수록 멀리 있어 보인다")]
        [Range(0f, 1f)] public float parallax = 0.6f;
        public Color tint = Color.white;

        [Header("이미지 바깥 채움 (경계 숨김)")]
        [Tooltip("이미지 위쪽 바깥을 채우는 하늘색 (이미지 맨 위 줄 평균색)")]
        public Color skyFill = new Color32(0xD9, 0xEC, 0xEB, 0xFF);
        [Tooltip("이미지 아래쪽 바깥을 채우는 땅 안개색 (이미지 맨 아래 줄 평균색)")]
        public Color groundFill = new Color32(0xAD, 0xB8, 0xAA, 0xFF);
        [Tooltip("위 경계를 섞는 높이 (이미지 안쪽, 월드 유닛)")]
        public float skyFade = 1f;
        [Tooltip("아래 경계를 섞는 높이 (이미지 안쪽, 월드 유닛). 잘린 나무 밑동을 안개로 덮는다")]
        public float groundFade = 3f;

        [Header("정렬")]
        [Tooltip("지형(6)·플레이어(-4~7)보다 뒤. 예전 임시 배경 BgFar 와 같은 값")]
        public int sortingOrder = -30;

        public Room AttachedRoom { get; private set; }

        private Transform root;
        private SpriteRenderer img, sky, ground, skyBlend, groundBlend;

        /// <summary>방에 배경을 붙이고 그 방의 임시 배경 도형을 숨긴다.</summary>
        public void Attach(Room room)
        {
            AttachedRoom = room;
            if (room != null) room.SetTempBackgroundVisible(image == null);
            EnsureBuilt();
            Apply();
            UpdatePosition();
        }

        private void EnsureBuilt()
        {
            if (root != null) return;
            root = new GameObject("Backdrop_Runtime").transform;
            root.SetParent(transform, false);
            img = Art.Quad(root, "Image", null, Color.white, Vector2.one, sortingOrder);
            sky = Art.Quad(root, "SkyFill", Art.Square, skyFill, Vector2.one, sortingOrder);
            ground = Art.Quad(root, "GroundFill", Art.Square, groundFill, Vector2.one, sortingOrder);
            skyBlend = Art.Quad(root, "SkyBlend", Gradient, skyFill, Vector2.one, sortingOrder + 1);
            groundBlend = Art.Quad(root, "GroundBlend", Gradient, groundFill, Vector2.one, sortingOrder + 1);
            skyBlend.flipY = true; // 위로 갈수록 진해짐
        }

        private static Sprite Gradient => Art.Gradient;

        /// <summary>Inspector 값을 렌더러에 반영 (크기·색·정렬)</summary>
        private void Apply()
        {
            if (root == null) return;
            bool show = AttachedRoom != null && image != null;
            root.gameObject.SetActive(show);
            if (!show) return;

            float w = Mathf.Max(1f, width);
            float h = w * image.rect.height / image.rect.width;
            float spriteW = image.rect.width / image.pixelsPerUnit;
            img.sprite = image;
            img.color = tint;
            img.sortingOrder = sortingOrder;
            img.transform.localScale = new Vector3(w / spriteW, w / spriteW, 1f);
            img.transform.localPosition = (Vector3)(-(image.rect.size * 0.5f - image.pivot) / image.pixelsPerUnit * (w / spriteW)); // 피벗과 무관하게 가운데 정렬

            const float far = 400f; // 화면 밖까지 충분히
            sky.color = skyFill;
            sky.sortingOrder = sortingOrder;
            sky.transform.localScale = new Vector3(far, far, 1f);
            sky.transform.localPosition = new Vector3(0f, h * 0.5f + far * 0.5f - 0.01f, 0f);
            ground.color = groundFill;
            ground.sortingOrder = sortingOrder;
            ground.transform.localScale = new Vector3(far, far, 1f);
            ground.transform.localPosition = new Vector3(0f, -h * 0.5f - far * 0.5f + 0.01f, 0f);

            float sf = Mathf.Clamp(skyFade, 0f, h * 0.5f), gf = Mathf.Clamp(groundFade, 0f, h * 0.5f);
            skyBlend.enabled = sf > 0.001f;
            skyBlend.color = skyFill;
            skyBlend.sortingOrder = sortingOrder + 1;
            skyBlend.transform.localScale = new Vector3(w + 0.02f, Mathf.Max(sf, 0.001f) / 64f, 1f);
            skyBlend.transform.localPosition = new Vector3(0f, h * 0.5f - sf * 0.5f, 0f);
            groundBlend.enabled = gf > 0.001f;
            groundBlend.color = groundFill;
            groundBlend.sortingOrder = sortingOrder + 1;
            groundBlend.transform.localScale = new Vector3(w + 0.02f, Mathf.Max(gf, 0.001f) / 64f, 1f);
            groundBlend.transform.localPosition = new Vector3(0f, -h * 0.5f + gf * 0.5f, 0f);
        }

        private void UpdatePosition()
        {
            if (root == null || AttachedRoom == null) return;
            Vector2 center = AttachedRoom.InnerBounds.center;
            var cam = CameraRig.I != null ? CameraRig.I.transform : (Camera.main != null ? Camera.main.transform : null);
            Vector2 camPos = cam != null ? (Vector2)cam.position : center;
            Vector2 p = center + offset + (camPos - center) * parallax;
            root.position = new Vector3(p.x, p.y, 6f);
        }

        private void LateUpdate()
        {
            if (AttachedRoom == null)
            {
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }
            UpdatePosition();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || root == null) return;
            if (AttachedRoom != null) AttachedRoom.SetTempBackgroundVisible(image == null);
            Apply();
            UpdatePosition();
        }
    }
}
