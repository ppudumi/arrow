using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public struct HitOutcome
    {
        public bool killed;
        public bool continueFlight;
        public Vector2 newDirection;
    }

    /// <summary>
    /// 모든 화살 적중이 지나가는 파이프라인. 피해 = 화살 피해 + 공격력 (차지·밀랍·일점타격·재앙 등 반영).
    /// 무한 반복 방지 규칙:
    ///  - 카드 화살은 한 비행에서 같은 적을 두 번 맞추지 않는다(hitThisFlight).
    ///  - 임시 투사체(Phantom)는 다른 임시 투사체를 만들지 않는다(파열·분열·삼살·거울·하데스·에코 효과 제외).
    ///  - 광역 피해(폭발·공명·번개·스태틱)는 '화살 적중'이 아니므로 적중 효과를 다시 일으키지 않는다.
    ///  - 임시 투사체 동시 존재 수 상한(phantomCap).
    /// </summary>
    public static class HitResolver
    {
        public static float NormalDamage(float cardDamage, float attack) => cardDamage + attack;

        public static HitOutcome Resolve(PlayerCombat c, ArrowBody body, Enemy enemy, Transform part, Vector2 point, HitSource src)
        {
            var outcome = new HitOutcome();
            var card = body.Card;
            var shot = body.Shot;
            var form = shot != null ? shot.form : card.Def;
            Vector2 dir = body.LastDirection;

            float dmg;
            if (src == HitSource.Pull || shot == null) dmg = NormalDamage(card.Damage, c.Attack);
            else dmg = shot.damage;
            if (form.effect == "wax") dmg = body.WaxDamage(dmg, form);
            dmg += c.FocusBonus(enemy);

            body.MarkEnemyHit(enemy);
            enemy.lastArrowForm = form;
            enemy.lastArrowDamage = NormalDamage(form.damage, c.Attack);
            enemy.lastHitCardUid = card.uid;

            bool wasBoss = enemy.IsBoss;
            enemy.TakeDamage(dmg, form.name, form.tint, true);
            c.Stats.hitsLanded++;
            Fx.Burst(point, form.tint, 5, 4f, 0.12f, 0.3f);
            Sfx.Play("hit", 0.7f);

            ApplyOnHit(c, form, enemy, point, dir, card, dmg, false, shot);

            outcome.killed = !enemy.IsAlive;

            // 카드 상태 변화
            switch (form.effect)
            {
                case "rage":
                    if (!card.rageActive) { card.rageActive = true; c.Log($"{card.Label}: 분노 활성 (이번 라운드)"); }
                    break;
                case "glass":
                    card.roundBonus -= form.p1 > 0 ? form.p1 : 4f;
                    if (card.Damage <= 0f) c.MarkConsumeAfterHit(body, "유리 화살 피해 0");
                    break;
                case "friction":
                    c.MarkConsumeAfterHit(body, "마찰 화살 피격");
                    break;
                case "sacrifice":
                    if (outcome.killed) { card.runBonus += form.p1 > 0 ? form.p1 : 6f; c.Log($"{card.Label}: 제물 — 영구 피해 +{form.p1:0} (이번 도전)"); }
                    break;
                case "ares":
                    if (outcome.killed && shot != null) { shot.damage *= 2f; shot.pierce += 1; c.Log($"{card.Label}: 처치 — 피해 2배, 관통 +1"); }
                    break;
            }

            if (src != HitSource.Shot || shot == null) return outcome;

            // 비행 계속 여부 (관통·볼링·망령)
            if (shot.pierce > 0)
            {
                shot.pierce--;
                outcome.continueFlight = true;
                return outcome;
            }
            if (form.effect == "bowling" && shot.bounces > 0)
            {
                shot.bounces--;
                float ang = (form.p2 > 0 ? form.p2 : 45f) * (Random.value < 0.5f ? 1f : -1f);
                outcome.newDirection = Quaternion.Euler(0, 0, ang) * dir;
                outcome.continueFlight = true;
                return outcome;
            }
            if (form.effect == "wraith" && shot.redirects > 0 && c.Room != null)
            {
                var next = c.Room.NearestEnemy(point, form.p2 > 0 ? form.p2 : 9f, body.hitThisFlight);
                if (next != null)
                {
                    shot.redirects--;
                    outcome.newDirection = ((Vector2)next.Center - point).normalized;
                    outcome.continueFlight = true;
                    return outcome;
                }
            }
            return outcome;
        }

        /// <summary>임시 투사체 적중: 피해와 단순 적중 효과만 (다른 투사체를 만들지 않음)</summary>
        public static void ResolvePhantom(PlayerCombat c, Phantom p, Enemy enemy, Vector2 point, Vector2 dir)
        {
            float dmg = p.damage + c.FocusBonus(enemy);
            enemy.lastArrowForm = p.form;
            enemy.lastArrowDamage = p.damage;
            enemy.TakeDamage(dmg, p.label, p.form.tint, true);
            c.Stats.phantomHits++;
            ApplyOnHit(c, p.form, enemy, point, dir, null, dmg, true, null);
        }

        /// <summary>화살 종류별 적중 효과</summary>
        public static void ApplyOnHit(PlayerCombat c, ArrowDef form, Enemy enemy, Vector2 point, Vector2 dir, ArrowCard card, float dmg, bool phantom, ShotInfo shot)
        {
            var st = DB.Balance.status;
            var room = c.Room;
            switch (form.effect)
            {
                case "knockback":
                    enemy.Knockback(dir.normalized * (form.p1 > 0 ? form.p1 : 7f));
                    break;
                case "poison":
                    enemy.Status.ApplyPoison(form.p1 > 0 ? form.p1 : st.poisonDuration, c.Attack);
                    break;
                case "fire":
                    enemy.Status.ApplyBurn(form.p1 > 0 ? form.p1 : 7f, st.burnDps + c.Attack * st.burnAttackFactor);
                    break;
                case "midas":
                    enemy.Status.ApplyMidas(form.p1 > 0 ? form.p1 : 5f);
                    enemy.DropMidasGold(true);
                    break;
                case "calamity":
                    if (enemy.IsAlive) enemy.Status.calamity = Mathf.Max(enemy.Status.calamity, Mathf.RoundToInt(form.p1 > 0 ? form.p1 : 3));
                    break;
                case "virus":
                    enemy.Status.ApplyVirus(form.p1 > 0 ? form.p1 : 6f);
                    break;
                case "vampire":
                    c.Avatar?.Heal(form.p1 > 0 ? form.p1 : 3f, "흡혈");
                    break;
                case "athena":
                    if (enemy.IsAlive) enemy.TakeTrueDamage(form.p1 > 0 ? form.p1 : 15f, "아테나");
                    break;
                case "explode":
                case "bomb":
                    {
                        float r = form.p1 > 0 ? form.p1 : (form.effect == "bomb" ? 3.2f : 1.6f);
                        room?.Explode(point, r, dmg, enemy, form.tint, form.name);
                        break;
                    }
                case "resonance":
                    if (!phantom && enemy.IsAlive) enemy.Status.resonanceDamage = Mathf.Max(enemy.Status.resonanceDamage, (form.p1 > 0 ? form.p1 : 6f) + c.Attack);
                    break;
                case "arachne":
                    room?.SpawnWeb(enemy.FeetPosition, form.p2 > 0 ? form.p2 : 2.6f, form.p1 > 0 ? form.p1 : 7f);
                    break;
                case "poseidon":
                    room?.SpawnWave(enemy.Center, form.p2 > 0 ? form.p2 : 5f, form.p1 > 0 ? form.p1 : 3f);
                    break;
                case "zeus":
                    ApplyZeusStatic(c, form, enemy);
                    break;
                case "sisyphus":
                    enemy.ApplySisyphus();
                    break;
                case "rupture":
                    if (!phantom)
                    {
                        int n = Mathf.RoundToInt(form.p1 > 0 ? form.p1 : 5);
                        float shardDmg = form.p2 > 0 ? form.p2 : 3f;
                        float range = form.p3 > 0 ? form.p3 : 2f;
                        Vector2 behind = (Vector2)enemy.Center + dir.normalized * (enemy.Radius + 0.2f);
                        for (int i = 0; i < n; i++)
                        {
                            float a = Mathf.Lerp(-35f, 35f, n == 1 ? 0.5f : i / (n - 1f));
                            Phantom.Spawn(c, DB.Arrow(0), behind, Quaternion.Euler(0, 0, a) * dir, 18f, shardDmg, range, "파열 파편", 0.45f);
                        }
                    }
                    break;
            }

            // 일점타격 (유물)
            c.RegisterFocusHit(enemy);
        }

        private static void ApplyZeusStatic(PlayerCombat c, ArrowDef form, Enemy target)
        {
            int total = Mathf.RoundToInt(form.p1 > 0 ? form.p1 : 5);
            float radius = form.p2 > 0 ? form.p2 : 6f;
            var others = new List<Enemy>();
            if (c.Room != null)
            {
                foreach (var e in c.Room.Enemies)
                {
                    if (e == null || !e.IsAlive || e == target) continue;
                    if (Vector2.Distance(e.Center, target.Center) <= radius) others.Add(e);
                }
                others.Sort((a, b) => Vector2.Distance(a.Center, target.Center).CompareTo(Vector2.Distance(b.Center, target.Center)));
            }
            int given = 0;
            if (target.IsAlive) { target.Status.AddStatic(1); given++; }
            for (int i = 0; i < others.Count && given < total; i++)
            {
                others[i].Status.AddStatic(1);
                Fx.Lightning(target.Center, others[i].Center, new Color(1f, 0.95f, 0.5f), 0.06f, 0.25f);
                given++;
            }
            // 주위에 적이 없으면 남은 횟수를 한 명에게 몰아준다
            if (target.IsAlive && given < total) target.Status.AddStatic(total - given);
        }
    }
}
