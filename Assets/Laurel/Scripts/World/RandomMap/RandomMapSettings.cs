using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 랜덤 맵 생성 설정 (에셋: Assets/Laurel/Art/RandomMap/RandomMapSettings.asset).
    /// 단위는 타일 칸(32px = 1유닛). 플레이어 키 ≈ 1칸, 한 번 점프 ≈ 7.5칸 높이.
    /// </summary>
    [CreateAssetMenu(menuName = "Laurel/Random Map Settings", fileName = "RandomMapSettings")]
    public class RandomMapSettings : ScriptableObject
    {
        [Header("시드")]
        [Tooltip("켜면 항상 아래 시드로 같은 맵을 만든다. 끄면 포탈로 들어갈 때마다 새 시드")]
        public bool useFixedSeed = false;
        public int fixedSeed = 12345;

        [Header("맵 크기 (칸)")]
        [Min(30)] public int width = 96;
        [Min(24)] public int height = 32;
        [Tooltip("좌우 외벽 두께")]
        [Range(2, 4)] public int wallThickness = 2;

        [Header("지면 (높이 랜덤 워크)")]
        [Tooltip("지면 윗면 최저 높이 (바닥에서 칸 수). 2 이상이어야 바닥 타일이 이어진다")]
        [Min(2)] public int groundMin = 3;
        [Tooltip("지면 윗면 최고 높이")]
        public int groundMax = 12;
        [Tooltip("같은 높이 구간 폭 (최소는 2 이상: Rule Tile 에 1칸 기둥용 조각이 없음)")]
        public Vector2Int segmentWidth = new Vector2Int(4, 10);
        [Tooltip("다음 구간으로 올라갈 수 있는 최대 칸 (한 번 점프로 오를 수 있게)")]
        [Range(0, 6)] public int maxStepUp = 3;
        [Tooltip("다음 구간으로 내려갈 수 있는 최대 칸")]
        [Range(0, 10)] public int maxStepDown = 4;
        [Tooltip("시작 지점 평지 폭")]
        [Min(4)] public int spawnFlatWidth = 8;

        [Header("공중 발판")]
        public Vector2Int platformCount = new Vector2Int(6, 11);
        public Vector2Int platformWidth = new Vector2Int(4, 9);
        [Tooltip("발판 두께 (2 이상)")]
        [Min(2)] public int platformThickness = 2;
        [Tooltip("발판 아래 빈 공간 최소 높이 (밑으로 지나갈 수 있게)")]
        [Min(3)] public int platformClearance = 4;
        [Tooltip("발판끼리·지면과 떨어뜨릴 최소 칸 (모서리 타일이 서로 붙지 않게)")]
        [Min(1)] public int platformMargin = 2;
        [Tooltip("배치 시도 횟수")]
        [Min(1)] public int platformAttempts = 200;

        [Header("도달 판정 (플레이어 이동 능력, 보수적으로)")]
        [Tooltip("한 번에 오를 수 있는 최대 칸 (실제 한 번 점프 ≈ 7.5, 2단 점프 ≈ 12)")]
        [Range(1, 10)] public int reachUp = 6;
        [Tooltip("한 번의 점프·낙하로 건너갈 수 있는 최대 가로 칸")]
        [Range(1, 12)] public int reachAcross = 6;
        [Tooltip("서 있을 칸 위로 비어 있어야 하는 칸 수 (플레이어 키 ≈ 1칸)")]
        [Range(2, 4)] public int headroom = 2;

        [Header("포탈·시작 지점")]
        [Tooltip("복귀 포탈과 시작 지점 사이 최소 가로 거리 (칸)")]
        [Min(0)] public int minPortalDistance = 30;
        [Tooltip("조건을 만족하지 못하면 시드를 바꿔 다시 생성하는 최대 횟수")]
        [Min(1)] public int maxRetries = 20;
    }
}
