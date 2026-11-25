using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    /// <summary>
    /// 전역 개발자용 성능·디버그 오버레이 (화면 우하단, F3 키로 표시/숨김 토글).
    /// </summary>
    public class DevStatsOverlay : MonoBehaviour
    {
        private static DevStatsOverlay instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (instance != null) return;
            var existing = FindAnyObjectByType<DevStatsOverlay>();
            if (existing != null) { instance = existing; return; }
            var go = new GameObject("DevStatsOverlay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DevStatsOverlay>();
        }

        private bool visible = true;
        private float smoothDt = 1f / 60f;
        private float curFps = 60f;
        private float minFps = float.MaxValue;
        private float maxFps = 0f;
        private float avgFps = 60f;
        private double fpsSum;
        private int fpsSamples;
        private float warmup = 0.5f;
        private float sampleTimer;

        private float allocMb;
        private float reservedMb;
        private float gcMb;
        private float memTimer;

        private GUIStyle headerStyle;
        private GUIStyle textStyle;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.f3Key.wasPressedThisFrame) visible = !visible;
                if (k.f4Key.wasPressedThisFrame) ResetStats();
            }
#endif
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0.0001f || dt > 1f) return;

            smoothDt = Mathf.Lerp(smoothDt, dt, 0.12f);
            float instFps = 1f / smoothDt;

            if (warmup > 0f)
            {
                warmup -= dt;
                curFps = instFps;
            }
            else
            {
                sampleTimer += dt;
                if (sampleTimer >= 0.2f)
                {
                    sampleTimer = 0f;
                    curFps = instFps;
                    if (curFps < minFps) minFps = curFps;
                    if (curFps > maxFps) maxFps = curFps;
                    fpsSum += curFps;
                    fpsSamples++;
                    avgFps = (float)(fpsSum / fpsSamples);
                }
            }

            memTimer -= dt;
            if (memTimer <= 0f)
            {
                memTimer = 0.5f;
                const float toMb = 1f / (1024f * 1024f);
                allocMb = Profiler.GetTotalAllocatedMemoryLong() * toMb;
                reservedMb = Profiler.GetTotalReservedMemoryLong() * toMb;
                gcMb = System.GC.GetTotalMemory(false) * toMb;
            }
        }

        private void ResetStats()
        {
            minFps = float.MaxValue;
            maxFps = 0f;
            fpsSum = 0;
            fpsSamples = 0;
            avgFps = curFps;
        }

        private void OnGUI()
        {
            if (!visible) return;

            const float VW = 1920f, VH = 1080f;
            float scale = Mathf.Min(Screen.width / VW, Screen.height / VH);
            if (scale <= 0.001f) return;

            GUI.depth = 90;
            Matrix4x4 prev = GUI.matrix;
            float ox = (Screen.width - VW * scale) * 0.5f;
            float oy = (Screen.height - VH * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            if (headerStyle == null)
                headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, richText = true };
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft, richText = true };

            const float w = 310f;
            const float h = 196f;
            var panel = new Rect(VW - w - 18f, VH - h - 42f, w, h);

            GUI.color = new Color(0.06f, 0.06f, 0.08f, 0.84f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.85f, 0.95f, 0.9f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = panel.x + 12f;
            float y = panel.y + 8f;
            float lw = panel.width - 24f;

            headerStyle.normal.textColor = new Color(0.7f, 0.92f, 1f);
            GUI.Label(new Rect(x, y, lw, 22f), "DEV MONITOR  <color=#8899AA>(F3 숨김 · F4 리셋)</color>", headerStyle);
            y += 24f;

            float ms = curFps > 0.1f ? 1000f / curFps : 0f;
            string fpsColor = curFps >= 55f ? "#7BE88A" : (curFps >= 30f ? "#FFD45A" : "#FF6B6B");
            float showMin = minFps < float.MaxValue ? minFps : curFps;
            float showMax = maxFps > 0f ? maxFps : curFps;

            textStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            GUI.Label(new Rect(x, y, lw, 22f), $"FPS   <color={fpsColor}><b>{curFps:0.0}</b></color> ({ms:0.0} ms)", textStyle);
            y += 22f;
            GUI.Label(new Rect(x, y, lw, 22f), $"최소/평균/최대   {showMin:0} / {avgFps:0} / {showMax:0}", textStyle);
            y += 22f;
            GUI.Label(new Rect(x, y, lw, 22f), $"메모리   {allocMb:0.0} MB (예약 {reservedMb:0.0} MB)", textStyle);
            y += 22f;
            GUI.Label(new Rect(x, y, lw, 22f), $"GC 힙   {gcMb:0.0} MB", textStyle);
            y += 22f;

            string vsync = QualitySettings.vSyncCount > 0 ? $"VSync {QualitySettings.vSyncCount}" : $"Cap {Application.targetFrameRate}";
            GUI.Label(new Rect(x, y, lw, 22f), $"화면   {Screen.width}×{Screen.height} · {vsync}", textStyle);
            y += 22f;

            string sceneName = SceneManager.GetActiveScene().name;
            string stateName = GameRoot.I != null ? GameRoot.I.State.ToString() : "-";
            GUI.Label(new Rect(x, y, lw, 22f), $"씬/상태   {sceneName} ({stateName})", textStyle);
            y += 22f;

            string gpu = SystemInfo.graphicsDeviceName;
            if (gpu.Length > 28) gpu = gpu.Substring(0, 28) + "…";
            GUI.Label(new Rect(x, y, lw, 22f), $"GPU   <color=#B8C4D0>{gpu}</color>", textStyle);

            GUI.matrix = prev;
        }
    }
}
