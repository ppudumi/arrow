using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public enum NodeType { Combat, Elite, Reward, Shop, Rest, Boss }

    public class MapNode
    {
        public int id;
        public int layer;
        public int index;
        public NodeType type;
        public readonly List<int> next = new List<int>();
        public bool visited;
        public int seed;

        public static string TypeName(NodeType t)
        {
            switch (t)
            {
                case NodeType.Elite: return "정예";
                case NodeType.Reward: return "보상";
                case NodeType.Shop: return "상점";
                case NodeType.Rest: return "샘";
                case NodeType.Boss: return "보스";
                default: return "전투";
            }
        }

        public static Color TypeColor(NodeType t)
        {
            switch (t)
            {
                case NodeType.Elite: return new Color(1f, 0.45f, 0.3f);
                case NodeType.Reward: return new Color(1f, 0.85f, 0.3f);
                case NodeType.Shop: return new Color(0.4f, 0.85f, 1f);
                case NodeType.Rest: return new Color(0.45f, 1f, 0.6f);
                case NodeType.Boss: return new Color(0.9f, 0.2f, 0.35f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }
    }

    /// <summary>
    /// 한 스테이지의 노드 지도 (슬레이 더 스파이어 방식의 층 구조).
    /// [임시 결정] 층 구성은 stages.json 의 layers, 마지막에 보스 1개. 각 노드는 다음 층의 가까운 노드 1~2개와 연결된다.
    /// </summary>
    public class MapData
    {
        public int stage;
        public readonly List<MapNode> nodes = new List<MapNode>();
        public readonly List<List<int>> layers = new List<List<int>>();
        public int LayerCount => layers.Count;
        public MapNode Boss => nodes[nodes.Count - 1];

        public MapNode Get(int id) => id >= 0 && id < nodes.Count ? nodes[id] : null;

        /// <summary>현재 위치에서 고를 수 있는 다음 노드</summary>
        public List<MapNode> Choices(int currentNode)
        {
            var list = new List<MapNode>();
            if (currentNode < 0)
            {
                foreach (var id in layers[0]) list.Add(nodes[id]);
                return list;
            }
            foreach (var id in nodes[currentNode].next) list.Add(nodes[id]);
            return list;
        }

        public static MapData Generate(StageDef def, int seed)
        {
            var rng = new System.Random(seed);
            var map = new MapData { stage = def.index };

            var layerSpecs = new List<string>(def.layers) { "B" };
            for (int L = 0; L < layerSpecs.Count; L++)
            {
                string spec = layerSpecs[L];
                var ids = new List<int>();
                for (int i = 0; i < spec.Length; i++)
                {
                    var n = new MapNode { id = map.nodes.Count, layer = L, index = i, seed = rng.Next() };
                    n.type = Parse(spec[i], def.mixWeights, rng);
                    map.nodes.Add(n);
                    ids.Add(n.id);
                }
                map.layers.Add(ids);
            }

            for (int L = 0; L + 1 < map.layers.Count; L++)
            {
                var cur = map.layers[L];
                var nxt = map.layers[L + 1];
                int n = cur.Count, m = nxt.Count;
                var incoming = new int[m];
                for (int i = 0; i < n; i++)
                {
                    var node = map.nodes[cur[i]];
                    int j = n == 1 ? m / 2 : Mathf.RoundToInt(i * (m - 1f) / (n - 1f));
                    Link(node, nxt[j], incoming, j);
                    // 이웃 노드로 가지를 하나 더 낸다 (경로 선택지)
                    int k = j + (rng.Next(2) == 0 ? -1 : 1);
                    if (k >= 0 && k < m && rng.NextDouble() < 0.6) Link(node, nxt[k], incoming, k);
                }
                // 들어오는 길이 없는 노드는 가장 가까운 앞 노드와 잇는다
                for (int j = 0; j < m; j++)
                {
                    if (incoming[j] > 0) continue;
                    int i = m == 1 ? 0 : Mathf.Clamp(Mathf.RoundToInt(j * (n - 1f) / (m - 1f)), 0, n - 1);
                    Link(map.nodes[cur[i]], nxt[j], incoming, j);
                }
            }
            return map;
        }

        private static void Link(MapNode from, int toId, int[] incoming, int j)
        {
            if (from.next.Contains(toId)) return;
            from.next.Add(toId);
            incoming[j]++;
        }

        private static NodeType Parse(char c, MixWeights w, System.Random rng)
        {
            switch (c)
            {
                case 'C': return NodeType.Combat;
                case 'E': return NodeType.Elite;
                case 'R': return NodeType.Reward;
                case 'S': return NodeType.Shop;
                case 'F': return NodeType.Rest;
                case 'B': return NodeType.Boss;
            }
            int total = w.C + w.R + w.S + w.F;
            int r = rng.Next(Mathf.Max(1, total));
            if ((r -= w.C) < 0) return NodeType.Combat;
            if ((r -= w.R) < 0) return NodeType.Reward;
            if ((r -= w.S) < 0) return NodeType.Shop;
            return NodeType.Rest;
        }
    }
}
