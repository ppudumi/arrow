using System.Collections.Generic;
using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 오르페우스 '부르기' 회수 이펙트: 연노란 픽셀 빛이 플레이어 몸 중심으로 모이는 반복 애니메이션.
    /// 스프라이트 시트와 프레임 정보는 Resources/LaurelFx/raurel-recall.png · raurel-recall.json (pixelfx 내보내기).
    /// JSON 의 프레임 순서(frames 배열 순)·프레임별 duration(ms)·기준점(meta.pixelfx.pivot)을 그대로 쓴다.
    /// 켜고 끄는 것은 OrpheusCall 이 '실제로 화살을 끌어오는 중'일 때만 Pulse() 를 부르는 방식이라,
    /// Pulse 가 끊기면(R 을 뗌·마지막 화살 회수·라운드 종료·입력 잠김 등) 다음 프레임에 스스로 숨는다.
    /// </summary>
    public class RecallFx : MonoBehaviour
    {
        public const string ResourcePath = "LaurelFx/raurel-recall";
        public const float PixelsPerUnit = 64f;   // 플레이어 파츠와 같은 픽셀 크기 → 112×74px = 1.75×1.16 유닛
        public const int SortingOrder = 8;        // 플레이어 파츠(최대 7) 바로 앞, 화살(19~20) 뒤

        private static Sprite[] frames;
        private static float[] durations;         // 초
        private static bool loadFailed;

        private SpriteRenderer sr;
        private System.Func<Vector2> follow;
        private int frame;
        private float frameT;
        private int lastPulseFrame = -10;
        private bool visible;

        public bool Visible => visible;
        public int FrameIndex => frame;
        public static int FrameCount => frames != null ? frames.Length : 0;

        [System.Serializable] private class Rect4 { public int x, y, w, h; }
        [System.Serializable] private class Frame { public string filename; public Rect4 frame; public int duration; }
        [System.Serializable] private class PixelFx { public int fps; public bool loop; public int[] pivot; }
        [System.Serializable] private class Meta { public PixelFx pixelfx; }
        [System.Serializable] private class Sheet { public List<Frame> frames; public Meta meta; }

        private static bool Load()
        {
            if (frames != null) return true;
            if (loadFailed) return false;
            var tex = Resources.Load<Texture2D>(ResourcePath);
            var json = Resources.Load<TextAsset>(ResourcePath);
            if (tex == null || json == null) { loadFailed = true; Debug.LogWarning("[Laurel] 부르기 이펙트 리소스가 없습니다: " + ResourcePath); return false; }
            var sheet = JsonUtility.FromJson<Sheet>(json.text);
            int n = sheet.frames.Count;
            frames = new Sprite[n];
            durations = new float[n];
            float fpsDur = sheet.meta?.pixelfx != null && sheet.meta.pixelfx.fps > 0 ? 1f / sheet.meta.pixelfx.fps : 1f / 24f;
            for (int i = 0; i < n; i++)
            {
                var f = sheet.frames[i];
                // JSON 은 왼쪽 위 기준 → Unity 텍스처는 왼쪽 아래 기준
                var r = new Rect(f.frame.x, tex.height - f.frame.y - f.frame.h, f.frame.w, f.frame.h);
                Vector2 pivot = new Vector2(0.5f, 0.5f);
                var pv = sheet.meta?.pixelfx?.pivot;
                if (pv != null && pv.Length == 2) pivot = new Vector2(pv[0] / (float)f.frame.w, 1f - pv[1] / (float)f.frame.h);
                frames[i] = Sprite.Create(tex, r, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
                frames[i].name = f.filename;
                durations[i] = f.duration > 0 ? f.duration / 1000f : fpsDur;
            }
            return true;
        }

        /// <summary>이펙트 하나를 만든다. follow 는 매 프레임 따라갈 월드 위치(플레이어 몸 중심).</summary>
        public static RecallFx Create(System.Func<Vector2> follow)
        {
            if (!Load()) return null;
            var go = new GameObject("RecallFx_Orpheus");
            var fx = go.AddComponent<RecallFx>();
            fx.follow = follow;
            fx.sr = go.AddComponent<SpriteRenderer>();
            fx.sr.sortingOrder = SortingOrder;
            fx.sr.sprite = frames[0];
            fx.sr.enabled = false;
            return fx;
        }

        /// <summary>이번 프레임에 실제로 화살을 끌어오는 중이면 호출. 끊기면 다음 프레임에 숨는다.</summary>
        public void Pulse()
        {
            if (!visible) { visible = true; frame = 0; frameT = 0f; sr.sprite = frames[0]; sr.enabled = true; }
            lastPulseFrame = Time.frameCount;
        }

        public void Hide()
        {
            visible = false;
            if (sr != null) sr.enabled = false;
        }

        private void LateUpdate()
        {
            if (visible && Time.frameCount - lastPulseFrame > 1) Hide();
            if (!visible) return;
            if (follow != null) transform.position = (Vector3)follow() + new Vector3(0f, 0f, -0.1f);
            frameT += Time.deltaTime;
            while (frameT >= durations[frame])
            {
                frameT -= durations[frame];
                frame = (frame + 1) % frames.Length; // 반복
            }
            sr.sprite = frames[frame];
        }
    }
}
