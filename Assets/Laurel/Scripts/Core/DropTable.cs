using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    public enum DropItemType
    {
        Gold,
        Ruby,
        Sapphire,
        Emerald,
        ExpOrb,
        Drumstick,
        Parchment
    }

    /// <summary>
    /// 적 처치 시 골드·보석 3종·경험치 구슬·닭다리·양피지를 랜덤으로 5개씩 떨어뜨리는 드랍 테이블.
    /// 바닥에 떨어진 뒤 플레이어가 근처로 오면 자석처럼 빨려 들어가며 이펙트를 재생한다.
    /// </summary>
    public static class DropTable
    {
        private struct Entry
        {
            public DropItemType type;
            public float weight;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { type = DropItemType.Gold,      weight = 24f },
            new Entry { type = DropItemType.ExpOrb,    weight = 24f },
            new Entry { type = DropItemType.Ruby,      weight = 11f },
            new Entry { type = DropItemType.Sapphire,  weight = 11f },
            new Entry { type = DropItemType.Emerald,   weight = 11f },
            new Entry { type = DropItemType.Drumstick, weight = 10f },
            new Entry { type = DropItemType.Parchment, weight = 9f  },
        };

        public static DropItemType RollOne()
        {
            float total = 0f;
            for (int i = 0; i < Entries.Length; i++) total += Entries[i].weight;
            float r = Random.Range(0f, total);
            for (int i = 0; i < Entries.Length; i++)
            {
                r -= Entries[i].weight;
                if (r <= 0f) return Entries[i].type;
            }
            return DropItemType.Gold;
        }

        public static void SpawnDrops(Vector2 pos, int enemyGold, int enemyExp, int count = 5)
        {
            int goldPer = Mathf.Max(3, Mathf.RoundToInt(Mathf.Max(5, enemyGold) * 0.4f));
            int expPer = Mathf.Max(5, Mathf.RoundToInt(Mathf.Max(10, enemyExp) * 0.4f));

            for (int i = 0; i < count; i++)
            {
                DropItemType type = RollOne();
                int value = type == DropItemType.Gold ? goldPer : (type == DropItemType.ExpOrb ? expPer : 1);
                SpawnItem(pos, type, value);
            }
        }

        public static void SpawnItem(Vector2 pos, DropItemType type, int value)
        {
            var go = new GameObject($"Drop_{type}");
            go.transform.position = new Vector3(pos.x, pos.y, -0.1f);
            BuildVisual(go.transform, type);
            var pickup = go.AddComponent<DropPickup>();
            pickup.Init(type, value, new Vector2(Random.Range(-4.2f, 4.2f), Random.Range(5.2f, 9.2f)));
        }

        private static void BuildVisual(Transform root, DropItemType type)
        {
            switch (type)
            {
                case DropItemType.Gold:
                    Art.Quad(root, "Outer", Art.Circle, new Color(1f, 0.82f, 0.18f), Vector2.one * 0.34f, 22);
                    Art.Quad(root, "Inner", Art.Circle, new Color(1f, 0.96f, 0.68f), Vector2.one * 0.16f, 23, new Vector3(-0.04f, 0.04f, 0f));
                    break;
                case DropItemType.Ruby:
                    Art.Quad(root, "Gem", Art.Diamond, new Color(0.95f, 0.16f, 0.25f), new Vector2(0.32f, 0.38f), 22);
                    Art.Quad(root, "Hi", Art.Diamond, new Color(1f, 0.7f, 0.75f), new Vector2(0.12f, 0.15f), 23, new Vector3(-0.04f, 0.05f, 0f));
                    break;
                case DropItemType.Sapphire:
                    Art.Quad(root, "Gem", Art.Diamond, new Color(0.18f, 0.55f, 1f), new Vector2(0.32f, 0.38f), 22);
                    Art.Quad(root, "Hi", Art.Diamond, new Color(0.75f, 0.92f, 1f), new Vector2(0.12f, 0.15f), 23, new Vector3(-0.04f, 0.05f, 0f));
                    break;
                case DropItemType.Emerald:
                    Art.Quad(root, "Gem", Art.Diamond, new Color(0.15f, 0.88f, 0.42f), new Vector2(0.32f, 0.38f), 22);
                    Art.Quad(root, "Hi", Art.Diamond, new Color(0.75f, 1f, 0.85f), new Vector2(0.12f, 0.15f), 23, new Vector3(-0.04f, 0.05f, 0f));
                    break;
                case DropItemType.ExpOrb:
                    Art.Quad(root, "Aura", Art.Soft, new Color(0.35f, 0.92f, 1f, 0.45f), Vector2.one * 0.52f, 21);
                    Art.Quad(root, "Core", Art.Circle, new Color(0.42f, 0.96f, 1f), Vector2.one * 0.26f, 22);
                    break;
                case DropItemType.Drumstick:
                    Art.Quad(root, "Bone", Art.Square, new Color(0.96f, 0.93f, 0.85f), new Vector2(0.24f, 0.1f), 22, new Vector3(-0.14f, -0.04f, 0f));
                    Art.Quad(root, "Knob", Art.Circle, new Color(0.96f, 0.93f, 0.85f), Vector2.one * 0.13f, 22, new Vector3(-0.24f, -0.04f, 0f));
                    Art.Quad(root, "Meat", Art.Circle, new Color(0.78f, 0.42f, 0.18f), new Vector2(0.34f, 0.25f), 23, new Vector3(0.06f, 0.02f, 0f));
                    break;
                case DropItemType.Parchment:
                    Art.Quad(root, "Paper", Art.Square, new Color(0.94f, 0.87f, 0.68f), new Vector2(0.32f, 0.26f), 22);
                    Art.Quad(root, "RollTop", Art.Square, new Color(0.72f, 0.58f, 0.36f), new Vector2(0.36f, 0.07f), 23, new Vector3(0f, 0.13f, 0f));
                    Art.Quad(root, "RollBot", Art.Square, new Color(0.72f, 0.58f, 0.36f), new Vector2(0.36f, 0.07f), 23, new Vector3(0f, -0.13f, 0f));
                    break;
            }
        }
    }

    /// <summary>바닥에 떨어진 드랍 아이템. 플레이어가 근처(magnetRadius)로 가면 빨려 들어가며 이펙트를 낸다.</summary>
    public class DropPickup : MonoBehaviour
    {
        public const float MagnetRadius = 3.4f;
        public const float CollectRadius = 0.48f;

        private DropItemType type;
        private int value;
        private Vector2 vel;
        private bool grounded;
        private bool magnetized;
        private float groundY;
        private float age;

        public void Init(DropItemType itemType, int itemValue, Vector2 initialVel)
        {
            type = itemType;
            value = itemValue;
            vel = initialVel;
            var hit = Physics2D.Raycast((Vector2)transform.position + Vector2.up * 0.5f, Vector2.down, 20f, Layers.TerrainMask);
            groundY = hit.collider != null && !hit.collider.isTrigger ? hit.point.y + 0.22f : transform.position.y - 1f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;

            var root = GameRoot.I;
            Transform playerTf = root != null && root.PlayerGO != null ? root.PlayerGO.transform : null;

            if (!magnetized && playerTf != null && age >= 0.28f)
            {
                Vector2 playerPos = (Vector2)playerTf.position + Vector2.up * 0.45f;
                if (Vector2.Distance(transform.position, playerPos) <= MagnetRadius)
                {
                    magnetized = true;
                }
            }

            if (magnetized && playerTf != null)
            {
                Vector3 target = playerTf.position + Vector3.up * 0.45f;
                Vector3 to = target - transform.position;
                float dist = to.magnitude;
                float speed = Mathf.Min(26f, 12f + age * 14f);
                if (dist <= Mathf.Max(CollectRadius, speed * dt))
                {
                    Collect(root);
                    return;
                }
                transform.position += (to / dist) * (speed * dt);
                return;
            }

            if (!grounded)
            {
                vel.y -= 24f * dt;
                Vector3 next = transform.position + (Vector3)(vel * dt);
                if (next.y <= groundY && vel.y <= 0f)
                {
                    next.y = groundY;
                    vel = Vector2.zero;
                    grounded = true;
                }
                transform.position = next;
            }
            else
            {
                // 바닥에서 살짝 둥실거리는 연출
                transform.position = new Vector3(transform.position.x, groundY + Mathf.Sin(age * 4f) * 0.06f, transform.position.z);
            }
        }

        private void Collect(GameRoot root)
        {
            Color fxColor = Color.white;
            string popup = "";

            switch (type)
            {
                case DropItemType.Gold:
                    fxColor = new Color(1f, 0.85f, 0.25f);
                    popup = $"+{value}G";
                    root?.AddPresentationGold(value);
                    Sfx.Play("coin", 0.4f);
                    break;
                case DropItemType.Ruby:
                    fxColor = new Color(1f, 0.25f, 0.35f);
                    popup = "루비!";
                    Sfx.Play("coin", 0.45f, 1.15f);
                    break;
                case DropItemType.Sapphire:
                    fxColor = new Color(0.3f, 0.65f, 1f);
                    popup = "사파이어!";
                    Sfx.Play("coin", 0.45f, 1.2f);
                    break;
                case DropItemType.Emerald:
                    fxColor = new Color(0.25f, 0.95f, 0.48f);
                    popup = "에메랄드!";
                    Sfx.Play("coin", 0.45f, 1.25f);
                    break;
                case DropItemType.ExpOrb:
                    fxColor = new Color(0.4f, 0.95f, 1f);
                    popup = $"+{value} EXP";
                    root?.AddPresentationExp(value);
                    Sfx.Play("coin", 0.35f, 1.35f);
                    break;
                case DropItemType.Drumstick:
                    fxColor = new Color(1f, 0.55f, 0.35f);
                    popup = "+15 HP";
                    root?.Avatar?.Heal(15f, "닭다리");
                    Sfx.Play("coin", 0.45f, 0.95f);
                    break;
                case DropItemType.Parchment:
                    fxColor = new Color(0.95f, 0.88f, 0.65f);
                    popup = "양피지!";
                    Sfx.Play("coin", 0.4f, 1.05f);
                    break;
            }

            Vector2 p = transform.position;
            Fx.Burst(p, fxColor, 8, 5f, 0.13f, 0.3f, 4f);
            Fx.WaveRing(p, 0.28f, 0.22f, fxColor);
            Fx.Text(p + Vector2.up * 0.4f, popup, fxColor, 20, 0.65f);
            Destroy(gameObject);
        }
    }
}
