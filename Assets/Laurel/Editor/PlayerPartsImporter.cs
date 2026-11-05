using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Laurel.EditorTools
{
    /// <summary>
    /// 플레이어 파츠 시트(player_parts.png) 임포트 규칙과 슬라이스.
    /// - 픽셀 아트가 흐려지지 않도록 Point 필터·무압축·밉맵 없음을 항상 강제한다.
    /// - 원본 파츠를 그대로 잘라 쓴다 (다시 그리지 않음). 영역은 아래 표의 픽셀 좌표(왼쪽 위 기준).
    /// 메뉴: Tools > Laurel > Slice Player Parts
    /// </summary>
    public class PlayerPartsImporter : AssetPostprocessor
    {
        public const string SheetPath = "Assets/pjs/Sprites/Character/player_parts.png";
        public const int SheetSize = 256;
        /// <summary>
        /// 이 시트는 기존 캐릭터 스프라이트(32 PPU)를 2배 해상도로 다시 그린 것이다 (상의 16×18 = 기존 Body 8×9 의 2배).
        /// 64 PPU 로 두면 월드 크기와 충돌 판정이 기존 캐릭터와 같다.
        /// </summary>
        public const float PixelsPerUnit = 64f;

        /// <summary>
        /// (이름, x 최소, x 최대, y 최소, y 최대, 피벗 x, 피벗 y)
        /// 좌표는 이미지 왼쪽 위 기준 픽셀. 피벗은 픽셀 경계 좌표(관절·허리띠 위치).
        /// </summary>
        public static readonly (string name, int x0, int x1, int y0, int y1, float px, float py)[] Parts =
        {
            // 머리 3종: 같은 높이 상자로 잘라 목(아래 가운데) 기준이 서로 맞게 한다
            ("Head_0", 18, 35, 4, 20, 27f, 21f),
            ("Head_1", 38, 55, 4, 20, 47f, 21f),
            ("Head_2", 58, 75, 4, 20, 67f, 21f),
            // 팔 3종: 피벗 = 어깨
            ("Arm_Right_Relaxed", 6, 17, 27, 32, 6.5f, 30f),    // 짧은 팔: 쏘지 않을 때 내린 오른팔
            ("Arm_Right_Draw", 24, 33, 27, 42, 31.5f, 28f),     // 굽은 팔: 시위를 당기는 오른팔
            ("Arm_Left_Bow", 85, 98, 27, 41, 86.5f, 28.5f),     // 곧은 팔: 활을 잡은 왼팔
            // 상의 (왼쪽 것을 사용, 오른쪽은 예비)
            ("Torso", 42, 57, 25, 42, 50f, 34f),
            ("Torso_Alt", 62, 75, 25, 42, 69f, 34f),
            // 하의: 피벗 = 허리띠 윗변 가운데 (프레임을 바꿔도 허리가 고정되도록)
            ("Skirt_Idle", 2, 32, 54, 79, 19.5f, 54f),
            ("Skirt_Jump", 38, 60, 54, 87, 47f, 54f),
            ("Skirt_Spin", 71, 104, 53, 83, 78.5f, 54f),
            ("Skirt_Run1", 111, 150, 52, 78, 131f, 52f),
            ("Skirt_Run2", 156, 178, 52, 85, 166.5f, 52f),
            ("Skirt_Run3", 184, 228, 52, 78, 210.5f, 52f),
            // 흰 활: 피벗 = 손잡이 중심 (테두리가 끊긴 곳 = 원본에서 손이 가리는 자리). 사선으로 그려져 있어 회전 0°가 정면 조준 자세
            ("Bow", 55, 96, 119, 160, 81f, 142.5f),
            // 흰 화살통 (가로, 화살촉이 왼쪽)
            ("Quiver", 3, 51, 144, 160, 27.5f, 152.5f),
        };

        /// <summary>예전 이름 → 새 이름. 이름을 바꿔도 spriteID 를 이어받아 기존 참조가 끊기지 않게 한다.</summary>
        public static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>
        {
            { "Ribbon", "Bow" }, // 처음에 리본으로 잘못 붙였던 흰 활
        };

        private void OnPreprocessTexture()
        {
            if (assetPath != SheetPath) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
        }

        [MenuItem("Tools/Laurel/Slice Player Parts")]
        public static void Slice()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
            if (ti == null) { Debug.LogError("[Laurel] 파츠 시트를 찾을 수 없습니다: " + SheetPath); return; }
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var dp = factories.GetSpriteEditorDataProviderFromObject(ti);
            dp.InitSpriteEditorDataProvider();
            // 이름이 같은 기존 영역은 spriteID 를 유지해 프리팹 참조가 끊기지 않게 한다
            var old = dp.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var kv in Renamed)
                if (old.TryGetValue(kv.Key, out var renamed) && !old.ContainsKey(kv.Value)) { old.Remove(kv.Key); old[kv.Value] = renamed; }
            var rects = new List<SpriteRect>();
            foreach (var p in Parts)
            {
                var r = old.TryGetValue(p.name, out var existing) ? existing : new SpriteRect { spriteID = GUID.Generate() };
                r.name = p.name;
                r.rect = new Rect(p.x0, SheetSize - 1 - p.y1, p.x1 - p.x0 + 1, p.y1 - p.y0 + 1);
                r.alignment = SpriteAlignment.Custom;
                r.pivot = new Vector2((p.px - p.x0) / r.rect.width, 1f - (p.py - p.y0) / r.rect.height);
                rects.Add(r);
            }
            dp.SetSpriteRects(rects.ToArray());
            var names = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            dp.Apply();
            ti.SaveAndReimport();
            Debug.Log($"[Laurel] 플레이어 파츠 {rects.Count}개 슬라이스 완료");
        }
    }
}
