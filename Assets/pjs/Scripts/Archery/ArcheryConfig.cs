using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 궁술 테스트 빌드의 모든 수치. 명세에 있는 값은 [명세], 테스트를 위해 임의로 정한 값은 [잠정] 으로 표시한다.
    /// 에셋 위치: Assets/pjs/Resources/ArcheryConfig.asset (인스펙터에서 수정 → 다시 빌드)
    /// 에셋이 없으면 이 클래스의 기본값으로 동작한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Archery/Archery Config", fileName = "ArcheryConfig")]
    public class ArcheryConfig : ScriptableObject
    {
        public const string ResourcePath = "ArcheryConfig";

        [Header("플레이어 공통 [명세]")]
        [Tooltip("[명세] 기본 체력 100. 로비에서도 변경 가능")]
        public float playerMaxHp = 100f;
        [Tooltip("[명세] 기본 공격력 1")]
        public float playerAttack = 1f;

        [Header("플레이어 이동 (테스트 씬 전용 덮어쓰기)")]
        [Tooltip("1단 점프력. 프리팹 원래 값 21(최고 높이 약 7.49) → 18.78(약 5.99, 20% 낮춤). 높이는 점프력의 제곱에 비례. 0 이하면 프리팹 값 사용")]
        public float playerFirstJumpForce = 18.78f;

        [Header("활시위 정규화 [잠정]")]
        [Tooltip("[잠정] 활시위(초) ÷ 이 값 = 활시위 배율. 기본 화살(1초)이 배율 1이 되도록 1초로 둔다.")]
        public float referenceDrawSeconds = 1f;

        [Header("화살 데이터 (엑셀 화살 시트)")]
        public List<ArrowDefinition> arrows = new List<ArrowDefinition>();
        [Tooltip("[잠정] 테스트 시작 시 화살통 구성. 로비에서 개수 변경 가능")]
        public List<string> defaultLoadout = new List<string>();

        [Header("화살 비행 [잠정]")]
        [Tooltip("[잠정] 화살 발사 속도 (유닛/초)")]
        public float arrowSpeed = 26f;
        [Tooltip("[잠정] 아테나 외 직사 화살은 이 거리를 날아가면 추진력을 잃고 떨어진다")]
        public float straightMaxRange = 32f;
        [Tooltip("[잠정] 추진력을 잃은/적에게서 떨어진 화살의 낙하 중력 가속도")]
        public float fallGravity = 30f;

        [Header("아폴론 - 기본사격")]
        [Tooltip("[명세] 기본 발사 간격 1초")]
        public float apolloBaseInterval = 1f;

        [Header("헤르메스 - 연사")]
        [Tooltip("[명세] 기본 발사 간격 1.25초")]
        public float hermesBaseInterval = 1.25f;
        [Tooltip("[명세] 스택당 공격속도 +10%")]
        public float hermesAttackSpeedPerStack = 0.10f;
        [Tooltip("[명세] 최대 5스택")]
        public int hermesMaxStacks = 5;
        [Tooltip("[명세] 지속시간 5초")]
        public float hermesBuffDuration = 5f;
        [Tooltip("[잠정] 스택 갱신 방식")]
        public HermesStackRefreshMode hermesRefreshMode = HermesStackRefreshMode.RefreshAllOnShot;

        [Header("아테나 - 곡사")]
        [Tooltip("[명세] 기본 발사 간격 0.6초")]
        public float athenaBaseInterval = 0.6f;
        [Tooltip("[잠정] 낙하 가속도 = 무게 × 이 값 (무게 1 → 18 유닛/초²)")]
        public float athenaGravityPerWeight = 18f;

        [Header("오디세우스 - 차지샷")]
        [Tooltip("[명세] 최소 충전시간 1.2초 (기본 화살 기준)")]
        public float odysseusMinCharge = 1.2f;
        [Tooltip("[명세] 최대 충전시간 1.5초 (기본 화살 기준)")]
        public float odysseusMaxCharge = 1.5f;
        [Tooltip("[잠정] 피해 배율 = 정규화 충전시간 ÷ 이 값. 1초 기준이면 1.2초→×1.2(7.2), 1.5초→×1.5(9.0)으로 엑셀 예시와 일치")]
        public float odysseusDamageReferenceSeconds = 1f;
        [Tooltip("[잠정] 충전시간을 화살 활시위 배율로 늘린다 (활시위 1.4초 화살은 1.68~2.1초 충전)")]
        public bool odysseusChargeScalesWithDraw = true;
        [Tooltip("[잠정] true면 (화살 피해+공격력)×배율, false면 화살 피해×배율+공격력")]
        public bool odysseusMultiplierIncludesAttack = false;

        [Header("아레스 - 찌르기")]
        [Tooltip("[명세] 기본 공격 간격 0.5초")]
        public float aresThrustBaseInterval = 0.5f;
        [Tooltip("[잠정] 찌르기 판정 길이 (조준점 기준 전방)")]
        public float aresThrustRange = 2.2f;
        [Tooltip("[잠정] 찌르기 판정 폭")]
        public float aresThrustWidth = 0.9f;
        [Tooltip("[잠정] 빗나간 찌르기도 간격을 소모한다")]
        public bool aresMissConsumesInterval = true;

        [Header("모든 회수 공통 - 자동 줍기")]
        [Tooltip("[잠정] 바닥(지형)의 화살을 자동으로 줍는 반경")]
        public float autoPickupRadius = 1.3f;

        [Header("오르페우스 - 부르기")]
        [Tooltip("[명세] 무게와 무관하게 5초에 걸쳐 회수")]
        public float orpheusReturnDuration = 5f;
        [Tooltip("[잠정] R을 뗐을 때 동작")]
        public OrpheusReleaseMode orpheusReleaseMode = OrpheusReleaseMode.DropInPlace;
        [Tooltip("[잠정] 귀환 곡선 지수. 남은거리 = 시작거리 × (1-t)^지수 → 멀수록 빠르고 가까울수록 느리다")]
        public float returnEaseExponent = 3f;

        [Header("아레스 - 뽑기")]
        [Tooltip("[명세] 박힌 뒤 0.5초가 지나야 뽑을 수 있다")]
        public float aresPullDelay = 0.5f;
        [Tooltip("[잠정] 적 콜라이더 가장자리까지 이 거리 안이면 '가까이 감'으로 판정")]
        public float aresPullRadius = 1.6f;

        [Header("데메테르 - 수확")]
        [Tooltip("[잠정] 낫 범위 반경")]
        public float demeterRadius = 3.6f;
        [Tooltip("[잠정] 낫 범위 각도(도)")]
        public float demeterAngle = 150f;
        [Tooltip("[명세] 피해 = 공격력 × 2")]
        public float demeterDamageMultiplier = 2f;
        [Tooltip("[잠정] 낫 간격 기준값. '기본공격속도'를 아폴론 기본 1초로 해석")]
        public float demeterReferenceInterval = 1f;
        [Tooltip("[잠정] true면 기준값 대신 선택한 발사 궁술의 기본 간격을 사용")]
        public bool demeterUseLaunchBaseInterval = false;
        [Tooltip("[잠정] 낫 간격 = 기준 × 이 값. '공격속도 2배'를 2배 빠름(0.5)으로 해석. 간격 2배로 해석하려면 2")]
        public float demeterIntervalFactor = 0.5f;
        [Tooltip("[잠정] 공격속도 증가 효과(헤르메스 버프 등)를 낫 간격에도 적용")]
        public bool demeterApplyAttackSpeed = true;

        [Header("하데스 - 부름")]
        [Tooltip("[명세] 5초에 걸쳐 귀환")]
        public float hadesReturnDuration = 5f;
        [Tooltip("[잠정] 바닥/박힘 상태가 된 뒤 귀환 시작까지 대기")]
        public float hadesStartDelay = 0f;

        [Header("제우스 - 뇌우")]
        [Tooltip("[잠정] 마우스 주변 원형 선택 반경")]
        public float zeusRadius = 3.2f;
        [Tooltip("[명세] 재사용 대기시간 6초")]
        public float zeusCooldown = 6f;
        [Tooltip("[명세] 번개 피해 = 공격력 ÷ 2")]
        public float zeusDamageFactor = 0.5f;
        [Tooltip("[잠정] 한 번의 뇌우에서 같은 적은 한 번만 번개 피해를 받는다. false면 연결선/박힌 화살마다 피해")]
        public bool zeusHitOncePerEnemy = true;
        [Tooltip("[잠정] 화살 연결 순서")]
        public ZeusChainOrder zeusChainOrder = ZeusChainOrder.NearestNeighborFromCursor;
        [Tooltip("[잠정] 연결선과 적 사이 판정 두께")]
        public float zeusLineThickness = 0.3f;
        [Tooltip("[잠정] 범위 안에 화살이 없으면 재사용 대기시간을 소모하지 않는다")]
        public bool zeusCooldownOnlyWhenRecalled = true;
        [Tooltip("번개선 표시 시간")]
        public float zeusBoltVisibleTime = 0.45f;

        [Header("테스트 적 [잠정]")]
        public float enemyRespawnDelay = 2.5f;
        [Tooltip("넉백 후 원래 자리로 돌아가기 시작하는 시간")]
        public float enemyKnockbackRecoverDelay = 1.2f;

        [Header("카메라 [잠정]")]
        public float cameraOrthoSize = 7f;

        [Header("로비 설명")]
        [TextArea(2, 6)] public string[] launchDescriptions = new string[5];
        [TextArea(2, 6)] public string[] retrievalDescriptions = new string[6];

        public static ArcheryConfig Load()
        {
            var cfg = Resources.Load<ArcheryConfig>(ResourcePath);
            if (cfg == null)
            {
                cfg = CreateInstance<ArcheryConfig>();
                cfg.name = "ArcheryConfig (runtime defaults)";
            }
            cfg.EnsureDefaults();
            return cfg;
        }

        /// <summary>비어 있는 데이터 항목을 엑셀 기반 기본값으로 채운다.</summary>
        public void EnsureDefaults()
        {
            if (arrows == null || arrows.Count == 0)
            {
                arrows = new List<ArrowDefinition>
                {
                    new ArrowDefinition { id = "basic", displayName = "기본 화살", damage = 6f, drawSeconds = 1f, weight = 1f,
                        tint = new Color(1f, 1f, 1f), effectDescription = "없음" },
                    new ArrowDefinition { id = "light", displayName = "가벼운 화살", damage = 4f, drawSeconds = 0.5f, weight = 0.5f,
                        tint = new Color(0.55f, 1f, 0.6f), effectDescription = "없음" },
                    new ArrowDefinition { id = "bronze", displayName = "청동 화살", damage = 10f, drawSeconds = 1.4f, weight = 1.5f,
                        tint = new Color(1f, 0.65f, 0.3f), effectDescription = "적중 시 크게 밀어냄",
                        hitEffects = new List<ArrowHitEffect> { new ArrowHitEffect { type = ArrowHitEffectType.Knockback, magnitude = 2.5f } } },
                };
            }

            if (defaultLoadout == null || defaultLoadout.Count == 0)
            {
                defaultLoadout = new List<string> { "basic", "light", "bronze", "basic", "light", "bronze", "basic", "basic" };
            }

            if (launchDescriptions == null || launchDescriptions.Length != 5 || string.IsNullOrEmpty(launchDescriptions[0]))
            {
                launchDescriptions = new[]
                {
                    "기본 발사 간격 1초.\n화살의 활시위와 공격속도를 반영해 한 발을 마우스 방향으로 발사한다.",
                    "기본 발사 간격 1.25초.\n발사할 때마다 공격속도 +10% 버프(5초, 최대 5스택)를 얻는다.",
                    "기본 발사 간격 0.6초.\n화살이 무게의 영향을 받아 아래로 떨어진다. 무거울수록 빨리 떨어진다.",
                    "좌클릭을 누르고 있으면 충전, 놓으면 발사.\n충전 1.2~1.5초(활시위 배율 적용). 0.1초당 피해 +10%.",
                    "기본 공격 간격 0.5초.\n화살 한 발로 마우스 방향을 찌른다. 적중하지 않으면 화살을 소모하지 않는다.",
                };
            }

            if (retrievalDescriptions == null || retrievalDescriptions.Length != 6 || string.IsNullOrEmpty(retrievalDescriptions[0]))
            {
                retrievalDescriptions = new[]
                {
                    "R을 누르고 있는 동안 모든 화살을 5초에 걸쳐 불러온다.\n멀수록 빠르게, 가까울수록 느리게 돌아온다.",
                    "적에게 박힌 지 0.5초가 지난 화살이 있는 적에게 다가가면 박힌 화살을 모두 뽑는다.\n뽑을 때 각 화살의 적중 판정을 한 번 더 발동한다.",
                    "R: 마우스 방향으로 낫을 휘둘러 공격력×2 피해를 주고\n범위 안의 화살을 모두 회수한다.",
                    "바닥에 떨어지거나 적에게 박힌 화살이 자동으로 5초에 걸쳐 돌아온다.",
                    "R: 마우스 주변 원 안의 화살을 즉시 회수한다 (재사용 6초).\n화살 사이에 적이 있거나 화살이 적에게 박혀 있으면 번개(공격력÷2)를 일으킨다.",
                    "화살이 적에게 박히지 않고 떨어진다.\n바닥의 화살에 가까이 가서 줍는다.",
                };
            }
        }

        public ArrowDefinition FindArrow(string id)
        {
            for (int i = 0; i < arrows.Count; i++)
            {
                if (arrows[i].id == id) return arrows[i];
            }
            return null;
        }

        /// <summary>활시위 배율 = 활시위(초) ÷ 기준 활시위(초)</summary>
        public float DrawMultiplier(ArrowDefinition def)
        {
            if (def == null) return 1f;
            return Mathf.Max(0.01f, def.drawSeconds) / Mathf.Max(0.01f, referenceDrawSeconds);
        }

        public float LaunchBaseInterval(LaunchStyle style)
        {
            switch (style)
            {
                case LaunchStyle.Hermes: return hermesBaseInterval;
                case LaunchStyle.Athena: return athenaBaseInterval;
                case LaunchStyle.Odysseus: return odysseusMinCharge;
                case LaunchStyle.Ares: return aresThrustBaseInterval;
                default: return apolloBaseInterval;
            }
        }

        public static string LaunchName(LaunchStyle s)
        {
            switch (s)
            {
                case LaunchStyle.Apollo: return "아폴론 — 기본사격";
                case LaunchStyle.Hermes: return "헤르메스 — 연사";
                case LaunchStyle.Athena: return "아테나 — 곡사";
                case LaunchStyle.Odysseus: return "오디세우스 — 차지샷";
                case LaunchStyle.Ares: return "아레스 — 찌르기";
            }
            return s.ToString();
        }

        public static string RetrievalName(RetrievalStyle s)
        {
            switch (s)
            {
                case RetrievalStyle.Orpheus: return "오르페우스 — 부르기";
                case RetrievalStyle.Ares: return "아레스 — 뽑기";
                case RetrievalStyle.Demeter: return "데메테르 — 수확";
                case RetrievalStyle.Hades: return "하데스 — 부름";
                case RetrievalStyle.Zeus: return "제우스 — 뇌우";
                case RetrievalStyle.Basic: return "기본 — 줍기";
            }
            return s.ToString();
        }
    }
}
