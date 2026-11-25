using UnityEngine;
using UnityEngine.Rendering;

namespace Laurel
{
    public enum PixelRenderMode
    {
        OutlineOnly = 0,
        PlayerAndOutline = 1,
        FullScene = 2
    }

    /// <summary>
    /// URP 호환 픽셀 렌더링 효과 + 화면 우측 상단 드래그 슬라이더.
    /// 씬의 Main Camera 에 부착하고 자식 카메라(PixelRenderCam)를 연결해 사용한다.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class PixelRenderEffect : MonoBehaviour
    {
        public const int MinPixelSize = 1;
        public const int MaxPixelSize = 8;
        public const int MinOutlineThickness = 0;
        public const int MaxOutlineThickness = 4;

        public static bool IsPointerOverPixelSlider { get; private set; }
        public static int GlobalPixelSize { get; private set; } = 3;
        public static int GlobalOutlineThickness { get; private set; } = 1;
        public static PixelRenderMode GlobalRenderMode { get; private set; } = PixelRenderMode.FullScene;
        private static bool globalInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobalState()
        {
            globalInitialized = false;
            IsPointerOverPixelSlider = false;
        }

        [Range(MinPixelSize, MaxPixelSize)]
        public int pixelSize = 3;
        [Range(MinOutlineThickness, MaxOutlineThickness)]
        public int outlineThickness = 1;
        public PixelRenderMode renderMode = PixelRenderMode.FullScene;
        public Camera subCam;

        private const int PlayerOutlineRenderLayer = 31;

        private Camera mainCam;
        private Camera charCam;
        private int savedCullingMask;
        private RenderTexture rt;
        private RenderTexture charRt;
        private int rtW, rtH;
        private bool draggingPixel;
        private bool draggingOutline;
        private GUIStyle labelStyle;
        private GUIStyle radioLabelStyle;
        private Material outlineMat;
        private readonly System.Collections.Generic.List<(Transform t, int layer)> savedLayers = new System.Collections.Generic.List<(Transform, int)>(16);
        private readonly System.Collections.Generic.List<(Transform t, int layer)> mainCamHiddenLayers = new System.Collections.Generic.List<(Transform, int)>(16);

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RestoreMainCamHiddenLayers();
        }

        private void Awake()
        {
            mainCam = GetComponent<Camera>();
            if (mainCam == null) { enabled = false; return; }
            savedCullingMask = mainCam.cullingMask;

            if (!globalInitialized)
            {
                GlobalPixelSize = Mathf.Clamp(pixelSize, MinPixelSize, MaxPixelSize);
                GlobalOutlineThickness = Mathf.Clamp(outlineThickness, MinOutlineThickness, MaxOutlineThickness);
                GlobalRenderMode = renderMode;
                globalInitialized = true;
            }
            else
            {
                pixelSize = GlobalPixelSize;
                outlineThickness = GlobalOutlineThickness;
                renderMode = GlobalRenderMode;
            }
        }

