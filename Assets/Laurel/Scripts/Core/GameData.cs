using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    // ───────────────────────── JSON 데이터 정의 (Resources/LaurelData/*.json) ─────────────────────────

    [Serializable]
    public class ArrowDef
    {
        public int code;
        public string name;
        public string grade;
        public bool gradeFilled;
        public float damage;
        public bool damageFilled;
        public string damageText;
        public float draw = 1f;
        public bool drawFilled;
        public float weight = 1f;
        public bool weightFilled;
        public string effect = "none";
        public float p1, p2, p3;
        public int variantOf = -1;
        public int variantTo = -1;
        public string special;
        public string flavor;
        public string color = "#FFFFFF";
        public bool inPool = true;

        [NonSerialized] public Color tint;
        [NonSerialized] public int rarity;

        public bool IsVariant => variantOf >= 0;
        public string DamageLabel => !string.IsNullOrEmpty(damageText) ? damageText : damage.ToString("0.#");
    }

    [Serializable] public class ArrowFile { public List<ArrowDef> arrows = new List<ArrowDef>(); }

    [Serializable]
    public class RelicDef
    {
        public string id;
        public int code;
        public bool temp;
        public string name;
        public string tag;
        public string grade;
        public string effect;
        public float p1, p2;
        public string desc;
        [NonSerialized] public int rarity;
    }

    [Serializable] public class RelicFile { public List<RelicDef> relics = new List<RelicDef>(); }

    [Serializable]
    public class EnemyDef
    {
        public string id;
        public string name;
        public string ai;
        public float hp;
        public float speed;
        public float damage;
        public float w = 1f, h = 1f;
        public string color = "#FFFFFF";
        public int gold;
        public float p1, p2, p3;
        public bool fly;
        public string special;
        [NonSerialized] public Color tint;
    }

    [Serializable]
    public class BossDef
    {
        public string id;
        public string name;
        public float hp;
        public float damage;
        public string color;
        public int nectar;
        public string intro;
        [NonSerialized] public Color tint;
    }

    [Serializable] public class EnemyFile { public List<EnemyDef> enemies = new List<EnemyDef>(); public List<BossDef> bosses = new List<BossDef>(); }

    [Serializable] public class MixWeights { public int C = 50, R = 20, S = 15, F = 15; }

    [Serializable]
    public class StageDef
    {
        public int index;
        public string name;
        public string subtitle;
        public string bg, bgFar, terrain, platform, accent;
        public float hpMul = 1f, dmgMul = 1f;
        public List<string> layers = new List<string>();
        public MixWeights mixWeights = new MixWeights();
        public List<string> normal = new List<string>();
        public List<string> elite = new List<string>();
        public int[] enemyCount = { 3, 4 };
        public int[] waves = { 1, 2 };
        public string boss;
        public string unlockSkill;
        public int[] goldPerNode = { 14, 22 };
        public int goldElite, goldBoss;
        public int nectarCombat, nectarElite;
    }

    [Serializable] public class StageFile { public List<StageDef> stages = new List<StageDef>(); }

    [Serializable]
    public class PlayerBase
    {
        public float maxHp = 100, attack = 1, defense = 0, attackSpeed = 0, moveSpeedMul = 1f, iframes = 0.9f;
        public int startGold = 30;
        public int[] startArrows = { 0, 0, 0, 0, 0, 0 };
        public int quiverMax = 30;
    }

    [Serializable]
    public class TutorialBase
    {
        public float maxHp = 300, attack = 6, defense = 3, attackSpeed = 0.5f, moveSpeedMul = 1.1f;
        public int[] arrows = { 0, 0, 0, 0, 0, 0, 0, 0 };
        public string launch = "Apollo", retrieval = "Orpheus";
        public bool doubleJump = true, dash = true;
    }

    [Serializable]
    public class ArcheryTuning
    {
        public float referenceDrawSeconds = 1f, minInterval = 0.1f, arrowSpeed = 26f, straightMaxRange = 32f, fallGravity = 30f;
        public float apolloBaseInterval = 1f;
        public float hermesBaseInterval = 1.25f, hermesAttackSpeedPerStack = 0.1f, hermesBuffDuration = 5f;
        public int hermesMaxStacks = 5;
        public float athenaBaseInterval = 0.6f, athenaGravityPerWeight = 18f;
        public float odysseusMinCharge = 1.2f, odysseusMaxCharge = 1.5f, odysseusDamageReferenceSeconds = 1f;
        public float aresThrustBaseInterval = 0.5f, aresThrustRange = 2.4f, aresThrustWidth = 1f;
        public float autoPickupRadius = 1.3f;
        public float orpheusReturnDuration = 5f, returnEaseExponent = 3f;
        public float aresPullDelay = 0.5f, aresPullRadius = 1.7f;
        public float demeterRadius = 3.6f, demeterAngle = 150f, demeterDamageMultiplier = 2f, demeterIntervalFactor = 0.5f;
        public float hadesReturnDuration = 5f;
        public float zeusRadius = 3.4f, zeusCooldown = 6f, zeusDamageFactor = 0.5f, zeusLineThickness = 0.35f;
        public float discardToss = 3.5f;
        public int phantomCap = 160;
        public float phantomLifetime = 3f;
    }

    [Serializable]
    public class StatusTuning
    {
        public float poisonDuration = 3, poisonTick = 1;
        public float burnDps = 2, burnAttackFactor = 0.5f;
        public float midasSlow = 0.2f; public int midasGoldMin = 2, midasGoldMax = 3; public float midasGoldCooldown = 0.5f;
        public float staticPerStack = 3, staticDelay = 1.2f; public int staticMaxStacks = 10;
        public float webSlowStart = 0.3f, webSlowPerSecond = 0.12f, webSlowMax = 0.7f;
        public float oilBurnMultiplier = 2f, oilFireDuration = 4f, oilFireDps = 3f;
        public float bossDurationMul = 0.5f, bossCrowdControlMul = 0.5f, bossKnockbackMul = 0.15f;
        public float sisyphusVulnerableMul = 2f, sisyphusStun = 3f, bossSisyphusMul = 1.5f, bossSisyphusDuration = 6f;
    }

    [Serializable]
    public class EconomyTuning
    {
        public List<string> rarityOrder = new List<string>();
        public int[] arrowPrice = { 25, 40, 60, 90, 130, 170 };
        public int[] relicPrice = { 60, 90, 120, 160, 200, 240 };
        public int removeBaseCost = 25, removeCostStep = 25;
        public int rerollGold = 15, rerollGoldStep = 10, rerollNectar = 1;
        public int healPotionPrice = 35, healPotionAmount = 35;
        public int ambrosiaPrice = 70, ambrosiaMaxHp = 10;
        public int shopArrows = 5, shopRelics = 3;
        public int rewardArrowChoices = 3, rewardRelicChoices = 3;
        public float restHealRatio = 0.35f, stageClearHealRatio = 0.3f;
        public float enemyGoldMul = 1f;
    }

    [Serializable] public class WorldTuning { public float arenaSize = 22, wallThickness = 2, cameraSize = 7f, bossCameraSize = 9f, lobbyWidth = 30; }
    [Serializable] public class NectarTuning { public int tutorialClear = 3; public int[] stageClearBonus = { 2, 3, 5 }; }

    [Serializable]
    public class BalanceFile
    {
        public PlayerBase player = new PlayerBase();
        public TutorialBase tutorial = new TutorialBase();
        public ArcheryTuning archery = new ArcheryTuning();
        public StatusTuning status = new StatusTuning();
        public EconomyTuning economy = new EconomyTuning();
        public WorldTuning world = new WorldTuning();
        public NectarTuning nectar = new NectarTuning();
    }

    [Serializable]
    public class NectarNodeDef
    {
        public string id;
        public string name;
        public string kind;      // stat / skill / launch / retrieval / quiver
        public string stat;
        public float value;
        public string skill;
        public string style;
        public int[] cost = { 1 };
        public float x, y;
        public List<string> requires = new List<string>();
        public int bossUnlock;   // 0 = 없음, 1~3 = 해당 스테이지 보스 처치 필요
        public string desc;
        public int MaxLevel => cost != null ? cost.Length : 1;
    }

    [Serializable] public class NectarFile { public List<NectarNodeDef> nodes = new List<NectarNodeDef>(); }

    [Serializable] public class StoryLine { public string who; public string text; }
    [Serializable] public class StoryScene { public string id; public List<StoryLine> lines = new List<StoryLine>(); }
    [Serializable] public class StoryFile { public List<StoryScene> scenes = new List<StoryScene>(); }

    // ───────────────────────── 데이터베이스 ─────────────────────────

    /// <summary>Resources/LaurelData 의 JSON 을 읽어 한 번만 구성하는 데이터 저장소.</summary>
    public static class DB
    {
        public static readonly Dictionary<int, ArrowDef> Arrows = new Dictionary<int, ArrowDef>();
        public static readonly List<ArrowDef> ArrowPool = new List<ArrowDef>();
        public static readonly Dictionary<string, RelicDef> Relics = new Dictionary<string, RelicDef>();
        public static readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public static readonly Dictionary<string, BossDef> Bosses = new Dictionary<string, BossDef>();
        public static readonly List<StageDef> Stages = new List<StageDef>();
        public static readonly List<NectarNodeDef> NectarNodes = new List<NectarNodeDef>();
        public static readonly Dictionary<string, NectarNodeDef> NectarById = new Dictionary<string, NectarNodeDef>();
        public static readonly Dictionary<string, StoryScene> Story = new Dictionary<string, StoryScene>();
        public static BalanceFile Balance = new BalanceFile();
        public static int[][] RarityWeights;
        public static bool Loaded { get; private set; }
        public static readonly List<string> LoadErrors = new List<string>();

        public static readonly string[] RarityNames = { "일반", "희귀", "고급", "영웅", "신화", "전설" };
        public static readonly Color[] RarityColors =
        {
            new Color(0.82f, 0.82f, 0.82f), new Color(0.45f, 0.85f, 0.45f), new Color(0.35f, 0.65f, 1f),
            new Color(0.75f, 0.45f, 1f), new Color(1f, 0.55f, 0.2f), new Color(1f, 0.25f, 0.35f)
        };

        public static int RarityIndex(string grade)
        {
            for (int i = 0; i < RarityNames.Length; i++) if (RarityNames[i] == grade) return i;
            return 0;
        }

        public static void EnsureLoaded()
        {
            if (Loaded) return;
            Load();
        }

        public static void Load()
        {
            LoadErrors.Clear();
            Arrows.Clear(); ArrowPool.Clear(); Relics.Clear(); Enemies.Clear(); Bosses.Clear();
            Stages.Clear(); NectarNodes.Clear(); NectarById.Clear(); Story.Clear();

            var af = Read<ArrowFile>("arrows");
            if (af != null)
            {
                foreach (var a in af.arrows)
                {
                    a.tint = Hex(a.color);
                    a.rarity = RarityIndex(a.grade);
                    Arrows[a.code] = a;
                }
                foreach (var a in af.arrows) if (a.inPool && a.variantOf < 0) ArrowPool.Add(a);
            }

            var rf = Read<RelicFile>("relics");
            if (rf != null) foreach (var r in rf.relics) { r.rarity = RarityIndex(r.grade); Relics[r.id] = r; }

            var ef = Read<EnemyFile>("enemies");
            if (ef != null)
            {
                foreach (var e in ef.enemies) { e.tint = Hex(e.color); Enemies[e.id] = e; }
                foreach (var b in ef.bosses) { b.tint = Hex(b.color); Bosses[b.id] = b; }
            }

            var sf = Read<StageFile>("stages");
            if (sf != null) Stages.AddRange(sf.stages);

            var bf = Read<BalanceFile>("balance");
            if (bf != null) Balance = bf;
            RarityWeights = ParseRarityWeights("balance");

            var nf = Read<NectarFile>("nectar_tree");
            if (nf != null) foreach (var n in nf.nodes) { NectarNodes.Add(n); NectarById[n.id] = n; }

            var st = Read<StoryFile>("story");
            if (st != null) foreach (var s in st.scenes) Story[s.id] = s;

            Validate();
            Loaded = true;
        }

        private static T Read<T>(string name) where T : class
        {
            var ta = Resources.Load<TextAsset>("LaurelData/" + name);
            if (ta == null) { LoadErrors.Add($"데이터 파일 없음: {name}.json"); return null; }
            try { return JsonUtility.FromJson<T>(ta.text); }
            catch (Exception ex) { LoadErrors.Add($"{name}.json 파싱 실패: {ex.Message}"); return null; }
        }

        /// <summary>JsonUtility 는 2차원 배열을 못 읽으므로 rarityWeightsByStage 만 직접 파싱한다.</summary>
        private static int[][] ParseRarityWeights(string file)
        {
            var fallback = new[] { new[] { 55, 25, 12, 5, 2, 1 }, new[] { 40, 28, 17, 9, 4, 2 }, new[] { 28, 27, 20, 13, 8, 4 } };
            var ta = Resources.Load<TextAsset>("LaurelData/" + file);
            if (ta == null) return fallback;
            string t = ta.text;
            int k = t.IndexOf("\"rarityWeightsByStage\"", StringComparison.Ordinal);
            if (k < 0) return fallback;
            int start = t.IndexOf('[', k);
            int depth = 0, end = -1;
            for (int i = start; i < t.Length; i++)
            {
                if (t[i] == '[') depth++;
                else if (t[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
            }
            if (end < 0) return fallback;
            var rows = new List<int[]>();
            string inner = t.Substring(start + 1, end - start - 1);
            int p = 0;
            while (true)
            {
                int a = inner.IndexOf('[', p);
                if (a < 0) break;
                int b = inner.IndexOf(']', a);
                var parts = inner.Substring(a + 1, b - a - 1).Split(',');
                var row = new List<int>();
                foreach (var s in parts) if (int.TryParse(s.Trim(), out int v)) row.Add(v);
                rows.Add(row.ToArray());
                p = b + 1;
            }
            return rows.Count > 0 ? rows.ToArray() : fallback;
        }

        private static void Validate()
        {
            foreach (var s in Stages)
            {
                foreach (var id in s.normal) if (!Enemies.ContainsKey(id)) LoadErrors.Add($"스테이지 {s.index}: 없는 적 {id}");
                foreach (var id in s.elite) if (!Enemies.ContainsKey(id)) LoadErrors.Add($"스테이지 {s.index}: 없는 정예 {id}");
                if (!Bosses.ContainsKey(s.boss)) LoadErrors.Add($"스테이지 {s.index}: 없는 보스 {s.boss}");
            }
            foreach (var n in NectarNodes)
                foreach (var r in n.requires) if (!NectarById.ContainsKey(r)) LoadErrors.Add($"넥타르 노드 {n.id}: 없는 선행 {r}");
            foreach (var e in LoadErrors) Debug.LogError("[Laurel] " + e);
        }

        public static ArrowDef Arrow(int code) => Arrows.TryGetValue(code, out var a) ? a : Arrows[0];
        public static StageDef Stage(int index) => Stages[Mathf.Clamp(index - 1, 0, Stages.Count - 1)];

        public static Color Hex(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return Color.white;
        }
    }
}
