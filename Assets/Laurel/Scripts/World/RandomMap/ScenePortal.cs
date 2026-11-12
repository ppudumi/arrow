using System.Collections.Generic;
using Procedural2D;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    /// <summary>
    /// 씬 사이 이동 상태 (정적). 씬을 바꿔도 남는 것은 이것뿐이고, 맵 오브젝트·플레이어는 씬과 함께 정리된다.
    /// </summary>
    public static class PortalTravel
    {
        /// <summary>씬을 불러오는 중이면 true → 다른 포탈 입력을 막는다 (키 한 번에 중복 입장 방지)</summary>
        public static bool Loading { get; private set; }
        /// <summary>다음 랜덤 맵에 쓸 시드 (포탈로 들어올 때 정함). null 이면 랜덤 맵이 스스로 정한다</summary>
        public static int? PendingSeed;
        /// <summary>도착할 포탈 id. 도착 씬에서 이 id 의 포탈 앞에 플레이어를 세운다</summary>
        public static string PendingArrival;
        /// <summary>테스트용: 지금까지 포탈로 이동한 횟수</summary>
        public static int TravelCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Loading = false; PendingSeed = null; PendingArrival = null; TravelCount = 0;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Loading = false; }

        /// <summary>씬 이동. 이미 이동 중이면 false</summary>
        public static bool Go(string scenePath, string arrivalId, int? seed)
        {
            if (Loading) return false;
            Loading = true;
            TravelCount++;
            PendingArrival = arrivalId;
            PendingSeed = seed;
#if UNITY_EDITOR
            // 에디터에서는 Build Settings 에 넣지 않은 테스트 씬도 경로로 바로 연다 (본편 빌드 목록은 건드리지 않음)
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
#endif
            return true;
        }

        public static int NewSeed() => new System.Random(System.Guid.NewGuid().GetHashCode() ^ System.Environment.TickCount).Next(1, int.MaxValue);
    }

    /// <summary>
    /// 문(포탈). 플레이어가 범위 안에 있으면 'W키로 입장' 안내를 띄우고, W 를 누르면 대상 씬으로 이동한다.
    /// 범위 밖에서는 반응하지 않고, 이동이 시작되면 PortalTravel.Loading 으로 추가 입력을 막는다.
    /// 그림은 런타임 도형(Art)으로 만든다. 씬에서 위치는 포탈 바닥 가운데(서 있는 면).
    /// </summary>
    public class ScenePortal : MonoBehaviour
    {
        [Tooltip("이 포탈의 id. 다른 씬의 포탈이 arrivalId 로 이 id 를 가리키면 그 씬에서 넘어올 때 이 포탈 앞에 선다")]
        public string portalId = "portal";
        [Tooltip("이동할 씬 경로")]
        public string targetScene = "Assets/Laurel/Scenes/RandomMap_Test.unity";
        [Tooltip("도착 씬에서 플레이어를 세울 포탈 id")]
        public string arrivalId = "";
        [Tooltip("켜면 이동할 때 새 랜덤 맵 시드를 정한다 (랜덤 맵으로 들어가는 포탈)")]
        public bool rollRandomMapSeed = true;
        [Tooltip("시드 설정 (고정 시드 사용 여부). 비우면 항상 새 시드")]
        public RandomMapSettings seedSettings;
        public string prompt = "W키로 입장";

        [Header("범위 (포탈 바닥 가운데 기준, 월드 유닛)")]
        public Vector2 rangeHalfSize = new Vector2(1.4f, 1.6f);
        public Vector2 rangeOffset = new Vector2(0f, 1.2f);

        [Header("그림")]
        public Vector2 size = new Vector2(1.8f, 2.6f);
        public Color frameColor = new Color32(0x2A, 0x1E, 0x14, 0xFF);
        public Color glowColor = new Color(1f, 0.82f, 0.45f, 1f);

        public bool PlayerInRange { get; private set; }
        public int EnterCount { get; private set; }

        private Transform player;
        private Collider2D playerCol;
        private SpriteRenderer glow, core;
        private static Font font;
        private GUIStyle style;

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            FindPlayer();
            // 다른 씬에서 이 포탈로 도착했으면 포탈 앞에 세운다
            if (!string.IsNullOrEmpty(PortalTravel.PendingArrival) && PortalTravel.PendingArrival == portalId)
            {
                PortalTravel.PendingArrival = null;
                PlaceAtPortal(player, (Vector2)transform.position + Vector2.right * 1.6f);
            }
        }

        private void FindPlayer()
        {
            if (player != null) return;
            var c = FindAnyObjectByType<ProceduralCharacterController>();
            if (c == null) return;
            player = c.transform;
            playerCol = c.GetComponent<Collider2D>();
        }

        /// <summary>플레이어의 발을 standOn 에 맞춰 옮기고 움직임 상태를 초기화한다</summary>
        public static void PlaceAtPortal(Transform p, Vector2 standOn)
        {
            if (p == null) return;
            var col = p.GetComponent<Collider2D>();
            float feet = col != null ? p.position.y - col.bounds.min.y : 0f;
            p.position = new Vector3(standOn.x, standOn.y + feet + 0.02f, p.position.z);
            var rb = p.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
            // (Unity 컴포넌트는 ?. 대신 명시적으로 검사: 없는 컴포넌트가 에디터에서 가짜 null 로 올 수 있음)
            var ctrl = p.GetComponent<ProceduralCharacterController>();
            if (ctrl != null) ctrl.ResetMotionState();
            Physics2D.SyncTransforms();
            var ribbon = p.GetComponent<ProceduralWaistRibbon>();
            if (ribbon != null) ribbon.SnapToAnchor();
            var skirt = p.GetComponent<ProceduralSkirtPhysics>();
            if (skirt != null) skirt.ResetMotion();
        }

        private void Build()
        {
            if (transform.Find("Visual") != null) return;
            var v = new GameObject("Visual").transform;
            v.SetParent(transform, false);
            // 아치 모양 문: 어두운 테두리 + 안쪽 빛
            Art.Quad(v, "Frame", Art.Square, frameColor, new Vector2(size.x, size.y - size.x * 0.5f), 4, new Vector3(0f, (size.y - size.x * 0.5f) * 0.5f, 0f));
            Art.Quad(v, "FrameTop", Art.Circle, frameColor, new Vector2(size.x, size.x), 4, new Vector3(0f, size.y - size.x * 0.5f, 0f));
            float inner = size.x * 0.72f;
            core = Art.Quad(v, "Core", Art.Square, glowColor.WithAlpha(0.85f), new Vector2(inner, size.y - size.x * 0.5f - 0.1f), 5, new Vector3(0f, (size.y - size.x * 0.5f) * 0.5f + 0.05f, 0f));
            Art.Quad(v, "CoreTop", Art.Circle, glowColor.WithAlpha(0.85f), new Vector2(inner, inner), 5, new Vector3(0f, size.y - size.x * 0.5f, 0f));
            glow = Art.Quad(v, "Glow", Art.Soft, glowColor.WithAlpha(0.45f), new Vector2(size.x * 2.2f, size.y * 1.6f), 3, new Vector3(0f, size.y * 0.5f, 0f));
        }

        private Bounds Range => new Bounds((Vector2)transform.position + rangeOffset, rangeHalfSize * 2f);

        private void Update()
        {
            FindPlayer();
            Vector2 p = playerCol != null ? (Vector2)playerCol.bounds.center : (player != null ? (Vector2)player.position : new Vector2(9999, 9999));
            var r = Range;
            PlayerInRange = player != null && player.gameObject.activeInHierarchy && Mathf.Abs(p.x - r.center.x) <= r.extents.x && Mathf.Abs(p.y - r.center.y) <= r.extents.y;

            if (glow != null)
            {
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
                glow.color = glowColor.WithAlpha((PlayerInRange ? 0.55f : 0.3f) + 0.15f * k);
            }

            if (PlayerInRange && !PortalTravel.Loading && EnterPressed()) Enter();
        }

        private static bool EnterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && k.wKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.W);
