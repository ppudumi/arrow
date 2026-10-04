using System.Collections.Generic;
using UnityEngine;

namespace Archery
{
    /// <summary>
    /// 한 번의 테스트 전투가 공유하는 상태: 설정, 적 목록, 이벤트 로그, 피해 팝업, 범위 표시.
    /// 전투를 초기화하거나 로비로 돌아가면 통째로 버려진다.
    /// </summary>
    public class ArcheryCombatContext
    {
        public readonly ArcheryConfig Config;
        public readonly List<ArcheryEnemy> Enemies = new List<ArcheryEnemy>();
        public readonly List<string> EventLog = new List<string>();
        public readonly List<DamagePopup> Popups = new List<DamagePopup>();
        public ArcheryDebugDraw Draw;
        public Transform Root;

        public struct DamagePopup
        {
            public Vector3 worldPos;
            public float amount;
            public float time;
        }

        public ArcheryCombatContext(ArcheryConfig config)
        {
            Config = config;
        }

        public void Log(string msg)
        {
            string line = $"[{Time.timeSinceLevelLoad,6:0.00}] {msg}";
            EventLog.Add(line);
            if (EventLog.Count > 200) EventLog.RemoveAt(0);
            if (ArcherySelfTest.Running) Debug.Log("[Archery] " + msg);
        }

        public void SpawnDamagePopup(Vector3 pos, float amount)
        {
            Popups.Add(new DamagePopup { worldPos = pos + (Vector3)(Random.insideUnitCircle * 0.2f), amount = amount, time = Time.time });
            if (Popups.Count > 64) Popups.RemoveAt(0);
        }

        public static string FormatDamage(float v)
        {
            return Mathf.Abs(v - Mathf.Round(v)) < 0.005f ? Mathf.Round(v).ToString("0") : v.ToString("0.0#");
        }
    }

    /// <summary>
    /// 적중 판정 파이프라인. 발사 적중, 찌르기 적중, 뽑기 재적중이 모두 여기를 통과한다.
    /// 피해 적용 → 화살의 적중 효과 실행 → 기록 순서로 동작한다.
    /// </summary>
    public static class ArrowHitResolver
    {
        /// <summary>일반 화살 최종 피해 = 화살 개별 피해 + 플레이어 공격력</summary>
        public static float NormalDamage(ArrowDefinition def, float attack) => def.damage + attack;

        public static float Resolve(ArcheryCombat combat, ArcheryArrow arrow, ArcheryEnemy enemy, HitSource source, float damage, Vector2 direction)
        {
            if (enemy == null) return 0f;
            bool wasAlive = enemy.IsAlive;
            arrow.RecordHit(source);
            if (wasAlive) enemy.ApplyDamage(damage, $"{arrow.Label}/{SourceName(source)}");

            var effects = arrow.Def.hitEffects;
            for (int i = 0; i < effects.Count; i++)
            {
                ApplyEffect(effects[i], enemy, direction);
            }

            combat.Ctx.Log($"{SourceName(source)} 적중: {arrow.Label} → {enemy.displayName} {ArcheryCombatContext.FormatDamage(damage)} 피해" +
                           (effects.Count > 0 ? $" (+{arrow.Def.effectDescription})" : "") + (wasAlive ? "" : " [이미 쓰러짐]"));
            return wasAlive ? damage : 0f;
        }

        private static void ApplyEffect(ArrowHitEffect effect, ArcheryEnemy enemy, Vector2 direction)
        {
            switch (effect.type)
            {
                case ArrowHitEffectType.Knockback:
                    enemy.ApplyKnockback(direction, effect.magnitude);
                    break;
            }
        }

        public static string SourceName(HitSource s)
        {
            switch (s)
            {
                case HitSource.Shot: return "발사";
                case HitSource.Thrust: return "찌르기";
                case HitSource.Pull: return "뽑기 재적중";
            }
            return s.ToString();
        }
    }
}
