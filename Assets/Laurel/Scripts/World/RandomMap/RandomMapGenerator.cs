using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laurel
{
    /// <summary>
    /// 랜덤 맵 씬(RandomMap_Test)의 생성기. 씬이 열릴 때마다(Start) 시드를 정해 RandomMapAlgorithm 으로 맵을 만들고
    /// 기존 32px Rule Tile 로 Tilemap 을 칠한 뒤, 플레이어·복귀 포탈·카메라·배경을 배치한다.
    /// - 시드: 포탈이 넘겨 준 시드(PortalTravel.PendingSeed) → 없으면 설정의 고정 시드 또는 새 시드
    /// - 다시 만들 때(Regenerate) 이전 타일·생성 오브젝트를 모두 지운다. 씬을 떠나면 씬과 함께 사라진다
    /// - 카메라: 기존 CameraRig 로 플레이어를 따라가고 맵 범위 밖은 비추지 않는다
    /// </summary>
    [DefaultExecutionOrder(100)] // CameraRig(카메라 이동) 다음에 배경 위치를 맞춘다
    public class RandomMapGenerator : MonoBehaviour
    {
        public RandomMapSettings settings;
        public Tilemap tilemap;
        public TileBase terrainTile;
        [Tooltip("씬에 있는 플레이어 (ProceduralCharacter 프리팹 인스턴스). 비우면 씬에서 찾는다")]
        public Transform player;
        public ScenePortal returnPortal;
        [Tooltip("비워 두면 Main Camera 에 실행 중에 붙인다. CameraRig 는 Fx.cs 안에 있어(파일명≠클래스명) 씬에 저장되지 않으므로 본편 GameRoot 처럼 런타임에 붙인다")]
        public CameraRig cameraRig;
        [Tooltip("튜토리얼 보스맵과 같은 크기 (화면 세로 절반, 유닛). 플레이어 키 ≈ 화면의 4.6%")]
        public float cameraSize = 10.5f;
        [Tooltip("맵 위쪽 보이지 않는 천장 (플레이어가 화면 밖으로 날아가지 않게)")]
        public BoxCollider2D ceiling;

        [Header("배경 (1스테이지 배경, 카메라를 일부 따라가는 원경)")]
        public Transform backdropRoot;
        public SpriteRenderer backdropImage, skyFill, groundFill, skyBlend, groundBlend;
        [Range(0f, 1f)] public float parallax = 0.8f;
        public float skyFade = 1.5f, groundFade = 2f;

        [Header("화면 밖 채움 (Rule Tile 가장자리가 보이지 않게 맵 바깥까지 지형을 이어 그린다)")]
        public int outsidePadding = 6;

        [Header("결과 (읽기 전용)")]
        public int lastSeed;
        public int generationCount;
        public RandomMapData Data { get; private set; }

        private float backdropWidth;

        private void Start()
        {
            if (player == null)
            {
                var c = FindAnyObjectByType<Procedural2D.ProceduralCharacterController>();
                if (c != null) player = c.transform;
            }
            if (cameraRig == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    cameraRig = cam.GetComponent<CameraRig>();
                    if (cameraRig == null) cameraRig = cam.gameObject.AddComponent<CameraRig>();
                }
            }
            int seed;
            if (PortalTravel.PendingSeed.HasValue) { seed = PortalTravel.PendingSeed.Value; PortalTravel.PendingSeed = null; }
            else seed = settings != null && settings.useFixedSeed ? settings.fixedSeed : PortalTravel.NewSeed();
            Generate(seed);
        }

        [ContextMenu("새 시드로 다시 생성 (Play 중)")]
        private void RegenerateNewSeed() { if (Application.isPlaying) Generate(PortalTravel.NewSeed()); }

        [ContextMenu("같은 시드로 다시 생성 (Play 중)")]
        private void RegenerateSameSeed() { if (Application.isPlaying) Generate(lastSeed); }

        /// <summary>이전 맵을 지우고 seed 로 새 맵을 만든다</summary>
        public void Generate(int seed)
        {
            Clear();
            lastSeed = seed;
            generationCount++;
            var d = RandomMapAlgorithm.Generate(settings, seed);
            Data = d;
            Paint(d);

            // 플레이어: 시작 칸 위 (벽·지형 안이 아님을 알고리즘이 보장)
            ScenePortal.PlaceAtPortal(player, new Vector2(d.spawn.x + 0.5f, d.spawn.y));
            if (player != null)
            {
                var ctrl = player.GetComponent<Procedural2D.ProceduralCharacterController>();
                if (ctrl != null) ctrl.inputEnabled = true;
            }
            // 복귀 포탈: 시작 지점과 서로 오갈 수 있는 가장 먼 평지
            if (returnPortal != null)
            {
                bool ok = d.portal.x >= 0;
                returnPortal.gameObject.SetActive(ok);
                if (ok) returnPortal.transform.position = new Vector3(d.portal.x + 0.5f, d.portal.y, 0f);
                else Debug.LogWarning($"[RandomMap] 시드 {seed}: 복귀 포탈 자리를 찾지 못했습니다. 시작 지점 옆에 둡니다.");
                if (!ok) { returnPortal.gameObject.SetActive(true); returnPortal.transform.position = new Vector3(d.spawn.x + 3.5f, d.spawn.y, 0f); }
            }

            var bounds = new Rect(0f, 0f, d.width, d.height);
            if (ceiling != null)
            {
                ceiling.transform.position = new Vector3(d.width * 0.5f, d.height + 1f, 0f);
                ceiling.size = new Vector2(d.width + 20f, 2f);
            }
            if (cameraRig != null)
            {
                cameraRig.Init();
                cameraRig.target = player;
                cameraRig.followY = true;
                cameraRig.playSize = cameraSize;
                cameraRig.Setup(bounds, cameraSize, skyFill != null ? skyFill.color : Color.gray);
            }
            LayoutBackdrop();
            Debug.Log($"[RandomMap] 생성 #{generationCount} 시드 {seed} (사용 시드 {d.usedSeed}, 시도 {d.attempts}) 크기 {d.width}×{d.height} 발판 {d.platforms.Count} (도달 불가로 제거 {d.removedPlatforms}) 시작 {d.spawn} 포탈 {d.portal}");
        }

        /// <summary>이전에 만든 타일·데이터를 지운다</summary>
        public void Clear()
        {
            if (tilemap != null) tilemap.ClearAllTiles();
            Data = null;
        }

        private void Paint(RandomMapData d)
        {
            if (tilemap == null || terrainTile == null) return;
            int pad = Mathf.Max(2, outsidePadding);
            int W = d.width, H = d.height;
            var positions = new System.Collections.Generic.List<Vector3Int>();
            for (int x = -pad; x < W + pad; x++)
                for (int y = -pad; y < H + pad; y++)
                {
                    bool solid;
                    if (y < 0) solid = true;                                  // 바닥 아래
                    else if (x < 0 || x >= W) solid = true;                   // 외벽 바깥
                    else if (y >= H) solid = x < settings.wallThickness || x >= W - settings.wallThickness; // 외벽은 위로 더 이어 그림
                    else solid = d.solid[x, y];
                    if (solid) positions.Add(new Vector3Int(x, y, 0));
                }
            var tiles = new TileBase[positions.Count];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = terrainTile;
            tilemap.SetTiles(positions.ToArray(), tiles);
            tilemap.RefreshAllTiles();
            var tc = tilemap.GetComponent<TilemapCollider2D>();
            if (tc != null) tc.ProcessTilemapChanges();
            var cc = tilemap.GetComponent<CompositeCollider2D>();
            if (cc != null) cc.GenerateGeometry();
        }

        // ── 배경: 카메라를 parallax 만큼 따라가는 1스테이지 배경. 화면이 맵 끝까지 가도 이미지가 끊기지 않을 폭으로 늘린다 ──

        private void LayoutBackdrop()
        {
            if (backdropRoot == null || backdropImage == null || backdropImage.sprite == null || Data == null) return;
            var cam = cameraRig != null ? cameraRig.Cam : Camera.main;
            float halfH = cameraSize, halfW = cameraSize * Mathf.Max(32f / 9f, cam != null ? cam.aspect : 1.78f);
            // 카메라 중심 이동 범위 (맵 중심 기준) × (1 - parallax) + 화면 절반 = 이미지가 덮어야 하는 반폭
            float travelX = Mathf.Max(0f, Data.width * 0.5f - halfW);
            backdropWidth = 2f * (travelX * (1f - parallax) + halfW) + 4f;
            var s = backdropImage.sprite;
            float spriteW = s.rect.width / s.pixelsPerUnit;
            float scale = backdropWidth / spriteW;
            float h = s.rect.height / s.pixelsPerUnit * scale;
            backdropImage.transform.localScale = new Vector3(scale, scale, 1f);
            Vector2 pivotOff = (s.pivot - s.rect.size * 0.5f) / s.pixelsPerUnit * scale;
            backdropImage.transform.localPosition = new Vector3(pivotOff.x, pivotOff.y, 0f);
            const float far = 400f;
            Fill(skyFill, new Vector2(0f, h * 0.5f + far * 0.5f - 0.01f), new Vector2(far, far));
            Fill(groundFill, new Vector2(0f, -h * 0.5f - far * 0.5f + 0.01f), new Vector2(far, far));
            float sf = Mathf.Clamp(skyFade, 0f, h * 0.5f), gf = Mathf.Clamp(groundFade, 0f, h * 0.5f);
            if (skyBlend != null) { if (skyFill != null) skyBlend.color = skyFill.color; Fill(skyBlend, new Vector2(0f, h * 0.5f - sf * 0.5f), new Vector2(backdropWidth + 0.02f, sf)); }
            if (groundBlend != null) { groundBlend.flipY = true; if (groundFill != null) groundBlend.color = groundFill.color; Fill(groundBlend, new Vector2(0f, -h * 0.5f + gf * 0.5f), new Vector2(backdropWidth + 0.02f, gf)); }
            UpdateBackdrop();
        }

        private static void Fill(SpriteRenderer r, Vector2 localCenter, Vector2 size)
        {
            if (r == null || r.sprite == null) return;
            var s = r.sprite;
            float sw = s.rect.width / s.pixelsPerUnit, sh = s.rect.height / s.pixelsPerUnit;
            r.transform.localScale = new Vector3(size.x / sw, size.y / sh, 1f);
            Vector2 pivotOff = (s.pivot - s.rect.size * 0.5f) / s.pixelsPerUnit;
            r.transform.localPosition = new Vector3(localCenter.x + pivotOff.x * size.x / sw, localCenter.y + pivotOff.y * size.y / sh, 0f);
        }

        private void UpdateBackdrop()
        {
            if (backdropRoot == null || Data == null) return;
            var cam = cameraRig != null ? cameraRig.transform : (Camera.main != null ? Camera.main.transform : null);
            Vector2 center = new Vector2(Data.width * 0.5f, cameraSize + 1f); // 지면 근처가 이미지 아래쪽에 오도록
            Vector2 camPos = cam != null ? (Vector2)cam.position : center;
            Vector2 p = center + (camPos - center) * parallax;
            backdropRoot.position = new Vector3(p.x, p.y, 5f);
        }

        private void LateUpdate() => UpdateBackdrop();
    }
}
