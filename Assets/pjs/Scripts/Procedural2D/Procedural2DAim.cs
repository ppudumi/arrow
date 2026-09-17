using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Procedural2D
{
    /// <summary>
    /// 화살 회수 실행 트리거 방식
    /// </summary>
    public enum RecallTriggerMode
    {
        [InspectorName("반경 내 자동 회수 + 키 입력 시 전체 회수 (기본)")]
        AutoNearbyAndManualGlobal = 0,

        [InspectorName("키 입력 시 반경 내 화살만 회수 (수동 근접)")]
        ManualNearbyOnly = 1,

        [InspectorName("키 입력 시 맵 전체 화살 회수 (수동 전체)")]
        ManualGlobalOnly = 2,

        [InspectorName("반경 내 접근 시 자동 회수만 (수동 키 비활성화)")]
        AutoNearbyOnly = 3,

        [InspectorName("화살을 모두 소비했을 때만")]
        OnlyWhenEmpty = 4
    }

    /// <summary>
    /// 화살이 회수 가능한 상태 조건
    /// </summary>
    public enum RecallConditionMode
    {
        [InspectorName("명중 또는 정지된 화살만 (기본)")]
        StuckOrStopped = 0,

        [InspectorName("벽이나 적에 꽂힌 화살만 (명중 전용)")]
        HitOnly = 1,

        [InspectorName("날아가는 화살 포함 모든 화살")]
        AllArrows = 2,

        [InspectorName("바닥에 버려진 화살만")]
        DroppedOnly = 3
    }

    /// <summary>
    /// 수동 회수 실행 단축키
    /// </summary>
    public enum RecallKey
    {
        [InspectorName("G")]
        G = 0,
        [InspectorName("E")]
        E = 1,
        [InspectorName("F")]
        F = 2,
        [InspectorName("R")]
        R = 3,
        [InspectorName("스페이스바 (Space)")]
        Space = 4,
        [InspectorName("우클릭 (Right Click)")]
        RightClick = 5,
        [InspectorName("좌클릭 (Left Click)")]
        LeftClick = 6
    }

    /// <summary>
    /// 탄약 완충 시 회수 동작
    /// </summary>
    public enum AmmoFullRecallOption
    {
        [InspectorName("탄약 부족할 때만 회수 (기본)")]
        OnlyWhenAmmoNotFull = 0,

        [InspectorName("탄약이 꽉 차도 화살 회수 허용")]
        AlwaysRecall = 1
    }

    /// <summary>
    /// 마우스 커서를 추적하여 360도 절차적 조준(Aiming), 화살 발사, 사격 반동(Recoil), 
    /// 6발 탄창(Quiver) 및 사정거리(Recall Radius) 원 표시, 머리 위 다이제틱 HUD와 스크린 캔버스 UI를 총괄 제어하는 컴포넌트입니다.
    /// </summary>
    public class Procedural2DAim : MonoBehaviour
    {
        public static Procedural2DAim Instance { get; private set; }
        private bool justFiredThisFrame = false;

        [Header("Aim Targets & Pivots")]
        [Tooltip("활을 쥐고 있는 왼팔의 어깨 회전축")]
        public Transform leftArmPivot;
        [Tooltip("활시위를 당기는 오른팔의 어깨 회전축")]
        public Transform rightArmPivot;
        [Tooltip("좌우 반전을 담당하는 루트 트랜스폼")]
        public Transform visualRoot;

        [Header("Two-Segment String Arm (시위 당기는 2단 팔)")]
        [Tooltip("오른팔 상완 (팔뚝)")]
        public Transform rightUpperArm;
        [Tooltip("오른팔 전완 (팔목 / 시위 당기는 손)")]
        public Transform rightForearm;
        [Tooltip("팔꿈치 굽힘 기본 각도")]
        public float elbowBaseAngle = 65f;

        [Header("Bow & Hand Separation (분리된 활과 손)")]
        [Tooltip("활 본체 트랜스폼")]
        public Transform bowTransform;
        [Tooltip("활을 잡은 손 트랜스폼")]
        public Transform bowHandTransform;
        public SpriteRenderer bowRenderer;
        public SpriteRenderer bowHandRenderer;

        [Header("Aiming Parameters")]
        [Tooltip("조준 회전 추적 속도 (부드러운 에이밍)")]
        public float aimSmoothSpeed = 25f;
        [Tooltip("조준 가능한 상하 각도 제한 (도 단위, -85 ~ 85)")]
        public Vector2 angleClamp = new Vector2(-85f, 85f);

        [Header("Shooting & Projectile (사격 & 화살 발사)")]
        [Tooltip("화살이 발사되는 총구/활 위치")]
        public Transform muzzlePoint;
        [Tooltip("발사할 화살 프리팹")]
        public GameObject arrowPrefab;
        [Tooltip("화살 발사 속도")]
        public float arrowSpeed = 33f;
        [Tooltip("사격 쿨다운 (초 단위. 0이면 쿨타임 없음 - 클릭 즉시 반응)")]
        public float fireRate = 0f;

        [Header("Magazine & Ammo System (화살 탄창 시스템)")]
        [Tooltip("화살 탄창 최대 용량 (기본 6발)")]
        public int maxAmmo = 12;
        [Tooltip("현재 소지 중인 화살 수량")]
        public int currentAmmo = 12;

        [Header("Recall Range & Circle Indicator (회수 사정거리 및 원 표시)")]
        [Tooltip("화살을 회수할 수 있는 플레이어 중심 반경 거리")]
        public float recallRadius = 10f;
        [Tooltip("회수 반경 원 시각화 표시 여부")]
        public bool showRangeCircle = true;
        [Tooltip("원 테두리 선 두께")]
        public float circleLineWidth = 0.05f;

        [Header("Recall Dropdown Configuration (회수 조건 & 방식 드롭다운)")]
        [Tooltip("화살을 어떤 트리거 방식으로 회수할지 선택합니다.")]
        public RecallTriggerMode recallTrigger = RecallTriggerMode.AutoNearbyAndManualGlobal;

        [Tooltip("어떤 상태의 화살을 회수 대상으로 인정할지 선택합니다.")]
        public RecallConditionMode recallCondition = RecallConditionMode.StuckOrStopped;

        [Tooltip("수동 회수 실행 단축키")]
        public RecallKey recallKey = RecallKey.G;

        [Tooltip("탄약이 꽉 찼을 때 회수 동작 여부")]
        public AmmoFullRecallOption ammoFullOption = AmmoFullRecallOption.OnlyWhenAmmoNotFull;

        [Header("Dynamic Sorting (레이어 순서)")]
        public SpriteRenderer leftArmBowRenderer;
        [Tooltip("위를 쏠 때 왼팔의 Sorting Order")]
        public int armOrderHigh = 5;
        [Tooltip("아래를 쏠 때 왼팔의 Sorting Order")]
        public int armOrderLow = 4;

        // 내부 상태 프로퍼티
        public float CurrentAimAngle { get; private set; }
        public bool IsFacingRight { get; private set; } = true;
        public Vector2 AimDirection { get; private set; }
        public int InRangeArrowsCount { get; private set; } = 0;

        private Camera mainCam;
        private float currentArmAngle;
        private float currentRecoilAngle = 0f;
        private float nextFireTime;
        private ProceduralBodyShift bodyShift;
        private ProceduralCharacterController charCtrl;
        private Rigidbody2D playerRb;

        // 회수 범위 원형 인디케이터
        private GameObject circleObj;
        private LineRenderer rangeLineRenderer;
        private Material circleMaterial;

        // 1. 머리 위 다이제틱 HUD (Overhead Pips)
        private GameObject overheadPipsRoot;
        private SpriteRenderer[] overheadPipRenderers;
        private Vector3 overheadBaseOffset = new Vector3(0f, 1.45f, 0f);

        // 2. 화면 스크린 UGUI 캔버스 HUD
        private GameObject canvasHUDObj;
        private Image[] uiArrowSlots;
        private Text uiCountText;
        private Text uiPromptText;

        private void Awake()
        {
            Instance = this;
            mainCam = Camera.main;
            bodyShift = GetComponent<ProceduralBodyShift>();
            charCtrl = GetComponent<ProceduralCharacterController>();
            playerRb = GetComponent<Rigidbody2D>();
            currentAmmo = maxAmmo;

            SetupRangeCircle();
            SetupOverheadAmmoPips();
            SetupScreenCanvasHUD();
        }

        private Sprite GetArrowSprite()
        {
            if (arrowPrefab != null)
            {
                SpriteRenderer sr = arrowPrefab.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) return sr.sprite;
            }
            return Resources.Load<Sprite>("Sprites/Character/Arrow");
        }

        private Font GetDefaultFont()
        {
            Font f = null;
            try
            {
                f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch { }

            if (f == null)
            {
                try
                {
                    string[] osFonts = Font.GetOSInstalledFontNames();
                    if (osFonts != null && osFonts.Length > 0)
                    {
                        f = Font.CreateDynamicFontFromOSFont(osFonts[0], 16);
                    }
                }
                catch { }
            }
            return f;
        }

        #region Setup Visuals (원형 범위 & 머리위 HUD & 캔버스 HUD)

        private void SetupRangeCircle()
        {
            if (circleObj != null) Destroy(circleObj);

            circleObj = new GameObject("Recall_Range_Circle");
            circleObj.transform.position = transform.position;

            rangeLineRenderer = circleObj.AddComponent<LineRenderer>();
            rangeLineRenderer.useWorldSpace = false;
            rangeLineRenderer.loop = true;
            rangeLineRenderer.positionCount = 65;
            rangeLineRenderer.startWidth = circleLineWidth;
            rangeLineRenderer.endWidth = circleLineWidth;
            rangeLineRenderer.sortingOrder = 3;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit");
            circleMaterial = new Material(shader);
            rangeLineRenderer.material = circleMaterial;

            float deltaTheta = (2f * Mathf.PI) / 64f;
            for (int i = 0; i <= 64; i++)
            {
                float theta = i * deltaTheta;
                float x = recallRadius * Mathf.Cos(theta);
                float y = recallRadius * Mathf.Sin(theta);
                rangeLineRenderer.SetPosition(i, new Vector3(x, y, 0f));
            }
        }

        private void SetupOverheadAmmoPips()
        {
            if (overheadPipsRoot != null) Destroy(overheadPipsRoot);

            overheadPipsRoot = new GameObject("Overhead_Ammo_Pips");
            overheadPipRenderers = new SpriteRenderer[maxAmmo];

            Sprite arrowSprite = GetArrowSprite();
            float spacing = maxAmmo > 8 ? 0.12f : 0.22f;
            float startX = -((maxAmmo - 1) * spacing) * 0.5f;

            for (int i = 0; i < maxAmmo; i++)
            {
                GameObject pipObj = new GameObject($"Pip_{i}");
                pipObj.transform.SetParent(overheadPipsRoot.transform);
                pipObj.transform.localPosition = new Vector3(startX + (i * spacing), 0f, 0f);
                pipObj.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // 세로 화살촉 정렬
                pipObj.transform.localScale = new Vector3(0.42f, 0.42f, 1f);

                SpriteRenderer psr = pipObj.AddComponent<SpriteRenderer>();
                psr.sprite = arrowSprite;
                psr.sortingOrder = 25; // 최상단 소팅
                overheadPipRenderers[i] = psr;
            }
        }

        private void SetupScreenCanvasHUD()
        {
            if (canvasHUDObj != null) Destroy(canvasHUDObj);

            canvasHUDObj = new GameObject("Quiver_Screen_Canvas");
            Canvas canvas = canvasHUDObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            CanvasScaler scaler = canvasHUDObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasHUDObj.AddComponent<GraphicRaycaster>();

            // 1. 하단 좌측 패널 (Card Panel)
            GameObject panelObj = new GameObject("HUD_Card_Panel");
            panelObj.transform.SetParent(canvasHUDObj.transform, false);

            RectTransform panelRt = panelObj.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 0f);
            panelRt.pivot = new Vector2(0f, 0f);
            panelRt.anchoredPosition = new Vector2(36f, 36f);
            panelRt.sizeDelta = new Vector2(maxAmmo > 6 ? 480f : 330f, 96f);

            Image panelBg = panelObj.AddComponent<Image>();
            panelBg.color = new Color(0.06f, 0.08f, 0.12f, 0.88f);

            Font uiFont = GetDefaultFont();

            // 2. 타이틀 & 잔여 수량 텍스트
            GameObject textObj = new GameObject("Ammo_Count_Text");
            textObj.transform.SetParent(panelObj.transform, false);

            RectTransform textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0f, 1f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.pivot = new Vector2(0f, 1f);
            textRt.anchoredPosition = new Vector2(18f, -12f);
            textRt.sizeDelta = new Vector2(-36f, 26f);

            uiCountText = textObj.AddComponent<Text>();
            uiCountText.font = uiFont;
            uiCountText.fontSize = 17;
            uiCountText.fontStyle = FontStyle.Bold;
            uiCountText.color = Color.white;
            uiCountText.text = $"QUIVER   {currentAmmo} / {maxAmmo}";

            // 3. 화살 6개 슬롯 아이콘 컨테이너
            GameObject slotsContainer = new GameObject("Arrow_Slots_Container");
            slotsContainer.transform.SetParent(panelObj.transform, false);

            RectTransform containerRt = slotsContainer.AddComponent<RectTransform>();
            containerRt.anchorMin = new Vector2(0f, 0.5f);
            containerRt.anchorMax = new Vector2(0f, 0.5f);
            containerRt.pivot = new Vector2(0f, 0.5f);
            containerRt.anchoredPosition = new Vector2(18f, -5f);
            containerRt.sizeDelta = new Vector2(maxAmmo > 6 ? 444f : 294f, 28f);

            Sprite arrowSprite = GetArrowSprite();
            uiArrowSlots = new Image[maxAmmo];
            float slotSpacing = maxAmmo > 6 ? 35f : 48f;
            Vector2 slotSize = maxAmmo > 6 ? new Vector2(32f, 12f) : new Vector2(44f, 13f);

            for (int i = 0; i < maxAmmo; i++)
            {
                GameObject slotObj = new GameObject($"Arrow_Slot_{i}");
                slotObj.transform.SetParent(slotsContainer.transform, false);

                RectTransform slotRt = slotObj.AddComponent<RectTransform>();
                slotRt.anchorMin = new Vector2(0f, 0.5f);
                slotRt.anchorMax = new Vector2(0f, 0.5f);
                slotRt.pivot = new Vector2(0.5f, 0.5f);
                slotRt.anchoredPosition = new Vector2(16f + (i * slotSpacing), 0f);
                slotRt.sizeDelta = slotSize;
                slotRt.localRotation = Quaternion.Euler(0f, 0f, 30f); // 살짝 비스듬히 꽂힌 화살 디자인

                Image slotImg = slotObj.AddComponent<Image>();
                slotImg.sprite = arrowSprite;
                slotImg.color = new Color(0.2f, 1f, 0.92f, 1f);
                uiArrowSlots[i] = slotImg;
            }

            // 4. 하단 회수 힌트 텍스트
            GameObject promptObj = new GameObject("Recall_Prompt_Text");
            promptObj.transform.SetParent(panelObj.transform, false);

            RectTransform promptRt = promptObj.AddComponent<RectTransform>();
            promptRt.anchorMin = new Vector2(0f, 0f);
            promptRt.anchorMax = new Vector2(1f, 0f);
            promptRt.pivot = new Vector2(0f, 0f);
            promptRt.anchoredPosition = new Vector2(18f, 8f);
            promptRt.sizeDelta = new Vector2(-36f, 20f);

            uiPromptText = promptObj.AddComponent<Text>();
            uiPromptText.font = uiFont;
            uiPromptText.fontSize = 12;
            uiPromptText.fontStyle = FontStyle.Normal;
            uiPromptText.color = new Color(0.2f, 1f, 0.92f, 0.95f);
            string initKey = (recallKey == RecallKey.RightClick) ? "R-CLICK" : recallKey.ToString(); uiPromptText.text = $"[{initKey}] RECALL";
        }

        #endregion

        private void Update()
        {
            if (mainCam == null)
            {
                mainCam = Camera.main;
                if (mainCam == null) return;
            }

            Vector2 mouseScreenPos = GetMouseScreenPosition();
            bool isFiring = GetFireInput();
            bool isDropping = GetDropInput();

            UpdateInRangeArrowsCount();
            HandleAiming(mouseScreenPos);
            HandleShooting(isFiring);
            HandleDropping(isDropping);

            bool isRecalling = GetRecallInput();
            ProcessRecall(isRecalling);
            justFiredThisFrame = false;
        }

        private void LateUpdate()
        {
            // 1. 원형 인디케이터 위치 및 스타일 업데이트
            if (circleObj != null)
            {
                circleObj.transform.position = transform.position;

                if (rangeLineRenderer != null)
                {
                    rangeLineRenderer.enabled = showRangeCircle;

                    if (showRangeCircle)
                    {
                        Color lineColor;
                        if (InRangeArrowsCount > 0)
                        {
                            float pulse = 0.65f + 0.35f * Mathf.PingPong(Time.time * 4f, 1f);
                            lineColor = new Color(0.2f, 1f, 0.95f, pulse);
                        }
                        else if (currentAmmo < maxAmmo)
                        {
                            lineColor = new Color(0.25f, 0.75f, 1f, 0.38f);
                        }
                        else
                        {
                            lineColor = new Color(0.25f, 0.75f, 1f, 0.16f);
                        }

                        rangeLineRenderer.startColor = lineColor;
                        rangeLineRenderer.endColor = lineColor;
                    }
                }
            }

            // 2. 머리 위 다이제틱 화살 HUD 업데이트
            if (overheadPipsRoot != null)
            {
                overheadPipsRoot.transform.position = transform.position + overheadBaseOffset;

                for (int i = 0; i < maxAmmo; i++)
                {
                    if (overheadPipRenderers[i] == null) continue;

                    if (i < currentAmmo)
                    {
                        overheadPipRenderers[i].color = new Color(0.2f, 1f, 0.92f, 0.95f);
                        overheadPipRenderers[i].transform.localScale = new Vector3(0.42f, 0.42f, 1f);
                    }
                    else
                    {
                        overheadPipRenderers[i].color = new Color(0.22f, 0.26f, 0.32f, 0.3f);
                        overheadPipRenderers[i].transform.localScale = new Vector3(0.32f, 0.32f, 1f);
                    }
                }

                // 탄창 0발일 때 머리 위 경고 펄스
                if (currentAmmo == 0)
                {
                    float pulse = Mathf.PingPong(Time.time * 5f, 1f);
                    Color warnCol = Color.Lerp(new Color(1f, 0.25f, 0.25f, 0.4f), new Color(1f, 0.85f, 0.3f, 0.9f), pulse);
                    for (int i = 0; i < maxAmmo; i++)
                    {
                        if (overheadPipRenderers[i] != null) overheadPipRenderers[i].color = warnCol;
                    }
                }
            }

            // 3. 화면 스크린 캔버스 HUD 업데이트
            UpdateScreenCanvasHUD();
        }

        private void UpdateScreenCanvasHUD()
        {
            if (uiCountText != null)
            {
                uiCountText.text = $"QUIVER   {currentAmmo} / {maxAmmo}";
                uiCountText.color = (currentAmmo == 0) ? new Color(1f, 0.4f, 0.4f, 1f) : Color.white;
            }

            if (uiArrowSlots != null)
            {
                for (int i = 0; i < maxAmmo; i++)
                {
                    if (uiArrowSlots[i] == null) continue;

                    if (i < currentAmmo)
                    {
                        uiArrowSlots[i].color = new Color(0.2f, 1f, 0.92f, 1f);
                        uiArrowSlots[i].rectTransform.localScale = Vector3.one;
                    }
                    else
                    {
                        uiArrowSlots[i].color = new Color(0.25f, 0.3f, 0.38f, 0.28f);
                        uiArrowSlots[i].rectTransform.localScale = new Vector3(0.85f, 0.85f, 1f);
                    }
                }
            }

            if (uiPromptText != null)
            {
                string keyLabel;
                switch (recallKey)
                {
                    case RecallKey.LeftClick: keyLabel = "L-CLICK"; break;
                    case RecallKey.RightClick: keyLabel = "R-CLICK"; break;
                    default: keyLabel = recallKey.ToString(); break;
                }
                if (recallTrigger == RecallTriggerMode.OnlyWhenEmpty && currentAmmo > 0)
                {
                    uiPromptText.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    uiPromptText.text = $"[{keyLabel}] (EMPTY AMMO TO RECALL)";
                }
                else if (recallTrigger == RecallTriggerMode.OnlyWhenEmpty && currentAmmo == 0)
                {
                    float pulse = Mathf.PingPong(Time.time * 5f, 1f);
                    uiPromptText.color = Color.Lerp(new Color(1f, 0.35f, 0.2f, 1f), Color.yellow, pulse);
                    uiPromptText.text = $"[{keyLabel}] RECALL ALL (AMMO EMPTY!)";
                }
                else if (recallTrigger == RecallTriggerMode.AutoNearbyOnly)
                {
                    if (InRangeArrowsCount > 0)
                    {
                        float pulse = Mathf.PingPong(Time.time * 5f, 1f);
                        uiPromptText.color = Color.Lerp(new Color(0.2f, 1f, 0.92f, 1f), Color.white, pulse * 0.5f);
                        uiPromptText.text = $"AUTO RECALL ({InRangeArrowsCount})";
                    }
                    else
                    {
                        uiPromptText.color = new Color(0.5f, 0.8f, 0.9f, 0.7f);
                        uiPromptText.text = "AUTO RECALL READY";
                    }
                }
                else if (InRangeArrowsCount > 0)
                {
                    float pulse = Mathf.PingPong(Time.time * 5f, 1f);
                    uiPromptText.color = Color.Lerp(new Color(0.2f, 1f, 0.92f, 1f), Color.white, pulse * 0.5f);
                    uiPromptText.text = $"[{keyLabel}] RECALL ({InRangeArrowsCount} in range)";
                }
                else if (currentAmmo < maxAmmo || ammoFullOption == AmmoFullRecallOption.AlwaysRecall)
                {
                    uiPromptText.color = new Color(0.85f, 0.7f, 0.4f, 0.9f);
                    uiPromptText.text = $"[{keyLabel}] Out of Range";
                }
                else
                {
                    uiPromptText.color = new Color(0.4f, 0.85f, 0.5f, 0.8f);
                    uiPromptText.text = "FULL";
                }
            }
        }

        private void OnDestroy()
        {
            if (circleObj != null) Destroy(circleObj);
            if (overheadPipsRoot != null) Destroy(overheadPipsRoot);
            if (canvasHUDObj != null) Destroy(canvasHUDObj);
        }

        private void UpdateInRangeArrowsCount()
        {
            int count = 0;
            var arrows = ArrowProjectile.ActiveArrows;
            for (int i = 0; i < arrows.Count; i++)
            {
                var arrow = arrows[i];
                if (arrow != null && arrow.CanBeRecalled)
                {
                    float dist = Vector2.Distance(arrow.transform.position, transform.position);
                    if (dist <= recallRadius)
                    {
                        count++;
                    }
                }
            }
            InRangeArrowsCount = count;
        }

        private Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
            return Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private bool GetFireInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.leftButton.wasPressedThisFrame;
            }
            return false;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private bool GetDropInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.rightButton.wasPressedThisFrame;
            }
            return false;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private bool GetRecallInput()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var m = Mouse.current;
            switch (recallKey)
            {
                case RecallKey.LeftClick:
                    if (justFiredThisFrame) return false;
                    return m != null && m.leftButton.wasPressedThisFrame;
                case RecallKey.RightClick:
                    return m != null && m.rightButton.wasPressedThisFrame;
                case RecallKey.E:
                    return k != null && k.eKey.wasPressedThisFrame;
                case RecallKey.F:
                    return k != null && k.fKey.wasPressedThisFrame;
                case RecallKey.R:
                    return k != null && k.rKey.wasPressedThisFrame;
                case RecallKey.Space:
                    return k != null && k.spaceKey.wasPressedThisFrame;
                case RecallKey.G:
                default:
                    return k != null && k.gKey.wasPressedThisFrame;
            }
