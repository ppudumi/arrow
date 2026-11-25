using System.Collections.Generic;
using pjs;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    /// <summary>
    /// 1스테이지 씬(Laurel_Stage1) 컨트롤러:
    /// - 랜덤 노드 지도 생성 (0층: 무조건 전투, 중간: 전투/물음표/보상/엘리트, 마지막: 보스)
    /// - 노드에 맞춰 랜덤 타일맵 생성 및 플레이어·미노타우르 배치
    /// - 적 처치 시 맵 끝에 포탈 개방 → 포탈 진입 시 Map_sheet 기반 지도 자동 표시
    /// - 언제든 M키로 지도 열기/닫기
    /// </summary>
    public class Stage1Controller : MonoBehaviour
    {
        public RandomMapGenerator mapGenerator;
        public MinotourEnemy minotourTemplate;
        public WarlockEnemy warlockTemplate;
        public Texture2D mapSheet;
        [Tooltip("Assets/pjs/Sprites/caravan.png 슬라이스: [0]마차1, [1]발판, [2]마차2")]
        public Sprite[] shopSprites;
        [Header("상점 룸 (씬 배치 오브젝트)")]
        public GameObject shopRoom;
        public Transform shopSpawnPoint;
        public Transform shopPortalPoint;
        public Transform[] shopStandPoints;

        public MapData Map { get; private set; }
        public MapNode CurrentNode { get; private set; }
        public bool MapOpen { get; private set; }
        public bool PortalOpen { get; private set; }
        public bool IsLoading { get; private set; }
        public int StageLoop { get; private set; }

        private GameRoot root;
        private readonly List<Enemy> activeEnemies = new List<Enemy>();
        private GameObject portalGo;
        private SpriteRenderer portalGlow;
        private Vector3 portalPos;
        private GameObject arrowChestGo;
        private Vector3 arrowChestPos;
        private bool arrowChestAvailable;
        private GameObject relicChestGo;
        private Vector3 relicChestPos;
        private bool relicChestAvailable;
        private RewardData cachedArrowReward;
        private RewardData cachedRelicReward;
        private class ShopStandItem
        {
            public Vector3 pos;
            public ShopOffer offer;
            public SpriteRenderer iconSr;
        }

        private GameObject shopRoot;
        private Vector3 leftWagonPos;
        private Vector3 rightWagonPos;
        private Vector3 rightBubblePos;
        private string bubbleText;
        private float bubbleTimer;
        private float bubbleElapsed;
        private GUIStyle bubbleStyle;
        private Texture2D shopParchmentTex;
        private GUIStyle shopTitleStyle, shopPriceStyle, shopDescStyle, shopHintStyle;
        private bool shopAvailable;
        private readonly List<ShopStandItem> shopStands = new List<ShopStandItem>();
        private static Sprite shopArrowSprite;
        private bool canAdvanceFromMap;
        private bool nodeHadCombat;
        private string loadingText = "맵 생성 중...";
        private Coroutine loadRoutine;
        private GUIStyle titleStyle, labelStyle, promptStyle, loadingStyle;
        private bool confirmArrowLossOpen;
        private MapNode pendingNextNode;
        private bool pendingBossClear;
        private GUIStyle warnTitleStyle, warnBodyStyle, warnBtnStyle;
        private float mapRevealT;
        private const float MapRevealDuration = 0.45f;

        // Map_sheet.png (512x512) UV 좌표
        private static readonly Rect UvParchment = Uv(8, 326, 297, 174);
        private static readonly Rect UvQuestion  = Uv(317, 468, 22, 24);
        private static readonly Rect UvCombat    = Uv(348, 470, 21, 21);
        private static readonly Rect UvReward    = Uv(380, 472, 22, 20);
        private static readonly Rect UvElite     = Uv(413, 471, 22, 21);
        private static readonly Rect UvBoss      = Uv(443, 470, 20, 23);
        private static readonly Rect UvShop      = Uv(474, 470, 22, 21);
        private static readonly Rect UvDot       = Uv(317, 446, 8, 7);
        private static readonly Rect UvSun       = Uv(309, 372, 60, 60);
        private static readonly Rect[] UvRolls   = { Uv(12, 115, 80, 199), Uv(92, 114, 69, 199), Uv(165, 115, 58, 198), Uv(233, 116, 48, 198) };
        private static readonly Vector2[] RollSizes = { new Vector2(80f, 199f), new Vector2(69f, 199f), new Vector2(58f, 198f), new Vector2(48f, 198f) };

        private static Rect Uv(float x, float y, float w, float h) => new Rect(x / 512f, y / 512f, w / 512f, h / 512f);

        private void Awake()
        {
            if (shopRoom != null) shopRoom.SetActive(false);
        }

        public void BeginStage(GameRoot g) => BeginStage(g, true);

        public void BeginStage(GameRoot g, bool resetLoop)
        {
            root = g;
            if (resetLoop) StageLoop = 0;
            int seed = PortalTravel.NewSeed();
            Map = MapData.GenerateStage1(seed);
            CurrentNode = Map.nodes[0];
            CurrentNode.visited = true;
            EnterNode(CurrentNode);
        }

        private void CompleteStage1AndLoop()
        {
            StageLoop++;
            if (root != null)
            {
                if (root.Run != null) root.Run.stage = 1 + StageLoop;
                root.Avatar?.Heal(root.Avatar.MaxHp * DB.Balance.economy.stageClearHealRatio, "스테이지 클리어");
                root.AddNectar(DB.Balance.nectar.stageClearBonus[0], $"1스테이지 클리어 (+{StageLoop})");
                root.ShowToast($"1스테이지 클리어! 강화된 1스테이지 (강화 +{StageLoop})를 시작합니다.");
            }
            BeginStage(root, false);
        }

        public void DebugJumpToBoss()
        {
            if (Map == null || Map.nodes == null || Map.nodes.Count == 0) return;
            MapNode boss = null;
            for (int i = 0; i < Map.nodes.Count; i++)
                if (Map.nodes[i].type == NodeType.Boss) { boss = Map.nodes[i]; break; }
            if (boss != null)
            {
                root?.ShowToast("블리츠: 1스테이지 보스방으로 이동");
                EnterNode(boss);
            }
        }

        private void EnterNode(MapNode node)
        {
            if (loadRoutine != null) StopCoroutine(loadRoutine);
            loadRoutine = StartCoroutine(LoadNodeRoutine(node));
        }

        private System.Collections.IEnumerator LoadNodeRoutine(MapNode node)
        {
            IsLoading = true;
            loadingText = "지형 생성 및 플레이어 배치 중...";
            CurrentNode = node;
            node.visited = true;
            SetMapOpen(false, false);
            PortalOpen = false;
            ClearSpawnedEnemies();
            ClearShopProps();

            if (root != null && root.Avatar != null) root.Avatar.SetInputEnabled(false);
            if (root != null && root.PlayerGO != null) root.PlayerGO.SetActive(false);

            // 로딩 화면을 먼저 1프레임 그린 뒤 지형·적·플레이어 배치 수행
            yield return null;

            if (mapGenerator == null) mapGenerator = FindAnyObjectByType<RandomMapGenerator>();
            if (node.type == NodeType.Shop && shopRoom != null)
            {
                if (mapGenerator != null)
                {
                    mapGenerator.Clear();
                    if (root != null && root.PlayerGO != null) mapGenerator.player = root.PlayerGO.transform;
                }
                shopRoom.SetActive(true);
                Vector2 spawnPos = shopSpawnPoint != null ? (Vector2)shopSpawnPoint.position : new Vector2(2.5f, 2f);
                if (root != null && root.PlayerGO != null)
                    ScenePortal.PlaceAtPortal(root.PlayerGO.transform, spawnPos);
                if (root != null && root.Room != null)
                    root.Room.InitStaticArena(RoomTheme.FromStage(DB.Stage(1)), 36f, 16f, spawnPos);
                if (CameraRig.I != null)
                {
                    CameraRig.I.target = root != null && root.PlayerGO != null ? root.PlayerGO.transform : null;
                    CameraRig.I.followY = true;
                    CameraRig.I.playSize = DB.Balance.world.cameraSize;
                    CameraRig.I.lookUp = 3.2f;
                    CameraRig.I.lookAhead = 2.5f;
                    CameraRig.I.fallLookDown = 2.5f;
                    CameraRig.I.fallSpeedThreshold = 8f;
                    CameraRig.I.lookSmoothing = 10f;
                    CameraRig.I.followSmoothing = 10f;
                    CameraRig.I.cursorLookAhead = 1.4f;
                    CameraRig.I.cursorLookRange = 12f;
                    CameraRig.I.cursorLookVerticalWeight = 1f;
                    CameraRig.I.Setup(new Rect(0f, 0f, 36f, 16f), CameraRig.I.PlaySize, Color.black);
                }
                if (mapGenerator != null) mapGenerator.PrepareShopBackdrop(36, 16);
                SetupShopScenePortal();
                BindSceneShopOffers();
                OpenPortal();
            }
            else
            {
                if (shopRoom != null) shopRoom.SetActive(false);
                if (mapGenerator != null)
                {
                    if (root != null && root.PlayerGO != null) mapGenerator.player = root.PlayerGO.transform;
                    mapGenerator.Generate(node.seed);
                    var d = mapGenerator.Data;
                    if (d != null && root != null && root.Room != null)
                    {
                        root.Room.InitStaticArena(RoomTheme.FromStage(DB.Stage(1)), d.width, d.height, new Vector2(d.spawn.x + 0.5f, d.spawn.y));
                        if (CameraRig.I != null)
                        {
                            CameraRig.I.target = root.PlayerGO.transform;
                            CameraRig.I.followY = true;
                            CameraRig.I.playSize = DB.Balance.world.cameraSize;
                            CameraRig.I.lookUp = 3.2f;
                            CameraRig.I.lookAhead = 2.5f;
                            CameraRig.I.fallLookDown = 2.5f;
                            CameraRig.I.fallSpeedThreshold = 8f;
                            CameraRig.I.lookSmoothing = 10f;
                            CameraRig.I.followSmoothing = 10f;
                            CameraRig.I.cursorLookAhead = 1.4f;
                            CameraRig.I.cursorLookRange = 12f;
                            CameraRig.I.cursorLookVerticalWeight = 1f;
                            CameraRig.I.Setup(new Rect(0f, 0f, d.width, d.height), CameraRig.I.PlaySize, Color.black);
                        }
                        SetupPortal(d);
                        SpawnNodeEncounter(node, d);
                    }
                }
            }

            Physics2D.SyncTransforms();
            if (root != null && root.PlayerGO != null) root.PlayerGO.SetActive(true);
            loadingText = "배치 완료";
            yield return new WaitForSecondsRealtime(0.2f);

            if (root != null && root.Avatar != null) root.Avatar.SetInputEnabled(true);
            IsLoading = false;
            loadRoutine = null;
            string loopTag = StageLoop > 0 ? $" (강화 +{StageLoop})" : "";
            root?.ShowToast($"1스테이지{loopTag} — {MapNode.TypeName(node.type)} ({node.layer + 1}구역)");
        }

        private void SetupPortal(RandomMapData d)
        {
            if (portalGo == null)
            {
                portalGo = new GameObject("Stage1_EndPortal");
                Art.Quad(portalGo.transform, "Frame", Art.Square, new Color32(0x2A, 0x1E, 0x14, 0xFF), new Vector2(1.8f, 2.6f), 12, new Vector3(0f, 1.3f, 0f));
                portalGlow = Art.Quad(portalGo.transform, "Core", Art.Soft, new Color(0.45f, 0.92f, 1f, 0.9f), new Vector2(1.3f, 2.1f), 13, new Vector3(0f, 1.3f, 0f));
            }
            Vector2 p = d.portal.x >= 0 ? new Vector2(d.portal.x + 0.5f, d.portal.y) : new Vector2(d.spawn.x + 12f, d.spawn.y);
            portalPos = new Vector3(p.x, p.y, -0.05f);
            portalGo.transform.position = portalPos;
            portalGo.SetActive(false);

            if (arrowChestGo == null)
            {
                arrowChestGo = new GameObject("Stage1_ArrowChest");
                Art.Quad(arrowChestGo.transform, "Base", Art.Square, new Color32(0x5A, 0x38, 0x18, 0xFF), new Vector2(1.05f, 0.72f), 12, new Vector3(0f, 0.36f, 0f));
                Art.Quad(arrowChestGo.transform, "Trim", Art.Square, new Color32(0xE8, 0xB8, 0x42, 0xFF), new Vector2(1.12f, 0.16f), 13, new Vector3(0f, 0.64f, 0f));
                Art.Quad(arrowChestGo.transform, "Glow", Art.Soft, new Color(1f, 0.86f, 0.32f, 0.85f), new Vector2(1.5f, 1.3f), 11, new Vector3(0f, 0.45f, 0f));
            }
            arrowChestPos = new Vector3(p.x - 2.3f, p.y, -0.05f);
            arrowChestGo.transform.position = arrowChestPos;
            arrowChestGo.SetActive(false);
            arrowChestAvailable = false;
            cachedArrowReward = null;

            if (relicChestGo == null)
            {
                relicChestGo = new GameObject("Stage1_RelicChest");
                Art.Quad(relicChestGo.transform, "Base", Art.Square, new Color32(0x3E, 0x24, 0x5A, 0xFF), new Vector2(1.05f, 0.72f), 12, new Vector3(0f, 0.36f, 0f));
                Art.Quad(relicChestGo.transform, "Trim", Art.Square, new Color32(0xD4, 0x8C, 0xFF, 0xFF), new Vector2(1.12f, 0.16f), 13, new Vector3(0f, 0.64f, 0f));
                Art.Quad(relicChestGo.transform, "Glow", Art.Soft, new Color(0.82f, 0.55f, 1f, 0.85f), new Vector2(1.5f, 1.3f), 11, new Vector3(0f, 0.45f, 0f));
            }
            relicChestPos = new Vector3(p.x - 4.1f, p.y, -0.05f);
            relicChestGo.transform.position = relicChestPos;
            relicChestGo.SetActive(false);
            relicChestAvailable = false;
            cachedRelicReward = null;
        }

        private void SpawnNodeEncounter(MapNode node, RandomMapData d)
        {
            nodeHadCombat = false;
            if (node.type == NodeType.Shop)
            {
                SpawnShopProps(d);
                OpenPortal();
                return;
            }
            if (node.type == NodeType.Reward)
            {
                DropTable.SpawnDrops(new Vector2(d.spawn.x + 6f, d.spawn.y + 1.5f), 25, 30, 5);
                OpenPortal();
                return;
            }
            if (node.type == NodeType.Question)
            {
                var rng = new System.Random(node.seed);
                if (rng.Next(2) == 0)
                {
                    DropTable.SpawnDrops(new Vector2(d.spawn.x + 6f, d.spawn.y + 1.5f), 20, 25, 4);
                    root?.Avatar?.Heal(20f, "물음표 보상");
                    OpenPortal();
                    return;
                }
            }

            // 전투 / 엘리트 / 보스 (첫 노드는 무조건 미노타우르 1마리, 이후 노드는 랜덤 배치)
            nodeHadCombat = true;
            var spawnRng = new System.Random(node.seed ^ 0x3A71F);
            int extra = (StageLoop > 0 && node.type != NodeType.Boss && node.layer > 0) ? Mathf.Min(2, StageLoop) : 0;
            int baseCount = node.type == NodeType.Boss ? 1 : (node.layer == 0 ? 2 : (node.type == NodeType.Elite ? 4 : spawnRng.Next(4, 8)));
            int count = baseCount + extra * 2;
            float loopHpMul = 1f + StageLoop * 0.45f;
            float hpMul = 0.5f * (node.type == NodeType.Boss ? 2.2f : (node.type == NodeType.Elite ? 1.5f : 1f)) * loopHpMul;
            float scaleMul = node.type == NodeType.Boss ? 1.35f : (node.type == NodeType.Elite ? 1.15f : 1f);

            for (int i = 0; i < count; i++)
            {
                if (node.type == NodeType.Boss)
                {
                    SpawnMinotour(PickEnemySpawn(d, i, count), hpMul, scaleMul);
                    continue;
                }

                Vector2 pos = PickRandomEnemySpawn(d, spawnRng);
                bool pickWarlock = warlockTemplate != null && (minotourTemplate == null || spawnRng.Next(2) == 0);
                if (pickWarlock) SpawnWarlock(pos, hpMul, scaleMul);
                else SpawnMinotour(pos, hpMul, scaleMul);
            }
        }

        private static Vector2 PickRandomEnemySpawn(RandomMapData d, System.Random rng)
        {
            int minX = Mathf.Clamp(d.spawn.x + 10, 4, d.width - 5);
            int maxX = Mathf.Max(minX + 1, d.width - 4);
            var candidates = new List<Vector2>();
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int x = rng.Next(minX, maxX);
                for (int y = d.height - 2; y >= 1; y--)
                {
                    if (d.solid[x, y - 1] && !d.solid[x, y] && !d.solid[x, y + 1])
                        candidates.Add(new Vector2(x + 0.5f, y + 0.05f));
                }
                if (candidates.Count > 0)
                    return candidates[rng.Next(candidates.Count)];
            }
            return PickEnemySpawn(d, 0, 1);
        }

        private void SpawnWarlock(Vector2 pos, float hpMul, float scaleMul)
        {
            if (warlockTemplate == null) return;
            var go = Instantiate(warlockTemplate.gameObject, pos, Quaternion.identity);
            go.name = "WarlockEnemy";
            go.transform.localScale = Vector3.one * scaleMul;
            go.SetActive(true);
            var w = go.GetComponent<WarlockEnemy>();
            if (w != null)
            {
                w.maxHp *= hpMul;
                activeEnemies.Add(w);
            }
        }

        private static Vector2 PickEnemySpawn(RandomMapData d, int index, int count)
        {
            int minX = Mathf.Min(d.width - 6, d.spawn.x + 14 + index * 10);
            int maxX = Mathf.Max(minX + 1, d.width - 4);
            int x = Mathf.Clamp(minX + (maxX - minX) / (count + 1), 4, d.width - 5);
            for (int y = d.height - 2; y >= 1; y--)
            {
                if (d.solid[x, y - 1] && !d.solid[x, y] && !d.solid[x, y + 1])
                    return new Vector2(x + 0.5f, y + 0.05f);
            }
            return new Vector2(d.spawn.x + 8f, d.spawn.y + 0.5f);
        }

        private void SpawnMinotour(Vector2 pos, float hpMul, float scaleMul)
        {
            if (minotourTemplate == null) return;
            var go = Instantiate(minotourTemplate.gameObject, pos, Quaternion.identity);
            go.name = "MinotourEnemy";
            go.transform.localScale = Vector3.one * scaleMul;
            go.SetActive(true);
            var mino = go.GetComponent<MinotourEnemy>();
            if (mino != null)
            {
                mino.maxHp *= hpMul;
                activeEnemies.Add(mino);
            }
        }

        private void ClearSpawnedEnemies()
        {
            for (int i = 0; i < activeEnemies.Count; i++)
                if (activeEnemies[i] != null) Destroy(activeEnemies[i].gameObject);
            activeEnemies.Clear();
            root?.Room?.ClearEnemiesAndDrops();
        }

        private void OpenPortal()
        {
            if (PortalOpen) return;
            PortalOpen = true;
            if (portalGo != null)
            {
                portalGo.SetActive(true);
                Fx.WaveRing(portalPos + Vector3.up * 1.3f, 1.4f, 0.5f, new Color(0.45f, 0.95f, 1f, 1f));
                Fx.Burst(portalPos + Vector3.up * 1.3f, new Color(0.55f, 0.95f, 1f), 16, 6f, 0.2f, 0.45f);
            }

            if (nodeHadCombat && CurrentNode != null)
            {
                arrowChestAvailable = true;
                if (arrowChestGo != null) arrowChestGo.SetActive(true);
                Fx.WaveRing(arrowChestPos + Vector3.up * 0.5f, 1.1f, 0.45f, new Color(1f, 0.86f, 0.35f, 1f));

                bool hasRelic = CurrentNode.type == NodeType.Elite || CurrentNode.type == NodeType.Boss;
                if (hasRelic)
                {
                    relicChestAvailable = true;
                    if (relicChestGo != null) relicChestGo.SetActive(true);
                    Fx.WaveRing(relicChestPos + Vector3.up * 0.5f, 1.1f, 0.45f, new Color(0.82f, 0.55f, 1f, 1f));
                }

                root?.ShowToast(hasRelic ? "모든 적 처치! 포탈 앞 화살·유물 상자를 여세요." : "모든 적 처치! 포탈 앞 화살 상자를 여세요.");
                return;
            }

            if (CurrentNode != null && CurrentNode.type == NodeType.Reward)
            {
                relicChestAvailable = true;
                if (relicChestGo != null) relicChestGo.SetActive(true);
                Fx.WaveRing(relicChestPos + Vector3.up * 0.5f, 1.1f, 0.45f, new Color(0.82f, 0.55f, 1f, 1f));
                return;
            }

            root?.ShowToast("포탈이 열렸습니다! (포탈에서 W / F키)");
        }

        public void OnStage1RewardTaken(bool isRelic)
        {
            if (isRelic)
            {
                relicChestAvailable = false;
                if (relicChestGo != null) relicChestGo.SetActive(false);
                cachedRelicReward = null;
            }
            else
            {
                arrowChestAvailable = false;
                if (arrowChestGo != null) arrowChestGo.SetActive(false);
                cachedArrowReward = null;
            }
        }

        private void OpenArrowChest()
        {
            if (!arrowChestAvailable || root == null || root.Run == null || CurrentNode == null) return;
            if (cachedArrowReward == null)
            {
                var rng = new System.Random(CurrentNode.seed ^ 0x51A9E1);
                cachedArrowReward = new RewardData
                {
                    title = "화살 상자 — 하나를 선택하세요 (Esc 취소)",
                    arrows = Loot.ArrowChoices(rng, 1, DB.Balance.economy.rewardArrowChoices),
                    relicTaken = true
                };
            }
            root.OpenStage1Reward(cachedArrowReward);
        }

        private void OpenRelicChest()
        {
            if (!relicChestAvailable || root == null || root.Run == null || CurrentNode == null) return;
            if (cachedRelicReward == null)
            {
                var rng = new System.Random(CurrentNode.seed ^ 0x7E11C);
                cachedRelicReward = new RewardData
                {
                    title = "유물 상자 — 하나를 선택하세요 (Esc 취소)",
                    relics = Loot.RelicChoices(rng, root.Run, DB.Balance.economy.rewardRelicChoices),
                    arrowTaken = true
                };
            }
            root.OpenStage1Reward(cachedRelicReward);
        }

        private void SetupShopScenePortal()
        {
            if (portalGo == null)
            {
                portalGo = new GameObject("Stage1_EndPortal");
                Art.Quad(portalGo.transform, "Frame", Art.Square, new Color32(0x2A, 0x1E, 0x14, 0xFF), new Vector2(1.8f, 2.6f), 12, new Vector3(0f, 1.3f, 0f));
                portalGlow = Art.Quad(portalGo.transform, "Core", Art.Soft, new Color(0.45f, 0.92f, 1f, 0.9f), new Vector2(1.3f, 2.1f), 13, new Vector3(0f, 1.3f, 0f));
            }
            Vector3 p = shopPortalPoint != null ? shopPortalPoint.position : new Vector3(33.5f, 2f, 0f);
            portalPos = new Vector3(p.x, p.y, -0.05f);
            portalGo.transform.position = portalPos;
            portalGo.SetActive(false);

            arrowChestAvailable = false;
            if (arrowChestGo != null) arrowChestGo.SetActive(false);
            relicChestAvailable = false;
            if (relicChestGo != null) relicChestGo.SetActive(false);
        }

        private void BindSceneShopOffers()
        {
            ClearShopProps();
            shopRoot = new GameObject("Stage1_ShopItems");
            nodeHadCombat = false;

            var leftWagon = shopRoom != null ? shopRoom.transform.Find("Shop_CaravanLeft") : null;
            var rightWagon = shopRoom != null ? shopRoom.transform.Find("Shop_CaravanRight") : null;
            leftWagonPos = leftWagon != null ? leftWagon.position + new Vector3(3.4f, 0f, 0f) : new Vector3(11.1f, 1.9f, 0f);
            rightWagonPos = rightWagon != null ? rightWagon.position + new Vector3(-2.5f, 0f, 0f) : new Vector3(26.5f, 2.08f, 0f);
            rightBubblePos = rightWagonPos + new Vector3(0f, 3.5f, 0f);
            shopAvailable = true;

            if (root != null)
            {
                root.ResetStage1Shop();
                root.EnsureStage1ShopOffers(CurrentNode != null ? CurrentNode.seed : 1);
            }

            var picks = PickFiveShopOffers();
            int count = shopStandPoints != null ? shopStandPoints.Length : 0;
            for (int i = 0; i < count; i++)
            {
                var stTf = shopStandPoints[i];
                if (stTf == null) continue;
                var pos = stTf.position;
                ShopOffer offer = i < picks.Count ? picks[i] : null;
                SpriteRenderer iconSr = null;
                if (offer != null)
                {
                    var iconGo = new GameObject($"Shop_Item_{i + 1}");
                    iconGo.transform.SetParent(shopRoot.transform, false);
                    iconGo.transform.position = pos + new Vector3(0.35f, 0.75f, -0.02f);
                    iconSr = iconGo.AddComponent<SpriteRenderer>();
                    iconSr.sortingOrder = 12;
                    if (offer.kind == "arrow")
                    {
                        var def = DB.Arrow(offer.arrowCode);
                        iconSr.sprite = GetShopArrowSprite();
                        iconSr.color = def != null ? def.tint : Color.white;
                        iconGo.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
                        iconGo.transform.localScale = new Vector3(1.1f, 1.1f, 1f);
                    }
                    else
                    {
                        iconSr.sprite = Art.Diamond;
                        iconSr.color = offer.kind == "relic"
                            ? DB.RarityColors[DB.Relics[offer.relicId].rarity]
                            : (offer.kind == "heal" ? new Color(0.4f, 0.95f, 0.55f) : new Color(1f, 0.8f, 0.3f));
                        iconGo.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
                    }
                }
                shopStands.Add(new ShopStandItem { pos = pos + new Vector3(0.35f, 0f, 0f), offer = offer, iconSr = iconSr });
            }
        }

        private void ClearShopProps()
        {
            shopAvailable = false;
            bubbleTimer = 0f;
            bubbleText = null;
            shopStands.Clear();
            if (shopRoot != null)
            {
                Destroy(shopRoot);
                shopRoot = null;
            }
        }

        private void SpawnShopProps(RandomMapData d)
        {
            ClearShopProps();
            EnsureShopSprites();

            shopRoot = new GameObject("Stage1_ShopProps");
            float floorY = d.spawn.y + 0.08f;
            const float wagonScale = 2.2f;
            const float standScale = 2.5f;

            // 마차 두 개가 서로를 바라보도록 좌우에 배치 (caravan_0, caravan_2)
            var leftWagon = SpawnShopProp("Shop_CaravanLeft", GetShopSprite(0), new Vector3(8f, floorY, 0.05f), wagonScale, 7, false);
            var rightWagon = SpawnShopProp("Shop_CaravanRight", GetShopSprite(2), new Vector3(24.0f, floorY, 0.05f), wagonScale, 7, true);

            leftWagonPos = leftWagon != null ? leftWagon.transform.position + new Vector3(3.4f, 0f, 0f) : new Vector3(11.4f, floorY, 0f);
            rightWagonPos = rightWagon != null ? rightWagon.transform.position + new Vector3(-2.5f, 0f, 0f) : new Vector3(21.5f, floorY, 0f);
            rightBubblePos = rightWagonPos + new Vector3(0f, 3.5f, 0f);
            shopAvailable = true;

            if (root != null)
            {
                root.ResetStage1Shop();
                root.EnsureStage1ShopOffers(CurrentNode != null ? CurrentNode.seed : 1);
            }

            var picks = PickFiveShopOffers();
            const float startX = 12.6f;
            const float spacing = 2.1f;
            for (int i = 0; i < 5; i++)
            {
                var pos = new Vector3(startX + i * spacing, floorY, 0.03f);
                SpawnShopProp($"Shop_Stand_{i + 1}", GetShopSprite(1), pos, standScale, 8, false);

                ShopOffer offer = i < picks.Count ? picks[i] : null;
                SpriteRenderer iconSr = null;
                if (offer != null)
                {
                    var iconGo = new GameObject($"Shop_Item_{i + 1}");
                    iconGo.transform.SetParent(shopRoot.transform, false);
                    iconGo.transform.position = pos + new Vector3(0.35f, 0.75f, -0.02f);
                    iconSr = iconGo.AddComponent<SpriteRenderer>();
                    iconSr.sortingOrder = 12;
                    if (offer.kind == "arrow")
                    {
                        var def = DB.Arrow(offer.arrowCode);
                        iconSr.sprite = GetShopArrowSprite();
                        iconSr.color = def != null ? def.tint : Color.white;
                        iconGo.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
                        iconGo.transform.localScale = new Vector3(1.1f, 1.1f, 1f);
                    }
                    else
                    {
                        iconSr.sprite = Art.Diamond;
                        iconSr.color = offer.kind == "relic"
                            ? DB.RarityColors[DB.Relics[offer.relicId].rarity]
                            : (offer.kind == "heal" ? new Color(0.4f, 0.95f, 0.55f) : new Color(1f, 0.8f, 0.3f));
                        iconGo.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
                    }
                }

                shopStands.Add(new ShopStandItem
                {
                    pos = pos + new Vector3(0.35f, 0f, 0f),
                    offer = offer,
                    iconSr = iconSr
                });
            }
        }

        private List<ShopOffer> PickFiveShopOffers()
        {
            var result = new List<ShopOffer>(5);
            if (root == null || root.ShopOffers == null) return result;
            int arrows = 0, relics = 0, consumables = 0;
            foreach (var o in root.ShopOffers)
            {
                if (o.kind == "arrow" && arrows < 3) { result.Add(o); arrows++; }
                else if (o.kind == "relic" && relics < 1) { result.Add(o); relics++; }
                else if ((o.kind == "heal" || o.kind == "ambrosia") && consumables < 1) { result.Add(o); consumables++; }
                if (result.Count >= 5) break;
            }
            return result;
        }

        private ShopStandItem FindNearestShopStand(Vector2 playerPos)
        {
            ShopStandItem best = null;
            float bestDist = 1.45f;
            for (int i = 0; i < shopStands.Count; i++)
            {
                var st = shopStands[i];
                if (st.offer == null || st.offer.sold) continue;
                float d = Vector2.Distance(playerPos, st.pos + Vector3.up * 0.5f);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = st;
                }
            }
            return best;
        }

        private GameObject SpawnShopProp(string name, Sprite sprite, Vector3 pos, float scale, int sortingOrder, bool flipX)
        {
            var go = new GameObject(name);
            go.transform.SetParent(shopRoot.transform, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = flipX;
            sr.sortingOrder = sortingOrder;
            return go;
        }

        private Sprite GetShopSprite(int idx)
        {
            if (shopSprites != null && idx >= 0 && idx < shopSprites.Length) return shopSprites[idx];
            return null;
        }

        private static Sprite GetShopArrowSprite()
        {
            if (shopArrowSprite != null) return shopArrowSprite;
            var tex = new Texture2D(36, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[36 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 36; x++)
                {
                    bool body = y >= 2 && y <= 5 && x >= 1 && x <= 28;
                    bool tip = x >= 27 && x <= 34 && Mathf.Abs(y - 3.5f) <= (35 - x) * 0.45f;
                    bool fletch = x <= 5 && (y == 1 || y == 6);
                    px[y * 36 + x] = (body || tip || fletch) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            shopArrowSprite = Sprite.Create(tex, new Rect(0, 0, 36, 8), new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect);
            return shopArrowSprite;
        }

        private void EnsureShopSprites()
        {
            if (shopSprites != null && shopSprites.Length >= 3 && shopSprites[0] != null) return;
#if UNITY_EDITOR
            var all = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/pjs/Sprites/caravan.png");
            var list = new List<Sprite>();
            if (all != null)
            {
                foreach (var obj in all)
                    if (obj is Sprite sp) list.Add(sp);
                list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
                if (list.Count > 0) shopSprites = list.ToArray();
            }
#endif
        }

        private void ShowWagonBubble(string text)
        {
            bubbleText = text;
            bubbleTimer = 3.2f;
            bubbleElapsed = 0f;
        }

        private void DrawPixelSpeechBubble(Vector3 worldPos, string fullText, float elapsed, float remaining)
        {
            if (string.IsNullOrEmpty(fullText) || Camera.main == null) return;
            Vector3 sc = Camera.main.WorldToScreenPoint(worldPos);
            if (sc.z < 0f) return;

            if (bubbleStyle == null)
            {
                bubbleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false
                };
            }

            int shownChars = Mathf.Clamp(Mathf.FloorToInt(elapsed * 36f), 1, fullText.Length);
            string shown = fullText.Substring(0, shownChars);
            float alpha = remaining < 0.35f ? Mathf.Clamp01(remaining / 0.35f) : 1f;

            Vector2 textSize = bubbleStyle.CalcSize(new GUIContent(fullText));
            float w = Mathf.Max(220f, Mathf.Ceil(textSize.x / 2f) * 2f + 36f);
            float h = 48f;
            float cx = Mathf.Round(sc.x);
            float bottomY = Mathf.Round(Screen.height - sc.y);
            float x = cx - w * 0.5f;
            float y = bottomY - h - 12f;

            var oldCol = GUI.color;
            // 외곽 검정 픽셀 테두리
            GUI.color = new Color(0.04f, 0.03f, 0.04f, alpha);
            GUI.DrawTexture(new Rect(x - 4f, y - 4f, w + 8f, h + 8f), Texture2D.whiteTexture);
            // 산나비풍 금빛/크림 픽셀 테두리
            GUI.color = new Color(0.92f, 0.83f, 0.62f, alpha);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, w + 4f, h + 4f), Texture2D.whiteTexture);
            // 어두운 내부 배경
            GUI.color = new Color(0.10f, 0.08f, 0.10f, 0.96f * alpha);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

            // 픽셀 말풍선 꼬리 (계단식 픽셀)
            GUI.color = new Color(0.04f, 0.03f, 0.04f, alpha);
            GUI.DrawTexture(new Rect(cx - 8f, y + h + 2f, 16f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 5f, y + h + 6f, 10f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 2f, y + h + 10f, 4f, 4f), Texture2D.whiteTexture);

            GUI.color = new Color(0.92f, 0.83f, 0.62f, alpha);
            GUI.DrawTexture(new Rect(cx - 6f, y + h, 12f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 3f, y + h + 4f, 6f, 4f), Texture2D.whiteTexture);

            GUI.color = new Color(0.10f, 0.08f, 0.10f, alpha);
            GUI.DrawTexture(new Rect(cx - 4f, y + h - 2f, 8f, 4f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            bubbleStyle.normal.textColor = new Color(0.98f, 0.94f, 0.84f, alpha);
            GUI.Label(new Rect(x + 10f, y, w - 20f, h), shown, bubbleStyle);
            GUI.color = oldCol;
        }

        private void DrawWorldPrompt(Vector3 worldPos, string msg)
        {
            Vector3 sc = Camera.main.WorldToScreenPoint(worldPos);
            if (sc.z < 0f) return;
            if (promptStyle == null) promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float sx = sc.x, sy = Screen.height - sc.y;
            float w = 270f;
            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.85f);
            GUI.DrawTexture(new Rect(sx - w * 0.5f, sy - 18f, w, 34f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            promptStyle.normal.textColor = new Color(1f, 0.92f, 0.5f);
            GUI.Label(new Rect(sx - w * 0.5f, sy - 18f, w, 34f), msg, promptStyle);
        }

        private static string GetShopOfferDesc(ShopOffer o)
        {
            if (o == null) return "";
            switch (o.kind)
            {
                case "arrow":
                {
                    var a = DB.Arrow(o.arrowCode);
                    string sp = !string.IsNullOrEmpty(a.special) ? $"\n{a.special}" : "";
                    return $"[{a.grade}] 피해 {a.DamageLabel}{sp}";
                }
                case "relic":
                {
                    var rd = DB.Relics[o.relicId];
                    return $"[{rd.grade}] {rd.desc}";
                }
                case "heal":
                    return $"체력 {DB.Balance.economy.healPotionAmount} 회복";
                case "ambrosia":
                    return $"이번 도전 최대 체력 +{DB.Balance.economy.ambrosiaMaxHp}";
                default:
                    return "보유 화살 1장 제거";
            }
        }

        private void DrawShopStandParchment(ShopStandItem stand)
        {
            if (stand == null || stand.offer == null) return;
            if (shopParchmentTex == null)
                shopParchmentTex = Resources.Load<Texture2D>("LaurelUI/shop_parchment");

            Vector3 sc = Camera.main.WorldToScreenPoint(stand.pos + Vector3.up * 1.9f);
            if (sc.z < 0f) return;

            if (shopTitleStyle == null)
            {
                shopTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                shopPriceStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                shopDescStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true };
                shopHintStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            const float pw = 250f;
            const float ph = 310f;
            float px = Mathf.Clamp(sc.x - pw * 0.5f, 12f, Screen.width - pw - 12f);
            float py = Mathf.Clamp((Screen.height - sc.y) - ph, 12f, Screen.height - ph - 12f);

            var oldCol = GUI.color;
            GUI.color = Color.white;
            if (shopParchmentTex != null)
            {
                GUI.DrawTexture(new Rect(px, py, pw, ph), shopParchmentTex, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.90f, 0.82f, 0.64f, 0.96f);
                GUI.DrawTexture(new Rect(px, py, pw, ph), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            float tx = px + 38f;
            float tw = pw - 76f;
            bool canAfford = root != null && root.Run != null && root.Run.gold >= stand.offer.price;

            shopTitleStyle.normal.textColor = new Color(0.18f, 0.11f, 0.05f);
            GUI.Label(new Rect(tx, py + 58f, tw, 36f), stand.offer.Title, shopTitleStyle);

            shopPriceStyle.normal.textColor = canAfford ? new Color(0.55f, 0.33f, 0.04f) : new Color(0.65f, 0.16f, 0.13f);
            GUI.Label(new Rect(tx, py + 94f, tw, 24f), $"{stand.offer.price} G", shopPriceStyle);

            GUI.color = new Color(0.35f, 0.23f, 0.12f, 0.45f);
            GUI.DrawTexture(new Rect(tx + 8f, py + 123f, tw - 16f, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            shopDescStyle.normal.textColor = new Color(0.24f, 0.16f, 0.09f);
            GUI.Label(new Rect(tx, py + 132f, tw, 110f), GetShopOfferDesc(stand.offer), shopDescStyle);

            shopHintStyle.normal.textColor = canAfford ? new Color(0.28f, 0.16f, 0.05f) : new Color(0.65f, 0.16f, 0.13f);
            GUI.Label(new Rect(tx, py + ph - 56f, tw, 28f), canAfford ? "[F] 구매" : "[F] 구매 (골드 부족)", shopHintStyle);

            GUI.color = oldCol;
        }

        private void SetMapOpen(bool open, bool allowAdvance)
        {
            if (open && !MapOpen) mapRevealT = 0f;
            MapOpen = open;
            canAdvanceFromMap = allowAdvance;
            if (root != null && root.Avatar != null) root.Avatar.SetInputEnabled(!open);
        }

        private void Update()
        {
            if (IsLoading || Map == null || CurrentNode == null) return;

            if (MapOpen && mapRevealT < 1f)
                mapRevealT = Mathf.MoveTowards(mapRevealT, 1f, Time.unscaledDeltaTime / MapRevealDuration);

            if (!PortalOpen)
            {
                int alive = 0;
                for (int i = activeEnemies.Count - 1; i >= 0; i--)
                {
                    if (activeEnemies[i] == null || !activeEnemies[i].IsAlive) activeEnemies.RemoveAt(i);
                    else alive++;
                }
                if (alive == 0) OpenPortal();
            }

            if (PortalOpen && portalGlow != null)
            {
                float p = 0.78f + Mathf.Sin(Time.time * 4f) * 0.18f;
                portalGlow.color = new Color(0.45f, 0.92f, 1f, p);
            }

            if (bubbleTimer > 0f)
            {
                bubbleTimer -= Time.unscaledDeltaTime;
                bubbleElapsed += Time.unscaledDeltaTime;
            }

#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (confirmArrowLossOpen)
                {
                    if (k.escapeKey.wasPressedThisFrame) CancelArrowLossWarning();
                    return;
                }

                if (root != null && root.State != GameState.Node) return;

                if (k.mKey.wasPressedThisFrame)
                {
                    SetMapOpen(!MapOpen, PortalOpen);
                }
                else if (MapOpen && k.escapeKey.wasPressedThisFrame)
                {
                    SetMapOpen(false, false);
                }
                else if (!MapOpen && shopAvailable && root != null && root.PlayerGO != null
                    && (k.wKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame)
                    && (FindNearestShopStand(root.PlayerGO.transform.position) != null
                        || Vector2.Distance(root.PlayerGO.transform.position, leftWagonPos + Vector3.up * 0.5f) <= 3.2f
                        || Vector2.Distance(root.PlayerGO.transform.position, rightWagonPos + Vector3.up * 0.5f) <= 3.0f))
                {
                    var stand = FindNearestShopStand(root.PlayerGO.transform.position);
                    if (stand != null)
                    {
                        if (root.Buy(stand.offer) && stand.iconSr != null)
                            stand.iconSr.gameObject.SetActive(false);
                    }
                    else if (Vector2.Distance(root.PlayerGO.transform.position, leftWagonPos + Vector3.up * 0.5f) <= 3.2f)
                    {
                        ShowWagonBubble("거긴 내 짐밖에 없다..");
                    }
                    else if (Vector2.Distance(root.PlayerGO.transform.position, rightWagonPos + Vector3.up * 0.5f) <= 3.0f)
                    {
                        ShowWagonBubble("나한테 말걸지 말고, 물건이나 사라..");
                    }
                }
                else if (!MapOpen && arrowChestAvailable && root != null && root.PlayerGO != null
                    && Vector2.Distance(root.PlayerGO.transform.position, arrowChestPos + Vector3.up * 0.5f) <= 2.1f
                    && (k.wKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame))
                {
                    OpenArrowChest();
                }
                else if (!MapOpen && relicChestAvailable && root != null && root.PlayerGO != null
                    && Vector2.Distance(root.PlayerGO.transform.position, relicChestPos + Vector3.up * 0.5f) <= 2.1f
                    && (k.wKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame))
                {
                    OpenRelicChest();
                }
                else if (!MapOpen && PortalOpen && root != null && root.PlayerGO != null)
                {
                    float d = Vector2.Distance(root.PlayerGO.transform.position, portalPos + Vector3.up * 1.0f);
                    if (d <= 2.1f && (k.wKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame))
                    {
                        if (CurrentNode.type == NodeType.Boss)
                        {
                            if (CountFieldArrows() > 0) RemoveFieldArrows();
                            CompleteStage1AndLoop();
                        }
                        else
                        {
                            SetMapOpen(true, true);
                        }
                    }
                }
            }
#endif
        }

        private void OnGUI()
        {
            if (IsLoading)
            {
                GUI.depth = -20;
                GUI.color = new Color(0.05f, 0.05f, 0.07f, 1f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (loadingStyle == null)
                    loadingStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                loadingStyle.normal.textColor = new Color(1f, 0.93f, 0.78f);
                GUI.Label(new Rect(0f, Screen.height * 0.5f - 30f, Screen.width, 60f), $"로딩 중...  {loadingText}", loadingStyle);
                return;
            }

            if (Map == null) return;

            if (!MapOpen && root != null && root.State == GameState.Node && root.PlayerGO != null && Camera.main != null)
            {
                if (shopAvailable && bubbleTimer > 0f)
                    DrawPixelSpeechBubble(rightBubblePos, bubbleText, bubbleElapsed, bubbleTimer);

                if (shopAvailable)
                {
                    var stand = FindNearestShopStand(root.PlayerGO.transform.position);
                    if (stand != null)
                    {
                        DrawShopStandParchment(stand);
                        return;
                    }
                    if (Vector2.Distance(root.PlayerGO.transform.position, leftWagonPos + Vector3.up * 0.5f) <= 3.2f)
                    {
                        DrawWorldPrompt(leftWagonPos + Vector3.up * 2.2f, "[F] 살펴보기");
                        return;
                    }
                    if (Vector2.Distance(root.PlayerGO.transform.position, rightWagonPos + Vector3.up * 0.5f) <= 3.0f)
                    {
                        DrawWorldPrompt(rightWagonPos + Vector3.up * 2.2f, "[F] 말 걸기");
                        return;
                    }
                }
                if (arrowChestAvailable && Vector2.Distance(root.PlayerGO.transform.position, arrowChestPos + Vector3.up * 0.5f) <= 2.2f)
                {
                    DrawWorldPrompt(arrowChestPos + Vector3.up * 1.9f, "[W / F] 화살 상자 열기");
                }
                else if (relicChestAvailable && Vector2.Distance(root.PlayerGO.transform.position, relicChestPos + Vector3.up * 0.5f) <= 2.2f)
                {
                    DrawWorldPrompt(relicChestPos + Vector3.up * 1.9f, "[W / F] 유물 상자 열기");
                }
                else if (PortalOpen && Vector2.Distance(root.PlayerGO.transform.position, portalPos + Vector3.up * 1.0f) <= 2.4f)
                {
                    string msg = CurrentNode.type == NodeType.Boss ? "[W / F] 1스테이지 완료" : "[W / F] 포탈 입장 (지도)";
                    DrawWorldPrompt(portalPos + Vector3.up * 3.0f, msg);
                }
            }

            if (confirmArrowLossOpen)
            {
                DrawArrowLossWarning();
                return;
            }

            if (!MapOpen) return;

            const float VW = 1920f, VH = 1080f;
            float scale = Mathf.Min(Screen.width / VW, Screen.height / VH);
            if (scale <= 0.001f) return;

            float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(mapRevealT), 3f);

            GUI.color = new Color(0f, 0f, 0f, 0.72f * Mathf.Clamp01(mapRevealT * 2f));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Matrix4x4 prev = GUI.matrix;
            float ox = (Screen.width - VW * scale) * 0.5f;
            float oy = (Screen.height - VH * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            var fullBoard = new Rect(VW * 0.5f - 620f, VH * 0.5f - 360f, 1240f, 720f);
            float clipW = Mathf.Max(1f, fullBoard.width * ease);
            GUI.BeginGroup(new Rect(fullBoard.x, fullBoard.y, clipW, fullBoard.height));
            var board = new Rect(0f, 0f, fullBoard.width, fullBoard.height);

            if (mapSheet != null) GUI.DrawTextureWithTexCoords(board, mapSheet, UvParchment);
            else { GUI.color = new Color(0.88f, 0.78f, 0.6f, 1f); GUI.DrawTexture(board, Texture2D.whiteTexture); GUI.color = Color.white; }

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            titleStyle.normal.textColor = new Color(0.24f, 0.16f, 0.09f);
            string sub = canAdvanceFromMap ? "다음 노드를 선택하세요" : "지도 보기 (M / Esc 닫기)";
            GUI.Label(new Rect(board.x, board.y + 38f, board.width, 46f), $"1스테이지 지도 — {sub}", titleStyle);

            var layout = ComputeLayout(board);
            var choices = Map.Choices(CurrentNode.id);

            // 길 (Map_sheet 점 아이콘으로 연결)
            foreach (var n in Map.nodes)
            {
                Vector2 a = layout[n.id];
                foreach (int nx in n.next)
                {
                    Vector2 b = layout[nx];
                    DrawDotPath(a, b, n.id == CurrentNode.id);
                }
            }

            // 노드 배치 (Map_sheet 원본 비율·크기 기준)
            foreach (var n in Map.nodes)
            {
                Vector2 p = layout[n.id];
                bool isCur = n.id == CurrentNode.id;
                bool canPick = canAdvanceFromMap && choices.Contains(n);
                Vector2 px = NodePxSize(n.type);
                float baseScale = n.type == NodeType.Boss ? 2.9f : 2.5f;
                Vector2 baseSize = px * baseScale;

                if (isCur)
                {
                    float sunSize = 60f * 1.75f;
                    if (mapSheet != null) GUI.DrawTextureWithTexCoords(new Rect(p.x - sunSize * 0.5f, p.y - sunSize * 0.5f, sunSize, sunSize), mapSheet, UvSun);
                }

                float pulse = canPick ? (1.08f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f) : 1f;
                Vector2 drawSize = baseSize * pulse;
                Color tint = (n.visited && !isCur)
                    ? new Color(0.55f, 0.5f, 0.45f, 0.85f)
                    : (canPick ? new Color(1f, 0.96f, 0.8f, 1f) : Color.white);
                GUI.color = tint;
                var iconRect = new Rect(p.x - drawSize.x * 0.5f, p.y - drawSize.y * 0.5f, drawSize.x, drawSize.y);
                if (mapSheet != null) GUI.DrawTextureWithTexCoords(iconRect, mapSheet, NodeUv(n.type));
                GUI.color = Color.white;

                labelStyle.normal.textColor = isCur
                    ? new Color(0.15f, 0.08f, 0.03f)
                    : (canPick ? new Color(0.65f, 0.22f, 0.05f) : new Color(0.32f, 0.22f, 0.14f));
                GUI.Label(new Rect(p.x - 45f, p.y + baseSize.y * 0.5f + 2f, 90f, 22f), MapNode.TypeName(n.type), labelStyle);

                if (!confirmArrowLossOpen && mapRevealT >= 0.85f && canPick && GUI.Button(new Rect(p.x - baseSize.x * 0.7f, p.y - baseSize.y * 0.7f, baseSize.x * 1.4f, baseSize.y * 1.4f), GUIContent.none, GUIStyle.none))
                {
                    TryEnterNode(n);
                    break;
                }
            }

            GUI.EndGroup();

            if (mapSheet != null && ease < 0.995f)
            {
                int idx = Mathf.Clamp(Mathf.FloorToInt(ease * UvRolls.Length), 0, UvRolls.Length - 1);
                float pxScale = fullBoard.height / 174f;
                float rollW = RollSizes[idx].x * pxScale;
                float rollH = RollSizes[idx].y * pxScale;
                float rollX = fullBoard.x + clipW - rollW * 0.45f;
                float rollY = fullBoard.center.y - rollH * 0.5f;
                GUI.DrawTextureWithTexCoords(new Rect(rollX, rollY, rollW, rollH), mapSheet, UvRolls[idx]);
            }

            GUI.matrix = prev;
        }

        private void TryEnterNode(MapNode node)
        {
            if (node == null) return;
            if (CountFieldArrows() > 0) RemoveFieldArrows();
            EnterNode(node);
        }

        private int CountFieldArrows()
        {
            if (root == null || root.Combat == null) return 0;
            int count = 0;
            var bodies = root.Combat.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b != null && b.State != ArrowState.InQuiver && b.State != ArrowState.Consumed)
                    count++;
            }
            return count;
        }

        private void RemoveFieldArrows()
        {
            if (root == null || root.Combat == null) return;
            var bodies = root.Combat.Bodies;
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b != null && b.State != ArrowState.InQuiver)
                    root.Combat.RemoveArrowPermanently(b);
            }
        }

        private void CancelArrowLossWarning()
        {
            confirmArrowLossOpen = false;
            pendingNextNode = null;
            pendingBossClear = false;
            SetMapOpen(false, false);
        }

        private void ConfirmArrowLossAndProceed()
        {
            var next = pendingNextNode;
            bool bossClear = pendingBossClear;
            confirmArrowLossOpen = false;
            pendingNextNode = null;
            pendingBossClear = false;

            RemoveFieldArrows();

            if (bossClear)
            {
                CompleteStage1AndLoop();
            }
            else if (next != null)
            {
                EnterNode(next);
            }
        }

        private void DrawArrowLossWarning()
        {
            const float VW = 1920f, VH = 1080f;
            float scale = Mathf.Min(Screen.width / VW, Screen.height / VH);
            if (scale <= 0.001f) return;

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Matrix4x4 prev = GUI.matrix;
            float ox = (Screen.width - VW * scale) * 0.5f;
            float oy = (Screen.height - VH * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            var box = new Rect(VW * 0.5f - 310f, VH * 0.5f - 145f, 620f, 290f);
            GUI.color = new Color(0.14f, 0.12f, 0.10f, 0.98f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.65f, 0.35f, 0.9f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 3f, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (warnTitleStyle == null)
            {
                warnTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                warnBodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                warnBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            }

            int leftCount = CountFieldArrows();
            warnTitleStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);
            warnBodyStyle.normal.textColor = new Color(0.94f, 0.92f, 0.88f);

            GUI.Label(new Rect(box.x + 24f, box.y + 24f, box.width - 48f, 40f), "화살이 남아있습니다", warnTitleStyle);
            GUI.Label(new Rect(box.x + 36f, box.y + 72f, box.width - 72f, 96f),
                $"필드에 남은 화살 {leftCount}발이 있습니다.\n그냥 화살통을 비운 채로 갈까요?\n(넘어가면 필드에 남은 화살은 제거됩니다)", warnBodyStyle);

            var btnCancel = new Rect(box.x + 70f, box.yMax - 78f, 210f, 48f);
            var btnConfirm = new Rect(box.xMax - 280f, box.yMax - 78f, 210f, 48f);

            if (GUI.Button(btnCancel, "취소 (회수하기)", warnBtnStyle))
                CancelArrowLossWarning();
            if (GUI.Button(btnConfirm, "비운 채로 이동", warnBtnStyle))
                ConfirmArrowLossAndProceed();

            GUI.matrix = prev;
        }

        private Dictionary<int, Vector2> ComputeLayout(Rect board)
        {
            var res = new Dictionary<int, Vector2>();
            float left = board.x + 150f, right = board.xMax - 150f;
            float top = board.y + 140f, bottom = board.yMax - 110f;
            int layers = Map.LayerCount;
            float stepX = layers > 1 ? (right - left) / (layers - 1) : 0f;
            float midY = (top + bottom) * 0.5f;
            for (int L = 0; L < layers; L++)
            {
                var ids = Map.layers[L];
                float x = left + L * stepX;
                float spanY = Mathf.Min(bottom - top, ids.Count * 145f);
                float y0 = midY - spanY * 0.5f;
                float stepY = ids.Count > 1 ? spanY / (ids.Count - 1) : 0f;
                for (int i = 0; i < ids.Count; i++)
                {
                    float y = ids.Count == 1 ? midY : y0 + i * stepY;
                    res[ids[i]] = new Vector2(x, y);
                }
            }
            return res;
        }

        private void DrawDotPath(Vector2 a, Vector2 b, bool highlight)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            Vector2 u = d / len;
            GUI.color = highlight ? new Color(1f, 0.9f, 0.55f, 1f) : new Color(0.85f, 0.78f, 0.68f, 0.85f);
            for (float t = 34f; t < len - 34f; t += 24f)
            {
                Vector2 p = a + u * t;
                float dw = 8f * 1.5f, dh = 7f * 1.5f;
                var r = new Rect(p.x - dw * 0.5f, p.y - dh * 0.5f, dw, dh);
                if (mapSheet != null) GUI.DrawTextureWithTexCoords(r, mapSheet, UvDot);
                else GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        private static Vector2 NodePxSize(NodeType t)
        {
            switch (t)
            {
                case NodeType.Question: return new Vector2(22f, 24f);
                case NodeType.Reward: return new Vector2(22f, 20f);
                case NodeType.Elite: return new Vector2(22f, 21f);
                case NodeType.Boss: return new Vector2(20f, 23f);
                case NodeType.Shop: return new Vector2(22f, 21f);
                default: return new Vector2(21f, 21f);
            }
        }

        private static Rect NodeUv(NodeType t)
        {
            switch (t)
            {
                case NodeType.Question: return UvQuestion;
                case NodeType.Reward: return UvReward;
                case NodeType.Elite: return UvElite;
                case NodeType.Boss: return UvBoss;
                case NodeType.Shop: return UvShop;
                default: return UvCombat;
            }
        }
    }
}