#endif
        }

        /// <summary>입장. 범위 밖이거나 이미 이동 중이면 아무것도 하지 않는다</summary>
        public bool Enter()
        {
            if (!PlayerInRange || PortalTravel.Loading) return false;
            int? seed = null;
            if (rollRandomMapSeed) seed = seedSettings != null && seedSettings.useFixedSeed ? seedSettings.fixedSeed : PortalTravel.NewSeed();
            if (!PortalTravel.Go(targetScene, arrivalId, seed)) return false;
            EnterCount++;
            // 이동하는 동안 조작을 멈춘다
            var c = player != null ? player.GetComponent<ProceduralCharacterController>() : null;
            if (c != null) c.inputEnabled = false;
            Sfx.Play("ui", 0.6f);
            return true;
        }

        private void OnGUI()
        {
            if (!PlayerInRange || PortalTravel.Loading) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (style == null)
            {
                if (font == null)
                {
                    try { font = Font.CreateDynamicFontFromOSFont(new[] { "Apple SD Gothic Neo", "AppleSDGothicNeo-Regular", "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans CJK KR", "Arial Unicode MS" }, 28); }
                    catch { font = null; }
                }
                style = new GUIStyle(GUI.skin.label) { font = font, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            }
            float scale = Screen.height / 1080f;
            style.fontSize = Mathf.RoundToInt(30 * scale);
            var sp = cam.WorldToScreenPoint((Vector2)transform.position + Vector2.up * (size.y + 0.7f));
            var rect = new Rect(sp.x - 200 * scale, Screen.height - sp.y - 24 * scale, 400 * scale, 48 * scale);
            // 불투명 외곽선 (Linear 색 공간에서도 밝은 배경 위에서 읽히게)
            style.normal.textColor = new Color(0.03f, 0.02f, 0.01f, 1f);
            float o = 2f * scale;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (dx != 0 || dy != 0) GUI.Label(new Rect(rect.x + dx * o, rect.y + dy * o, rect.width, rect.height), prompt, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, prompt, style);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireCube((Vector2)transform.position + Vector2.up * size.y * 0.5f, size);
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.6f);
            var r = Range;
            Gizmos.DrawWireCube(r.center, r.size);
        }
    }
}