#else
            switch (recallKey)
            {
                case RecallKey.LeftClick:
                    if (justFiredThisFrame) return false;
                    return Input.GetMouseButtonDown(0);
                case RecallKey.RightClick:
                    return Input.GetMouseButtonDown(1);
                case RecallKey.E:
                    return Input.GetKeyDown(KeyCode.E);
                case RecallKey.F:
                    return Input.GetKeyDown(KeyCode.F);
                case RecallKey.R:
                    return Input.GetKeyDown(KeyCode.R);
                case RecallKey.Space:
                    return Input.GetKeyDown(KeyCode.Space);
                case RecallKey.G:
                default:
                    return Input.GetKeyDown(KeyCode.G);
            }
#endif
        }

        /// <summary>
        /// 회수한 화살이 플레이어에 도달하면 탄창을 충전합니다.
        /// </summary>
        public void RefillAmmo(int amount = 1)
        {
            currentAmmo = Mathf.Min(currentAmmo + amount, maxAmmo);
        }

        /// <summary>
        /// 설정된 드롭다운 조건에 따라 반경 내 자동 회수 및 키 입력 수동 회수를 통합 처리합니다.
        /// </summary>
        private void ProcessRecall(bool isKeyPressed)
        {
            bool canRecallByAmmo = (ammoFullOption == AmmoFullRecallOption.AlwaysRecall) || (currentAmmo < maxAmmo);
            if (!canRecallByAmmo) return;

            // '화살을 모두 소비했을 때만' 모드인 경우, 탄약이 남아있으면(> 0) 회수 불가
            if (recallTrigger == RecallTriggerMode.OnlyWhenEmpty && currentAmmo > 0)
            {
                return;
            }

            var activeList = ArrowProjectile.ActiveArrows;
            ArrowProjectile[] arrows = activeList.Count > 0 ? activeList.ToArray() : Object.FindObjectsByType<ArrowProjectile>(FindObjectsSortMode.None);

            bool recalledAny = false;

            // 1. 자동 근접 회수 처리 (OnlyWhenEmpty는 자동 근접 제외 -> 키 입력으로만 회수)
            if (recallTrigger == RecallTriggerMode.AutoNearbyAndManualGlobal || recallTrigger == RecallTriggerMode.AutoNearbyOnly)
            {
                for (int i = 0; i < arrows.Length; i++)
                {
                    var arrow = arrows[i];
                    if (arrow != null && arrow.IsConditionMet(recallCondition))
                    {
                        float dist = Vector2.Distance(arrow.transform.position, transform.position);
                        if (dist <= recallRadius)
                        {
                            arrow.StartRecall(transform);
                            recalledAny = true;
                        }
                    }
                }
            }

            // 2. 수동 키 입력 회수 처리
            if (isKeyPressed && recallTrigger != RecallTriggerMode.AutoNearbyOnly)
            {
                int manualRecalled = 0;
                if (recallTrigger == RecallTriggerMode.ManualNearbyOnly)
                {
                    // 반경 내 화살만 선별 회수
                    for (int i = 0; i < arrows.Length; i++)
                    {
                        var arrow = arrows[i];
                        if (arrow != null && arrow.IsConditionMet(recallCondition))
                        {
                            float dist = Vector2.Distance(arrow.transform.position, transform.position);
                            if (dist <= recallRadius)
                            {
                                arrow.StartRecall(transform);
                                manualRecalled++;
                            }
                        }
                    }
                }
                else // ManualGlobalOnly 또는 AutoNearbyAndManualGlobal
                {
                    // 맵 전체 화살 즉시 회수
                    for (int i = 0; i < arrows.Length; i++)
                    {
                        var arrow = arrows[i];
                        if (arrow != null && arrow.IsConditionMet(recallCondition))
                        {
                            arrow.StartRecall(transform);
                            manualRecalled++;
                        }
                    }
                }

                if (manualRecalled > 0)
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlayArrowRecall(transform.position);
                }
                else
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlayEmptyClick(transform.position);
                }
            }
            else if (recalledAny)
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlayArrowRecall(transform.position);
            }
        }
        private void HandleAiming(Vector2 mouseScreen)
        {
            Vector3 mouseWorld = mainCam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -mainCam.transform.position.z));
            
            Vector3 pivotPos = leftArmPivot != null ? leftArmPivot.position : transform.position;
            Vector2 dirToMouse = (mouseWorld - pivotPos);
            if (dirToMouse.sqrMagnitude < 0.0001f) return;
            AimDirection = dirToMouse.normalized;

            bool shouldFaceRight = mouseWorld.x >= transform.position.x;
            if (shouldFaceRight != IsFacingRight)
            {
                if (charCtrl == null || (!charCtrl.IsSpinning && !charCtrl.IsRolling))
                {
                    IsFacingRight = shouldFaceRight;
                    if (visualRoot != null)
                    {
                        Vector3 scale = visualRoot.localScale;
                        scale.x = IsFacingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
                        visualRoot.localScale = scale;
                    }
                }
            }

            float rawAngle = Mathf.Atan2(dirToMouse.y, Mathf.Abs(dirToMouse.x)) * Mathf.Rad2Deg;
            rawAngle = Mathf.Clamp(rawAngle, angleClamp.x, angleClamp.y);
            CurrentAimAngle = rawAngle;

            currentArmAngle = Mathf.LerpAngle(currentArmAngle, rawAngle, Time.deltaTime * aimSmoothSpeed);
            if (leftArmPivot != null)
            {
                leftArmPivot.localRotation = Quaternion.Euler(0f, 0f, currentArmAngle);
            }

            // 오른팔 2마디 관절 회전 (상완 + 전완 팔꿈치 굽힘)
            currentRecoilAngle = Mathf.Lerp(currentRecoilAngle, 0f, Time.deltaTime * 12f);
            if (rightUpperArm != null)
            {
                float upperAngle = -20f + (currentArmAngle * 0.35f);
                rightUpperArm.localRotation = Quaternion.Euler(0f, 0f, upperAngle);

                if (rightForearm != null)
                {
                    float elbowAngle = elbowBaseAngle + (currentRecoilAngle * 0.4f);
                    rightForearm.localRotation = Quaternion.Euler(0f, 0f, elbowAngle);
                }
            }
            else if (rightArmPivot != null)
            {
                float rightAngle = -15f + (currentArmAngle * 0.35f);
                rightArmPivot.localRotation = Quaternion.Euler(0f, 0f, rightAngle);
            }

            // 왼팔 및 분리된 활/손 소팅 오더 갱신
            int sortingOrder = (rawAngle > 30f) ? armOrderHigh : armOrderLow;
            if (leftArmBowRenderer != null)
            {
                leftArmBowRenderer.sortingOrder = sortingOrder;
            }
            if (bowRenderer != null)
            {
                bowRenderer.sortingOrder = sortingOrder;
            }
            if (bowHandRenderer != null)
            {
                bowHandRenderer.sortingOrder = sortingOrder + 1;
            }
        }

        private void HandleDropping(bool isDropping)
        {
            if (!isDropping) return;

            if (currentAmmo <= 0)
            {
                if (AudioManager.Instance != null && muzzlePoint != null)
                {
                    AudioManager.Instance.PlayEmptyClick(muzzlePoint.position);
                }
                return;
            }

            if (arrowPrefab == null || muzzlePoint == null) return;

            currentAmmo--;

            Vector3 spawnPos = muzzlePoint.position;
            float facing = IsFacingRight ? 1f : -1f;

            float popUpForce = Random.Range(3.8f, 5.2f);
            float forwardToss = facing * Random.Range(1.2f, 2.5f);
            Vector2 dropVelocity = new Vector2(forwardToss, popUpForce);

            if (playerRb != null)
            {
                dropVelocity.x += playerRb.linearVelocity.x * 0.3f;
            }

            float initZ = Random.Range(-30f, 30f);
            GameObject arrow = Instantiate(arrowPrefab, spawnPos, Quaternion.Euler(0f, 0f, initZ));

            Collider2D arrowCol = arrow.GetComponent<Collider2D>();
            if (arrowCol != null)
            {
                Collider2D[] playerCols = GetComponentsInChildren<Collider2D>();
                foreach (var pCol in playerCols)
                {
                    if (pCol != null && pCol.enabled)
                    {
                        Physics2D.IgnoreCollision(arrowCol, pCol, true);
                    }
                }
            }

            float spinSpeed = Random.Range(420f, 720f) * -facing;

            ArrowProjectile projectile = arrow.GetComponent<ArrowProjectile>();
            if (projectile != null)
            {
                projectile.InitializeDropped(transform, dropVelocity, spinSpeed);
            }
            else
            {
                Rigidbody2D aRb = arrow.GetComponent<Rigidbody2D>();
                if (aRb != null)
                {
                    aRb.linearVelocity = dropVelocity;
                    aRb.angularVelocity = spinSpeed;
                }
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayArrowShoot(spawnPos);
            }
        }

        private void HandleShooting(bool isFiring)
        {
            if (isFiring)
            {
                if (currentAmmo <= 0)
                {
                    if (AudioManager.Instance != null && muzzlePoint != null)
                    {
                        AudioManager.Instance.PlayEmptyClick(muzzlePoint.position);
                    }
                    return;
                }

                if (fireRate <= 0f)
                {
                    Shoot();
                }
                else if (Time.time >= nextFireTime)
                {
                    nextFireTime = Time.time + fireRate;
                    Shoot();
                }
            }
        }

        private void Shoot()
        {
            if (currentAmmo <= 0) return;
            currentAmmo--;
            justFiredThisFrame = true;

            if (bodyShift != null)
            {
                bodyShift.ApplyRecoil(AimDirection);
            currentRecoilAngle = 22f;
            }

            if (arrowPrefab != null && muzzlePoint != null)
            {
                Vector3 spawnPos = muzzlePoint.position;
                float rotZ = Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg;
                GameObject arrow = Instantiate(arrowPrefab, spawnPos, Quaternion.Euler(0f, 0f, rotZ));
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayArrowShoot(spawnPos);
                }

                Collider2D arrowCol = arrow.GetComponent<Collider2D>();
                if (arrowCol != null)
                {
                    Collider2D[] playerCols = GetComponentsInChildren<Collider2D>();
                    foreach (var pCol in playerCols)
                    {
                        if (pCol != null && pCol.enabled)
                        {
                            Physics2D.IgnoreCollision(arrowCol, pCol, true);
                        }
                    }
                }

                ArrowProjectile projectile = arrow.GetComponent<ArrowProjectile>();
                if (projectile != null)
                {
                    projectile.Initialize(transform, AimDirection * arrowSpeed);
                }
                else
                {
                    Rigidbody2D rb = arrow.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.linearVelocity = AimDirection * arrowSpeed;
                    }
                }
            }
        }
    }
}

