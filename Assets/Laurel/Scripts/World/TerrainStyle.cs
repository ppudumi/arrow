using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 지형(공중 발판·바닥·벽·천장)의 모양. 씬 오브젝트에 붙여 Inspector 에서 두께·색·테두리를 바꾼다.
    /// GameRoot 가 방을 만들 때 RoomTheme.style 로 넘기고, Room.Build 가 이 값으로 그린다 (Play 를 다시 시작하면 반영).
    /// - 충돌체 크기는 바꾸지 않는다. 발판은 '보이는 몸체'만 아래로 늘어나 착지 높이·점프 경로가 그대로다.
    /// - 테두리는 월드 유닛 고정 두께로 그려 블록 크기와 상관없이 같은 굵기다.
    /// - 외곽 블록은 플레이 공간 쪽 면에만 테두리를 그려, 맞닿은 블록 사이에 이중 테두리가 생기지 않는다.
    /// </summary>
    public class TerrainStyle : MonoBehaviour
    {
        [Header("공중 발판")]
        [Tooltip("발판이 보이는 두께 (월드 유닛). 윗면 높이는 고정이고 아래로 늘어난다. 충돌체 두께는 0.6 그대로 (이전 보이는 두께 0.6)")]
        public float platformThickness = 1.2f;
        [Tooltip("켜면 발판이 보이는 몸체 전체에서 막힌다 (밑면·옆면 충돌). 끄면 예전처럼 한쪽 통과 발판: 위에만 착지하고 아래·옆에서는 통과")]
        public bool solidPlatforms = true;

        [Header("색 (Laurel 지형 컨셉아트)")]
        [Tooltip("몸체: 아주 어두운 흙갈색")]
        public Color body = new Color32(0x13, 0x0B, 0x04, 0xFF);
        [Tooltip("테두리: 탁한 갈색")]
        public Color border = new Color32(0x6E, 0x57, 0x41, 0xFF);
        [Tooltip("착지면(윗면) 흙색 띠")]
        public Color topBand = new Color32(0x7A, 0x69, 0x57, 0xFF);

        [Header("테두리 두께 (월드 유닛, 블록 크기와 무관하게 일정)")]
        [Tooltip("옆·아래 테두리 선 두께")]
        public float borderWidth = 0.05f;
        [Tooltip("착지면 흙색 띠 두께")]
        public float topBandWidth = 0.1f;
        [Tooltip("테두리 안쪽으로 몸체 색에 섞여 들어가는 그라데이션 폭 (0 이면 없음)")]
        public float innerShade = 0.1f;
        [Tooltip("안쪽 그라데이션의 진하기 (0~1). 높을수록 테두리가 굵어 보인다")]
        [Range(0f, 1f)] public float innerShadeStrength = 0.55f;

        [Header("적용 범위")]
        [Tooltip("바닥·벽·천장(외곽 블록)에도 같은 색과 테두리를 쓴다. 플레이 공간 쪽 면에만 테두리를 그린다")]
        public bool styleOuterBlocks = true;
        [Tooltip("좌우 외벽 바깥을 화면 끝까지 몸체색으로 덮는다 (시각용, 충돌 없음). 보스 줌·초광폭 화면에서 벽 너머 배경이 보이지 않게 한다")]
        public bool maskOutsideWalls = true;
        [Tooltip("외벽 바깥 가림의 폭·높이 (월드 유닛). 화면이 보여 줄 수 있는 최대 범위(보스 줌 9 × 32:9 ≈ 중심에서 ±32)보다 충분히 커야 한다")]
        public float outsideMaskSize = 500f;
    }
}
