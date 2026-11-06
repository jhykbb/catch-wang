using System.Collections;
using System.Collections.Generic;
using System.Text;
using StockDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>
    /// 오른쪽 주식 시장 패널 + 매매 상자. 한국식으로 상승은 빨강, 하락은 파랑.
    /// 회사 줄을 누르면 아래 매매 상자에서 수량을 정해 사고팔 수 있다.
    /// </summary>
    public sealed class StockPanel : MonoBehaviour
    {
        private sealed class Row
        {
            public int CompanyId;
            public RectTransform Root;
            public Image Bg;
            public Image Flash;
            public Image Select;
            public Text Name;
            public Text Info;
            public Text Price;
            public Text Change;
        }

        private HudView _hud;
        private readonly List<Row> _rows = new List<Row>();
        private int _selected = -1;
        private int _qty = 1;
        private bool _interactive;

        private Text _header;
        private Text _tbTitle;
        private Text _tbOdds;
        private Text _tbHold;
        private Text _tbQty;
        private Text _tbHint;
        private Button _buy;
        private Button _sell;
        private readonly List<Button> _qtyButtons = new List<Button>();

        private GameSession S { get { return _hud.Session; } }
        private GameState State { get { return S.State; } }

        public static StockPanel Create(RectTransform parent, HudView hud)
        {
            var go = UIKit.Create("StockPanel", parent);
            var panel = go.AddComponent<StockPanel>();
            panel._hud = hud;
            panel.Build((RectTransform)go.transform);
            return panel;
        }

        private void Build(RectTransform rt)
        {
            int companies = State.Companies.Length;
            float rowH = 56f;
            float listH = 52f + companies * rowH + 10f;
            UIKit.PlaceAt(rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -84f), new Vector2(476f, listH + 10f + 250f));

            var list = UIKit.Panel(rt, "Market", Palette.PanelBg);
            UIKit.Place(list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -listH), Vector2.zero);
            _header = UIKit.Label(list.transform, "주식 시장", 26, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Place(_header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -48f), new Vector2(-16f, -6f));

            for (int c = 0; c < companies; c++)
            {
                var row = new Row { CompanyId = c };
                int id = c;
                var bg = UIKit.Panel(list.transform, "Row" + c, Color.white, UIKit.RoundedSmall);
                row.Root = UIKit.Place(bg, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -52f - (c + 1) * rowH + 4f), new Vector2(-10f, -52f - c * rowH));
                row.Bg = bg;
                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => Select(id));

                row.Select = UIKit.Panel(bg.transform, "Select", Palette.ButtonPrimary, UIKit.RoundedSmall);
                UIKit.Fill(row.Select);
                row.Select.raycastTarget = false;
                row.Select.color = Palette.WithAlpha(Palette.ButtonPrimary, 0.18f);
                row.Select.enabled = false;

                row.Flash = UIKit.Panel(bg.transform, "Flash", Color.clear, UIKit.RoundedSmall);
                UIKit.Fill(row.Flash);
                row.Flash.raycastTarget = false;

                var def = State.CompanyDef(c);
                var pill = UIKit.Panel(bg.transform, "Sector", Palette.SectorColor(def.Sector), UIKit.RoundedSmall);
                UIKit.Place(pill, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 8f), new Vector2(16f, -8f));
                pill.raycastTarget = false;

                row.Name = UIKit.Label(bg.transform, def.Name, 22, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Place(row.Name, new Vector2(0f, 0.45f), new Vector2(0.62f, 1f), new Vector2(26f, 0f), Vector2.zero);
                row.Info = UIKit.Label(bg.transform, "", 15, Palette.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(row.Info, new Vector2(0f, 0f), new Vector2(0.7f, 0.5f), new Vector2(26f, 0f), Vector2.zero);
                row.Price = UIKit.Label(bg.transform, "", 22, Palette.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
                UIKit.Place(row.Price, new Vector2(0.55f, 0.45f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-12f, 0f));
                row.Change = UIKit.Label(bg.transform, "", 16, Palette.Flat, TextAnchor.MiddleRight, FontStyle.Bold);
                UIKit.Place(row.Change, new Vector2(0.6f, 0f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(-12f, 0f));
                _rows.Add(row);
            }

            BuildTradeBox(rt, listH);
        }

        private void BuildTradeBox(RectTransform rt, float listH)
        {
            var box = UIKit.Panel(rt, "TradeBox", Palette.PanelBg);
            UIKit.Place(box, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -listH - 260f), new Vector2(0f, -listH - 10f));

            _tbTitle = UIKit.Label(box.transform, "종목을 고르세요", 24, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Place(_tbTitle, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -40f), new Vector2(-14f, -6f));
            _tbOdds = UIKit.Label(box.transform, "", 16, Palette.InkSoft, TextAnchor.UpperLeft);
            UIKit.Place(_tbOdds, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -84f), new Vector2(-14f, -40f));
            _tbHold = UIKit.Label(box.transform, "", 16, Palette.Ink, TextAnchor.UpperLeft);
            UIKit.Place(_tbHold, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -126f), new Vector2(-14f, -84f));

            // 수량 줄
            float y = -130f;
            string[] labels = { "-10", "-1", "+1", "+10", "최대", "전량" };
            int[] deltas = { -10, -1, 1, 10, 0, 0 };
            float x = 14f;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                float w = i >= 4 ? 64f : 52f;
                if (i == 2) x += 72f; // 수량 표시 자리
                var b = UIKit.Button(box.transform, labels[i], Palette.ButtonNeutral, Palette.Ink, () => AdjustQty(deltas[idx], labels[idx]), 18);
                UIKit.Place(b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y - 40f), new Vector2(x + w, y));
                x += w + 6f;
                _qtyButtons.Add(b);
            }
            _tbQty = UIKit.Label(box.transform, "1주", 22, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_tbQty, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + 2 * 58f, y - 40f), new Vector2(14f + 2 * 58f + 66f, y));

            _buy = UIKit.Button(box.transform, "매수", Palette.Up, Color.white, () => Trade(true), 22);
            UIKit.Place(_buy, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(14f, -230f), new Vector2(-5f, -180f));
            _sell = UIKit.Button(box.transform, "매도", Palette.Down, Color.white, () => Trade(false), 22);
            UIKit.Place(_sell, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(5f, -230f), new Vector2(-14f, -180f));
            _tbHint = UIKit.Label(box.transform, "", 15, Palette.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Place(_tbHint, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 2f), new Vector2(-10f, 26f));
        }

        // ------------------------------------------------------------------

        private int Viewer { get { return State.CurrentPlayer; } }

        private void Select(int companyId)
        {
            _selected = companyId;
            _qty = 1;
            Refresh();
        }

        private void AdjustQty(int delta, string label)
        {
            if (_selected < 0) return;
            var engine = S.Engine;
            if (label == "최대") _qty = Mathf.Max(1, engine.MaxBuyable(Viewer, _selected));
            else if (label == "전량") _qty = Mathf.Max(1, State.Players[Viewer].Holdings[_selected].UnlockedShares);
            else _qty = Mathf.Max(1, _qty + delta);
            RefreshTradeBox();
        }

        private void Trade(bool buy)
        {
            if (_selected < 0) return;
            var action = buy ? GameAction.Buy(Viewer, _selected, _qty) : GameAction.Sell(Viewer, _selected, _qty);
            if (S.Submit(action)) _qty = 1;
        }

        public void SetInteractive(bool on)
        {
            _interactive = on;
            RefreshTradeBox();
        }

        public void FlashRow(int companyId, Color color)
        {
            if (companyId < 0 || companyId >= _rows.Count) return;
            StartCoroutine(FlashRoutine(_rows[companyId], color));
        }

        private IEnumerator FlashRoutine(Row row, Color color)
        {
            yield return Tween.Run(0.7f, k =>
            {
                if (row.Flash != null) row.Flash.color = Palette.WithAlpha(color, 0.45f * (1f - k));
            });
        }

        public void Refresh()
        {
            var st = State;
            int listed = 0;
            for (int c = 0; c < _rows.Count; c++)
            {
                var row = _rows[c];
                var company = st.Companies[c];
                var def = st.CompanyDef(c);
                var holding = st.Players[Viewer].Holdings[c];
                row.Select.enabled = c == _selected;
                if (company.Listed)
                {
                    listed++;
                    int investors = StockMarket.CountInvestors(st, c);
                    row.Bg.color = Color.white;
                    row.Name.color = Palette.Ink;
                    row.Price.text = Fmt.Won(company.Price);
                    row.Price.color = Palette.Change(company.LastChangeRate);
                    row.Change.text = Palette.ChangeArrow(company.LastChangeRate) + " " + Fmt.Percent(company.LastChangeRate);
                    row.Change.color = Palette.Change(company.LastChangeRate);
                    string mine = holding.TotalShares > 0 ? UIKit.Colored(" · 보유 " + Fmt.Num(holding.TotalShares) + "주" + (holding.LockedShares > 0 ? "(잠금 " + holding.LockedShares + ")" : ""), Palette.Ink) : "";
                    row.Info.text = BoardDefinition.SectorName(def.Sector) + " · 투자자 " + investors + "/" + st.PlayerCount + mine;
                }
                else
                {
                    row.Bg.color = new Color(0.95f, 0.95f, 0.96f, 1f);
                    row.Name.color = Palette.InkSoft;
                    row.Price.text = "비상장";
                    row.Price.color = Palette.InkSoft;
                    row.Change.text = company.TimesDelisted > 0 ? "상장폐지됨" : "";
                    row.Change.color = Palette.InkSoft;
                    row.Info.text = BoardDefinition.SectorName(def.Sector) + " · 회사 칸을 밟으면 상장";
                }
            }
            _header.text = "주식 시장  " + UIKit.Colored("상장 " + listed + "/" + _rows.Count, Palette.InkSoft)
                           + "   " + UIKit.Colored(st.Players[Viewer].Name + "님 기준", Palette.Player(Viewer));
            if (_selected >= 0 && !st.Companies[_selected].Listed) _selected = -1;
            if (_selected < 0)
            {
                for (int c = 0; c < _rows.Count; c++) if (st.Companies[c].Listed) { _selected = c; break; }
                if (_selected >= 0) _rows[_selected].Select.enabled = true;
            }
            RefreshTradeBox();
        }

        private static string LockText(Holding h)
        {
            if (h.LockedLots.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var lot in h.LockedLots)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(lot.Shares).Append("주(잠금").Append(lot.TurnsLeft).Append(')');
            }
            return sb.ToString();
        }

        private void RefreshTradeBox()
        {
            if (_tbTitle == null) return;
            var st = State;
            var engine = S.Engine;
            bool hasCompany = _selected >= 0 && st.Companies[_selected].Listed;
            if (!hasCompany)
            {
                _tbTitle.text = "상장된 종목이 없습니다";
                _tbOdds.text = _tbHold.text = "";
                UIKit.SetButton(_buy, "매수", false);
                UIKit.SetButton(_sell, "매도", false);
                foreach (var b in _qtyButtons) b.interactable = false;
                _tbHint.text = "";
                return;
            }

            var company = st.Companies[_selected];
            var def = st.CompanyDef(_selected);
            var p = st.Players[Viewer];
            var h = p.Holdings[_selected];
            _tbTitle.text = def.Name + "  " + UIKit.Colored(Fmt.Won(company.Price), Palette.Change(company.LastChangeRate))
                            + "  " + UIKit.Colored(BoardDefinition.SectorName(def.Sector), Palette.SectorColor(def.Sector));

            var odds = StockMarket.GetOdds(st, _selected);
            bool amInvestor = h.TotalShares * company.Price >= st.Config.InvestorThreshold;
            string line = string.Format("투자자 {0}/{1}명 · 다음 마감 상승 {2:0}% · 변동 최대 ±{3:0}%", odds.Investors, st.PlayerCount, odds.UpChance * 100, odds.Volatility * 100);
            if (!amInvestor)
            {
                var mine = StockMarket.GetOdds(st, _selected, odds.Investors + 1);
                line += string.Format("\n{0}주 이상 사서 투자자가 되면: 상승 {1:0}% · ±{2:0}%", StockMarket.SharesForInvestor(st, _selected) - h.TotalShares, mine.UpChance * 100, mine.Volatility * 100);
            }
            _tbOdds.text = line;

            string hold = "보유 " + Fmt.Num(h.TotalShares) + "주";
            if (h.TotalShares > 0)
            {
                double gain = h.AveragePrice > 0 ? company.Price / h.AveragePrice - 1.0 : 0;
                hold += " · 팔 수 있음 " + Fmt.Num(h.UnlockedShares) + "주";
                if (h.LockedShares > 0) hold += " · " + LockText(h);
                hold += "\n평균 " + Fmt.Won((long)h.AveragePrice) + " · 수익률 " + UIKit.Colored(Fmt.Percent(gain), Palette.Change(gain));
            }
            hold += "   현금 " + Fmt.Won(p.Cash);
            _tbHold.text = hold;

            bool human = S.IsHuman(Viewer);
            bool tradePhase = st.Phase == GamePhase.AwaitTrade || st.Phase == GamePhase.Ipo;
            bool canTrade = _interactive && human && tradePhase && p.CanTrade && S.AwaitingHuman;
            _tbQty.text = Fmt.Num(_qty) + "주";
            long cost = company.Price * _qty;
            bool canBuy = canTrade && cost <= p.Cash;
            bool canSell = canTrade && st.Phase == GamePhase.AwaitTrade && h.UnlockedShares >= _qty;
            UIKit.SetButton(_buy, "매수 " + Fmt.Num(_qty) + "주 · " + Fmt.Won(cost), canBuy);
            UIKit.SetButton(_sell, "매도 " + Fmt.Num(_qty) + "주 · " + Fmt.Won(cost), canSell);
            foreach (var b in _qtyButtons) b.interactable = canTrade;

            if (!human) _tbHint.text = "AI 차례입니다";
            else if (!tradePhase) _tbHint.text = "매매는 턴 종료 때만 할 수 있어요 (주사위를 먼저 굴리세요)";
            else if (!p.CanTrade) _tbHint.text = "거래정지 중이라 매매할 수 없어요";
            else if (!canBuy && cost > p.Cash) _tbHint.text = "현금이 부족해요 (매수 한도 = 소지 현금)";
            else if (st.Phase == GamePhase.Ipo) _tbHint.text = "공모주 청약: 매수만 가능, 산 주식은 다다음 턴부터 팔 수 있어요";
            else _tbHint.text = "산 주식은 다다음 내 턴부터 팔 수 있어요 (보호예수)";
        }
    }
}
