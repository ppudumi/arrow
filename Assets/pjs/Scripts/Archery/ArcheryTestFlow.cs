using System.Collections.Generic;
using Procedural2D;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Archery
{
    /// <summary>
    /// 궁술 테스트 빌드의 진입점. 로비(발사·회수 선택) ↔ 테스트 전투를 오가며,
    /// 전투 공간·적·플레이어·화살을 하나의 루트 아래 생성해 초기화/로비 복귀 시 통째로 정리한다.
    /// </summary>
    public class ArcheryTestFlow : MonoBehaviour
    {
        public enum FlowScreen { Lobby, Battle }

        [Header("References (ArcheryTestSceneBuilder 가 자동 연결)")]
        public GameObject playerPrefab;
        public Sprite arrowSprite;
        public Sprite dummySprite;
        public Sprite targetSprite;
        public Sprite eyeSprite;
        public Material lineMaterial;
        public CameraFollow2D cameraFollow;

        public static ArcheryTestFlow Instance { get; private set; }

        public ArcheryConfig Config { get; private set; }
        public FlowScreen Current { get; private set; } = FlowScreen.Lobby;

        // 로비 선택
        public LaunchStyle SelectedLaunch = LaunchStyle.Apollo;
        public RetrievalStyle SelectedRetrieval = RetrievalStyle.Basic;
        public float SelectedMaxHp;
        public readonly Dictionary<string, int> LoadoutCounts = new Dictionary<string, int>();
        public bool ShuffleLoadout;
        public bool ShowRanges = true;
        /// <summary>자동 검증이 화살통 구성을 직접 지정할 때 사용 (null이면 로비 설정)</summary>
        public List<string> LoadoutOverride;

        // 전투 상태
        public ArcheryCombatContext Ctx { get; private set; }
        public ArcheryCombat Combat { get; private set; }
        public GameObject Player { get; private set; }
        public GameObject ArenaRoot { get; private set; }
        public int BattleSerial { get; private set; }

        public static readonly Vector2 PlayerSpawn = new Vector2(-16f, 1.2f);
        public static readonly Vector2 LightningDummyPos = new Vector2(4f, 1f);

        private Sprite whiteSprite;
        private Camera cam;

        private void Awake()
        {
            Instance = this;
            Config = ArcheryConfig.Load();
            SelectedMaxHp = Config.playerMaxHp;
            foreach (var def in Config.arrows) LoadoutCounts[def.id] = 0;
            foreach (var id in Config.defaultLoadout)
            {
                if (LoadoutCounts.ContainsKey(id)) LoadoutCounts[id]++;
            }
            cam = Camera.main;
            whiteSprite = CreateWhiteSprite();
            if (cameraFollow == null && cam != null) cameraFollow = cam.GetComponent<CameraFollow2D>();
            if (cam != null) cam.orthographicSize = Config.cameraOrthoSize;
        }

        private void Start()
        {
            if (ArcherySelfTest.RequestedFromCommandLine())
            {
                ArcherySelfTest.Launch(this, true);
            }
        }

        private void Update()
        {
            if (ArcherySelfTest.Running) return;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null || Current != FlowScreen.Battle) return;
            if (k.f5Key.wasPressedThisFrame) ResetBattle();
            else if (k.escapeKey.wasPressedThisFrame) ReturnToLobby();
            else if (k.f1Key.wasPressedThisFrame) ToggleRanges();
            else if (k.f2Key.wasPressedThisFrame) PlaceLightningTestArrows();
            else if (k.f3Key.wasPressedThisFrame) ResetEnemyStats();
#endif
        }

        // ───────────── 흐름 ─────────────

        public void StartBattle()
        {
            TearDownBattle();
            BattleSerial++;
            Ctx = new ArcheryCombatContext(Config);

            ArenaRoot = new GameObject($"ArcheryArena_{BattleSerial}");
            Ctx.Root = ArenaRoot.transform;
            var draw = ArenaRoot.AddComponent<ArcheryDebugDraw>();
            draw.lineMaterial = lineMaterial;
            draw.showRanges = ShowRanges;
            Ctx.Draw = draw;

            BuildArena(ArenaRoot.transform);
            var arrowParent = new GameObject("Arrows").transform;
            arrowParent.SetParent(ArenaRoot.transform, false);

            Player = SpawnPlayer(PlayerSpawn);
            Combat = Player.AddComponent<ArcheryCombat>();
            Combat.PointerOverUi = () => ArcheryTestUI.PointerOverBattleButtons;
            Combat.Init(Ctx, SelectedLaunch, SelectedRetrieval, BuildLoadout(), SelectedMaxHp, arrowSprite != null ? arrowSprite : whiteSprite,
                        arrowParent, System.Environment.TickCount ^ BattleSerial);

            if (cameraFollow != null)
            {
                cameraFollow.target = Player.transform;
                cameraFollow.minBounds = new Vector2(-14f, -20f);
                cameraFollow.maxBounds = new Vector2(14f, 60f);
                cameraFollow.transform.position = new Vector3(PlayerSpawn.x, PlayerSpawn.y + 1.8f, -10f);
            }
            Current = FlowScreen.Battle;
        }

        public void ResetBattle()
        {
            Ctx?.Log("전투 초기화");
            StartBattle();
        }

        public void ReturnToLobby()
        {
            TearDownBattle();
            Current = FlowScreen.Lobby;
        }

        /// <summary>화살·적·플레이어(버프/충전/대기시간 포함)를 모두 파괴한다.</summary>
        private void TearDownBattle()
        {
            if (Player != null)
            {
                Player.SetActive(false);
                Destroy(Player);
            }
            if (ArenaRoot != null)
            {
                ArenaRoot.SetActive(false);
                Destroy(ArenaRoot);
            }
            Player = null;
            ArenaRoot = null;
            Combat = null;
            Ctx = null;
        }

        public void ToggleRanges()
        {
            ShowRanges = !ShowRanges;
            if (Ctx != null && Ctx.Draw != null) Ctx.Draw.showRanges = ShowRanges;
        }

        public void PlaceLightningTestArrows()
        {
            if (Combat == null) return;
            Combat.DebugPlaceArrows(new Vector2(LightningDummyPos.x - 2.2f, 0.12f), new Vector2(LightningDummyPos.x + 2.2f, 0.12f));
        }

        public void ResetEnemyStats()
        {
            if (Ctx == null) return;
            foreach (var e in Ctx.Enemies) e.ResetStats();
            Ctx.Log("[테스트 보조] 적 체력/기록 초기화");
        }

        public List<ArrowDefinition> BuildLoadout()
        {
            var result = new List<ArrowDefinition>();
            if (LoadoutOverride != null)
            {
                foreach (var id in LoadoutOverride)
                {
                    var d = Config.FindArrow(id);
                    if (d != null) result.Add(d);
                }
                return result;
            }

            // 종류별 개수를 번갈아 배치 (기본·가벼운·청동·기본…)
            var remaining = new Dictionary<string, int>(LoadoutCounts);
            bool any = true;
            while (any)
            {
                any = false;
                foreach (var def in Config.arrows)
                {
                    if (remaining.TryGetValue(def.id, out int n) && n > 0)
                    {
                        result.Add(def);
                        remaining[def.id] = n - 1;
                        any = true;
                    }
                }
            }
            if (ShuffleLoadout)
            {
                for (int i = result.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (result[i], result[j]) = (result[j], result[i]);
                }
            }
            if (result.Count == 0 && Config.arrows.Count > 0) result.Add(Config.arrows[0]);
            return result;
        }

        // ───────────── 플레이어 ─────────────

        private GameObject SpawnPlayer(Vector2 pos)
        {
            GameObject player;
            if (playerPrefab != null)
            {
                // 비활성 부모 아래에서 생성 → Awake 전에 기존 사격/회수/HUD 를 끈다
                var holder = new GameObject("PlayerSpawnHolder");
                holder.SetActive(false);
                player = Instantiate(playerPrefab, pos, Quaternion.identity, holder.transform);
                var aim = player.GetComponent<Procedural2DAim>();
                if (aim != null) aim.legacyCombatEnabled = false;
                player.transform.SetParent(null, true);
                Destroy(holder);
            }
            else
            {
                // 프리팹 누락 시 대체 플레이어 (이동만)
                player = new GameObject("Player_Fallback");
                player.tag = "Player";
                player.transform.position = pos;
                var rb = player.AddComponent<Rigidbody2D>();
                rb.gravityScale = 3f;
                rb.freezeRotation = true;
                player.AddComponent<CapsuleCollider2D>().size = new Vector2(0.48f, 0.96f);
                var sr = player.AddComponent<SpriteRenderer>();
                sr.sprite = whiteSprite;
                player.AddComponent<ProceduralCharacterController>();
            }
            player.name = "Player_Character";
            return player;
        }

        /// <summary>테스트용: 플레이어를 지정 위치로 순간이동</summary>
        public void TeleportPlayer(Vector2 pos)
        {
            if (Player == null) return;
            var rb = Player.GetComponent<Rigidbody2D>();
            Player.transform.position = pos;
            if (rb != null)
            {
                rb.position = pos;
                rb.linearVelocity = Vector2.zero;
            }
            Physics2D.SyncTransforms();
        }

        // ───────────── 테스트 공간 ─────────────

        private void BuildArena(Transform root)
        {
            Color groundCol = new Color(0.22f, 0.24f, 0.3f);
            Color platCol = new Color(0.32f, 0.36f, 0.46f);
            Color wallCol = new Color(0.17f, 0.18f, 0.23f);

            Block(root, "Ground", new Vector2(0f, -1f), new Vector2(56f, 2f), groundCol);
            Block(root, "Wall_Left", new Vector2(-27f, 10f), new Vector2(2f, 24f), wallCol);
            Block(root, "Wall_Right", new Vector2(27f, 10f), new Vector2(2f, 24f), wallCol);
            Block(root, "Platform_Low", new Vector2(-20f, 1.4f), new Vector2(4f, 0.5f), platCol);
            Block(root, "Platform_1", new Vector2(-9f, 2.6f), new Vector2(5f, 0.5f), platCol);
            Block(root, "Platform_2", new Vector2(0f, 4.6f), new Vector2(5f, 0.5f), platCol);
            Block(root, "Platform_3", new Vector2(9f, 6.6f), new Vector2(5f, 0.5f), platCol);
            Block(root, "Platform_4", new Vector2(18f, 3.2f), new Vector2(4f, 0.5f), platCol);

            // 번개 시험 구역 표시 (바닥 표식)
            var mark = Block(root, "LightningZone_Marker", new Vector2(LightningDummyPos.x, 0.02f), new Vector2(6f, 0.04f), new Color(0.55f, 0.8f, 1f, 0.8f));
            Destroy(mark.GetComponent<BoxCollider2D>());

            Enemy(root, "허수아비 A", new Vector2(-6f, 1f), new Vector2(1.2f, 2f), dummySprite, Color.white, 60f, ArcheryEnemy.MovePattern.Static);
            Enemy(root, "번개 시험 허수아비", LightningDummyPos, new Vector2(1.2f, 2f), dummySprite, new Color(0.75f, 0.9f, 1f), 60f, ArcheryEnemy.MovePattern.Static);
            Enemy(root, "발판 위 표적", new Vector2(9f, 7.65f), new Vector2(1.6f, 1.6f), targetSprite, Color.white, 60f, ArcheryEnemy.MovePattern.Static);
            var patrol = Enemy(root, "순찰 적", new Vector2(14f, 1f), new Vector2(1.2f, 2f), dummySprite, new Color(1f, 0.75f, 0.75f), 80f, ArcheryEnemy.MovePattern.Patrol);
            patrol.moveRange = 3f; patrol.moveSpeed = 2f;
            var hover = Enemy(root, "부유 적", new Vector2(-2f, 8f), new Vector2(1.4f, 1.4f), eyeSprite, Color.white, 60f, ArcheryEnemy.MovePattern.Hover);
            hover.moveRange = 3f; hover.moveSpeed = 1.5f; hover.bobAmplitude = 0.6f;
            Enemy(root, "원거리 표적", new Vector2(23f, 1f), new Vector2(1.2f, 2f), dummySprite, new Color(1f, 0.95f, 0.7f), 60f, ArcheryEnemy.MovePattern.Static);
        }

        private GameObject Block(Transform root, string name, Vector2 center, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = whiteSprite;
            sr.color = color;
            sr.sortingOrder = -5;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            return go;
        }

        private ArcheryEnemy Enemy(Transform root, string name, Vector2 pos, Vector2 size, Sprite sprite, Color color, float hp, ArcheryEnemy.MovePattern pattern)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var e = go.AddComponent<ArcheryEnemy>();
            e.displayName = name;
            e.maxHp = hp;
            e.pattern = pattern;
            e.Setup(Ctx, sprite != null ? sprite : whiteSprite, size, color);
            Ctx.Enemies.Add(e);
            return e;
        }

        public ArcheryEnemy FindEnemy(string name)
        {
            if (Ctx == null) return null;
            foreach (var e in Ctx.Enemies) if (e.displayName == name) return e;
            return null;
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
    }
}
