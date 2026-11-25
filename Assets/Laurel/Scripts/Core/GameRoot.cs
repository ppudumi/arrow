using System.Collections;
using System.Collections.Generic;
using Procedural2D;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    public enum GameState { Boot, Title, Story, Tutorial, Map, Node, Reward, Shop, Rest, Result, Death, Lobby, NectarTree, ArcherySelect, Records, ArcheryTest }

    public class RewardData
    {
        public string title;
        public int gold, nectar;
        public List<ArrowDef> arrows = new List<ArrowDef>();
        public List<RelicDef> relics = new List<RelicDef>();
        public bool relicTaken, arrowTaken;
        public int skipGold;
        public bool afterBoss;
    }

    /// <summary>
    /// 게임 전체 흐름. 씬은 하나(Laurel_Main)이고 방·UI·플레이어를 코드로 구성한다.
    /// 타이틀 → 스토리 → 튜토리얼(파에톤) → 저주 → 1~3스테이지(노드) → 결과 → 로비, 사망 시 어디서든 로비.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        [Header("씬에서 연결 (Unity MCP 로 설정)")]
        [Tooltip("재활용하는 기존 플레이어 프리팹 (Assets/pjs/Prefabs/ProceduralCharacter.prefab)")]
        public GameObject playerPrefab;
        [Tooltip("기존 캐릭터 화살 스프라이트 (Assets/pjs/Sprites/Character/Arrow.png)")]
        public Sprite arrowSprite;
        [Tooltip("타이틀 화면 로고 스프라이트 (Assets/pjs/Sprites/Title/title.png)")]
        public Sprite titleSprite;
        [Tooltip("튜토리얼 방 배경 이미지 (씬의 Backdrop_Tutorial 오브젝트). 비우면 코드로 만든 임시 배경을 쓴다")]
        public RoomBackdrop tutorialBackdrop;
        [Tooltip("튜토리얼 방 지형 모양 (씬의 TerrainStyle_Tutorial 오브젝트): 발판 두께·색·테두리. 비우면 예전 단색 지형")]
        public TerrainStyle tutorialTerrainStyle;
        [Header("전투 HUD")]
        [Tooltip("퀘스트 패널 배경 (양피지 PNG, 읽기 가능·최대 512px 로 임포트). 비우면 어두운 기본 패널")]
        public Texture2D questParchment;
        [Header("대화창")]
        [Tooltip("모든 대사창·안내 메시지 배경 양피지 (읽기 가능·최대 1024px 로 임포트). 비우면 어두운 기본 패널")]
        public Texture2D dialogueParchment;
        [Tooltip("'모두 건너뛰기' 버튼 배경 (읽기 가능·최대 512px). 글자는 코드에서 넣는다")]
        public Texture2D dialogueSkipButton;
        [Header("로비 (지하 신전)")]
        [Tooltip("로비 배경 이미지 (씬의 Backdrop_Lobby). 비우면 임시 배경")]
        public RoomBackdrop lobbyBackdrop;
        [Tooltip("로비 지형 모양 (씬의 TerrainStyle_Lobby): 어두운 석재 몸체 + 밝은 착지면. 발판 충돌은 한쪽 통과 그대로")]
        public TerrainStyle lobbyTerrainStyle;
        [Tooltip("로비 장식 (씬의 LobbyDecor): 도리아식 기둥·그리스 문양·제단 그림·불빛")]
        public LobbyDecor lobbyDecor;

        /// <summary>궁술 테스트 씬(Laurel_ArcheryTest)이면 그 씬의 설정. null 이면 일반 게임</summary>
        public ArcheryTestArena ArcheryTest { get; private set; }
        public bool InArcheryTest => ArcheryTest != null;
        public static bool EnteredFromFlow { get; private set; }
        public static bool IsBlitzMode { get; private set; }
        private static Profile blitzProfile;
        public Stage1Controller Stage1 { get; private set; }
        public bool InStage1 => Stage1 != null;

        public GameState State { get; private set; } = GameState.Boot;
        public GameState ReturnState { get; set; }
        public Profile Profile { get; private set; }
        public RunState Run { get; private set; }
        public Room Room { get; private set; }
        public GameObject PlayerGO { get; private set; }
        public PlayerAvatar Avatar { get; private set; }
        public PlayerCombat Combat { get; private set; }
        public MapNode CurrentNode => Run != null && Run.map != null ? Run.map.Get(Run.currentNode) : null;
        public bool Paused { get; private set; }
        public bool DevMode { get; set; }
        public bool SelfTestRunning { get; set; }

        // 연출용 골드·레벨(경험치) 표시 카운터 (기능 없음, 숫자만 증가)
        public int DisplayGold { get; private set; }
        public int DisplayExp { get; private set; }
        public int DisplayLevel => 1 + DisplayExp / 100;
        public int DisplayExpInLevel => DisplayExp % 100;
        public void AddPresentationGold(int amount) { if (amount > 0) DisplayGold += amount; }
        public void AddPresentationExp(int amount) { if (amount > 0) DisplayExp += amount; }

        // 스토리
        public readonly List<StoryLine> StoryQueue = new List<StoryLine>();
        public int StoryIndex { get; private set; }
        private System.Action storyDone;
        public GameState StoryReturnState { get; private set; }

        // 튜토리얼
        public int TutorialStep { get; private set; }
        public float tutMoved;
        public bool tutJumped, tutDoubleJumped, tutDashed;
        private float tutLastX;

        // 노드 UI 데이터
        public RewardData Reward { get; private set; }
        public List<ShopOffer> ShopOffers { get; private set; }
        public int ShopRerolls { get; private set; }
        public bool ShopRemoveMode { get; set; }
        public bool RestUsed { get; private set; }
        public bool ConfirmNewGame { get; set; }
        public string LastDeathCause { get; private set; } = "";
        public int LastRunNectar { get; private set; }
        public int LastRunStage { get; private set; }
        public NectarNodeDef SelectedNectarNode { get; set; }

        private string toast;
        private float toastT;
        public string Toast => toastT > 0f ? toast : null;
        public float ToastAlpha => Mathf.Clamp01(toastT);

        private Camera cam;

        // ───────────── 부트 ─────────────

        private void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Application.targetFrameRate = 60;
            DB.Load();
            // 궁술 테스트 씬, 블리츠 시연 모드, 또는 단독 실행한 1스테이지 씬: 세이브를 읽지도 쓰지도 않는다
            ArcheryTest = FindAnyObjectByType<ArcheryTestArena>();
            Stage1 = FindAnyObjectByType<Stage1Controller>();
            SaveStore.Disabled = InArcheryTest || IsBlitzMode || (InStage1 && !EnteredFromFlow);
            if (arrowSprite != null) Art.ArrowSprite = arrowSprite;

            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            var rig = cam.GetComponent<CameraRig>(); // 씬의 Main Camera 에 붙어 있으면 Inspector 값(줌·따라가기)을 그대로 쓴다
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Init();
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();

            gameObject.AddComponent<Sfx>();
            var fxGo = new GameObject("Fx");
            fxGo.AddComponent<Fx>();
            DontDestroyOnLoad(fxGo);

            if (IsBlitzMode)
            {
                if (blitzProfile == null) blitzProfile = MakeBlitzProfile();
                Profile = blitzProfile;
            }
            else
            {
                Profile = (InArcheryTest || (InStage1 && !EnteredFromFlow)) ? ArcheryTestArena.MakeProfile() : SaveStore.Load();
            }
            CurrentQuiver = System.Enum.TryParse(Profile.quiver, out QuiverType pq) ? pq : QuiverType.Basic;
            Sfx.Volume = Profile.sfxVolume;

            if (playerPrefab == null) Debug.LogError("[Laurel] GameRoot.playerPrefab 이 연결되지 않았습니다.");
            else SpawnPlayer();

            if (GetComponent<GameUI>() == null) gameObject.AddComponent<GameUI>();
        }

        private void Start()
        {
            if (InArcheryTest) { StartArcheryTest(); return; }
            if (InStage1) { StartStage1Scene(); return; }
            if (HasArg("-laurelSelfTest") || ArgValue("-laurelPersistPhase") != null) { gameObject.AddComponent<SelfTest>(); return; }
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "Laurel_Tutorial") { StartTutorial(); return; }
            if (scene == "Laurel_Lobby") { GoLobby(); return; }
            GoTitle();
        }

        private void StartStage1Scene()
        {
            Run = new RunState { stage = 1, seed = Random.Range(1, 999999), startTime = Time.time };
            InitPlayerForRun(Run);

            Room = FindAnyObjectByType<Room>();
            if (Room == null)
            {
                var rgo = new GameObject("Room_Stage1");
                Room = rgo.AddComponent<Room>();
            }
            Room.Player = Avatar;
            Room.Combat = Combat;
            PlayerGO.SetActive(true);
            Combat.BeginRound(Room, Run, Run.launch, Run.retrieval);
            SetState(GameState.Node);
            Stage1.BeginStage(this);
        }

        private void OnDestroy()
        {
            if (I == this && (InArcheryTest || InStage1)) SaveStore.Disabled = false;
        }

        public static bool HasArg(string a)
        {
            foreach (var s in System.Environment.GetCommandLineArgs()) if (s == a) return true;
            return false;
        }

        public static string ArgValue(string a)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == a) return args[i + 1];
            return null;
        }

        private void SpawnPlayer()
        {
            PlayerGO = Instantiate(playerPrefab);
            PlayerGO.name = "Player (ProceduralCharacter)";
            Avatar = PlayerGO.AddComponent<PlayerAvatar>();
            Avatar.Setup();
            Combat = PlayerGO.AddComponent<PlayerCombat>();
            Combat.Bind(Avatar);
            Avatar.OnDeath = OnPlayerDied;
            Avatar.TryRevive = () =>
            {
                if (Run == null || Run.isTutorial || Run.reviveUsed || !Avatar.Stats.revive) return false;
                Run.reviveUsed = true;
                return true;
            };
            PlayerGO.SetActive(false);
            CameraRig.I.target = PlayerGO.transform;
        }

        public void ReloadProfile()
        {
            Profile = SaveStore.Load();
        }

        // ───────────── 공통 ─────────────

        public void ShowToast(string msg, float time = 2.6f)
        {
            toast = msg;
            toastT = time;
        }

        public bool StatusOpen { get; private set; }

        private void SetState(GameState s)
        {
            State = s;
            StatusOpen = false;
            bool playable = s == GameState.Node || s == GameState.Lobby || s == GameState.Tutorial || s == GameState.ArcheryTest;
            if (Avatar != null) Avatar.SetInputEnabled(playable && !Paused);
        }

        public void SetStatusOpen(bool open)
        {
            bool playable = State == GameState.Node || State == GameState.Lobby || State == GameState.Tutorial || State == GameState.ArcheryTest;
            if (open && (!playable || Paused)) return;
            StatusOpen = open;
            Time.timeScale = (Paused || StatusOpen) ? 0f : 1f;
            Avatar?.SetInputEnabled(playable && !Paused && !StatusOpen);
            Sfx.Play("ui", 0.45f);
        }

        public void ToggleStatus() => SetStatusOpen(!StatusOpen);

        public static int ArrowDismantleGold(ArrowCard c)
        {
            if (c == null || c.fromRelic) return 0;
            int price = DB.Balance.economy.arrowPrice[Mathf.Clamp(c.Def.rarity, 0, DB.Balance.economy.arrowPrice.Length - 1)];
            return Mathf.Max(5, Mathf.RoundToInt(price * 0.5f));
        }

        public bool DismantleArrow(ArrowCard card)
        {
            var r = Run ?? PracticeRun;
            if (r == null || card == null || card.fromRelic || r.isTutorial) return false;
            if (r.NonRelicCardCount <= 1) { Sfx.Play("empty"); ShowToast("마지막 화살은 분해할 수 없습니다"); return false; }

            int gain = ArrowDismantleGold(card);
            bool removed = false;
            if (Combat != null && Combat.Active)
            {
                for (int i = 0; i < Combat.Bodies.Count; i++)
                {
                    var b = Combat.Bodies[i];
                    if (b != null && b.Card == card)
                    {
                        removed = Combat.RemoveArrowPermanently(b);
                        break;
                    }
                }
            }
            if (!removed) removed = r.RemoveCard(card);
            if (!removed) return false;

            r.gold += gain;
            r.goldEarned += gain;
            Sfx.Play("buy", 0.6f, 1.15f);
            ShowToast($"{card.Def.name} 분해 (+{gain} G)");
            return true;
        }

        private void DestroyRoom()
        {
            if (Combat != null && Combat.Active) Combat.EndRound();
            if (Room != null) Destroy(Room.gameObject);
            Room = null;
            Fx.I?.ClearAll();
        }

        private void PlacePlayer(Room room, bool show = true)
        {
            room.Player = Avatar;
            room.Combat = Combat;
            PlayerGO.SetActive(show);
            Avatar.Teleport(room.PlayerSpawn);
            Avatar.Aim?.SetFacing(true);
            Avatar.ClearIframes();
            var b = room.InnerBounds;
            float wall = DB.Balance.world.wallThickness;
            CameraRig.I.focus2 = null;
            CameraRig.I.Setup(new Rect(b.xMin - wall, b.yMin - wall, b.width + wall * 2f, b.height + wall * 2f), CameraRig.I.PlaySize, room.Theme.bgFar);
        }

        public void AddNectar(int n, string reason)
        {
            if (n <= 0) return;
            Profile.nectar += n;
            Profile.nectarEarnedTotal += n;
            if (Run != null) Run.nectarEarned += n;
            SaveStore.Save(Profile); // 사용하지 않은 넥타르도 항상 저장
            ShowToast($"넥타르 +{n} ({reason})");
        }

        public void AddGold(int n, bool applyBonus)
        {
            if (Run == null || n <= 0) return;
            if (applyBonus) n = Mathf.RoundToInt(n * (1f + Run.RelicSum("goldGain")));
            Run.gold += n;
            Run.goldEarned += n;
        }

        public void RefreshStats(bool refill)
        {
            if (Run == null || Avatar == null) return;
            Avatar.ApplyStats(Run.ComputeStats(Profile), refill);
        }

        // ───────────── 타이틀·스토리 ─────────────

        private bool LoadSeparatedScene(string sceneName)
        {
            if (SceneManager.GetActiveScene().name == sceneName) return false;
            if (!Application.CanStreamedLevelBeLoaded(sceneName)) return false;
            EnteredFromFlow = true;
            SetState(GameState.Boot);
            SceneManager.LoadScene(sceneName);
            return true;
        }

        public void GoTitle()
        {
            IsBlitzMode = false;
            blitzProfile = null;
            SaveStore.Disabled = InArcheryTest;
            if (!SelfTestRunning && LoadSeparatedScene("Laurel_Title")) return;
            DestroyRoom();
            Run = null;
            Paused = false;
            Time.timeScale = 1f;
            if (PlayerGO != null) PlayerGO.SetActive(false);
            ConfirmNewGame = false;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
            CameraRig.I.Setup(new Rect(-20, -12, 40, 24), 8.5f, new Color(0.08f, 0.08f, 0.12f));
            SetState(GameState.Title);
        }

        /// <summary>발표 시연용 블리츠 프로필: 모든 회수 방식 · 트리플점프 · 대쉬(공격) 해금, 세이브 미사용</summary>
        public static Profile MakeBlitzProfile()
        {
            var p = new Profile { tutorialDone = true, lobbyIntroSeen = true, nectar = 99 };
            foreach (var n in DB.NectarNodes)
                if (n.kind == "retrieval") p.SetLevel(n.id, 1);
            p.SetLevel("double_jump", 1);
            p.SetLevel("triple_jump", 1);
            p.SetLevel("dash", 1);
            return p;
        }

        public void StartBlitz()
        {
            IsBlitzMode = true;
            blitzProfile = MakeBlitzProfile();
            Profile = blitzProfile;
            SaveStore.Disabled = true;
            CurrentQuiver = QuiverType.Basic;
            if (!SelfTestRunning && LoadSeparatedScene("Laurel_Lobby")) return;
            GoLobby();
        }

        public void OpenArcheryTest()
        {
            IsBlitzMode = false;
            blitzProfile = null;
            LoadSeparatedScene("Laurel_ArcheryTest");
        }

        public bool CanContinue => SaveStore.Exists() && Profile.tutorialDone;

        public void NewGame()
        {
            SaveStore.DeleteAll();
            Profile = new Profile();
            SaveStore.Save(Profile);
            PlayStory("intro", () =>
            {
                if (!SelfTestRunning && LoadSeparatedScene("Laurel_Tutorial")) return;
                StartTutorial();
            });
        }

        public void Continue()
        {
            Profile = SaveStore.Load();
            if (Profile.tutorialDone)
            {
                if (!SelfTestRunning && LoadSeparatedScene("Laurel_Lobby")) return;
                GoLobby();
            }
            else
            {
                PlayStory("intro", () =>
                {
                    if (!SelfTestRunning && LoadSeparatedScene("Laurel_Tutorial")) return;
                    StartTutorial();
                });
            }
        }

        public void PlayStory(string id, System.Action done)
        {
            StoryQueue.Clear();
            if (DB.Story.TryGetValue(id, out var sc)) StoryQueue.AddRange(sc.lines);
            StoryIndex = 0;
            storyDone = done;
            if (StoryQueue.Count == 0) { done?.Invoke(); return; }
            StoryReturnState = State == GameState.Story ? StoryReturnState : State;
            SetState(GameState.Story);
        }

        public void AdvanceStory()
        {
            Sfx.Play("ui", 0.5f);
            StoryIndex++;
            if (StoryIndex >= StoryQueue.Count)
            {
                var d = storyDone;
                storyDone = null;
                StoryQueue.Clear();
                d?.Invoke();
                if (State == GameState.Story) SetState(StoryReturnState);
            }
        }

        public void SkipStory()
        {
            StoryIndex = StoryQueue.Count - 1;
            AdvanceStory();
        }

        // ───────────── 튜토리얼 ─────────────

        private void InitPlayerForRun(RunState run)
        {
            run.gold = DB.Balance.player.startGold;
            StyleNames.TryParseLaunch(Profile.launch, out run.launch);
            StyleNames.TryParseRetrieval(Profile.retrieval, out run.retrieval);
            if (!Profile.StyleUnlocked("launch", run.launch.ToString())) run.launch = LaunchStyle.Apollo;
            if (!Profile.StyleUnlocked("retrieval", run.retrieval.ToString())) run.retrieval = RetrievalStyle.Basic;
            run.quiver = CurrentQuiver;
            var stats = run.ComputeStats(Profile);
            var startArrows = DB.Balance.player.startArrows;
            int baseCount = run.quiver == QuiverType.Basic ? startArrows.Length : QuiverStats.ArrowCount(run.quiver);
            for (int i = 0; i < baseCount; i++) run.AddCard(i < startArrows.Length ? startArrows[i] : 0);
            for (int i = 0; i < stats.extraStartArrows; i++) run.AddCard(0);
            Avatar.ApplyStats(stats, true);
        }

        public void StartTutorial()
        {
            DestroyRoom();
            Run = new RunState { isTutorial = true, stage = 0, seed = Random.Range(1, 999999), startTime = Time.time };
            InitPlayerForRun(Run);
            // 저주받기 전 신의 힘은 유물 형태로 부여, 회수는 오르페우스 장착
            Run.AddRelic("apollo_divinity");
            Run.retrieval = RetrievalStyle.Orpheus;
            Avatar.ApplyStats(Run.ComputeStats(Profile), true);

            var theme = RoomTheme.Tutorial;
            theme.style = tutorialTerrainStyle;
            Room = Room.Create("Tutorial", theme, DB.Balance.world.arenaSize, 3, 11);
            PlacePlayer(Room);
            if (tutorialBackdrop != null) tutorialBackdrop.Attach(Room);
            Combat.BeginRound(Room, Run, Run.launch, Run.retrieval);
            TutorialStep = 0;
            tutMoved = 0f; tutJumped = tutDoubleJumped = tutDashed = false;
            tutLastX = PlayerGO.transform.position.x;
            SetState(GameState.Tutorial);
            PlayStory("tut_move", null);
        }

        private void UpdateTutorial()
        {
            if (State != GameState.Tutorial || Room == null) return;
            var ctrl = Avatar.Controller;
            float x = PlayerGO.transform.position.x;
            tutMoved += Mathf.Abs(x - tutLastX);
            tutLastX = x;
            if (!ctrl.IsGrounded) tutJumped = true;
            if (ctrl.IsSpinning) tutDoubleJumped = true;
            if (ctrl.IsDashing) tutDashed = true;

            switch (TutorialStep)
            {
                case 0:
                    bool needDouble = Avatar.Stats.doubleJump;
                    bool needDash = Avatar.Stats.dash;
                    if (tutMoved > 6f && tutJumped && (!needDouble || tutDoubleJumped) && (!needDash || tutDashed)) AdvanceTutorial();
                    break;
                case 1:
                    if (Room.AliveCount == 0) AdvanceTutorial();
                    break;
                case 2:
                    if (Combat.Quiver.Count >= Run.deck.Count) AdvanceTutorial();
                    break;
            }
        }

        public void AdvanceTutorial()
        {
            TutorialStep++;
            Sfx.Play("buy", 0.5f);
            switch (TutorialStep)
            {
                case 1:
                    PlayStory("tut_shoot", null);
                    for (int i = 0; i < 3; i++)
                    {
                        var d = SpawnDummy(Room, new Vector2(2f + i * 3.2f, Room.InnerBounds.yMin + 0.9f), false);
                        d.SetHp(14f);
                    }
                    break;
                case 2:
                    PlayStory("tut_recall", null);
                    break;
                case 3:
                    PlayStory("tut_boss", SpawnPhaeton);
                    break;
            }
        }

        private void SpawnPhaeton()
        {
            if (Room == null) return;
            var def = DB.Bosses["phaeton"];
            var go = new GameObject("Boss_Phaeton");
            go.transform.SetParent(Room.transform, false);
            go.transform.position = new Vector2(Room.InnerBounds.xMax - 5f, 2f);
            var boss = go.AddComponent<PhaetonBoss>();
            boss.InitBoss(Room, def, new Vector2(2.2f, 1.8f));
            Room.SetupBoss(boss, 1f, 1f);
            FrameBoss(boss);
            Room.OnCleared = OnPhaetonDefeated;
        }

        /// <summary>보스방: 카메라를 넓히고 플레이어와 보스 사이를 비춘다</summary>
        private void FrameBoss(Boss boss)
        {
            CameraRig.I.Cam.orthographicSize = DB.Balance.world.bossCameraSize;
            CameraRig.I.focus2 = boss.transform;
        }

        private void OnPhaetonDefeated()
        {
            StartCoroutine(AfterPhaeton());
        }

        private IEnumerator AfterPhaeton()
        {
            yield return new WaitForSeconds(1.2f);
            Profile.tutorialDone = true;
            AddNectar(DB.Balance.nectar.tutorialClear, "파에톤 처치");
            SaveStore.Save(Profile);
            PlayStory("curse", () =>
            {
                // 저주: 튜토리얼 능력을 잃고 저주 후 능력치(=본게임 기본 상태)로 본게임 시작
                Fx.Burst(PlayerGO.transform.position, new Color(1f, 0.85f, 0.3f), 40, 9f, 0.3f, 1f);
                if (!SelfTestRunning && LoadSeparatedScene("Laurel_Stage1")) return;
                StartRun(true);
            });
        }

        // ───────────── 도전(런) ─────────────

        /// <summary>튜토리얼(저주) 직후 시작한 도전에서 아직 첫 전투 방에 들어가지 않았으면 true. 그 방 하나에만 튜토리얼 배경·지형을 쓴다.</summary>
        public bool PendingTutorialLookCombat { get; private set; }

        public void StartRun(bool fromCurse = false)
        {
            if (!InStage1 && !SelfTestRunning && Application.CanStreamedLevelBeLoaded("Laurel_Stage1"))
            {
                Profile.runs++;
                SaveStore.Save(Profile);
                LoadSeparatedScene("Laurel_Stage1");
                return;
            }
            DestroyRoom();
            PendingTutorialLookCombat = fromCurse;
            Profile.runs++;
            SaveStore.Save(Profile);
            Run = new RunState { stage = 1, seed = Random.Range(1, 999999), startTime = Time.time };
            InitPlayerForRun(Run);
            Run.map = MapData.Generate(DB.Stage(1), Run.seed);
            Run.currentNode = -1;
            PlayerGO.SetActive(false);
            PlayStory("stage1_intro", () => SetState(GameState.Map));
            if (State != GameState.Story) SetState(GameState.Map);
        }

        public void EnterNode(MapNode node)
        {
            if (Run == null || node == null) return;
            var choices = Run.map.Choices(Run.currentNode);
            if (!choices.Contains(node)) return;
            DestroyRoom();
            Run.currentNode = node.id;
            node.visited = true;
            var stage = DB.Stage(Run.stage);
            var theme = RoomTheme.FromStage(stage);
            var rng = new System.Random(node.seed);
            Sfx.Play("ui");

            switch (node.type)
            {
                case NodeType.Combat:
                case NodeType.Elite:
                    {
                        // 튜토리얼 직후 첫 전투 방 하나만: 튜토리얼과 같은 숲·유적 배경 + 어두운 흙갈색 지형(막힌 두꺼운 발판, 외벽 바깥 가림)
                        bool tutorialLook = PendingTutorialLookCombat;
                        PendingTutorialLookCombat = false;
                        if (tutorialLook) theme.style = tutorialTerrainStyle;
                        Room = Room.Create($"S{Run.stage}_N{node.id}", theme, DB.Balance.world.arenaSize, rng.Next(0, 4), node.seed);
                        PlacePlayer(Room);
                        if (tutorialLook && tutorialBackdrop != null) tutorialBackdrop.Attach(Room);
                        Room.SetupCombat(ComposeWaves(stage, node, rng), stage.hpMul, stage.dmgMul);
                        Room.OnGoldCollected = g => AddGold(g, true);
                        Room.OnEnemyKilled = e => { Run.kills++; Profile.kills++; };
                        Room.OnCleared = () => StartCoroutine(OnCombatCleared(node));
                        Combat.BeginRound(Room, Run, Run.launch, Run.retrieval);
                        SetState(GameState.Node);
                        break;
                    }
                case NodeType.Boss:
                    {
                        Room = Room.Create($"S{Run.stage}_Boss", theme, DB.Balance.world.arenaSize, Room.BossLayout, node.seed);
                        PlacePlayer(Room);
                        Room.OnGoldCollected = g => AddGold(g, true);
                        Combat.BeginRound(Room, Run, Run.launch, Run.retrieval);
                        SetState(GameState.Node);
                        PlayStory($"stage{Run.stage}_boss", () => SpawnStageBoss(stage));
                        break;
                    }
                case NodeType.Reward:
                    {
                        Room = Room.Create($"S{Run.stage}_Reward", theme, DB.Balance.world.arenaSize, 1, node.seed);
                        PlacePlayer(Room);
                        Reward = new RewardData { title = "보상방 — 신전의 공물", skipGold = 50 };
                        Reward.relics = Loot.RelicChoices(rng, Run, DB.Balance.economy.rewardRelicChoices);
                        Reward.arrowTaken = true;
                        SetState(GameState.Reward);
                        break;
                    }
                case NodeType.Shop:
                    {
                        Room = Room.Create($"S{Run.stage}_Shop", theme, DB.Balance.world.arenaSize, 1, node.seed);
                        PlacePlayer(Room);
                        ShopOffers = Loot.BuildShop(rng, Run);
                        ShopRerolls = 0;
                        ShopRemoveMode = false;
                        SetState(GameState.Shop);
                        break;
                    }
                case NodeType.Rest:
                    {
                        Room = Room.Create($"S{Run.stage}_Rest", theme, DB.Balance.world.arenaSize, 1, node.seed);
                        PlacePlayer(Room);
                        RestUsed = false;
                        SetState(GameState.Rest);
                        break;
                    }
            }
        }

        private List<List<Room.SpawnSpec>> ComposeWaves(StageDef stage, MapNode node, System.Random rng)
        {
            var waves = new List<List<Room.SpawnSpec>>();
            int count = rng.Next(stage.enemyCount[0], stage.enemyCount[1] + 1);
            int waveN = rng.Next(stage.waves[0], stage.waves[1] + 1);
            // 층이 깊을수록 한 마리씩 더
            count += node.layer >= 3 ? 1 : 0;
            for (int w = 0; w < waveN; w++) waves.Add(new List<Room.SpawnSpec>());
            int idx = 0;
            if (node.type == NodeType.Elite)
            {
                var eid = stage.elite[rng.Next(stage.elite.Count)];
                waves[0].Add(new Room.SpawnSpec { def = DB.Enemies[eid], elite = true });
                count = Mathf.Max(2, count - 2);
                idx = 1;
            }
            int flyers = 0;
            for (int i = 0; i < count; i++)
            {
                EnemyDef def = null;
                for (int tries = 0; tries < 6; tries++)
                {
                    def = DB.Enemies[stage.normal[rng.Next(stage.normal.Count)]];
                    bool air = def.ai == "flyer" || def.fly;
                    if (air && flyers >= 2) continue;
                    if (air) flyers++;
                    break;
                }
                waves[(idx + i) % waveN].Add(new Room.SpawnSpec { def = def, elite = false });
            }
            return waves;
        }

        private void SpawnStageBoss(StageDef stage)
        {
            if (Room == null) return;
            var def = DB.Bosses[stage.boss];
            var go = new GameObject("Boss_" + def.id);
            go.transform.SetParent(Room.transform, false);
            Boss boss;
            Vector2 size;
            switch (def.id)
            {
                case "achelous":
                    go.transform.position = new Vector2(Room.InnerBounds.xMax - 5f, Room.InnerBounds.yMax - 4f);
                    boss = go.AddComponent<AchelousBoss>(); size = new Vector2(2.6f, 2.6f); break;
                case "eros":
                    go.transform.position = new Vector2(Room.InnerBounds.xMax - 5f, 3f);
                    boss = go.AddComponent<ErosBoss>(); size = new Vector2(1.5f, 1.5f); break;
                default:
                    go.transform.position = new Vector2(Room.InnerBounds.xMax - 4f, Room.InnerBounds.yMin + 1.2f);
                    boss = go.AddComponent<BoarBoss>(); size = new Vector2(3.2f, 2.2f); break;
            }
            boss.InitBoss(Room, def, size);
            Room.SetupBoss(boss, stage.hpMul, stage.dmgMul);
            FrameBoss(boss);
            Room.OnCleared = () => StartCoroutine(OnBossCleared(stage, def));
            Room.OnEnemyKilled = e => { Run.kills++; Profile.kills++; };
        }

        private IEnumerator OnCombatCleared(MapNode node)
        {
            ShowToast("방 정리 완료!");
            Sfx.Play("buy", 0.6f);
            yield return new WaitForSeconds(1.1f);
            if (Run == null || Avatar.Dead) yield break;
            Room.CollectAllGoldNow();
            Combat.EndRound();
            var stage = DB.Stage(Run.stage);
            var rng = new System.Random(node.seed ^ 0x5bd1e995);
            bool elite = node.type == NodeType.Elite;
            int gold = elite ? stage.goldElite : rng.Next(stage.goldPerNode[0], stage.goldPerNode[1] + 1);
            AddGold(gold, true);
            int nectar = elite ? stage.nectarElite : stage.nectarCombat;
            AddNectar(nectar, elite ? "정예 처치" : "노드 정리");
            var heal = Run.RelicWithEffect("nodeHeal");
            if (heal != null) Avatar.Heal(heal.p1, "아스클레피오스");
            Run.nodesCleared++;
            Reward = new RewardData { title = elite ? "정예 처치 보상" : "전투 보상", gold = gold, nectar = nectar, skipGold = 10 };
            Reward.arrows = Loot.ArrowChoices(rng, Run.stage, DB.Balance.economy.rewardArrowChoices);
            if (elite) Reward.relics = Loot.RelicChoices(rng, Run, DB.Balance.economy.rewardRelicChoices);
            else Reward.relicTaken = true;
            SetState(GameState.Reward);
        }

        private IEnumerator OnBossCleared(StageDef stage, BossDef def)
        {
            ShowToast($"{def.name} 처치!", 3f);
            yield return new WaitForSeconds(1.8f);
            if (Run == null || Avatar.Dead) yield break;
            Room.CollectAllGoldNow();
            Combat.EndRound();
            Run.bossesKilled++;
            // 보스 클리어 = 해당 스킬 '구매 자격' 해금 (스킬 자동 지급 아님). 저장해 재도전·재실행 후에도 유지.
            if (!Profile.bossesDefeated.Contains(stage.index)) Profile.bossesDefeated.Add(stage.index);
            Profile.bestStage = Mathf.Max(Profile.bestStage, stage.index);
            SaveStore.Save(Profile);
            int bonus = DB.Balance.nectar.stageClearBonus[Mathf.Clamp(stage.index - 1, 0, 2)];
            AddNectar(def.nectar + bonus, $"{stage.index}스테이지 보스");
            AddGold(stage.goldBoss, true);
            PlayStory($"stage{stage.index}_clear", () =>
            {
                if (stage.index >= DB.Stages.Count) FinishRunVictory();
                else NextStage();
            });
        }

        private void NextStage()
        {
            DestroyRoom();
            Run.stage++;
            Run.map = MapData.Generate(DB.Stage(Run.stage), Run.seed + Run.stage * 1013);
            Run.currentNode = -1;
            Avatar.Heal(Avatar.MaxHp * DB.Balance.economy.stageClearHealRatio, "스테이지 이동");
            PlayerGO.SetActive(false);
            SetState(GameState.Map);
            PlayStory($"stage{Run.stage}_intro", null);
        }

        public void OpenStage1Reward(RewardData data)
        {
            Reward = data;
            SetState(GameState.Reward);
        }

        public void CancelStage1Reward()
        {
            if (!InStage1 || State != GameState.Reward) return;
            Sfx.Play("ui");
            Reward = null;
            SetState(GameState.Node);
        }

        public void FinishReward()
        {
            Sfx.Play("ui");
            if (Reward != null && Reward.skipGold > 0 && !Reward.arrowTaken && (Reward.relics.Count == 0 || !Reward.relicTaken)) AddGold(Reward.skipGold, false);
            Reward = null;
            if (InStage1)
            {
                SetState(GameState.Node);
                return;
            }
            DestroyRoom();
            PlayerGO.SetActive(false);
            SetState(GameState.Map);
        }

        public bool TakeRewardArrow(ArrowDef a)
        {
            if (Reward == null || Reward.arrowTaken) return false;
            if (Run.NonRelicCardCount >= DB.Balance.player.quiverMax) { ShowToast("화살통이 가득 찼습니다"); return false; }
            if (InStage1 && Combat != null && Combat.Active)
            {
                if (!Combat.AddNewArrow(a.code)) { ShowToast("화살통이 가득 찼습니다 (Tab 상태창에서 분해 가능)"); return false; }
            }
            else
            {
                Run.AddCard(a.code);
            }
            Reward.arrowTaken = true;
            Sfx.Play("buy", 0.6f);
            ShowToast($"{a.name} 획득 — 화살통 맨 뒤에 추가");
            if (InStage1)
            {
                Stage1?.OnStage1RewardTaken(false);
                Reward = null;
                SetState(GameState.Node);
            }
            return true;
        }

        public bool TakeRewardRelic(RelicDef r)
        {
            if (Reward == null || Reward.relicTaken) return false;
            Run.AddRelic(r.id);
            RefreshStats(false);
            Reward.relicTaken = true;
            Sfx.Play("buy", 0.6f);
            ShowToast($"유물 {r.name} 획득");
            if (InStage1)
            {
                Stage1?.OnStage1RewardTaken(true);
                Reward = null;
                SetState(GameState.Node);
            }
            return true;
        }

        public bool TakeRewardGold()
        {
            if (Reward == null || Reward.relicTaken) return false;
            AddGold(Reward.skipGold, false);
            Reward.relicTaken = true;
            Reward.skipGold = 0;
            return true;
        }

        // ───────────── 상점·샘 ─────────────

        public void ResetStage1Shop() => ShopOffers = null;

        public void OpenStage1Shop(int seed)
        {
            if (Run == null) return;
            if (ShopOffers == null)
            {
                var rng = new System.Random(seed ^ 0x5409);
                ShopOffers = Loot.BuildShop(rng, Run);
                ShopRerolls = 0;
            }
            ShopRemoveMode = false;
            SetState(GameState.Shop);
        }

        public void CloseStage1Shop() => SetState(GameState.Node);

        public bool Buy(ShopOffer o)
        {
            if (Run == null || o.sold || Run.gold < o.price) { Sfx.Play("empty"); return false; }
            switch (o.kind)
            {
                case "arrow":
                    if (Run.NonRelicCardCount >= DB.Balance.player.quiverMax) { ShowToast("화살통이 가득 찼습니다"); return false; }
                    Run.AddCard(o.arrowCode);
                    break;
                case "relic":
                    Run.AddRelic(o.relicId);
                    RefreshStats(false);
                    break;
                case "heal":
                    Avatar.Heal(DB.Balance.economy.healPotionAmount, "물약");
                    break;
                case "ambrosia":
                    Run.bonusMaxHp += DB.Balance.economy.ambrosiaMaxHp;
                    RefreshStats(false);
                    Avatar.Heal(DB.Balance.economy.ambrosiaMaxHp, "암브로시아");
                    break;
            }
            Run.gold -= o.price;
            if (o.kind != "heal") o.sold = true;
            Sfx.Play("buy", 0.6f);
            return true;
        }

        public bool RemoveCard(ArrowCard c)
        {
            int cost = Loot.RemoveCost(Run);
            if (Run.gold < cost || c.fromRelic || Run.NonRelicCardCount <= 1) { Sfx.Play("empty"); return false; }
            Run.gold -= cost;
            Run.RemoveCard(c);
            Run.removeCount++;
            ShopRemoveMode = false;
            Sfx.Play("buy", 0.6f);
            ShowToast($"{c.Def.name} 제거");
            return true;
        }

        public bool RerollShop(bool useNectar)
        {
            var eco = DB.Balance.economy;
            if (useNectar)
            {
                if (Profile.nectar < eco.rerollNectar) { Sfx.Play("empty"); return false; }
                Profile.nectar -= eco.rerollNectar;
                Profile.nectarSpentTotal += eco.rerollNectar;
                SaveStore.Save(Profile);
            }
            else
            {
                int cost = eco.rerollGold + eco.rerollGoldStep * ShopRerolls;
                if (Run.gold < cost) { Sfx.Play("empty"); return false; }
                Run.gold -= cost;
            }
            ShopRerolls++;
            ShopOffers = Loot.BuildShop(new System.Random(Random.Range(1, 999999)), Run);
            Sfx.Play("ui");
            return true;
        }

        public int RerollGoldCost => DB.Balance.economy.rerollGold + DB.Balance.economy.rerollGoldStep * ShopRerolls;

        public void RestHeal()
        {
            if (RestUsed) return;
            RestUsed = true;
            Avatar.Heal(Avatar.MaxHp * DB.Balance.economy.restHealRatio, "샘");
            Sfx.Play("buy", 0.6f);
        }

        public void RestMeditate()
        {
            if (RestUsed) return;
            RestUsed = true;
            Run.bonusMaxHp += 6f;
            RefreshStats(false);
            Avatar.Heal(6f, "명상");
            Sfx.Play("buy", 0.6f);
        }

        public void LeaveNode()
        {
            Sfx.Play("ui");
            DestroyRoom();
            PlayerGO.SetActive(false);
            SetState(GameState.Map);
        }

        // ───────────── 사망·종료 ─────────────

        private void OnPlayerDied()
        {
            if (Run == null) return;
            if (Run.isTutorial)
            {
                // 튜토리얼에서는 패널티 없이 다시 일어선다
                Avatar.ApplyStats(Run.ComputeStats(Profile), true);
                Avatar.Teleport(Room.PlayerSpawn);
                ShowToast("아폴론이 다시 일어섰다 (튜토리얼)");
                return;
            }
            LastDeathCause = Room != null && Room.Boss != null ? Room.Boss.DisplayName : "적";
            EndRun(false);
            SetState(GameState.Death);
            Sfx.Play("hurt", 1f, 0.6f);
        }

        /// <summary>
        /// 도전 종료(사망·포기·클리어). 돈·화살·유물·도전 중 능력치·일시 효과는 Run 과 함께 버려진다.
        /// 넥타르·넥타르 강화·보스 해금 자격·구매 스킬은 Profile 에 있어 유지된다.
        /// </summary>
        private void EndRun(bool victory)
        {
            if (Run == null) return;
            LastRunNectar = Run.nectarEarned;
            LastRunStage = Run.stage;
            if (!victory) Profile.deaths++;
            Profile.bestStage = Mathf.Max(Profile.bestStage, victory ? Run.stage : Run.stage - 1);
            SaveStore.Save(Profile);
            DestroyRoom();
            Run = null;
            Paused = false;
            Time.timeScale = 1f;
            PlayerGO.SetActive(false);
        }

        public void AbandonRun()
        {
            Paused = false;
            Time.timeScale = 1f;
            if (Run != null && Run.isTutorial) { DestroyRoom(); Run = null; GoTitle(); return; }
            LastDeathCause = "도전 포기";
            EndRun(false);
            SetState(GameState.Death);
        }

        private void FinishRunVictory()
        {
            Profile.clears++;
            EndRun(true);
            SetState(GameState.Result);
        }

        // ───────────── 로비 ─────────────

        public RunState PracticeRun { get; private set; }

        public void GoLobby()
        {
            if (!SelfTestRunning && LoadSeparatedScene("Laurel_Lobby")) return;
            DestroyRoom();
            Run = null;
            Paused = false;
            Time.timeScale = 1f;
            Room = Room.CreateLobby(DB.Balance.world.lobbyWidth, lobbyTerrainStyle);
            PlacePlayer(Room);
            if (lobbyBackdrop != null) lobbyBackdrop.Attach(Room);
            if (lobbyDecor != null) lobbyDecor.Apply(Room);
            CameraRig.I.followY = true;
            StartPractice();
            ArcheryArmoryDisplays.SpawnLobbyInScene(Room);
            for (int i = 0; i < 2; i++) SpawnDummy(Room, new Vector2(Room.InnerBounds.xMax - 3f - i * 3f, Room.InnerBounds.yMin + 0.9f), true);
            SetState(GameState.Lobby);
            if (!Profile.lobbyIntroSeen)
            {
                Profile.lobbyIntroSeen = true;
                SaveStore.Save(Profile);
                PlayStory("lobby_first", null);
            }
        }

        public QuiverType CurrentQuiver { get; private set; } = QuiverType.Basic;

        /// <summary>로비 수련장: 고른 궁술 조합을 허수아비로 시험 (도전과 무관한 임시 상태)</summary>
        public void StartPractice()
        {
            if (Room == null) return;
            if (Combat.Active) Combat.EndRound();
            PracticeRun = new RunState { stage = 1, seed = 1234 };
            StyleNames.TryParseLaunch(Profile.launch, out PracticeRun.launch);
            StyleNames.TryParseRetrieval(Profile.retrieval, out PracticeRun.retrieval);
            PracticeRun.quiver = CurrentQuiver;
            var stats = PracticeRun.ComputeStats(Profile);
            int arrowCount = QuiverStats.ArrowCount(CurrentQuiver) + stats.extraStartArrows;
            for (int i = 0; i < arrowCount; i++) PracticeRun.AddCard(0);
            Avatar.ApplyStats(stats, true);
            Combat.BeginRound(Room, PracticeRun, PracticeRun.launch, PracticeRun.retrieval);
        }

        public TrainingDummy SpawnDummy(Room room, Vector2 pos, bool immortal)
        {
            var go = new GameObject("Dummy");
            go.transform.SetParent(room.transform, false);
            go.transform.position = pos;
            var d = go.AddComponent<TrainingDummy>();
            d.InitDummy(room, immortal);
            room.Enemies.Add(d);
            return d;
        }

        public void SelectLaunch(LaunchStyle s)
        {
            if (!Profile.StyleUnlocked("launch", s.ToString())) { Sfx.Play("empty"); return; }
            Profile.launch = s.ToString();
            SaveStore.Save(Profile);
            Sfx.Play("ui");
            if (State == GameState.ArcherySelect || State == GameState.Lobby || State == GameState.ArcheryTest) StartPractice();
        }

        public void SelectRetrieval(RetrievalStyle s)
        {
            if (!Profile.StyleUnlocked("retrieval", s.ToString())) { Sfx.Play("empty"); return; }
            Profile.retrieval = s.ToString();
            SaveStore.Save(Profile);
            Sfx.Play("ui");
            if (State == GameState.ArcheryTest && Combat != null && Combat.Active && PracticeRun != null)
            {
                PracticeRun.retrieval = s;
                Combat.SwapRetrieval(s);
                return;
            }
            if (State == GameState.ArcherySelect || State == GameState.Lobby || State == GameState.ArcheryTest) StartPractice();
        }

        public void SelectQuiver(QuiverType q)
        {
            CurrentQuiver = q;
            if (Profile != null)
            {
                Profile.quiver = q.ToString();
                SaveStore.Save(Profile);
            }
            Sfx.Play("ui");
            if (State == GameState.ArcherySelect || State == GameState.Lobby || State == GameState.ArcheryTest) StartPractice();
        }

        public bool BuyNectarNode(NectarNodeDef n)
        {
            if (!NectarShop.TryBuy(Profile, n)) { Sfx.Play("empty"); return false; }
            Sfx.Play("buy", 0.7f);
            ShowToast($"{n.name} 획득!");
            if (Room != null && State != GameState.Title) StartPractice();
            return true;
        }

        public void OpenPanel(GameState s)
        {
            Sfx.Play("ui");
            SetState(s);
        }

        public void ClosePanel() { Sfx.Play("ui"); SetState(InArcheryTest ? GameState.ArcheryTest : GameState.Lobby); }

        // ───────────── 궁술 테스트 씬 ─────────────

        /// <summary>
        /// 정적으로 씬에 배치된 궁술 테스트 맵을 초기화한다.
        /// (지형·발판·배경·허수아비는 Laurel_ArcheryTest.unity 에 정적 배치되어 있으므로 런타임 생성하지 않는다.)
        /// </summary>
        public void StartArcheryTest()
        {
            var t = ArcheryTest;
            Run = null;
            Paused = false;
            Time.timeScale = 1f;

            Room = FindAnyObjectByType<Room>();
            if (Room == null)
            {
                Debug.LogError("[Laurel] Laurel_ArcheryTest 씬에 Room 컴포넌트가 없습니다.");
                return;
            }
            Vector2 spawn = t.playerSpawnPoint != null ? (Vector2)t.playerSpawnPoint.position : t.playerSpawn + Vector2.up * 1.2f;
            Room.InitStaticArena(t.MakeTheme(), t.Width, t.Height, spawn);
            Room.Enemies.Clear();

            PlacePlayer(Room);
            ArcheryArmoryDisplays.SpawnInScene(Room);
            CameraRig.I.followY = true;
            Avatar.minHp = 1f; // 맞기는 하지만 쓰러지지 않는다
            StartPractice();
            SetState(GameState.ArcheryTest);
        }

        // ───────────── 일시정지·입력 ─────────────

        public void SetPaused(bool p)
        {
            bool canPause = State == GameState.Node || State == GameState.Tutorial || State == GameState.Lobby;
            if (p && !canPause) return;
            Paused = p;
            if (p) StatusOpen = false;
            Time.timeScale = (p || StatusOpen) ? 0f : 1f;
            Avatar?.SetInputEnabled(!p && !StatusOpen && canPause);
        }

        private void Update()
        {
            if (toastT > 0f) toastT -= Time.unscaledDeltaTime;
            UpdateTutorial();
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null || SelfTestRunning) return;
            if (k.tabKey.wasPressedThisFrame)
            {
                bool canStatus = State == GameState.Node || State == GameState.Tutorial || State == GameState.Lobby || State == GameState.ArcheryTest;
                if (canStatus && !Paused) ToggleStatus();
            }
            if (k.escapeKey.wasPressedThisFrame)
            {
                if (StatusOpen) SetStatusOpen(false);
                else if (InStage1 && State == GameState.Reward) CancelStage1Reward();
                else if (State == GameState.NectarTree || State == GameState.ArcherySelect || State == GameState.Records) ClosePanel();
                else if (State == GameState.ArcheryTest) OpenPanel(GameState.ArcherySelect); // 테스트 씬: 일시정지 대신 궁술 선택
                else SetPaused(!Paused);
            }
            if (State == GameState.Story && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.eKey.wasPressedThisFrame)) AdvanceStory();
            if (k.f12Key.wasPressedThisFrame) { DevMode = !DevMode; ShowToast(DevMode ? "개발자 모드 켬 (F9 넥타르+20, F10 방 정리, F11 보스 해금 전체)" : "개발자 모드 끔"); }
            if (DevMode)
            {
                if (k.f9Key.wasPressedThisFrame) AddNectar(20, "개발자 모드");
                if (k.f10Key.wasPressedThisFrame && Room != null) Room.DebugKillAll();
                if (k.f11Key.wasPressedThisFrame) { for (int s = 1; s <= 3; s++) if (!Profile.bossesDefeated.Contains(s)) Profile.bossesDefeated.Add(s); SaveStore.Save(Profile); ShowToast("보스 구매 자격 전체 해금 (개발자 모드)"); }
            }
            else if (IsBlitzMode)
            {
                if (k.f9Key.wasPressedThisFrame && Room != null) { Room.DebugKillAll(); ShowToast("블리츠: 현재 방 정리"); }
                if (k.f10Key.wasPressedThisFrame && InStage1 && Stage1 != null) Stage1.DebugJumpToBoss();
            }
#endif
        }

        // ───────────── 테스트 지원 ─────────────

        public void ForceState(GameState s) => SetState(s);

        public Room BuildTestRoom(int layout, int seed)
        {
            DestroyRoom();
            Room = Room.Create("Test", RoomTheme.Tutorial, DB.Balance.world.arenaSize, layout, seed);
            PlacePlayer(Room);
            return Room;
        }

        public RunState NewTestRun(LaunchStyle l, RetrievalStyle r, IList<int> arrows)
        {
            Run = new RunState { stage = 1, seed = 77, launch = l, retrieval = r };
            foreach (var c in arrows) Run.AddCard(c);
            Avatar.ApplyStats(Run.ComputeStats(Profile), true);
            return Run;
        }

        public void ClearTestRun()
        {
            DestroyRoom();
            Run = null;
        }
    }
}
