using UnityEngine;

namespace Laurel
{
    /// <summary>
    /// 게임 내 모든 대화창(공용): 스토리·튜토리얼 대사(GameRoot.PlayStory → DrawStory)와 안내 메시지(GameRoot.ShowToast → DrawToast).
    /// 양피지 배경(GameRoot.dialogueParchment)을 9-slice 로 그리고, 대사는 짙은 갈색 왼쪽 정렬, 이어가기 안내는 오른쪽 아래.
    /// 대사창 오른쪽 위에 '모두 건너뛰기' 버튼(GameRoot.dialogueSkipButton 배경 + Unity 텍스트). 누르면 G.SkipStory():
    /// 남은 대사만 넘기고 대화를 끝까지 넘겼을 때와 같은 종료 처리(storyDone)를 한 번 실행한다.
    /// 진행 입력(클릭 · Enter · Space · E)은 그대로.
    /// </summary>
    public partial class GameUI
    {
        // 배치 (1920×1080 가상 좌표, 레이아웃 참고 이미지 기준)
        private const float DialogueMargin = 20f, DialogueH = 248f, DialogueBottomGap = 22f;
        private const float DialoguePadL = 66f, DialoguePadR = 62f, DialoguePadT = 40f, DialoguePadB = 26f, DialogueHintH = 36f;
        private const int DialogueFontMax = 40, DialogueFontMin = 26;
        private const string SkipLabel = "모두 건너뛰기";
        private const int SkipFont = 34;
        private const float SkipPadX = 72f, SkipH = 84f, SkipMinW = 360f, SkipGap = 12f;
        // 9-slice 테두리 (가져온 텍스처 픽셀): 대사창 1024×177 은 바깥 윤곽+금색 띠가 ~12px, 계단형 모서리 ~20px / 버튼 512×99 는 ~6px, 모서리 ~18px
        private const int DialogueBorder = 22, SkipBorder = 20;

        private GUIStyle dialogueStyle, skipStyle; private Texture2D dialogueSrc, skipSrc;

        /// <summary>읽기 가능한 원본을 Linear 색 공간 보정(ForScreen)한 복사본으로 바꿔 9-slice 스타일을 만든다.</summary>
        private static GUIStyle MakeScreenStyle(Texture2D src, int border)
        {
            Texture2D tex = src;
            if (src.isReadable)
            {
                tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(ForScreen(src.GetPixels32()));
                tex.Apply();
            }
            var st = new GUIStyle { border = new RectOffset(border, border, border, border) };
            st.normal.background = tex; st.hover.background = tex; st.active.background = tex;
            return st;
        }

        private GUIStyle DialogueStyle()
        {
            var src = G != null ? G.dialogueParchment : null;
            if (src == null) return null;
            if (dialogueStyle == null || dialogueSrc != src) { dialogueSrc = src; dialogueStyle = MakeScreenStyle(src, DialogueBorder); }
            return dialogueStyle;
        }

        private GUIStyle SkipStyle()
        {
            var src = G != null ? G.dialogueSkipButton : null;
            if (src == null) return null;
            if (skipStyle == null || skipSrc != src) { skipSrc = src; skipStyle = MakeScreenStyle(src, SkipBorder); }
            return skipStyle;
        }

        private string Ink(Color32 c) => Hex(ScreenColor(c));
        private static readonly Color32 InkBrown = new Color32(0x3B, 0x24, 0x10, 255);
        private static readonly Color32 InkName = new Color32(0x7A, 0x2E, 0x14, 255);
        private static readonly Color32 InkSoft = new Color32(0x6E, 0x58, 0x40, 255);

        private GUIStyle SkipTextStyle => new GUIStyle(label) { fontSize = SkipFont, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };

        /// <summary>'모두 건너뛰기' 버튼 사각형. 글자 폭 + 좌우 여백으로 폭을 정해 한 줄이 잘리지 않게 한다. 대사창 오른쪽 위.</summary>
        public Rect SkipButtonRect(Rect box)
        {
            float tw = SkipTextStyle.CalcSize(new GUIContent(SkipLabel)).x;
            float w = Mathf.Max(SkipMinW, tw + SkipPadX * 2f);
            return new Rect(box.xMax - w, box.y - SkipH - SkipGap, w, SkipH);
        }

        public Rect DialogueRect() => new Rect(DialogueMargin, VH - DialogueH - DialogueBottomGap, VW - DialogueMargin * 2f, DialogueH);

        /// <summary>검증용: 버튼 글자 폭·한 줄 높이·버튼 안쪽 폭</summary>
        public (float textW, float lineH, float wrappedH, float innerW, float buttonW) SkipFitInfo()
        {
            var st = SkipTextStyle; var r = SkipButtonRect(DialogueRect());
            float inner = r.width - SkipPadX * 2f;
            var wrapped = new GUIStyle(st) { wordWrap = true };
            return (st.CalcSize(new GUIContent(SkipLabel)).x, st.CalcSize(new GUIContent(SkipLabel)).y, wrapped.CalcHeight(new GUIContent(SkipLabel), inner), inner, r.width);
        }

