using System.Collections;
using StockDice.Core;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>
    /// 이벤트 하나를 애니메이션으로 재생한다. 로직은 이미 끝난 상태이고, 화면은 그 결과를 "보여주기만" 한다.
    /// 새 이벤트를 추가하면 여기에 case 하나만 더하면 된다.
    /// </summary>
    public sealed class EventPlayer
    {
        private readonly GameSession _s;

        public EventPlayer(GameSession session)
        {
            _s = session;
        }

        private float Speed { get { return Mathf.Max(0.25f, _s.EffectiveSpeed); } }
        private float T(float seconds) { return seconds / Speed; }
        private GameState State { get { return _s.State; } }
        private HudView Hud { get { return _s.Hud; } }
        private BoardView Board { get { return _s.Board; } }
        private TokenManager Tokens { get { return _s.Tokens; } }
        private FxManager Fx { get { return _s.Fx; } }
        private CameraRig Rig { get { return _s.Rig; } }

        private bool ShowsTo(GameEvent e)
        {
            if (e.VisibleTo < 0) return true;
            return _s.IsHuman(e.VisibleTo);
        }

        private void FocusPlayer(int pid, float distance = -1f)
        {
            int tile = State.Players[pid].Position;
            Rig.Focus(Tokens.Position(pid), Board.SideOf(tile), distance);
        }

        private Vector3 Head(int pid)
        {
            return Tokens.Token(pid).HeadPosition;
        }

        /// <summary>한 판이 끝날 때마다 화면을 최종 상태에 맞춘다.</summary>
        public void SyncAll()
        {
            Board.SyncFromState(State);
            var pos = new int[State.PlayerCount];
            for (int i = 0; i < pos.Length; i++) pos[i] = State.Players[i].Position;
            Tokens.PlaceAll(pos, false);
            Tokens.Highlight(State.CurrentPlayer);
        }

        public IEnumerator Play(GameEvent e)
        {
            if (!ShowsTo(e)) yield break;
            Hud.Log(e.Describe(State), e);

            switch (e)
            {
                case GameStartedEvent _:
                    Rig.Overview();
                    yield return Hud.Banner("게임 시작!", Palette.ButtonPrimary, T(1.1f));
                    break;

                case CompanyListedEvent ev:
                {
                    bool big = ev.ByPlayer >= 0;
                    if (big)
                    {
                        var def = State.CompanyDef(ev.CompanyId);
                        Rig.Focus(Board.TileTop(def.TileIndex), Board.SideOf(def.TileIndex), 15f);
                        yield return Tween.Wait(T(0.25f));
                        Rig.Shake(0.15f, 0.4f);
                        _s.StartCoroutine(Board.AnimateListing(ev.CompanyId, ev.Price, true, Speed));
                        Hud.FlashStockRow(ev.CompanyId, Palette.Gold);
                        yield return Hud.Banner("신규 상장!  " + State.CompanyName(ev.CompanyId) + "  " + Fmt.Won(ev.Price), Palette.Hex("#7048E8"), T(1.3f));
                    }
                    else
                    {
                        _s.StartCoroutine(Board.AnimateListing(ev.CompanyId, ev.Price, false, Speed));
                        Hud.FlashStockRow(ev.CompanyId, Palette.Gold);
                        yield return Tween.Wait(T(0.25f));
                    }
                    break;
                }

                case IpoStartedEvent _:
                    Hud.Refresh();
                    yield return Hud.Banner("공모주 청약", Palette.Hex("#1C7ED6"), T(1.2f));
                    Hud.Toast("시작 자금으로 첫 투자를 하세요. 산 주식은 다다음 턴부터 팔 수 있어요.", null, 3f);
                    break;

                case IpoTurnEvent ev:
                    Hud.SetTurn(ev.PlayerId);
                    Tokens.Highlight(ev.PlayerId);
                    Hud.PulseCard(ev.PlayerId);
                    yield return Tween.Wait(T(0.2f));
                    break;

                case RoundStartedEvent ev:
                    Hud.Refresh();
                    if (ev.FinalStretch)
                        yield return Hud.Banner(ev.Round + "라운드 · 마감 임박! 변동폭 1.5배", Palette.Up, T(1.3f));
                    else
                        yield return Hud.Banner(ev.Round + "라운드", Palette.ButtonDark, T(0.8f));
                    break;

                case TurnStartedEvent ev:
                    _s.Dice.Hide();
                    Hud.SetTurn(ev.PlayerId);
                    Tokens.Highlight(ev.PlayerId);
                    Hud.PulseCard(ev.PlayerId);
                    FocusPlayer(ev.PlayerId);
                    yield return Tween.Wait(T(0.35f));
                    break;

                case LockupReleasedEvent ev:
                    Hud.Float(Head(ev.PlayerId), "잠금 해제 " + State.CompanyName(ev.CompanyId), Palette.Gain, 26);
                    yield return Tween.Wait(T(0.2f));
                    break;

                case TurnSkippedEvent ev:
                    Hud.Float(Head(ev.PlayerId), "휴가 중~", Palette.Hex("#0CA678"), 34);
                    yield return Tokens.Token(ev.PlayerId).Spin(T(0.7f));
                    break;

                case DiceRolledEvent ev:
                {
                    // 이동 전 위치 기준으로 던진다 (이 이벤트 다음에 이동 이벤트가 온다)
                    int from = TokenTile(ev.PlayerId);
                    Vector3 tokenPos = Board.TileTop(from);
                    Vector3 inward = -Board.Outward(from);
                    Vector3 center = tokenPos + inward * 3.4f;
                    center.y = 0.1f;
                    Rig.Focus((tokenPos + center) * 0.5f, Board.SideOf(from), 15f, 0.25f);
                    yield return _s.Dice.Roll(ev.Die1, ev.Die2, center, inward, Speed, ev.Controlled);
                    Rig.Shake(0.08f, 0.25f);
                    string label = ev.Controlled ? "조작! " + ev.Total : ev.Total.ToString();
                    Hud.Float(_s.Dice.TopOfDice, label, ev.IsDouble ? Palette.Up : Palette.Ink, 60);
                    if (ev.IsDouble) Hud.Toast("더블!", Palette.Up, 1.2f);
                    _s.Dice.HideAfter(1.6f);
                    yield return Tween.Wait(T(0.35f));
                    break;
                }

                case TokenMovedEvent ev:
                {
                    float step = T(0.2f);
                    yield return Tokens.MoveAlong(ev.PlayerId, ev.Path, step, (tile, pos) =>
                    {
                        Rig.Focus(pos, Board.SideOf(tile), -1f, 0.3f);
                        if (tile == State.Board.StartIndex) Fx.Sparkle(Board.TileTop(tile), Palette.Gold);
                    });
                    Fx.DustRing(Board.TileTop(ev.To), Palette.Lighten(Palette.Player(ev.PlayerId), 0.6f));
                    _s.StartCoroutine(Board.PunchTile(ev.To, 0.06f, 0.2f));
                    break;
                }

                case SalaryPaidEvent ev:
                    Hud.Float(Head(ev.PlayerId), "월급 " + Fmt.SignedWon(ev.Amount), Palette.Gain, 36);
                    _s.StartCoroutine(Fx.CoinFlight(Board.TileTop(State.Board.StartIndex) + Vector3.up, Head(ev.PlayerId), 6, T(0.6f)));
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.45f));
                    break;

                case LandBoughtEvent ev:
                    yield return Fx.CoinFlight(Head(ev.PlayerId), Board.TileTop(ev.TileIndex) + Vector3.up * 0.3f, 5, T(0.45f));
                    yield return Board.AnimateLandBought(ev.TileIndex, ev.PlayerId, Speed);
                    Hud.Float(Board.TileTop(ev.TileIndex) + Vector3.up, "매입!", Palette.Player(ev.PlayerId), 34);
                    Hud.RefreshPlayers();
                    break;

                case LandDeclinedEvent ev:
                    Hud.Float(Head(ev.PlayerId), ev.CouldNotAfford ? "돈이 부족해..." : "패스", Palette.InkSoft, 28);
                    yield return Tween.Wait(T(0.3f));
                    break;

                case BuildingBuiltEvent ev:
                    Rig.Focus(Board.TileTop(ev.TileIndex), Board.SideOf(ev.TileIndex), 14f);
                    yield return Board.AnimateBuild(ev.TileIndex, ev.Level, ev.PlayerId, Speed);
                    Rig.Shake(0.1f, 0.25f);
                    Hud.Float(Board.TileTop(ev.TileIndex) + Vector3.up * 2f, ev.Level + "단계 건물!", Palette.Player(ev.PlayerId), 34);
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.2f));
                    break;

                case TollPaidEvent ev:
                {
                    int coins = Mathf.Clamp((int)(ev.Amount / 300) + 3, 3, 18);
                    Rig.Shake(Mathf.Clamp(ev.Amount / 8000f, 0.08f, 0.45f), 0.45f);
                    Hud.Float(Head(ev.PayerId), "통행료 -" + Fmt.Won(ev.Amount), Palette.Up, 38);
                    yield return Fx.CoinFlight(Head(ev.PayerId), Head(ev.OwnerId), coins, T(0.7f));
                    Hud.Float(Head(ev.OwnerId), "+" + Fmt.Won(ev.Amount), Palette.Gain, 34);
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.25f));
                    break;
                }

                case TollWaivedEvent ev:
                    Hud.Float(Head(ev.PlayerId), "통행료 면제!", Palette.Hex("#1C7ED6"), 36);
                    Fx.Sparkle(Head(ev.PlayerId), Palette.Hex("#74C0FC"));
                    yield return Tween.Wait(T(0.6f));
                    break;

                case CompanyVisitedEvent ev:
                    Hud.Float(Head(ev.PlayerId), State.CompanyName(ev.CompanyId) + " 방문", Palette.InkSoft, 26);
                    yield return Tween.Wait(T(0.25f));
                    break;

                case ItemGainedEvent ev:
                    Fx.Confetti(Board.TileTop(State.Players[ev.PlayerId].Position) + Vector3.up * 0.6f, 16, new[] { Palette.Hex("#FF8FB1"), Palette.Gold, Color.white }, 3f);
                    Hud.Float(Head(ev.PlayerId), ev.Discarded ? "가방이 가득 찼다!" : "아이템: " + ItemInfo.Name(ev.Item), Palette.Hex("#D6336C"), 34);
                    if (!ev.Discarded && _s.IsHuman(ev.PlayerId) && ev.Item != ItemType.CashGrant)
                        Hud.Toast(ItemInfo.Name(ev.Item) + ": " + ItemInfo.Description(ev.Item), null, 3f);
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.7f));
                    break;

                case CashBonusEvent ev:
                    Hud.Float(Head(ev.PlayerId) + Vector3.up * 0.4f, ev.Reason + " " + Fmt.SignedWon(ev.Amount), Palette.Gain, 34);
                    Fx.Sparkle(Head(ev.PlayerId), Palette.Gold);
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.45f));
                    break;

                case VacationEvent ev:
                    Hud.Float(Head(ev.PlayerId), "휴가!", Palette.Hex("#0CA678"), 40);
                    yield return Tokens.Token(ev.PlayerId).Spin(T(0.6f), 2f);
                    Hud.RefreshPlayers();
                    break;

                case TradeHaltEvent ev:
                    Rig.Shake(0.2f, 0.35f);
                    Hud.Float(Head(ev.PlayerId), "거래정지!", Palette.Up, 42);
                    yield return Tokens.Token(ev.PlayerId).Shake(T(0.5f), 0.12f);
                    Hud.RefreshPlayers();
                    break;

                case CoinBetEvent ev:
                    if (ev.Skipped)
                    {
                        Hud.Float(Head(ev.PlayerId), "코인은 패스", Palette.InkSoft, 28);
                        yield return Tween.Wait(T(0.3f));
                        break;
                    }
                    yield return Fx.CoinToss(Head(ev.PlayerId) + Vector3.up * 0.2f, ev.Won, Speed);
                    if (ev.Won) Rig.Shake(0.15f, 0.35f);
                    Hud.Float(Head(ev.PlayerId), ev.Won ? "2배! " + Fmt.SignedWon(ev.Delta) : "반토막... " + Fmt.SignedWon(ev.Delta), ev.Won ? Palette.Up : Palette.Down, 40);
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.5f));
                    break;

                case BonusRollEvent ev:
                    Hud.Toast("더블! " + State.PlayerName(ev.PlayerId) + "님 한 번 더", Palette.Up, 1.4f);
                    yield return Tween.Wait(T(0.4f));
                    break;

                case StockTradedEvent ev:
                {
                    Hud.RefreshPlayers();
                    Hud.RefreshMarket();
                    Hud.FlashStockRow(ev.CompanyId, ev.IsBuy ? Palette.Up : Palette.Down);
                    if (ev.Forced)
                    {
                        Hud.Float(Head(ev.PlayerId), (ev.FireSale ? "반값 처분 " : "자동 매도 ") + State.CompanyName(ev.CompanyId), Palette.Up, 30);
                        yield return Tokens.Token(ev.PlayerId).Shake(T(0.35f), 0.08f);
                    }
                    else if (!_s.IsHuman(ev.PlayerId))
                    {
                        Hud.Toast(string.Format("{0}님 {1} {2}주 {3}", State.PlayerName(ev.PlayerId), State.CompanyName(ev.CompanyId), Fmt.Num(ev.Quantity), ev.IsBuy ? "매수" : "매도"),
                            ev.IsBuy ? Palette.Up : Palette.Down, 1.3f);
                        yield return Tween.Wait(T(0.35f));
                    }
                    break;
                }

                case LandSoldEvent ev:
                    yield return Board.AnimateLandSold(ev.TileIndex, Speed);
                    Hud.Float(Board.TileTop(ev.TileIndex) + Vector3.up, "매각 +" + Fmt.Won(ev.Amount), Palette.InkSoft, 30);
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.2f));
                    break;

                case BailoutEvent ev:
                    Rig.Shake(0.5f, 0.8f);
                    yield return Hud.Banner(State.PlayerName(ev.PlayerId) + "님 파산! 구제금융 " + Fmt.Won(ev.Cash) + " (부채 " + Fmt.Won(ev.Debt) + ")", Palette.Up, T(1.8f));
                    Hud.RefreshPlayers();
                    break;

                case ItemUsedEvent ev:
                {
                    string text = State.PlayerName(ev.PlayerId) + "님 " + ItemInfo.Name(ev.Item) + " 사용!";
                    Hud.Toast(text, Palette.Hex("#D6336C"), 1.6f);
                    if (ev.CompanyId >= 0)
                    {
                        string tag = ev.Item == ItemType.Rumor ? (ev.Param >= 0 ? "호재 찌라시!" : "악재 찌라시!")
                            : ev.Item == ItemType.Short ? "공매도!" : "내부자 정보?";
                        Hud.Float(Board.CompanyTop(ev.CompanyId), tag, ev.Item == ItemType.Rumor && ev.Param >= 0 ? Palette.Up : Palette.Down, 32);
                    }
                    Hud.RefreshPlayers();
                    yield return Tween.Wait(T(0.6f));
                    break;
                }

                case InsiderInfoEvent ev:
                    Hud.ShowPrivate(State.PlayerName(ev.PlayerId) + "님만 보세요",
                        State.CompanyName(ev.CompanyId) + "은(는) 다음 장 마감에 " + UIKit.Colored(ev.Direction > 0 ? "오른다 ▲" : "내린다 ▼", ev.Direction > 0 ? Palette.Up : Palette.Down));
                    yield return Tween.Wait(T(0.3f));
                    break;

                case TaxAuditEvent ev:
                    Hud.Float(Head(ev.TargetId), "세금 -" + Fmt.Won(ev.Amount), Palette.Up, 36);
                    yield return Fx.CoinFlight(Head(ev.TargetId), Vector3.up * 0.5f, 8, T(0.6f));
                    Hud.RefreshPlayers();
                    break;

                case TurnEndedEvent _:
                    break;

                case NewsEvent ev:
                    Rig.Overview();
                    yield return Hud.Banner(ev.Headline, Palette.Hex("#F59F00"), T(1.6f));
                    break;

                case MarketClosedEvent ev:
                    Rig.Overview();
                    foreach (var ch in ev.Changes) Board.PulseCompany(ch.CompanyId, ch.Rate, ch.LimitUp || ch.LimitDown);
                    yield return Hud.MarketClose(ev, Speed);
                    foreach (var ch in ev.Changes) Board.SetCompanySub(ch.CompanyId, ch.NewPrice, ch.Rate);
                    Hud.RefreshMarket();
                    Hud.RefreshPlayers();
                    break;

                case DelistedEvent ev:
                {
                    var def = State.CompanyDef(ev.CompanyId);
                    Rig.Focus(Board.TileTop(def.TileIndex), Board.SideOf(def.TileIndex), 15f);
                    Rig.Shake(0.3f, 0.6f);
                    _s.StartCoroutine(Board.AnimateDelisting(ev.CompanyId, Speed));
                    yield return Hud.Banner("상장폐지!  " + State.CompanyName(ev.CompanyId), Palette.Down, T(1.4f));
                    Hud.RefreshMarket();
                    break;
                }

                case ShortSettledEvent ev:
                    Hud.Toast(ev.Describe(State), ev.Success ? Palette.Gain : Palette.InkSoft, 1.6f);
                    yield return Tween.Wait(T(0.3f));
                    break;

                case GameEndedEvent _:
                    Rig.Overview();
                    yield return Hud.Banner("게임 종료!", Palette.ButtonPrimary, T(1.6f));
                    break;

                default:
                    yield return null;
                    break;
            }
        }

        /// <summary>말이 지금 화면상 서 있는 칸 (이동 애니메이션 전).</summary>
        private int TokenTile(int pid)
        {
            var token = Tokens.Token(pid);
            int best = State.Players[pid].Position;
            float bestDist = float.MaxValue;
            for (int i = 0; i < State.Board.TileCount; i++)
            {
                float d = (Board.TileTop(i) - token.Rest).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
