using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Laurel
{
    /// <summary>
    /// 궁술 테스트 씬 왼쪽 무기고 방의 진열대(보조무기 · 화살통 · 화살 · 유물 받침대), 안전지대, 바닥 화살 마우스 오버 UI 관리.
    /// - 플레이어는 보조무기 1개, 화살통 1개만 장착 가능하며 다른 도구를 선택하면 기존 도구는 진열장으로 돌아간다.
    /// - 화살 진열대(기본 · 독 · 폭탄)는 화살통에 한 자리 이상 여유가 있어야 획득 가능하다.
    /// - 바닥에 떨어진 화살에 마우스를 올리면 [F] 획득 / [G] 분해 UI가 뜬다.
    /// - 왼쪽 방에 들어오면 모든 적(허수아비 제외)과 바닥 드랍 보상을 제거하고, 방에 있는 동안 적이 플레이어를 보지 못한다.
    /// </summary>
    public class ArcheryArmoryDisplays : MonoBehaviour
    {
        private const float SafeRoomMaxX = -27.5f;
        private const float InteractRange = 1.45f;
        private const float HoverRange = 0.85f;
        private const float GroundArrowPlayerRange = 2.8f;

        private enum PedestalKind { Retrieval, Quiver, Arrow, EmptyRelic }

        private class Pedestal
        {
            public PedestalKind kind;
            public RetrievalStyle retrieval;
            public QuiverType quiver;
            public int arrowCode;
            public Vector3 worldPos;
            public SpriteRenderer iconSr;
            public SpriteRenderer baseSr;
            public float baseY;
        }

        private Room room;
        private bool lobbyMode;
        private readonly List<Pedestal> pedestals = new List<Pedestal>();
        private Pedestal nearestAvailable;
        private bool wasInSafeRoom;
        private GUIStyle promptTitleStyle, promptDescStyle;
        private static Sprite arrowDisplaySprite;

        public static void SpawnInScene(Room room)
        {
            if (room == null) return;
            var go = new GameObject("ArcheryArmoryDisplays");
            go.transform.SetParent(room.transform, false);
            var mgr = go.AddComponent<ArcheryArmoryDisplays>();
            mgr.Init(room, false);
        }

        /// <summary>로비 씬용: 화살통 3종과 회수 방식(보조무기) 진열대만 배치한다.</summary>
        public static void SpawnLobbyInScene(Room room)
        {
            if (room == null) return;
            var go = new GameObject("LobbyArmoryDisplays");
            go.transform.SetParent(room.transform, false);
            var mgr = go.AddComponent<ArcheryArmoryDisplays>();
            mgr.Init(room, true);
        }

        public static string RetrievalResourcePath(RetrievalStyle s)
        {
            switch (s)
            {
                case RetrievalStyle.Orpheus: return "LaurelFx/orpheus-lyre";
                case RetrievalStyle.Ares: return "LaurelFx/retrieval-ares";
                case RetrievalStyle.Demeter: return "LaurelFx/retrieval-demeter";
                case RetrievalStyle.Hades: return "LaurelFx/retrieval-hades";
                case RetrievalStyle.Zeus: return "LaurelFx/retrieval-zeus";
                default: return "LaurelFx/retrieval-basic";
            }
        }

        public static string RetrievalShortName(RetrievalStyle s)
        {
            switch (s)
            {
                case RetrievalStyle.Orpheus: return "오르페우스";
                case RetrievalStyle.Ares: return "아레스";
                case RetrievalStyle.Demeter: return "데메테르";
                case RetrievalStyle.Hades: return "하데스";
                case RetrievalStyle.Zeus: return "제우스";
                default: return "기본";
            }
        }

        private static Sprite LoadIconSprite(string path)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return null;
            tex.filterMode = FilterMode.Point;
            float ppu = Mathf.Max(64f, Mathf.Max(tex.width, tex.height) * 1.15f);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
        }

        private static Sprite GetArrowDisplaySprite()
        {
            if (arrowDisplaySprite != null) return arrowDisplaySprite;
            var tex = new Texture2D(36, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[36 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 36; x++)
                {
                    bool body = y >= 2 && y <= 5 && x >= 1 && x <= 28;
                    bool tip = x >= 27 && x <= 34 && Mathf.Abs(y - 3.5f) <= (35 - x) * 0.45f;
                    bool fletch = x <= 5 && (y == 1 || y == 6);
                    px[y * 36 + x] = (body || tip || fletch) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            arrowDisplaySprite = Sprite.Create(tex, new Rect(0, 0, 36, 8), new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect);
            return arrowDisplaySprite;
        }

        private static Transform FindAnchor(params string[] names)
        {
            foreach (var n in names)
            {
                var go = GameObject.Find(n);
                if (go != null) return go.transform;
            }
            return null;
        }

        private void Init(Room r, bool isLobby)
        {
            room = r;
            lobbyMode = isLobby;

            if (isLobby)
            {
                var subAnchor = FindAnchor("sub_weapon_pos", "보조무기위치", "보조무기 위치");
                Vector3 subOrigin = subAnchor != null ? subAnchor.position : new Vector3(r.InnerBounds.xMin + 8.0f, r.InnerBounds.yMin, 0f);
                SpawnRetrievalRack(subOrigin);

                var quiverAnchor = FindAnchor("quiver_pos", "화살통 위치", "화살통위치");
                Vector3 quiverOrigin = quiverAnchor != null ? quiverAnchor.position : new Vector3(r.InnerBounds.xMin + 1.8f, r.InnerBounds.yMin, 0f);
                SpawnQuiverRack(quiverOrigin);
                return;
            }

            SpawnRetrievalRack(new Vector3(-88f, -7.91f, 0f));
            SpawnQuiverRack(new Vector3(-96f, -7.91f, 0f));
            SpawnArrowRack(new Vector3(-98f, 3.66f, 0f));

            if (CameraRig.I != null)
            {
                var b = CameraRig.I.bounds;
                float xMin = Mathf.Min(b.xMin, -102f);
                float xMax = Mathf.Max(b.xMax, 29.5f);
                float yMin = Mathf.Min(b.yMin, -16f);
                float yMax = Mathf.Max(b.yMax, 24f);
                CameraRig.I.bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
        }

        private void SpawnRetrievalRack(Vector3 origin)
        {
            var all = StyleNames.AllRetrieval;
            float spacing = 1.65f;
            for (int i = 0; i < all.Length; i++)
            {
                var pos = new Vector3(origin.x + i * spacing, origin.y, 0f);
                CreatePedestal(PedestalKind.Retrieval, all[i], QuiverType.Basic, 0, pos, LoadIconSprite(RetrievalResourcePath(all[i])), Color.white);
            }
        }

        private void SpawnQuiverRack(Vector3 origin)
        {
            var all = QuiverStats.All;
            float spacing = 1.85f;
            for (int i = 0; i < all.Length; i++)
            {
                var pos = new Vector3(origin.x + i * spacing, origin.y, 0f);
                CreatePedestal(PedestalKind.Quiver, RetrievalStyle.Basic, all[i], 0, pos, LoadIconSprite(QuiverStats.ResourcePath(all[i])), Color.white);
            }
        }

        private void SpawnArrowRack(Vector3 origin)
        {
            var arrows = new List<ArrowDef>();
            foreach (var kv in DB.Arrows)
            {
                if (kv.Value != null && kv.Value.variantOf < 0)
                    arrows.Add(kv.Value);
            }
            arrows.Sort((a, b) => a.code.CompareTo(b.code));

            const float startX = -98f;
            const float maxX = -42f;
            const float spacing = 1.75f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((maxX - startX) / spacing) + 1);
            float[] rowOffsetsY = { 0f, 6f, 13f };

            for (int i = 0; i < arrows.Count; i++)
            {
                var def = arrows[i];
                int row = i / perRow;
                int col = i % perRow;
                float yOff = row < rowOffsetsY.Length ? rowOffsetsY[row] : row * 6f;
                var pos = new Vector3(startX + col * spacing, origin.y + yOff, 0f);
                Color tint = (def.code == 0 || def.code == 1) ? new Color(0.92f, 0.87f, 0.75f, 1f) : def.tint;
                CreatePedestal(PedestalKind.Arrow, RetrievalStyle.Basic, QuiverType.Basic, def.code, pos, GetArrowDisplaySprite(), tint);
            }
        }

        private void SpawnRelicPedestals(Vector3 origin, int count)
        {
            float spacing = 1.65f;
            for (int i = 0; i < count; i++)
            {
                var pos = new Vector3(origin.x + i * spacing, origin.y, 0f);
                CreatePedestal(PedestalKind.EmptyRelic, RetrievalStyle.Basic, QuiverType.Basic, 0, pos, null, Color.white);
            }
        }

        private void CreatePedestal(PedestalKind kind, RetrievalStyle rStyle, QuiverType qType, int arrowCode, Vector3 pos, Sprite sprite, Color tint)
        {
            var go = new GameObject($"Display_{kind}_{rStyle}_{qType}_{arrowCode}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x, pos.y, -0.04f);

            SpriteRenderer iconSr = null;
            if (sprite != null)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(go.transform, false);
                iconGo.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                if (kind == PedestalKind.Arrow) iconGo.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
                iconSr = iconGo.AddComponent<SpriteRenderer>();
                iconSr.sprite = sprite;
                iconSr.color = tint;
                iconSr.sortingOrder = 12;
            }

            pedestals.Add(new Pedestal
            {
                kind = kind,
                retrieval = rStyle,
                quiver = qType,
                arrowCode = arrowCode,
                worldPos = pos,
                iconSr = iconSr,
                baseSr = null,
                baseY = 0.85f
            });
        }

        private bool IsEquipped(Pedestal p)
        {
            var g = GameRoot.I;
            if (g == null) return false;
            if (p.kind == PedestalKind.Quiver) return g.CurrentQuiver == p.quiver;
            if (p.kind == PedestalKind.Retrieval) return g.Combat != null && g.Combat.RetrievalType == p.retrieval;
            return false;
        }

        private void Update()
        {
            var g = GameRoot.I;
            if (g == null || room == null || room.Player == null) return;

            Vector2 playerPos = room.Player.transform.position;

            if (!lobbyMode)
            {
                // 안전 방 판정: 왼쪽 방에 있으면 적에게 보이지 않으며, 진입 시 적·바닥 보상 모두 제거
                bool inSafeRoom = playerPos.x < SafeRoomMaxX;
                room.IsPlayerHidden = inSafeRoom;
                if (inSafeRoom && !wasInSafeRoom)
                {
                    room.ClearEnemiesAndDrops();
                }
                wasInSafeRoom = inSafeRoom;
            }

            // 진열대 아이콘 상태 및 가장 가까운 선택 가능 진열대 탐색
            nearestAvailable = null;
            float bestDist = InteractRange;
            float t = Time.time;

            for (int i = 0; i < pedestals.Count; i++)
            {
                var p = pedestals[i];
                if (p.kind == PedestalKind.EmptyRelic) continue;

                bool equipped = IsEquipped(p);
                if (p.iconSr != null)
                {
                    p.iconSr.enabled = !equipped;
                    if (!equipped)
                    {
                        float bob = Mathf.Sin(t * 2.8f + i * 0.9f) * 0.08f;
                        p.iconSr.transform.localPosition = new Vector3(0f, p.baseY + bob, 0f);
                    }
                }
                if (p.baseSr != null)
                {
                    p.baseSr.color = equipped ? new Color(0.18f, 0.16f, 0.16f, 0.65f) : new Color(0.32f, 0.25f, 0.19f, 0.95f);
                }

                if (!equipped)
                {
                    float d = Vector2.Distance(playerPos + Vector2.up * 0.6f, (Vector2)p.worldPos + Vector2.up * 0.6f);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        nearestAvailable = p;
                    }
                }
            }

#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            bool canInteract = g.State == GameState.ArcheryTest || g.State == GameState.Lobby;
            if (k != null && canInteract && nearestAvailable != null && k.fKey.wasPressedThisFrame)
            {
                EquipPedestal(nearestAvailable);
            }
#endif
        }

        private void EquipPedestal(Pedestal p)
        {
            var g = GameRoot.I;
            if (g == null) return;
            if (p.kind == PedestalKind.Quiver)
            {
                g.SelectQuiver(p.quiver);
                g.ShowToast($"{QuiverStats.Name(p.quiver)} 장착 ({QuiverStats.Desc(p.quiver)})");
            }
            else if (p.kind == PedestalKind.Retrieval)
            {
                if (lobbyMode && g.Profile != null && !g.Profile.StyleUnlocked("retrieval", p.retrieval.ToString()))
                {
                    Sfx.Play("empty");
                    g.ShowToast($"{StyleNames.Retrieval(p.retrieval)} — 넥타르 강화에서 해금하세요");
                    return;
                }
                g.SelectRetrieval(p.retrieval);
                g.ShowToast($"{StyleNames.Retrieval(p.retrieval)} 장착");
            }
            else if (p.kind == PedestalKind.Arrow)
            {
                if (g.Combat == null || !g.Combat.HasQuiverSpace)
                {
                    Sfx.Play("empty");
                    g.ShowToast("화살통이 가득 찼습니다 (Tab 상태창에서 분해 가능)");
                    return;
                }
                g.Combat.AddNewArrow(p.arrowCode);
                g.ShowToast($"{DB.Arrow(p.arrowCode).name} 획득");
            }
            Fx.Burst(p.worldPos + Vector3.up * 0.85f, new Color(1f, 0.88f, 0.45f), 10, 4.5f, 0.15f, 0.35f);
            Sfx.Play("buy", 0.65f, 1.05f);
        }

        private void OnGUI()
        {
            if (GameRoot.I == null || (GameRoot.I.State != GameState.ArcheryTest && GameRoot.I.State != GameState.Lobby)) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (promptTitleStyle == null)
            {
                promptTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                promptDescStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }

            if (nearestAvailable == null) return;
            Vector3 screen = cam.WorldToScreenPoint(nearestAvailable.worldPos + Vector3.up * 1.85f);
            if (screen.z < 0f) return;

            {
                float sx = screen.x;
                float sy = Screen.height - screen.y;

                string title;
                string desc;
                if (nearestAvailable.kind == PedestalKind.Quiver)
                {
                    title = $"[F] {QuiverStats.Name(nearestAvailable.quiver)}";
                    desc = QuiverStats.Desc(nearestAvailable.quiver);
                }
                else if (nearestAvailable.kind == PedestalKind.Retrieval)
                {
                    bool unlocked = !lobbyMode || GameRoot.I.Profile == null || GameRoot.I.Profile.StyleUnlocked("retrieval", nearestAvailable.retrieval.ToString());
                    title = unlocked ? $"[F] {StyleNames.Retrieval(nearestAvailable.retrieval)}" : $"{StyleNames.Retrieval(nearestAvailable.retrieval)} (잠김)";
                    desc = unlocked ? StyleNames.RetrievalDesc(nearestAvailable.retrieval) : "로비 넥타르 강화에서 해금할 수 있습니다";
                }
                else
                {
                    var d = DB.Arrow(nearestAvailable.arrowCode);
                    bool canTake = GameRoot.I.Combat != null && GameRoot.I.Combat.HasQuiverSpace;
                    title = canTake ? $"[F] {d.name} 획득" : $"{d.name} (화살통 가득 참)";
                    desc = d.code == 5 ? "적중 시 0.3초마다 3회 독 피해" : (d.code == 12 ? "쏘고 터지면 사라짐 (회수 불가)" : d.special);
                }

                float w = 310f, h = 64f;
                var r = new Rect(sx - w * 0.5f, sy - h, w, h);
                GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.88f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;

                promptTitleStyle.normal.textColor = new Color(1f, 0.9f, 0.45f);
                GUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, 24f), title, promptTitleStyle);
                promptDescStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
                GUI.Label(new Rect(r.x + 8f, r.y + 26f, r.width - 16f, 34f), desc, promptDescStyle);
            }
        }
    }
}