        private void LateUpdate()
        {
            if (mainCam == null || subCam == null) return;

            if (globalInitialized)
            {
                pixelSize = GlobalPixelSize;
                outlineThickness = GlobalOutlineThickness;
                renderMode = GlobalRenderMode;
            }

            bool needFullSceneRt = renderMode == PixelRenderMode.FullScene && pixelSize > 1;
            bool needCharRt = renderMode != PixelRenderMode.FullScene || pixelSize > 1;
            if (!needCharRt)
            {
                if (subCam.enabled) subCam.enabled = false;
                if (mainCam.cullingMask != savedCullingMask) mainCam.cullingMask = savedCullingMask;
                return;
            }

            int wantW = Mathf.Max(64, Screen.width / pixelSize);
            int wantH = Mathf.Max(36, Screen.height / pixelSize);
            if (rt == null || rtW != wantW || rtH != wantH)
            {
                if (rt != null) rt.Release();
                if (charRt != null) charRt.Release();
                rtW = wantW;
                rtH = wantH;
                rt = new RenderTexture(rtW, rtH, 24, RenderTextureFormat.Default)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                rt.Create();
                charRt = new RenderTexture(rtW, rtH, 16, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                charRt.Create();
            }

            subCam.orthographic = mainCam.orthographic;
            subCam.orthographicSize = mainCam.orthographicSize;

            if (needFullSceneRt)
            {
                subCam.backgroundColor = mainCam.backgroundColor;
                subCam.clearFlags = mainCam.clearFlags;
                subCam.cullingMask = savedCullingMask;
                subCam.targetTexture = rt;
                subCam.enabled = true;
                mainCam.cullingMask = 0;
            }
            else
            {
                if (subCam.enabled) subCam.enabled = false;
                mainCam.cullingMask = renderMode == PixelRenderMode.PlayerAndOutline
                    ? savedCullingMask & ~(1 << PlayerOutlineRenderLayer)
                    : savedCullingMask;
            }

            RenderPlayerSilhouette();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != mainCam || renderMode != PixelRenderMode.PlayerAndOutline) return;
            var playerGo = GameRoot.I != null ? GameRoot.I.PlayerGO : null;
            if (playerGo == null || !playerGo.activeInHierarchy) return;

            mainCamHiddenLayers.Clear();
            var renderers = playerGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var t = renderers[i].transform;
                mainCamHiddenLayers.Add((t, t.gameObject.layer));
                t.gameObject.layer = PlayerOutlineRenderLayer;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != mainCam) return;
            RestoreMainCamHiddenLayers();
        }

        private void RestoreMainCamHiddenLayers()
        {
            for (int i = 0; i < mainCamHiddenLayers.Count; i++)
            {
                if (mainCamHiddenLayers[i].t != null)
                    mainCamHiddenLayers[i].t.gameObject.layer = mainCamHiddenLayers[i].layer;
            }
            mainCamHiddenLayers.Clear();
        }

        private void RenderPlayerSilhouette()
        {
            var playerGo = GameRoot.I != null ? GameRoot.I.PlayerGO : null;
            if (playerGo == null || !playerGo.activeInHierarchy || charRt == null) return;

            if (charCam == null)
            {
                var go = new GameObject("PixelCharCam");
                go.transform.SetParent(subCam.transform, false);
                charCam = go.AddComponent<Camera>();
                charCam.enabled = false;
            }
            charCam.orthographic = mainCam.orthographic;
            charCam.orthographicSize = mainCam.orthographicSize;
            charCam.clearFlags = CameraClearFlags.SolidColor;
            charCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            charCam.cullingMask = 1 << PlayerOutlineRenderLayer;
            charCam.targetTexture = charRt;

            savedLayers.Clear();
            var renderers = playerGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var t = renderers[i].transform;
                savedLayers.Add((t, t.gameObject.layer));
                t.gameObject.layer = PlayerOutlineRenderLayer;
            }
            charCam.Render();
            for (int i = 0; i < savedLayers.Count; i++)
            {
                if (savedLayers[i].t != null) savedLayers[i].t.gameObject.layer = savedLayers[i].layer;
            }
        }

        private void OnGUI()
        {
            GUI.depth = 100;

            bool drawFullScene = renderMode == PixelRenderMode.FullScene && pixelSize > 1 && rt != null;
            bool drawCharOverlay = (renderMode != PixelRenderMode.FullScene || pixelSize > 1)
                && charRt != null && GameRoot.I != null && GameRoot.I.PlayerGO != null && GameRoot.I.PlayerGO.activeInHierarchy;

            if (Event.current.type == EventType.Repaint)
            {
                if (drawFullScene)
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), rt, ScaleMode.StretchToFill, false);

                if (drawCharOverlay)
                {
                    if (outlineMat == null)
                    {
                        var sh = Resources.Load<Shader>("LaurelFx/PixelOutline") ?? Shader.Find("Laurel/PixelOutline");
                        if (sh != null) outlineMat = new Material(sh);
                    }
                    if (outlineMat != null)
                    {
                        outlineMat.SetFloat("_Thickness", outlineThickness);
                        outlineMat.SetFloat("_OutlineOnly", renderMode == PixelRenderMode.OutlineOnly ? 1f : 0f);
                        Graphics.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), charRt, outlineMat);
                    }
                }
            }

            if (GameRoot.I == null || (!GameRoot.I.InArcheryTest && !GameRoot.I.InStage1))
            {
                IsPointerOverPixelSlider = false;
                draggingPixel = false;
                draggingOutline = false;
                return;
            }

            const float VW = 1920f, VH = 1080f;
            float scale = Mathf.Min(Screen.width / VW, Screen.height / VH);
            if (scale <= 0.001f) return;

            Matrix4x4 prev = GUI.matrix;
            float ox = (Screen.width - VW * scale) * 0.5f;
            float oy = (Screen.height - VH * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            var panel = new Rect(VW - 296f, 24f, 272f, 224f);
            GUI.color = new Color(0.08f, 0.07f, 0.06f, 0.9f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.68f, 0.3f, 0.95f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            if (radioLabelStyle == null)
                radioLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft };

            string pixelTxt = pixelSize <= 1 ? "픽셀 크기  원본 (1x)" : $"픽셀 크기  {pixelSize}x";
            int nextPixel = DrawIntSlider(panel.x, panel.y + 6f, panel.width, pixelTxt, pixelSize, MinPixelSize, MaxPixelSize, ref draggingPixel);
            if (nextPixel != pixelSize)
            {
                pixelSize = nextPixel;
                GlobalPixelSize = nextPixel;
            }

            string outlineTxt = outlineThickness <= 0 ? "윤곽 두께  끔 (0px)" : $"윤곽 두께  {outlineThickness}px";
            int nextOutline = DrawIntSlider(panel.x, panel.y + 68f, panel.width, outlineTxt, outlineThickness, MinOutlineThickness, MaxOutlineThickness, ref draggingOutline);
            if (nextOutline != outlineThickness)
            {
                outlineThickness = nextOutline;
                GlobalOutlineThickness = nextOutline;
            }

            GUI.color = new Color(0.32f, 0.28f, 0.22f, 0.8f);
            GUI.DrawTexture(new Rect(panel.x + 12f, panel.y + 126f, panel.width - 24f, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            DrawRadioOption(panel.x, panel.y + 132f, panel.width, "플레이어의 윤곽선만", PixelRenderMode.OutlineOnly);
            DrawRadioOption(panel.x, panel.y + 160f, panel.width, "플레이어 + 윤곽선", PixelRenderMode.PlayerAndOutline);
            DrawRadioOption(panel.x, panel.y + 188f, panel.width, "기존 상태 유지", PixelRenderMode.FullScene);

            Vector2 mp = Event.current.mousePosition;
            IsPointerOverPixelSlider = panel.Contains(mp) || draggingPixel || draggingOutline;
            GUI.matrix = prev;
        }

        private void DrawRadioOption(float x, float y, float width, string labelText, PixelRenderMode option)
        {
            var row = new Rect(x + 12f, y, width - 24f, 26f);
            bool selected = renderMode == option;

            var outer = new Rect(row.x + 2f, row.y + 5f, 16f, 16f);
            GUI.color = selected ? new Color(0.85f, 0.68f, 0.3f, 1f) : new Color(0.45f, 0.42f, 0.38f, 1f);
            GUI.DrawTexture(outer, Texture2D.whiteTexture);

            var innerBg = new Rect(outer.x + 2f, outer.y + 2f, 12f, 12f);
            GUI.color = new Color(0.12f, 0.1f, 0.09f, 1f);
            GUI.DrawTexture(innerBg, Texture2D.whiteTexture);

            if (selected)
            {
                var dot = new Rect(outer.x + 4f, outer.y + 4f, 8f, 8f);
                GUI.color = new Color(0.45f, 0.92f, 0.65f, 1f);
                GUI.DrawTexture(dot, Texture2D.whiteTexture);
            }

            radioLabelStyle.normal.textColor = selected ? new Color(1f, 0.95f, 0.82f) : new Color(0.75f, 0.72f, 0.66f);
            GUI.color = Color.white;
            GUI.Label(new Rect(row.x + 26f, row.y, row.width - 26f, row.height), labelText, radioLabelStyle);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                renderMode = option;
                GlobalRenderMode = option;
                e.Use();
            }
        }

        private int DrawIntSlider(float x, float y, float width, string labelText, int value, int minVal, int maxVal, ref bool isDragging)
        {
            labelStyle.normal.textColor = new Color(1f, 0.93f, 0.78f);
            GUI.Label(new Rect(x + 12f, y, width - 24f, 24f), labelText, labelStyle);

            var track = new Rect(x + 14f, y + 36f, width - 28f, 8f);
            GUI.color = new Color(0.22f, 0.2f, 0.18f, 1f);
            GUI.DrawTexture(track, Texture2D.whiteTexture);

            float ratio = (value - minVal) / (float)Mathf.Max(1, maxVal - minVal);
            GUI.color = new Color(0.45f, 0.92f, 0.65f, 1f);
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * ratio, track.height), Texture2D.whiteTexture);

            float knobX = track.x + track.width * ratio;
            GUI.color = new Color(1f, 0.9f, 0.45f, 1f);
            GUI.DrawTexture(new Rect(knobX - 7f, track.y - 6f, 14f, 20f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Event e = Event.current;
            Vector2 mp = e.mousePosition;
            var hitArea = new Rect(track.x - 10f, track.y - 12f, track.width + 20f, 32f);
            if (e.type == EventType.MouseDown && e.button == 0 && hitArea.Contains(mp))
            {
                isDragging = true;
                value = SnapIntFromMouse(mp.x, track, minVal, maxVal);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDragging)
            {
                value = SnapIntFromMouse(mp.x, track, minVal, maxVal);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && isDragging)
            {
                isDragging = false;
                e.Use();
            }
            return value;
        }

        private static int SnapIntFromMouse(float mouseX, Rect track, int minVal, int maxVal)
        {
            float t = Mathf.Clamp01((mouseX - track.x) / Mathf.Max(1f, track.width));
            return Mathf.RoundToInt(Mathf.Lerp(minVal, maxVal, t));
        }

        private void OnDestroy()
        {
            IsPointerOverPixelSlider = false;
            if (mainCam != null) mainCam.cullingMask = savedCullingMask;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (charRt != null) { charRt.Release(); Destroy(charRt); }
            if (outlineMat != null) Destroy(outlineMat);
        }
    }
}
