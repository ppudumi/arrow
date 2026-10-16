using UnityEngine;

namespace Laurel
{
    /// <summary>튜토리얼 과녁 / 로비 수련장 허수아비. 움직이지 않고 공격하지 않는다. immortal 이면 체력이 바닥나도 다시 찬다.</summary>
    public class TrainingDummy : Enemy
    {
        private bool immortal;
        private float regenDelay;

        public void InitDummy(Room room, bool immortal)
        {
            var def = new EnemyDef { id = "dummy", name = immortal ? "수련 허수아비" : "과녁", ai = "dummy", hp = immortal ? 200 : 14, speed = 0, damage = 0, w = 0.9f, h = 1.6f, color = "#C8A060", gold = 0 };
            def.tint = DB.Hex(def.color);
            Init(room, def, 1f, 1f, false);
            this.immortal = immortal;
            ContactDamage = 0f;
            goldValue = 0;
            usesGravity = true;
            Art.Quad(transform, "Target", Art.Ring, new Color(0.9f, 0.2f, 0.2f), new Vector2(0.7f, 0.7f), 13, new Vector3(0, 0.25f, 0));
        }

        public void SetHp(float hp) { MaxHp = hp; Hp = hp; }

        protected override void Think(float dt)
        {
            velocity.x = 0f;
            facing = -1f;
            if (immortal)
            {
                regenDelay -= dt;
                if (regenDelay <= 0f && Hp < MaxHp) Hp = Mathf.Min(MaxHp, Hp + MaxHp * dt * 0.5f);
            }
        }

        protected override void OnDamaged(float amount)
        {
            regenDelay = 2.5f;
            if (immortal && Hp <= 1f) Hp = 1f;
        }

        public override void TakeDamage(float amount, string label, Color color, bool isArrowHit)
        {
            base.TakeDamage(amount, label, color, isArrowHit);
            if (immortal && Hp <= 1f) Hp = MaxHp;
        }
    }
}
