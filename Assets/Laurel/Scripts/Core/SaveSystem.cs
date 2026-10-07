using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Laurel
{
    [Serializable]
    public class NodeLevel
    {
        public string id;
        public int level;
    }

    /// <summary>
    /// 영구 저장 데이터. 사망해도 유지되는 것만 담는다:
    /// 넥타르(사용하지 않은 것 포함), 넥타르 강화 레벨, 구매한 유니크 스킬, 보스 처치로 얻은 구매 자격, 궁술 선택, 기록.
    /// 도전 중의 돈·화살·유물·능력치는 여기에 저장하지 않는다(사망 시 초기화 대상).
    /// </summary>
    [Serializable]
    public class Profile
    {
        public int version = 1;
        public bool tutorialDone;
        public bool lobbyIntroSeen;
        public int nectar;
        public int nectarEarnedTotal;
        public int nectarSpentTotal;
        public List<NodeLevel> purchases = new List<NodeLevel>();
        public List<int> bossesDefeated = new List<int>();
        public string launch = "Apollo";
        public string retrieval = "Basic";
        public int runs, deaths, clears, bestStage, kills;
        public float sfxVolume = 0.6f;
        public string lastSavedUtc;

        public int Level(string nodeId)
        {
            for (int i = 0; i < purchases.Count; i++) if (purchases[i].id == nodeId) return purchases[i].level;
            return 0;
        }

        public void SetLevel(string nodeId, int level)
        {
            for (int i = 0; i < purchases.Count; i++)
            {
                if (purchases[i].id == nodeId) { purchases[i].level = level; return; }
            }
            purchases.Add(new NodeLevel { id = nodeId, level = level });
        }

        public bool BossDefeated(int stage) => bossesDefeated.Contains(stage);

        public bool HasSkill(string skill)
        {
            foreach (var n in DB.NectarNodes)
                if (n.kind == "skill" && n.skill == skill && Level(n.id) > 0) return true;
            return false;
        }

        public bool StyleUnlocked(string kind, string style)
        {
            if (kind == "launch" && style == "Apollo") return true;
            if (kind == "retrieval" && style == "Basic") return true;
            foreach (var n in DB.NectarNodes)
                if (n.kind == kind && n.style == style && Level(n.id) > 0) return true;
            return false;
        }

        /// <summary>넥타르 강화로 얻은 스탯 합계</summary>
        public float StatBonus(string stat)
        {
            float sum = 0f;
            foreach (var n in DB.NectarNodes)
            {
                if ((n.kind == "stat" || n.kind == "quiver") && n.stat == stat) sum += n.value * Level(n.id);
            }
            return sum;
        }
    }

    /// <summary>
    /// 프로필 저장소. 원자적 쓰기(임시 파일 → 교체) + 백업 파일로 손상에 대비한다.
    /// 넥타르가 바뀌거나 구매·해금·도전 종료가 일어날 때마다 즉시 저장한다.
    /// </summary>
    public static class SaveStore
    {
        public const string FileName = "laurel_profile.json";
        /// <summary>자동 검증 등에서 저장 위치를 바꿀 때 사용</summary>
        public static string PathOverride;
        public static string LastError { get; private set; }
        public static string LastLoadSource { get; private set; }
        public static int SaveCount { get; private set; }

        public static string FilePath => !string.IsNullOrEmpty(PathOverride) ? PathOverride : Path.Combine(Application.persistentDataPath, FileName);
        public static string BackupPath => FilePath + ".bak";
        public static string TempPath => FilePath + ".tmp";

        public static bool Exists() => File.Exists(FilePath) || File.Exists(BackupPath);

        public static Profile Load()
        {
            LastError = null;
            var p = TryRead(FilePath);
            if (p != null) { LastLoadSource = "main"; return Sanitize(p); }
            p = TryRead(BackupPath);
            if (p != null)
            {
                LastLoadSource = "backup";
                Debug.LogWarning("[Laurel] 저장 파일을 읽지 못해 백업에서 복구했습니다.");
                return Sanitize(p);
            }
            LastLoadSource = "new";
            return new Profile();
        }

        private static Profile TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var p = JsonUtility.FromJson<Profile>(json);
                return p;
            }
            catch (Exception ex)
            {
                LastError = $"{Path.GetFileName(path)} 읽기 실패: {ex.Message}";
                Debug.LogWarning("[Laurel] " + LastError);
                return null;
            }
        }

        private static Profile Sanitize(Profile p)
        {
            if (p.purchases == null) p.purchases = new List<NodeLevel>();
            if (p.bossesDefeated == null) p.bossesDefeated = new List<int>();
            if (p.nectar < 0) p.nectar = 0;
            if (string.IsNullOrEmpty(p.launch)) p.launch = "Apollo";
            if (string.IsNullOrEmpty(p.retrieval)) p.retrieval = "Basic";
            return p;
        }

        public static bool Save(Profile p)
        {
            try
            {
                p.lastSavedUtc = DateTime.UtcNow.ToString("o");
                string json = JsonUtility.ToJson(p, true);
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(TempPath, json);
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, BackupPath, true);
                    File.Delete(FilePath);
                }
                File.Move(TempPath, FilePath);
                SaveCount++;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = "저장 실패: " + ex.Message;
                Debug.LogError("[Laurel] " + LastError);
                return false;
            }
        }

        public static void DeleteAll()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
            }
            catch (Exception ex) { LastError = "삭제 실패: " + ex.Message; }
        }
    }
}
