using System.Collections;
using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>결과 화면: 순위, 자산 구성, 라운드별 총자산 그래프 (역전 장면이 보이도록).</summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        public static ResultScreen Create(RectTransform parent, GameSession session, System.Action onRestart, System.Action onMenu)
        {
            var go = UIKit.Create("ResultScreen", parent);
            UIKit.Fill(go.GetComponent<RectTransform>());
            var screen = go.AddComponent<ResultScreen>();
            screen.Build(session, onRestart, onMenu);
            return screen;
        }

        private void Build(GameSession session, System.Action onRestart, System.Action onMenu)
        {
            var st = session.State;
            var ranks = Valuation.Rankings(st);

            var dim = gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.35f);

            var card = UIKit.Panel(transform, "Card", Palette.PanelBg);
            UIKit.PlaceAt(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1160f, 940f));
            UIKit.AddShadow(card, new Color(0f, 0f, 0f, 0.3f), new Vector2(0f, -8f));

            var winner = st.Players[ranks[0].PlayerId];
            var title = UIKit.Label(card.transform, "최종 결과", 56, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), new Vector2(0f, -18f));
            var sub = UIKit.Label(card.transform, string.Format("{0}라운드 종료 · 우승 {1} {2}", st.Config.Rounds,
                UIKit.Colored(winner.Name, Palette.Player(winner.Id)), UIKit.Colored(Fmt.Won(ranks[0].Total), Palette.Up)), 26, Palette.InkSoft, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(sub, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -136f), new Vector2(0f, -96f));

            float y = -150f;
            long maxTotal = 1;
            foreach (var r in ranks) maxTotal = System.Math.Max(maxTotal, r.Cash + r.Stocks + r.Land);
            float rowH = Mathf.Min(86f, 380f / ranks.Count);
            foreach (var r in ranks)
            {
                var p = st.Players[r.PlayerId];
                var row = UIKit.Panel(card.transform, "Rank" + r.Rank, r.Rank == 1 ? Palette.Lighten(Palette.Gold, 0.7f) : new Color(0.97f, 0.97f, 0.98f, 1f), UIKit.RoundedSmall);
                UIKit.Place(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, y - rowH + 6f), new Vector2(-40f, y));
                var rank = UIKit.Label(row.transform, r.Rank + "위", 36, r.Rank == 1 ? Palette.GoldDark : Palette.InkSoft, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Place(rank, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(90f, 0f));
                var setup = session.Setups[r.PlayerId];
                string tag = setup.Kind == PlayerKind.Human ? "사람" : "AI · " + PlayerSetup.StyleName(setup.Style);
                var name = UIKit.Label(row.transform, UIKit.Colored("● ", Palette.Player(r.PlayerId)) + p.Name + "  " + UIKit.Colored(tag, Palette.InkSoft), 26, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Place(name, new Vector2(0f, 0.5f), new Vector2(0.45f, 1f), new Vector2(100f, 0f), Vector2.zero);
                var total = UIKit.Label(row.transform, Fmt.Won(r.Total), 30, Palette.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
                UIKit.Place(total, new Vector2(0.7f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-20f, 0f));
                string detail = string.Format("현금 {0} · 주식 {1} · 땅 {2}{3}", Fmt.Won(r.Cash), Fmt.Won(r.Stocks), Fmt.Won(r.Land),
                    r.Debt > 0 ? UIKit.Colored(" · 부채 -" + Fmt.Won(r.Debt), Palette.Up) : "");
                var det = UIKit.Label(row.transform, detail, 17, Palette.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(det, new Vector2(0f, 0f), new Vector2(0.7f, 0.5f), new Vector2(100f, 0f), Vector2.zero);

                // 자산 구성 막대
                var bar = UIKit.Create("Bar", row.transform);
                var brt = UIKit.Place(bar.GetComponent<RectTransform>(), new Vector2(0.45f, 0.55f), new Vector2(0.7f, 0.85f), Vector2.zero, new Vector2(-10f, 0f));
                float gross = Mathf.Max(1f, r.Cash + r.Stocks + r.Land);
                float scale = gross / maxTotal;
                float x0 = 0f;
                AddSeg(bar.transform, ref x0, r.Cash / gross * scale, Palette.Gain);
                AddSeg(bar.transform, ref x0, r.Stocks / gross * scale, Palette.Up);
                AddSeg(bar.transform, ref x0, r.Land / gross * scale, Palette.Hex("#F59F00"));
                y -= rowH;
            }

            var legend = UIKit.Label(card.transform, UIKit.Colored("■ 현금  ", Palette.Gain) + UIKit.Colored("■ 주식  ", Palette.Up) + UIKit.Colored("■ 땅", Palette.Hex("#F59F00")), 18, Palette.InkSoft, TextAnchor.MiddleRight);
            UIKit.Place(legend, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, y - 30f), new Vector2(-44f, y));
            y -= 34f;

            BuildChart(card.transform, st, y, -110f);

            var again = UIKit.Button(card.transform, "다시 하기", Palette.ButtonPrimary, Color.white, () => onRestart(), 28);
            UIKit.PlaceAt(again, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 26f), new Vector2(300f, 64f));
            var menu = UIKit.Button(card.transform, "처음으로", Palette.ButtonNeutral, Palette.Ink, () => onMenu(), 28);
            UIKit.PlaceAt(menu, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(10f, 26f), new Vector2(300f, 64f));

            StartCoroutine(Tween.Run(0.4f, k => card.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, k), Ease.OutBack));
        }

        private static void AddSeg(Transform parent, ref float x0, float width, Color color)
        {
            if (width <= 0.0005f) return;
            var seg = UIKit.Rect(parent, "Seg", color);
            UIKit.Place(seg, new Vector2(x0, 0f), new Vector2(x0 + width, 1f), Vector2.zero, Vector2.zero);
            x0 += width;
        }

        /// <summary>라운드별 총자산 선 그래프.</summary>
        private void BuildChart(Transform parent, GameState st, float top, float bottom)
        {
            var area = UIKit.Panel(parent, "Chart", new Color(0.97f, 0.97f, 0.98f, 1f), UIKit.RoundedSmall);
            var rt = UIKit.Place(area, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40f, -bottom), new Vector2(-40f, top));
            var caption = UIKit.Label(area.transform, "라운드별 총자산", 18, Palette.InkSoft, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Place(caption, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -32f), new Vector2(0f, -6f));

            int points = st.Players[0].AssetHistory.Count;
            if (points < 2) return;
            long min = long.MaxValue, max = long.MinValue;
            foreach (var p in st.Players)
                foreach (var v in p.AssetHistory) { min = System.Math.Min(min, v); max = System.Math.Max(max, v); }
            if (max <= min) max = min + 1;

            var plot = UIKit.Create("Plot", area.transform);
            var prt = UIKit.Place(plot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(20f, 16f), new Vector2(-20f, -38f));
            Canvas.ForceUpdateCanvases();
            float w = prt.rect.width, h = prt.rect.height;
            if (w <= 0f || h <= 0f) { w = 1000f; h = 200f; }

            foreach (var p in st.Players)
            {
                Color c = Palette.Player(p.Id);
                Vector2 prev = Vector2.zero;
                for (int i = 0; i < points; i++)
                {
                    float x = w * i / (points - 1);
                    float yv = h * (float)((p.AssetHistory[i] - min) / (double)(max - min));
                    var pt = new Vector2(x, yv);
                    if (i > 0)
                    {
                        var line = UIKit.Rect(plot.transform, "L", c);
                        var lrt = line.rectTransform;
                        lrt.anchorMin = lrt.anchorMax = Vector2.zero;
                        lrt.pivot = new Vector2(0f, 0.5f);
                        lrt.anchoredPosition = prev;
                        Vector2 d = pt - prev;
                        lrt.sizeDelta = new Vector2(d.magnitude, 4f);
                        lrt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    }
                    prev = pt;
                }
                var endDot = UIKit.Panel(plot.transform, "End", c, UIKit.Circle);
                endDot.raycastTarget = false;
                var ert = endDot.rectTransform;
                ert.anchorMin = ert.anchorMax = Vector2.zero;
                ert.pivot = new Vector2(0.5f, 0.5f);
                ert.anchoredPosition = prev;
                ert.sizeDelta = new Vector2(14f, 14f);
            }
        }
    }
}
