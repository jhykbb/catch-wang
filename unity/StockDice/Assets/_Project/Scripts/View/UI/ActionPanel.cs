using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>
    /// 화면 아래 가운데 행동 패널. 지금 단계(주사위/땅 매입/건설/코인/턴 종료 매매)에 맞는 버튼만 보여준다.
    /// </summary>
    public sealed class ActionPanel : MonoBehaviour
    {
        private HudView _hud;
        private RectTransform _root;
        private Text _prompt;
        private RectTransform _buttonRow;
        private readonly List<GameObject> _buttons = new List<GameObject>();

        private GameSession S { get { return _hud.Session; } }
        private GameState State { get { return S.State; } }
        private GameEngine Engine { get { return S.Engine; } }

        public static ActionPanel Create(RectTransform parent, HudView hud)
        {
            var bg = UIKit.Panel(parent, "ActionPanel", Palette.PanelBg);
            var panel = bg.gameObject.AddComponent<ActionPanel>();
            panel._hud = hud;
            panel._root = UIKit.PlaceAt(bg, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(42f, 14f), new Vector2(780f, 158f));
            UIKit.AddShadow(bg, new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, -4f));
            panel._prompt = UIKit.Label(bg.transform, "", 24, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(panel._prompt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -66f), new Vector2(-20f, -8f));
            var row = UIKit.Create("Buttons", bg.transform);
            panel._buttonRow = UIKit.Place(row.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 16f), new Vector2(-20f, 82f));
            var h = UIKit.HStack(row, 12f, null, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true;
            panel.ShowBusy();
            return panel;
        }

        private void Clear()
        {
            foreach (var b in _buttons) if (b != null) Destroy(b);
            _buttons.Clear();
        }

        private Button Add(string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick, bool enabled = true, int fontSize = 24)
        {
            var b = UIKit.Button(_buttonRow, label, bg, fg, onClick, fontSize);
            b.interactable = enabled;
            UIKit.Pref(b, -1, 62f, 1f);
            _buttons.Add(b.gameObject);
            return b;
        }

        public void ShowBusy()
        {
            Clear();
            _prompt.text = "";
            PromptFull();
        }

        private void PromptFull()
        {
            UIKit.Place(_prompt, Vector2.zero, Vector2.one, new Vector2(20f, 10f), new Vector2(-20f, -10f));
        }

        private void PromptTop()
        {
            UIKit.Place(_prompt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -66f), new Vector2(-20f, -8f));
        }

        public void ShowThinking(int playerId)
        {
            Clear();
            var p = State.Players[playerId];
            PromptFull();
            _prompt.text = UIKit.Colored("●", Palette.Player(playerId)) + " " + p.Name + " (AI · " + PlayerSetup.StyleName(p.Style) + ") 생각 중...";
        }

        public void ShowFor(int pid)
        {
            Clear();
            PromptTop();
            var st = State;
            var p = st.Players[pid];
            string who = UIKit.Colored("●", Palette.Player(pid)) + " " + p.Name + "님, ";

            switch (st.Phase)
            {
                case GamePhase.Ipo:
                    _prompt.text = who + "공모주 청약! 오른쪽 주식 시장에서 첫 투자를 하세요.";
                    Add("청약 끝내기", Palette.ButtonPrimary, Color.white, () => S.Submit(GameAction.EndTurn(pid)));
                    break;

                case GamePhase.AwaitRoll:
                    _prompt.text = who + (st.BonusRollsUsed > 0 ? "더블! 한 번 더 굴리세요." : "주사위를 굴리세요.");
                    Add("주사위 굴리기", Palette.ButtonPrimary, Color.white, () => S.Submit(GameAction.Roll(pid)), true, 28);
                    AddItemButton(pid);
                    break;

                case GamePhase.AwaitLandPurchase:
                {
                    int tile = st.PendingTile;
                    var def = st.Tile(tile);
                    _prompt.text = who + string.Format("{0}을(를) {1}에 살까요?  (통행료 {2}, 건물을 지으면 최대 {3})",
                        def.Name, Fmt.Won(def.LandPrice), Fmt.Won(Engine.TollAtLevel(tile, 0)), Fmt.Won(Engine.TollAtLevel(tile, st.Config.MaxBuildingLevel)));
                    Add("매입 " + Fmt.Won(def.LandPrice), Palette.Up, Color.white, () => S.Submit(GameAction.Decide(pid, true)), p.Cash >= def.LandPrice);
                    Add("패스", Palette.ButtonNeutral, Palette.Ink, () => S.Submit(GameAction.Decide(pid, false)));
                    break;
                }

                case GamePhase.AwaitBuild:
                {
                    int tile = st.PendingTile;
                    var land = st.Lands[tile];
                    long cost = Engine.BuildCost(tile);
                    _prompt.text = who + string.Format("{0}에 {1}단계 건물을 지을까요?  통행료 {2} → {3}",
                        st.Tile(tile).Name, land.Level + 1, Fmt.Won(Engine.Toll(tile)), Fmt.Won(Engine.TollAtLevel(tile, land.Level + 1)));
                    Add("건설 " + Fmt.Won(cost), Palette.Up, Color.white, () => S.Submit(GameAction.Decide(pid, true)), p.Cash >= cost);
                    Add("패스", Palette.ButtonNeutral, Palette.Ink, () => S.Submit(GameAction.Decide(pid, false)));
                    break;
                }

                case GamePhase.AwaitCoinBet:
                {
                    var cfg = st.Config;
                    _prompt.text = who + string.Format("코인 거래소! {0:0}% 확률로 2배, {1:0}% 확률로 반토막. 얼마나 걸까요?", cfg.CoinWinChance * 100, (1 - cfg.CoinWinChance) * 100);
                    for (int i = 0; i < cfg.CoinStakeRates.Length; i++)
                    {
                        int idx = i;
                        long stake = Engine.CoinStake(pid, i);
                        Add(string.Format("{0:0}%  {1}", cfg.CoinStakeRates[i] * 100, Fmt.Won(stake)), Palette.Hex("#7048E8"), Color.white,
                            () => S.Submit(GameAction.Bet(pid, idx)), stake > 0, 20);
                    }
                    Add("패스", Palette.ButtonNeutral, Palette.Ink, () => S.Submit(GameAction.Bet(pid, -1)));
                    break;
                }

                case GamePhase.AwaitTrade:
                {
                    string msg = p.CanTrade ? "턴 종료 매매: 오른쪽에서 주식을 사고팔고 턴을 끝내세요." : "거래정지 중이라 매매할 수 없어요.";
                    _prompt.text = who + msg;
                    AddItemButton(pid);
                    if (st.Config.BuildAtTurnEnd) AddBuildButton(pid);
                    Add("땅 팔기", Palette.ButtonNeutral, Palette.Ink, () => OpenLandSell(pid), HasLand(pid), 22);
                    Add("턴 종료", Palette.ButtonPrimary, Color.white, () => S.Submit(GameAction.EndTurn(pid)), true, 26);
                    break;
                }

                default:
                    _prompt.text = "";
                    break;
            }
        }

        private bool HasLand(int pid)
        {
            foreach (var land in State.Lands) if (land != null && land.Owner == pid) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // 아이템
        // ------------------------------------------------------------------

        private void AddItemButton(int pid)
        {
            var usable = Engine.UsableItems(pid);
            int owned = State.Players[pid].Items.Count;
            string label = owned > 0 ? "아이템 (" + owned + ")" : "아이템 없음";
            Add(label, Palette.Hex("#FFDEEB"), Palette.Hex("#C2255C"), () => OpenItems(pid), usable.Count > 0, 22);
        }

        private void OpenItems(int pid)
        {
            var choices = new List<PopupChoice>();
            foreach (var item in Engine.UsableItems(pid))
            {
                var it = item;
                choices.Add(new PopupChoice(ItemInfo.Name(it), () => ChooseItemTarget(pid, it), Palette.Hex("#FFDEEB"), ItemInfo.Description(it)));
            }
            Popup.Show(_hud.PopupLayer, "아이템 사용", "통행료 면제권은 통행료를 낼 때 자동으로 쓰입니다.", choices, () => { });
        }

        private void ChooseItemTarget(int pid, ItemType item)
        {
            if (item == ItemType.DiceControl)
            {
                var nums = new List<PopupChoice>();
                int pos = State.Players[pid].Position;
                for (int total = 2; total <= State.Config.DiceSides * 2; total++)
                {
                    int t = total;
                    var dest = State.Tile((pos + t) % State.Board.TileCount);
                    nums.Add(new PopupChoice(t + "칸\n<size=15>" + dest.Name + "</size>", () => S.Submit(GameAction.UseItem(pid, item, -1, t)), Palette.ButtonNeutral));
                }
                Popup.Show(_hud.PopupLayer, "주사위 조작", "몇 칸 이동할까요? (더블 판정 없음)", nums, () => { }, 4, 760f);
                return;
            }

            if (ItemInfo.NeedsCompany(item))
            {
                var companies = new List<PopupChoice>();
                for (int c = 0; c < State.Companies.Length; c++)
                {
                    if (!State.Companies[c].Listed) continue;
                    int cid = c;
                    var company = State.Companies[c];
                    var odds = StockMarket.GetOdds(State, c);
                    string detail = string.Format("{0} · 투자자 {1}명 · 상승 {2:0}%", Fmt.Won(company.Price), odds.Investors, odds.UpChance * 100);
                    var color = Palette.Lighten(Palette.SectorColor(State.CompanyDef(c).Sector), 0.7f);
                    companies.Add(new PopupChoice(State.CompanyName(c), () =>
                    {
                        if (item == ItemType.Rumor) ChooseRumorDirection(pid, cid);
                        else S.Submit(GameAction.UseItem(pid, item, cid));
                    }, color, detail));
                }
                Popup.Show(_hud.PopupLayer, ItemInfo.Name(item), "종목을 고르세요.", companies, () => { });
                return;
            }

            S.Submit(GameAction.UseItem(pid, item));
        }

        private void ChooseRumorDirection(int pid, int companyId)
        {
            Popup.Show(_hud.PopupLayer, "찌라시: " + State.CompanyName(companyId), "어떤 소문을 퍼뜨릴까요?", new List<PopupChoice>
            {
                new PopupChoice("호재 찌라시 (상승 확률 +25%p)", () => S.Submit(GameAction.UseItem(pid, ItemType.Rumor, companyId, 1)), Palette.Lighten(Palette.Up, 0.6f)),
                new PopupChoice("악재 찌라시 (상승 확률 -25%p)", () => S.Submit(GameAction.UseItem(pid, ItemType.Rumor, companyId, -1)), Palette.Lighten(Palette.Down, 0.6f)),
            }, () => { });
        }

        // ------------------------------------------------------------------
        // 땅
        // ------------------------------------------------------------------

        private void OpenLandSell(int pid)
        {
            var choices = new List<PopupChoice>();
            foreach (var land in State.Lands)
            {
                if (land == null || land.Owner != pid) continue;
                int tile = land.TileIndex;
                string detail = string.Format("투자 {0} → 매각 {1} ({2}단계, 통행료 {3})", Fmt.Won(land.Invested), Fmt.Won(Engine.BankSellValue(tile)), land.Level, Fmt.Won(Engine.Toll(tile)));
                choices.Add(new PopupChoice(State.Tile(tile).Name, () => S.Submit(GameAction.SellLand(pid, tile)), Palette.ButtonNeutral, detail));
            }
            Popup.Show(_hud.PopupLayer, "땅 팔기", "은행에 투자액의 50%로 팝니다. 총자산이 줄어드니 신중하게!", choices, () => { });
        }

        private void AddBuildButton(int pid)
        {
            var choices = new List<PopupChoice>();
            foreach (var land in State.Lands)
            {
                if (land == null || land.Owner != pid || land.Level >= State.Config.MaxBuildingLevel) continue;
                int tile = land.TileIndex;
                long cost = Engine.BuildCost(tile);
                choices.Add(new PopupChoice(State.Tile(tile).Name + " " + (land.Level + 1) + "단계",
                    () => S.Submit(GameAction.Build(pid, tile)), Palette.ButtonNeutral,
                    "건설비 " + Fmt.Won(cost) + " · 통행료 " + Fmt.Won(Engine.Toll(tile)) + " → " + Fmt.Won(Engine.TollAtLevel(tile, land.Level + 1)),
                    State.Players[pid].Cash >= cost));
            }
            bool can = choices.Count > 0 && State.TurnEndBuilds < State.Config.MaxTurnEndBuilds;
            Add("건설", Palette.ButtonNeutral, Palette.Ink, () => Popup.Show(_hud.PopupLayer, "건물 짓기", "턴 종료 건설 (선택 규칙)", choices, () => { }), can, 22);
        }
    }
}
