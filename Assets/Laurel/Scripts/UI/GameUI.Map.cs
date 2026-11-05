using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 노드 선택 맵 (모든 스테이지 공용, 시안 raurel-map-concept-balanced.png 참고).
    /// 양피지 바탕 + 갈색 점선 경로 + 네모 칸(종류별 바탕색) 위 노드 아이콘(Resources/LaurelMap/*.png, 제공 PNG 그대로).
    /// 선택 가능한 노드는 얇은 금색 테두리. 제목·노드·연결선은 실제 맵 데이터(Run.map)로 그린다.
    /// 1스테이지는 맨 왼쪽에 올리브 나무 시작 표시(튜토리얼을 마친 출발 지점)를 두고 첫 층 노드들에만 점선을 잇는다. 나무는 클릭되지 않는다.
    /// 노드 위치·크기는 MapLayout 하나로 계산해 그리기·연결선·클릭 영역이 같은 값을 쓴다.
    /// </summary>
    public partial class GameUI
    {
        public const float MapMarginX = 80f, MapTop = 140f, MapBottomMargin = 70f;
        public const int StartNodeId = -1; // 시작 나무 (맵 데이터에는 없는 표시용)

        private static readonly Color MapParchment = new Color32(0xF4, 0xE4, 0xC4, 0xFF);
        private static readonly Color MapParchmentEdge = new Color32(0xC9, 0xA4, 0x72, 0xFF);
        private static readonly Color MapInk = new Color32(0x4A, 0x34, 0x22, 0xFF);        // 칸 테두리·제목
        private static readonly Color MapDash = new Color32(0x5C, 0x46, 0x32, 0xFF);       // 점선
        private static readonly Color MapCombat = new Color32(0x63, 0x83, 0xC1, 0xFF);     // 탁한 파랑
        private static readonly Color MapDark = new Color32(0x26, 0x22, 0x22, 0xFF);       // 정예·보스
        private static readonly Color MapLight = new Color32(0xFA, 0xEE, 0xD6, 0xFF);      // 시작·상점·샘·보상
        private static readonly Color MapGold = new Color32(0xF0, 0xA8, 0x2A, 0xFF);       // 선택 가능
        private static readonly Color MapBossEdge = new Color32(0x7A, 0x1F, 0x1F, 0xFF);

        private readonly Dictionary<string, Texture2D> mapIcons = new Dictionary<string, Texture2D>();

        /// <summary>노드 종류 → 스프라이트 이름 (Resources/LaurelMap/이름.png)</summary>
        public static string MapIconName(NodeType t)
        {
            switch (t)
            {
                case NodeType.Elite: return "elite";
                case NodeType.Reward: return "reward";
                case NodeType.Shop: return "shop";
                case NodeType.Rest: return "spring";
                case NodeType.Boss: return "boss";
                default: return "combat";
            }
        }

        private Texture2D MapIcon(string name)
        {
            if (mapIcons.TryGetValue(name, out var t) && t != null) return t;
            var src = Resources.Load<Texture2D>("LaurelMap/" + name);
            if (src == null) return null;
            Texture2D tex = src;
            if (src.isReadable) // Linear 색 공간 보정한 복사본 (색·투명도 그대로 보이게)
            {
                tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(ForScreen(src.GetPixels32()));
                tex.Apply();
            }
            mapIcons[name] = tex;
            return tex;
        }

        private static Color MapBoxColor(NodeType t) => t == NodeType.Combat ? MapCombat : (t == NodeType.Elite || t == NodeType.Boss ? MapDark : MapLight);

        /// <summary>
        /// 맵 노드의 화면 위치와 반폭. withStart 면 맨 왼쪽에 시작 나무 열(id -1)을 하나 더 둔다.
        /// 열 수·층별 최대 노드 수·화면 크기로 칸을 나눠 노드 크기와 간격을 정한다.
        /// </summary>
        public static Dictionary<int, (Vector2 p, float rad)> MapLayout(MapData map, float vw, float vh, bool withStart = false)
        {
            var res = new Dictionary<int, (Vector2, float)>();
            int layers = map.LayerCount, cols = layers + (withStart ? 1 : 0), maxPerLayer = 1;
            for (int L = 0; L < layers; L++) maxPerLayer = Mathf.Max(maxPerLayer, map.layers[L].Count);
            float left = MapMarginX, right = vw - MapMarginX, top = MapTop, bottom = vh - MapBottomMargin;
            float cellW = (right - left) / Mathf.Max(1, cols);
            float cellH = (bottom - top) / Mathf.Max(1, maxPerLayer);
            float rad = Mathf.Clamp(Mathf.Min(cellW, cellH) * 0.19f, 26f, 48f);
            float bossRad = Mathf.Min(rad * 1.4f, Mathf.Min(cellW, cellH) * 0.36f);
            float midY = (top + bottom) * 0.5f;
            int c0 = 0;
            if (withStart) { res[StartNodeId] = (new Vector2(left + cellW * 0.5f, midY), rad); c0 = 1; }
            for (int L = 0; L < layers; L++)
            {
                var layer = map.layers[L];
                float x = left + cellW * (L + c0 + 0.5f);
                float y0 = midY - cellH * layer.Count * 0.5f;
                for (int i = 0; i < layer.Count; i++)
                {
                    var n = map.nodes[layer[i]];
                    res[layer[i]] = (new Vector2(x, y0 + cellH * (i + 0.5f)), n.type == NodeType.Boss ? bossRad : rad);
                }
            }
            return res;
        }

        /// <summary>이 맵에 시작 나무를 그리는지 (1스테이지)</summary>
        public static bool MapHasStart(RunState run) => run != null && run.stage == 1;

        private void DashedLine(Vector2 a, Vector2 b, Color c, float w, float dash = 11f, float gap = 8f)
        {
            Vector2 d = b - a; float len = d.magnitude; if (len < 0.01f) return;
            Vector2 u = d / len;
            for (float t = 0f; t < len; t += dash + gap) Line(a + u * t, a + u * Mathf.Min(t + dash, len), c, w);
        }

        private void MapBox(Vector2 p, float rad, Color fill, Color edge, float edgeW, Texture2D icon, bool dim)
        {
            Rect(new Rect(p.x - rad - edgeW, p.y - rad - edgeW, rad * 2 + edgeW * 2, rad * 2 + edgeW * 2), ScreenColor(edge));
            Rect(new Rect(p.x - rad, p.y - rad, rad * 2, rad * 2), ScreenColor(fill));
            if (icon != null)
            {
                float s = rad * 2f * 0.84f;
                var old = GUI.color; if (dim) GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), icon, ScaleMode.ScaleToFit);
                GUI.color = old;
            }
            if (dim) Rect(new Rect(p.x - rad, p.y - rad, rad * 2, rad * 2), ScreenColor(new Color(0.35f, 0.27f, 0.2f, 0.35f))); // 지나온 칸
        }

        private void DrawMap()
        {
            var run = G.Run;
            // 양피지 바탕 + 가장자리
            Rect(new Rect(0, 0, VW, VH), ScreenColor(MapParchmentEdge));
            Rect(new Rect(10, 10, VW - 20, VH - 20), ScreenColor(MapParchment));
            if (run == null || run.map == null) return;
            var stage = DB.Stage(run.stage);
            var titleStyle = new GUIStyle(label) { fontSize = 40, fontStyle = FontStyle.Bold, wordWrap = false };
            InkText(new Rect(60, 34, 900, 54), $"<color=#{Hex(ScreenColor(MapInk))}>{stage.name}</color>", titleStyle);
            float tw = titleStyle.CalcSize(new GUIContent(stage.name)).x;
            InkText(new Rect(60 + tw + 18, 50, 600, 32), $"<color=#{Hex(ScreenColor(new Color32(0x8A, 0x6E, 0x50, 255)))}>{run.stage}스테이지 · {stage.subtitle}</color>", new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false });

            var map = run.map;
            bool withStart = MapHasStart(run);
            var lay = MapLayout(map, VW, VH, withStart);
            var choices = map.Choices(run.currentNode);

            // 연결선 (실제 맵 데이터만): 시작 나무 → 첫 층, 그리고 노드 간 연결
            if (withStart) foreach (var id in map.layers[0]) DashedLine(lay[StartNodeId].p, lay[id].p, ScreenColor(MapDash), 3f);
            foreach (var n in map.nodes)
                foreach (var nx in n.next)
                    DashedLine(lay[n.id].p, lay[nx].p, ScreenColor(n.visited && n.id != run.currentNode ? MapDash.WithAlpha(0.45f) : MapDash), n.id == run.currentNode ? 4f : 3f);

            // 시작 나무 (표시용, 클릭 없음)
            if (withStart) { var (sp, sr) = lay[StartNodeId]; MapBox(sp, sr, MapLight, MapInk, 4f, MapIcon("start"), false); }

            foreach (var n in map.nodes)
            {
                var (p, rad) = lay[n.id];
                bool can = choices.Contains(n);
                bool cur = n.id == run.currentNode;
                Color edge = n.type == NodeType.Boss ? MapBossEdge : MapInk;
                if (can) Rect(new Rect(p.x - rad - 9, p.y - rad - 9, rad * 2 + 18, rad * 2 + 18), ScreenColor(MapGold)); // 얇은 금색 테두리
                MapBox(p, rad, MapBoxColor(n.type), edge, n.type == NodeType.Boss ? 6f : 4f, MapIcon(MapIconName(n.type)), n.visited && !cur);
                if (cur) Rect(new Rect(p.x - rad, p.y + rad + 8, rad * 2, 5), ScreenColor(MapInk)); // 지금 있는 칸
                if (can && GUI.Button(new Rect(p.x - rad, p.y - rad, rad * 2, rad * 2), GUIContent.none, GUIStyle.none)) { G.EnterNode(n); return; }
            }
        }
    }
}