        private void DrawStory()
        {
            if (G.StoryIndex >= G.StoryQueue.Count) return;
            var line = G.StoryQueue[G.StoryIndex];
            var r = DialogueRect();
            var bg = DialogueStyle();
            // GUIStyle.Draw 는 Repaint 이벤트에서만 호출 가능 (클릭 이벤트에서 부르면 예외로 OnGUI 가 끊겨 버튼이 안 눌림)
            if (bg != null) { if (Event.current.type == EventType.Repaint) bg.Draw(r, false, false, false, false); } else Panel(r, 0.94f);

            // 모두 건너뛰기 (오른쪽 위). 먼저 그려 클릭을 가로채고, 누르면 이번 프레임은 여기서 끝낸다(종료 처리는 SkipStory 안에서 한 번)
            var sr = SkipButtonRect(r);
            var ss = SkipStyle();
            // 클릭은 직접 판정한다 (Unity 버튼 자동 판정으로는 '모두 건너뛰기' 클릭이 뒤의 '다음 대사' 영역으로 넘어가는 문제가 있었음).
            // 마우스를 누른 위치가 버튼 안이면 모두 건너뛰기, 아니면 다음 대사. 이벤트를 소비해 한 번만 처리한다.
            var ev = Event.current;
            bool overSkip = sr.Contains(ev.mousePosition);
            if (ev.type == EventType.Repaint)
            {
                if (ss != null) ss.Draw(sr, false, false, false, false);
                else button.Draw(sr, GUIContent.none, overSkip, false, false, false);
                if (overSkip) Rect(sr, new Color(1f, 1f, 1f, 0.08f)); // 마우스를 올리면 살짝 밝게
            }
            InkText(sr, $"<color=#{(ss != null ? Ink(InkBrown) : "F5E6C0")}>{SkipLabel}</color>", SkipTextStyle);
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                ev.Use();
                if (overSkip) { Sfx.Play("ui", 0.5f); G.SkipStory(); }
                else G.AdvanceStory();
                return;
            }

            float x = r.x + DialoguePadL, w = r.width - DialoguePadL - DialoguePadR, y = r.y + DialoguePadT;
            if (!string.IsNullOrEmpty(line.who))
            {
                InkText(new Rect(x, y - 8f, w, 34f), $"<color=#{Ink(InkName)}>{line.who}</color>", new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = false });
                y += 30f;
            }
            // 이어가기 안내 (오른쪽 아래)
            var hintStyle = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            var hint = new Rect(r.xMax - DialoguePadR - 520f, r.yMax - DialoguePadB - DialogueHintH, 520f, DialogueHintH);
            InkText(hint, $"<color=#{Ink(InkSoft)}>{G.StoryIndex + 1} / {G.StoryQueue.Count}</color>     <color=#{Ink(InkBrown)}>Enter · 계속</color>", hintStyle);

            // 대사: 왼쪽 정렬·자동 줄바꿈. 안내 줄과 겹치지 않도록 안내 위까지만 쓰고, 넘치면 글자를 줄인다
            float textH = hint.y - 6f - y;
            var ts = new GUIStyle(label) { fontSize = DialogueFontMax, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.UpperLeft };
            string wrapped = WrapWords(line.text, ts, w);
            while (ts.fontSize > DialogueFontMin && ts.CalcSize(new GUIContent(wrapped)).y > textH) { ts.fontSize -= 2; wrapped = WrapWords(line.text, ts, w); }
            InkText(new Rect(x, y, w, textH), $"<color=#{Ink(InkBrown)}>{wrapped}</color>", ts);

            // 화면 클릭 진행은 위의 MouseDown 처리에서 함께 한다
        }

        /// <summary>
        /// 띄어쓰기 단위로 줄을 나눈다 (IMGUI 기본 줄바꿈은 한글을 글자 중간에서 자른다). 한 단어가 폭보다 길 때만 글자 단위로 자른다.
        /// 원문의 줄바꿈(\n)은 유지.
        /// </summary>
        public static string WrapWords(string text, GUIStyle st, float width)
        {
            var plain = new GUIStyle(st) { wordWrap = false, richText = false };
            float W(string s) => plain.CalcSize(new GUIContent(s)).x;
            var sb = new System.Text.StringBuilder();
            foreach (var para in (text ?? "").Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string tryLine = cur.Length == 0 ? word : cur + " " + word;
                    if (W(tryLine) <= width) { cur = tryLine; continue; }
                    if (cur.Length > 0) { sb.Append(cur).Append('\n'); cur = ""; }
                    // 단어 하나가 폭보다 길면 글자 단위로 자른다
                    string w2 = word;
                    while (W(w2) > width && w2.Length > 1)
                    {
                        int n = w2.Length - 1;
                        while (n > 1 && W(w2.Substring(0, n)) > width) n--;
                        sb.Append(w2.Substring(0, n)).Append('\n');
                        w2 = w2.Substring(n);
                    }
                    cur = w2;
                }
                sb.Append(cur);
            }
            return sb.ToString();
        }

        private void DrawToast()
        {
            var t = G.Toast;
            if (t == null) return;
            var st = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            float tw = new GUIStyle(st) { wordWrap = false }.CalcSize(new GUIContent(t)).x;
            float w = Mathf.Clamp(tw + 140f, 420f, VW - 120f);
            float h = Mathf.Max(76f, st.CalcHeight(new GUIContent(t), w - 100f) + 40f);
            var r = new Rect(VW * 0.5f - w * 0.5f, 250f, w, h);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, G.ToastAlpha);
            var bg = DialogueStyle();
            if (bg != null)
            {
                if (Event.current.type == EventType.Repaint) bg.Draw(r, false, false, false, false);
                InkText(new Rect(r.x + 50f, r.y, r.width - 100f, r.height), $"<color=#{Ink(InkBrown)}>{t}</color>", st);
            }
            else
            {
                Rect(r, new Color(0, 0, 0, 0.7f * G.ToastAlpha));
                Text(r, t, st, new Color(1f, 0.92f, 0.6f, G.ToastAlpha));
            }
            GUI.color = old;
        }
    }
}
