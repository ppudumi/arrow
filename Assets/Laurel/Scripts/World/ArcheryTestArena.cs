using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 궁술 테스트 씬(Laurel_ArcheryTest) 설정. 씬에 이 오브젝트가 있으면 GameRoot 가 타이틀 대신 바로 테스트 맵을 연다.
    /// - 모든 발사·회수 궁술 + 더블점프·3단 점프·대쉬 사용 가능. 공격력·화살은 강화 없는 기본값(기본 화살 6발)
    /// - 플레이어는 정상적으로 맞지만 체력이 1 아래로 내려가지 않는다
    /// - 이 씬의 해금·선택은 메모리에만 있고 세이브 파일에 쓰지 않는다 (SaveStore.Disabled)
    /// 맵: 1스테이지 일반 전투 방(정사각형 arenaSize)을 기준으로 좌우 충돌 경계 사이 폭 = 기준 × widthMultiplier.
    /// 높이·발판 두께·캐릭터 크기는 기존 그대로. 좌표는 방 중심 기준 월드 유닛.
    /// </summary>
    public class ArcheryTestArena : MonoBehaviour
    {
        [Header("1스테이지 배경·지형 (씬 오브젝트)")]
        [Tooltip("배경 이미지 (델포이 숲 콘셉트아트). 폭은 카메라가 양 끝까지 가도 이미지가 끊기지 않게 잡는다 (32:9 화면 기준 ≥ 53.4)")]
        public RoomBackdrop backdrop;
        [Tooltip("지형 모양 (튜토리얼·1스테이지 첫 전투 방과 같은 어두운 흙갈색 막힌 발판)")]
        public TerrainStyle terrainStyle;

        [Header("크기 (기준 = 1스테이지 일반 전투 방)")]
        [Tooltip("기준 방의 플레이 가능 폭. 0 이면 balance.json → world.arenaSize (현재 22)")]
        public float referenceWidth = 0f;
        [Tooltip("기준 방 폭에 곱할 배율 (요청: 2.5배)")]
        public float widthMultiplier = 2.5f;
        [Tooltip("방 안쪽 높이. 0 이면 기준 방과 같게 (arenaSize)")]
        public float height = 0f;

        [Header("배치 (방 중심 기준, 바닥 윗면 y = -높이/2)")]
        [Tooltip("플레이어 시작 위치 (발 위치)")]
        public Vector2 playerSpawn = new Vector2(-24.5f, -11f);
        [Tooltip("발판: x = 가운데, y = 가운데(윗면 = y + 0.3), z = 길이. 막힌 발판이라 아래에서 뚫고 올라갈 수 없다")]
        public Vector3[] platforms =
        {
            new Vector3(-21f, -7f, 5f),   // A 왼쪽 낮은 발판
            new Vector3(-13f, -4f, 5f),   // B
            new Vector3(-20f, 0.5f, 5f),  // C 왼쪽 높은 표적
            new Vector3(-4f, -7f, 5f),    // D 가운데 낮은 발판
            new Vector3(3f, -2.5f, 6f),   // E 가운데 중간 표적
            new Vector3(-6f, 2.5f, 5f),   // F
            new Vector3(4f, 7f, 5f),      // G 가운데 가장 높은 표적
            new Vector3(13f, -7f, 5f),    // H 오른쪽 낮은 발판
            new Vector3(19f, -2.5f, 5f),  // I 먼 중간 표적
            new Vector3(13f, 2.5f, 4f),   // J
            new Vector3(23f, 5f, 5f),     // K 먼 높은 표적
        };
        [Tooltip("허수아비 위치: x, 서 있을 면의 윗면 높이 (바닥 = -11, 발판 = 발판 y + 0.3)")]
        public Vector2[] dummies =
        {
            new Vector2(-15f, -11f),   // 가까운 바닥 (시작점에서 9.5)
            new Vector2(-5f, -11f),    // 중간 바닥 (19.5)
            new Vector2(9f, -11f),     // 먼 바닥 (33.5, 직사 사거리 32 경계)
            new Vector2(17f, -11f),    // 더 먼 바닥 (41.5)
            new Vector2(26f, -11f),    // 맨 끝 바닥 (50.5)
            new Vector2(-20f, 0.8f),   // C 왼쪽 높은 곳
            new Vector2(3f, -2.2f),    // E 가운데 중간 높이
            new Vector2(4f, 7.3f),     // G 가운데 가장 높은 곳
            new Vector2(19f, -2.2f),   // I 먼 중간 높이
            new Vector2(23f, 5.3f),    // K 먼 높은 곳
        };
        [Tooltip("허수아비 최대 체력. 체력이 바닥나기 전에 다시 차서 쓰러지지 않는다")]
        public float dummyHp = 99999f;

        public float ReferenceWidth => referenceWidth > 0f ? referenceWidth : DB.Balance.world.arenaSize;
        public float Width => ReferenceWidth * widthMultiplier;
        public float Height => height > 0f ? height : DB.Balance.world.arenaSize;

        public RoomTheme MakeTheme()
        {
            var theme = RoomTheme.FromStage(DB.Stage(1));
            theme.name = "궁술 수련장 — " + theme.name;
            theme.style = terrainStyle;
            return theme;
        }

        /// <summary>이 씬 전용 프로필: 모든 궁술·이동 스킬 보유, 능력치 강화 없음. 저장되지 않는다.</summary>
        public static Profile MakeProfile()
        {
            var p = new Profile { tutorialDone = true, lobbyIntroSeen = true };
            foreach (var n in DB.NectarNodes)
                if (n.kind == "launch" || n.kind == "retrieval" || n.kind == "skill") p.SetLevel(n.id, 1);
            return p;
        }

        public List<Vector3> PlatformList() => new List<Vector3>(platforms);
    }
}
