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
        public Texture2D mapSheet;

        public MapData Map { get; private set; }
        public MapNode CurrentNode { get; private set; }
        public bool MapOpen { get; private set; }
        public bool PortalOpen { get; private set; }
        public bool IsLoading { get; private set; }

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
        private bool canAdvanceFromMap;
        private bool nodeHadCombat;
        private string loadingText = "맵 생성 중...";
        private Coroutine loadRoutine;
        private GUIStyle titleStyle, labelStyle, promptStyle, loadingStyle;
        private bool confirmArrowLossOpen;
        private MapNode pendingNextNode;
        private bool pendingBossClear;
        private GUIStyle warnTitleStyle, warnBodyStyle, warnBtnStyle;
        private ArrowBody hoveredGroundArrow;
        private GUIStyle groundTitleStyle, groundDescStyle;

        // Map_sheet.png (512x512) UV 좌표
        private static readonly Rect UvParchment = Uv(8, 326, 297, 174);
        private static readonly Rect UvQuestion  = Uv(317, 468, 22, 24);
        private static readonly Rect UvCombat    = Uv(348, 470, 21, 21);
        private static readonly Rect UvReward    = Uv(380, 472, 22, 20);
        private static readonly Rect UvElite     = Uv(413, 471, 22, 21);
        private static readonly Rect UvBoss      = Uv(443, 470, 20, 23);
        private static readonly Rect UvDot       = Uv(317, 446, 8, 7);
        private static readonly Rect UvSun       = Uv(309, 372, 60, 60);

        private static Rect Uv(float x, float y, float w, float h) => new Rect(x / 512f, y / 512f, w / 512f, h / 512f);

        public void BeginStage(GameRoot g)
        {
            root = g;
            int seed = PortalTravel.NewSeed();
            Map = MapData.GenerateStage1(seed);
            CurrentNode = Map.nodes[0];
            CurrentNode.visited = true;
            EnterNode(CurrentNode);
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

            if (root != null && root.Avatar != null) root.Avatar.SetInputEnabled(false);
            if (root != null && root.PlayerGO != null) root.PlayerGO.SetActive(false);

            // 로딩 화면을 먼저 1프레임 그린 뒤 지형·적·플레이어 배치 수행
            yield return null;

            if (mapGenerator == null) mapGenerator = FindAnyObjectByType<RandomMapGenerator>();
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
                        CameraRig.I.playSize = 7f;
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

            Physics2D.SyncTransforms();
            if (root != null && root.PlayerGO != null) root.PlayerGO.SetActive(true);
            loadingText = "배치 완료";
            yield return new WaitForSecondsRealtime(0.2f);

            if (root != null && root.Avatar != null) root.Avatar.SetInputEnabled(true);
            IsLoading = false;
            loadRoutine = null;
            root?.ShowToast($"1스테이지 — {MapNode.TypeName(node.type)} ({node.layer + 1}구역)");
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

            // 전투 / 엘리트 / 보스 (첫 노드는 무조건 미노타우르 1마리)
            nodeHadCombat = true;
            int count = (node.layer == 0 || node.type == NodeType.Boss) ? 1 : (node.type == NodeType.Elite ? 1 : 2);
            float hpMul = node.type == NodeType.Boss ? 2.2f : (node.type == NodeType.Elite ? 1.5f : 1f);
            float scaleMul = node.type == NodeType.Boss ? 1.35f : (node.type == NodeType.Elite ? 1.15f : 1f);

            for (int i = 0; i < count; i++)
            {
                Vector2 pos = PickEnemySpawn(d, i, count);
                SpawnMinotour(pos, hpMul, scaleMul);
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

        private void SetMapOpen(bool open, bool allowAdvance)
        {
            MapOpen = open;
            canAdvanceFromMap = allowAdvance;
            if (root != null && root.Avatar != null) root.Avatar.SetInputEnabled(!open);
        }

        private void Update()
        {
            if (IsLoading || Map == null || CurrentNode == null) return;

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

#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (confirmArrowLossOpen)
                {
                    if (k.escapeKey.wasPressedThisFrame) CancelArrowLossWarning();
                    return;
                }

                if (root != null && root.State != GameState.Node) { hoveredGroundArrow = null; return; }

                hoveredGroundArrow = !MapOpen ? ArcheryArmoryDisplays.FindHoveredGroundArrow(root?.Combat) : null;

                if (k.mKey.wasPressedThisFrame)
                {
                    SetMapOpen(!MapOpen, PortalOpen);
                }
                else if (MapOpen && k.escapeKey.wasPressedThisFrame)
                {
                    SetMapOpen(false, false);
                }
                else if (!MapOpen && hoveredGroundArrow != null && k.gKey.wasPressedThisFrame)
                {
                    Vector3 pos = hoveredGroundArrow.transform.position;
                    Color c = hoveredGroundArrow.Card.Def.tint;
                    root?.Combat?.RemoveArrowPermanently(hoveredGroundArrow);
                    Fx.Burst(pos, c, 8, 4f, 0.12f, 0.3f);
                    Sfx.Play("wall", 0.6f, 1.2f);
                    hoveredGroundArrow = null;
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
                            if (CountFieldArrows() > 0)
                            {
                                pendingNextNode = null;
                                pendingBossClear = true;
                                confirmArrowLossOpen = true;
                                root?.Avatar?.SetInputEnabled(false);
                            }
                            else
                            {
                                root.ShowToast("1스테이지 클리어! 새 지도를 생성합니다.");
                                BeginStage(root);
                            }
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
                if (hoveredGroundArrow != null)
                {
                    Vector3 sc = Camera.main.WorldToScreenPoint(hoveredGroundArrow.transform.position + Vector3.up * 0.9f);
                    if (sc.z >= 0f)
                    {
                        if (groundTitleStyle == null)
                        {
                            groundTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                            groundDescStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                        }
                        float sx = sc.x, sy = Screen.height - sc.y;
                        float w = 220f, h = 54f;
                        var r = new Rect(sx - w * 0.5f, sy - h, w, h);
                        GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.9f);
                        GUI.DrawTexture(r, Texture2D.whiteTexture);
                        GUI.color = Color.white;
                        groundTitleStyle.normal.textColor = hoveredGroundArrow.Card.Def.tint;
                        GUI.Label(new Rect(r.x + 4f, r.y + 3f, r.width - 8f, 22f), hoveredGroundArrow.Card.Def.name, groundTitleStyle);
                        groundDescStyle.normal.textColor = new Color(0.95f, 0.92f, 0.72f);
                        GUI.Label(new Rect(r.x + 4f, r.y + 26f, r.width - 8f, 22f), "[G] 제거", groundDescStyle);
                    }
                }
                else if (arrowChestAvailable && Vector2.Distance(root.PlayerGO.transform.position, arrowChestPos + Vector3.up * 0.5f) <= 2.2f)
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

            Matrix4x4 prev = GUI.matrix;
            float ox = (Screen.width - VW * scale) * 0.5f;
            float oy = (Screen.height - VH * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, VW, VH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var board = new Rect(VW * 0.5f - 620f, VH * 0.5f - 360f, 1240f, 720f);
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

            // 노드 배치 (Map_sheet 아이콘 사용)
            foreach (var n in Map.nodes)
            {
                Vector2 p = layout[n.id];
                bool isCur = n.id == CurrentNode.id;
                bool canPick = canAdvanceFromMap && choices.Contains(n);
                float size = n.type == NodeType.Boss ? 68f : 52f;

                if (isCur)
                {
                    float sunSize = size * 1.85f;
                    if (mapSheet != null) GUI.DrawTextureWithTexCoords(new Rect(p.x - sunSize * 0.5f, p.y - sunSize * 0.5f, sunSize, sunSize), mapSheet, UvSun);
                }

                float drawSize = canPick ? size * (1.08f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f) : size;
                Color tint = (n.visited && !isCur)
                    ? new Color(0.55f, 0.5f, 0.45f, 0.85f)
                    : (canPick ? new Color(1f, 0.96f, 0.8f, 1f) : Color.white);
                GUI.color = tint;
                var iconRect = new Rect(p.x - drawSize * 0.5f, p.y - drawSize * 0.5f, drawSize, drawSize);
                if (mapSheet != null) GUI.DrawTextureWithTexCoords(iconRect, mapSheet, NodeUv(n.type));
                GUI.color = Color.white;

                labelStyle.normal.textColor = isCur
                    ? new Color(0.15f, 0.08f, 0.03f)
                    : (canPick ? new Color(0.65f, 0.22f, 0.05f) : new Color(0.32f, 0.22f, 0.14f));
                GUI.Label(new Rect(p.x - 45f, p.y + size * 0.5f + 2f, 90f, 22f), MapNode.TypeName(n.type), labelStyle);

                if (!confirmArrowLossOpen && canPick && GUI.Button(new Rect(p.x - size * 0.7f, p.y - size * 0.7f, size * 1.4f, size * 1.4f), GUIContent.none, GUIStyle.none))
                {
                    TryEnterNode(n);
                    break;
                }
            }

            GUI.matrix = prev;
        }

        private void TryEnterNode(MapNode node)
        {
            if (node == null) return;
            if (CountFieldArrows() > 0)
            {
                pendingNextNode = node;
                pendingBossClear = false;
                confirmArrowLossOpen = true;
                return;
            }
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
                root?.ShowToast("1스테이지 클리어! 새 지도를 생성합니다.");
                BeginStage(root);
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

            Matrix4x4 prev = GUI.matrix;
            float ox = (Screen.width - VW * scale) * 0.5f;
            float oy = (Screen.height - VH * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, VW, VH), Texture2D.whiteTexture);

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
                var r = new Rect(p.x - 6f, p.y - 6f, 12f, 12f);
                if (mapSheet != null) GUI.DrawTextureWithTexCoords(r, mapSheet, UvDot);
                else GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        private static Rect NodeUv(NodeType t)
        {
            switch (t)
            {
                case NodeType.Question: return UvQuestion;
                case NodeType.Reward: return UvReward;
                case NodeType.Elite: return UvElite;
                case NodeType.Boss: return UvBoss;
                default: return UvCombat;
            }
        }
    }
}
