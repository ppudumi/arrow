using System;
using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>발사 궁술. 회수 궁술과 독립적으로 선택된다.</summary>
    public enum LaunchStyle
    {
        Apollo = 0,     // 아폴론 - 기본사격
        Hermes = 1,     // 헤르메스 - 연사
        Athena = 2,     // 아테나 - 곡사
        Odysseus = 3,   // 오디세우스 - 차지샷
        Ares = 4,       // 아레스 - 찌르기
    }

    /// <summary>회수 궁술. 발사 궁술과 같은 신 이름이어도 별개의 기능이다.</summary>
    public enum RetrievalStyle
    {
        Orpheus = 0,    // 오르페우스 - 부르기
        Ares = 1,       // 아레스 - 뽑기
        Demeter = 2,    // 데메테르 - 수확
        Hades = 3,      // 하데스 - 부름
        Zeus = 4,       // 제우스 - 뇌우
        Basic = 5,      // 기본 - 줍기
    }

    /// <summary>
    /// 화살 한 발의 위치 상태. 한 화살은 항상 정확히 하나의 상태에 있다.
    /// InQuiver 로 들어가는 경로는 ArcheryCombat.TryRecover 하나뿐이고, InQuiver 에서 나가는 경로는 ArrowQuiver.TakeNext 하나뿐이다.
    /// </summary>
    public enum ArrowState
    {
        InQuiver = 0,   // 화살통 안
        Flying = 1,     // 발사되어 비행 중 (회수 대상 아님)
        Falling = 2,    // 추진력을 잃고 낙하 중 (사거리 초과/적에게서 떨어짐/부르기 중단)
        Grounded = 3,   // 바닥·벽·발판(지형)에 떨어지거나 꽂힘
        Stuck = 4,      // 적에게 박힘
        Returning = 5,  // 회수되어 플레이어에게 돌아오는 중
    }

    /// <summary>적중 판정이 어디서 발생했는지. 같은 적중 파이프라인을 공유한다.</summary>
    public enum HitSource
    {
        Shot,       // 발사된 화살의 비행 적중
        Thrust,     // 아레스 찌르기
        Pull,       // 아레스 뽑기에 의한 재적중
    }

    public enum ArrowHitEffectType
    {
        None = 0,
        Knockback = 1,  // 적중 방향으로 밀어냄 (청동 화살: 크게 밀어냄)
    }

    [Serializable]
    public class ArrowHitEffect
    {
        public ArrowHitEffectType type = ArrowHitEffectType.None;
        [Tooltip("효과 강도. Knockback: 밀어내는 거리(유닛)")]
        public float magnitude = 0f;
    }

    /// <summary>
    /// 화살 데이터. 덱빌딩 시 화살통에 들어가는 한 장의 '카드'에 해당한다.
    /// </summary>
    [Serializable]
    public class ArrowDefinition
    {
        public string id = "basic";
        public string displayName = "화살";
        [Tooltip("화살 개별 피해량. 최종 피해 = 개별 피해량 + 플레이어 공격력")]
        public float damage = 6f;
        [Tooltip("기획 데이터의 활시위(초). referenceDrawSeconds 로 나누어 발사 간격 배율로 정규화한다.")]
        public float drawSeconds = 1f;
        [Tooltip("화살 무게. 아테나(곡사)의 낙하 가속도에 곱해진다.")]
        public float weight = 1f;
        public Color tint = Color.white;
        public List<ArrowHitEffect> hitEffects = new List<ArrowHitEffect>();
        [TextArea] public string effectDescription = "없음";
    }

    /// <summary>테스트용 잠정 규칙의 선택지들. 모두 ArcheryConfig 에서 바꿀 수 있다.</summary>
    public enum HermesStackRefreshMode
    {
        RefreshAllOnShot = 0,   // 발사할 때마다 스택 +1, 전체 지속시간을 5초로 갱신 (만료 시 전부 소멸)
        IndependentStacks = 1,  // 스택마다 각자 5초 뒤 만료
    }

    public enum OrpheusReleaseMode
    {
        DropInPlace = 0,        // R을 떼면 귀환 중이던 화살이 그 자리에서 떨어진다
        ContinueReturn = 1,     // R을 떼도 이미 출발한 화살은 계속 돌아온다
    }

    public enum ZeusChainOrder
    {
        NearestNeighborFromCursor = 0,  // 커서에 가장 가까운 화살부터 최근접 이웃 순으로 연결
        AngleAroundCursor = 1,          // 커서를 중심으로 각도 순으로 연결
    }
}
