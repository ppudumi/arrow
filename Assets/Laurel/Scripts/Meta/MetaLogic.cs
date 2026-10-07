using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public enum NectarNodeState { Locked, BossLocked, Available, TooExpensive, Maxed }

    /// <summary>
    /// 넥타르 강화 규칙. 유니크 스킬은 '보스 클리어 → 구매 자격 해금 → 로비에서 넥타르로 구매 → 영구 보유' 순서만 허용한다.
    /// 보스 처치는 구매 자격만 줄 뿐 스킬을 자동 지급하지 않는다.
    /// </summary>
    public static class NectarShop
    {
        public static int NextCost(Profile p, NectarNodeDef n)
        {
            int lv = p.Level(n.id);
            return lv < n.MaxLevel ? n.cost[lv] : 0;
        }

        public static NectarNodeState StateOf(Profile p, NectarNodeDef n, out string reason)
        {
            reason = "";
            int lv = p.Level(n.id);
            if (lv >= n.MaxLevel) { reason = n.kind == "skill" ? "구매 완료 (영구 보유)" : "최대 레벨"; return NectarNodeState.Maxed; }
            foreach (var r in n.requires)
            {
                if (p.Level(r) <= 0)
                {
                    reason = "선행 강화 필요: " + (DB.NectarById.TryGetValue(r, out var rn) ? rn.name : r);
                    return NectarNodeState.Locked;
                }
            }
            if (n.bossUnlock > 0 && !p.BossDefeated(n.bossUnlock))
            {
                var st = DB.Stage(n.bossUnlock);
                string boss = DB.Bosses.TryGetValue(st.boss, out var b) ? b.name : st.boss;
                reason = $"잠금: {n.bossUnlock}스테이지 보스 '{boss}' 처치 시 구매 가능";
                return NectarNodeState.BossLocked;
            }
            int cost = n.cost[lv];
            if (p.nectar < cost) { reason = $"넥타르 부족 ({p.nectar}/{cost})"; return NectarNodeState.TooExpensive; }
            reason = $"구매 가능 — 넥타르 {cost}";
            return NectarNodeState.Available;
        }

        public static bool TryBuy(Profile p, NectarNodeDef n)
        {
            if (StateOf(p, n, out _) != NectarNodeState.Available) return false;
            int cost = NextCost(p, n);
            p.nectar -= cost;
            p.nectarSpentTotal += cost;
            p.SetLevel(n.id, p.Level(n.id) + 1);
            SaveStore.Save(p);
            return true;
        }
    }

    public class ShopOffer
    {
        public string kind;     // arrow / relic / heal / ambrosia / remove
        public int arrowCode;
        public string relicId;
        public int price;
        public bool sold;
        public string Title
        {
            get
            {
                switch (kind)
                {
                    case "arrow": return DB.Arrow(arrowCode).name;
                    case "relic": return DB.Relics[relicId].name;
                    case "heal": return "회복 물약";
                    case "ambrosia": return "암브로시아 조각";
                    default: return "화살 제거";
                }
            }
        }
    }

    /// <summary>
    /// [임시 결정] 상점·보상 규칙. 등급 확률은 스테이지별 가중치(balance.json), 가격은 등급별 고정값.
    /// 화살 제거는 첫 회 무료, 이후 25씩 증가. 리롤은 골드(15, +10씩) 또는 넥타르 1.
    /// </summary>
    public static class Loot
    {
        public static int RollRarity(System.Random rng, int stage)
        {
            var w = DB.RarityWeights[Mathf.Clamp(stage - 1, 0, DB.RarityWeights.Length - 1)];
            int total = 0;
            foreach (var x in w) total += x;
            int r = rng.Next(Mathf.Max(1, total));
            for (int i = 0; i < w.Length; i++) { if ((r -= w[i]) < 0) return i; }
            return 0;
        }

        public static ArrowDef RollArrow(System.Random rng, int stage, ICollection<int> exclude = null)
        {
            int rarity = RollRarity(rng, stage);
            for (int d = 0; d < 6; d++)
            {
                foreach (int rr in new[] { rarity - d, rarity + d })
                {
                    if (rr < 0 || rr > 5) continue;
                    var pool = new List<ArrowDef>();
                    foreach (var a in DB.ArrowPool) if (a.rarity == rr && (exclude == null || !exclude.Contains(a.code))) pool.Add(a);
                    if (pool.Count > 0) return pool[rng.Next(pool.Count)];
                }
            }
            return DB.Arrow(0);
        }

        public static List<ArrowDef> ArrowChoices(System.Random rng, int stage, int n)
        {
            var list = new List<ArrowDef>();
            var codes = new List<int>();
            for (int i = 0; i < n; i++) { var a = RollArrow(rng, stage, codes); list.Add(a); codes.Add(a.code); }
            return list;
        }

        public static List<RelicDef> RelicChoices(System.Random rng, RunState run, int n)
        {
            var pool = new List<RelicDef>();
            foreach (var r in DB.Relics.Values) if (!run.HasRelic(r.id)) pool.Add(r);
            var list = new List<RelicDef>();
            while (list.Count < n && pool.Count > 0)
            {
                // 높은 등급일수록 덜 나온다
                int total = 0;
                foreach (var r in pool) total += 6 - Mathf.Min(5, r.rarity);
                int k = rng.Next(Mathf.Max(1, total));
                RelicDef pick = pool[0];
                foreach (var r in pool) { k -= 6 - Mathf.Min(5, r.rarity); if (k < 0) { pick = r; break; } }
                list.Add(pick);
                pool.Remove(pick);
            }
            return list;
        }

        public static int ArrowPrice(ArrowDef a) => DB.Balance.economy.arrowPrice[Mathf.Clamp(a.rarity, 0, 5)];
        public static int RelicPrice(RelicDef r) => DB.Balance.economy.relicPrice[Mathf.Clamp(r.rarity, 0, 5)];
        public static int RemoveCost(RunState run) => run.removeCount == 0 ? 0 : DB.Balance.economy.removeBaseCost + DB.Balance.economy.removeCostStep * (run.removeCount - 1);

        public static List<ShopOffer> BuildShop(System.Random rng, RunState run)
        {
            var eco = DB.Balance.economy;
            var list = new List<ShopOffer>();
            foreach (var a in ArrowChoices(rng, run.stage, eco.shopArrows)) list.Add(new ShopOffer { kind = "arrow", arrowCode = a.code, price = ArrowPrice(a) });
            foreach (var r in RelicChoices(rng, run, eco.shopRelics)) list.Add(new ShopOffer { kind = "relic", relicId = r.id, price = RelicPrice(r) });
            list.Add(new ShopOffer { kind = "heal", price = eco.healPotionPrice });
            list.Add(new ShopOffer { kind = "ambrosia", price = eco.ambrosiaPrice });
            return list;
        }
    }
}
