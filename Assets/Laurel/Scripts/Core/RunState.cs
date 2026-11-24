using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public enum LaunchStyle { Apollo, Hermes, Athena, Odysseus, Ares }
    public enum RetrievalStyle { Basic, Orpheus, Ares, Demeter, Hades, Zeus }
    public enum QuiverType { Basic, Light, Heavy }

    public static class QuiverStats
    {
        public static readonly QuiverType[] All = { QuiverType.Basic, QuiverType.Light, QuiverType.Heavy };

        public static string Name(QuiverType q)
        {
            switch (q)
            {
                case QuiverType.Light: return "경량 화살통";
                case QuiverType.Heavy: return "중형 화살통";
                default: return "기본 화살통";
            }
        }

        public static string ShortName(QuiverType q)
        {
            switch (q)
            {
                case QuiverType.Light: return "경량";
                case QuiverType.Heavy: return "중형";
                default: return "기본";
            }
        }

        public static int ArrowCount(QuiverType q)
        {
            switch (q)
            {
                case QuiverType.Light: return 1;
                case QuiverType.Heavy: return 20;
                default: return 6;
            }
        }

        public static float MoveSpeedMul(QuiverType q) => q == QuiverType.Heavy ? 0.72f : 1f;

        public static string Desc(QuiverType q)
        {
            switch (q)
            {
                case QuiverType.Light: return "화살 1발";
                case QuiverType.Heavy: return "화살 20발 · 이동속도 감소";
                default: return "화살 6발";
            }
        }

        public static string ResourcePath(QuiverType q)
        {
            switch (q)
            {
                case QuiverType.Light: return "LaurelFx/quiver-light";
                case QuiverType.Heavy: return "LaurelFx/quiver-heavy";
                default: return "LaurelFx/quiver-basic";
            }
        }
    }

    public static class StyleNames
    {
        public static readonly LaunchStyle[] AllLaunch = { LaunchStyle.Apollo, LaunchStyle.Hermes, LaunchStyle.Athena, LaunchStyle.Odysseus, LaunchStyle.Ares };
        public static readonly RetrievalStyle[] AllRetrieval = { RetrievalStyle.Basic, RetrievalStyle.Orpheus, RetrievalStyle.Ares, RetrievalStyle.Demeter, RetrievalStyle.Hades, RetrievalStyle.Zeus };

        public static string Launch(LaunchStyle s)
        {
            switch (s)
            {
                case LaunchStyle.Hermes: return "헤르메스 — 연사";
                case LaunchStyle.Athena: return "아테나 — 곡사";
                case LaunchStyle.Odysseus: return "오디세우스 — 차지샷";
                case LaunchStyle.Ares: return "아레스 — 찌르기";
                default: return "아폴론 — 기본사격";
            }
        }

        public static string Retrieval(RetrievalStyle s)
        {
            switch (s)
            {
                case RetrievalStyle.Orpheus: return "오르페우스 — 부르기";
                case RetrievalStyle.Ares: return "아레스 — 뽑기";
                case RetrievalStyle.Demeter: return "데메테르 — 수확";
                case RetrievalStyle.Hades: return "하데스 — 부름";
                case RetrievalStyle.Zeus: return "제우스 — 뇌우";
                default: return "기본 — 줍기";
            }
        }

        public static string LaunchDesc(LaunchStyle s)
        {
            switch (s)
            {
                case LaunchStyle.Hermes: return "기본 간격 1.25초. 쏠 때마다 공격속도 +10% (5초, 최대 5중첩).";
                case LaunchStyle.Athena: return "기본 간격 0.6초. 화살이 무게만큼 아래로 휘어 떨어진다.";
                case LaunchStyle.Odysseus: return "좌클릭을 누르고 1.2~1.5초 당겼다 놓는다. 0.1초당 피해 +10%.";
                case LaunchStyle.Ares: return "기본 간격 0.5초. 눈앞의 적을 찌른다. 빗나가면 화살을 쓰지 않는다.";
                default: return "기본 간격 1초. 마우스 방향으로 곧게 쏜다.";
            }
        }

        public static string RetrievalDesc(RetrievalStyle s)
        {
            switch (s)
            {
                case RetrievalStyle.Orpheus: return "R을 누르고 있으면 모든 화살이 5초에 걸쳐 돌아온다. 멀수록 빠르다.";
                case RetrievalStyle.Ares: return "적에게 박힌 지 0.5초 지난 화살에 다가가면 모두 뽑으며 한 번 더 적중시킨다.";
                case RetrievalStyle.Demeter: return "R로 낫을 휘둘러 공격력×2 피해를 주고 범위 안 화살을 거둔다.";
                case RetrievalStyle.Hades: return "땅이나 적에 있는 화살이 저절로 5초에 걸쳐 돌아온다.";
                case RetrievalStyle.Zeus: return "R로 커서 주변 화살을 즉시 부른다(6초). 화살 사이 적에게 번개.";
                default: return "화살이 적에게 박히지 않고 떨어진다. 바닥의 화살을 주워 쓴다.";
            }
        }

        public static bool TryParseLaunch(string s, out LaunchStyle v) => System.Enum.TryParse(s, out v);
        public static bool TryParseRetrieval(string s, out RetrievalStyle v) => System.Enum.TryParse(s, out v);
    }

    /// <summary>
    /// 화살 카드 한 장(덱의 한 칸). 변형(분노 비활성→활성, 이카루스↔밀랍)은 별도 카드가 아니라 이 카드의 상태다.
    /// </summary>
    public class ArrowCard
    {
        private static int nextUid = 1;
        public readonly int uid;
        public readonly int baseCode;
        /// <summary>도전 동안 유지되는 추가 피해 (제물 화살 처치 시 +6). 사망하면 카드째 사라진다.</summary>
        public float runBonus;
        /// <summary>라운드(=노드) 동안만 유지되는 추가 피해 (예리한 +6, 유리 -4)</summary>
        public float roundBonus;
        /// <summary>이번 라운드 동안 소멸됨 (폭탄·제물·유리·분열체 버림·조약돌·마찰). 노드가 끝나면 복구.</summary>
        public bool consumed;
        /// <summary>분노 화살 활성 상태 (라운드 동안)</summary>
        public bool rageActive;
        /// <summary>조약돌 주머니처럼 유물로 받은 카드 (상점 제거 대상 아님)</summary>
        public bool fromRelic;

        public ArrowCard(int code, bool fromRelic = false)
        {
            uid = nextUid++;
            baseCode = code;
            this.fromRelic = fromRelic;
        }

        public ArrowDef Base => DB.Arrow(baseCode);

        /// <summary>현재 형태(변형 반영)</summary>
        public ArrowDef Def
        {
            get
            {
                var b = Base;
                if (b.effect == "rage" && rageActive && b.variantTo >= 0) return DB.Arrow(b.variantTo);
                return b;
            }
        }

        public float Damage => Mathf.Max(0f, Def.damage + runBonus + roundBonus);

        public void ResetRound()
        {
            roundBonus = 0f;
            consumed = false;
            rageActive = false;
        }

        public string Label => $"{Def.name}#{uid}";
    }

    /// <summary>계산된 최종 능력치</summary>
    public struct PlayerStats
    {
        public float maxHp, attack, defense, attackSpeedBonus, moveMul, regenPer5s;
        public bool revive, doubleJump, dash, tripleJump;
        public int extraStartArrows;

        public int MaxJumps => tripleJump && doubleJump ? 3 : (doubleJump ? 2 : 1);
    }

    /// <summary>
    /// 도전(런) 하나의 상태. 사망·포기·클리어로 도전이 끝나면 통째로 버린다(돈·화살·유물·도전 중 능력치·일시 효과 초기화).
    /// </summary>
    public class RunState
    {
        public bool isTutorial;
        public int stage = 1;
        public MapData map;
        public int currentNode = -1;
        public int gold;
        public readonly List<ArrowCard> deck = new List<ArrowCard>();
        /// <summary>노드 사이에 유지되는 화살통 순서 (카드 uid)</summary>
        public readonly List<int> quiverOrder = new List<int>();
        public readonly List<string> relics = new List<string>();
        public float hp;
        public float bonusMaxHp;      // 상점 암브로시아 (도전 중)
        public bool reviveUsed;
        public int removeCount;
        public int kills, nodesCleared, goldEarned, nectarEarned, bossesKilled;
        public float startTime;
        public LaunchStyle launch;
        public RetrievalStyle retrieval;
        public QuiverType quiver = QuiverType.Basic;
        public int seed;

        public bool HasRelic(string id) => relics.Contains(id);

        public RelicDef RelicWithEffect(string effect)
        {
            foreach (var id in relics)
                if (DB.Relics.TryGetValue(id, out var r) && r.effect == effect) return r;
            return null;
        }

        public float RelicSum(string effect)
        {
            float s = 0f;
            foreach (var id in relics)
                if (DB.Relics.TryGetValue(id, out var r) && r.effect == effect) s += r.p1;
            return s;
        }

        public ArrowCard AddCard(int code, bool fromRelic = false)
        {
            var c = new ArrowCard(code, fromRelic);
            deck.Add(c);
            quiverOrder.Add(c.uid);
            return c;
        }

        public bool RemoveCard(ArrowCard c)
        {
            if (!deck.Remove(c)) return false;
            quiverOrder.Remove(c.uid);
            return true;
        }

        public ArrowCard Card(int uid)
        {
            for (int i = 0; i < deck.Count; i++) if (deck[i].uid == uid) return deck[i];
            return null;
        }

        public void AddRelic(string id)
        {
            if (relics.Contains(id) || !DB.Relics.ContainsKey(id)) return;
            relics.Add(id);
            var r = DB.Relics[id];
            if (r.effect == "pebbles")
            {
                for (int i = 0; i < Mathf.RoundToInt(r.p1); i++) AddCard(41, true);
            }
            if (r.effect == "maxHp") hp += r.p1;
            if (r.effect == "tutorialPower") hp += Mathf.Max(0f, DB.Balance.tutorial.maxHp - DB.Balance.player.maxHp);
        }

        public int NonRelicCardCount
        {
            get
            {
                int n = 0;
                foreach (var c in deck) if (!c.fromRelic) n++;
                return n;
            }
        }

        /// <summary>능력치 계산: 기본 + 넥타르 강화 + 유물 + 도전 중 보너스</summary>
        public PlayerStats ComputeStats(Profile profile)
        {
            var b = DB.Balance;
            var s = new PlayerStats();
            s.maxHp = b.player.maxHp + profile.StatBonus("maxHp") + bonusMaxHp + RelicSum("maxHp");
            s.attack = b.player.attack + profile.StatBonus("attack") + RelicSum("attack");
            s.defense = b.player.defense + profile.StatBonus("defense");
            s.attackSpeedBonus = b.player.attackSpeed + profile.StatBonus("attackSpeed") + RelicSum("attackSpeed");
            s.moveMul = (b.player.moveSpeedMul + profile.StatBonus("moveSpeed") + RelicSum("moveSpeed")) * QuiverStats.MoveSpeedMul(quiver);
            s.regenPer5s = profile.StatBonus("regen");
            s.revive = profile.StatBonus("revive") > 0f;
            s.extraStartArrows = Mathf.RoundToInt(profile.StatBonus("startArrows"));
            s.doubleJump = profile.HasSkill("doubleJump");
            s.dash = profile.HasSkill("dash");
            s.tripleJump = profile.HasSkill("tripleJump");
            if (RelicWithEffect("tutorialPower") != null)
            {
                var t = b.tutorial;
                s.maxHp += Mathf.Max(0f, t.maxHp - b.player.maxHp);
                s.attack += Mathf.Max(0f, t.attack - b.player.attack);
                s.defense += Mathf.Max(0f, t.defense - b.player.defense);
                s.attackSpeedBonus += Mathf.Max(0f, t.attackSpeed - b.player.attackSpeed);
                s.moveMul += Mathf.Max(0f, t.moveSpeedMul - b.player.moveSpeedMul);
                if (t.doubleJump) s.doubleJump = true;
                if (t.dash) s.dash = true;
            }
            return s;
        }
    }
}
