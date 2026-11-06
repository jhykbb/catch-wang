using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    /// <summary>행동을 고르는 주체. 사람(화면 입력), AI, 온라인 원격 플레이어가 모두 이 형태로 들어온다.</summary>
    public interface IPlayerAgent
    {
        /// <summary>지금 이 플레이어가 할 행동 하나를 돌려준다. 엔진 상태를 바꾸면 안 된다.</summary>
        GameAction Decide(GameEngine engine, int playerId);
    }

    /// <summary>성향별 숫자. 🟡 시뮬레이터로 조정하는 값.</summary>
    public sealed class AiProfile
    {
        public double ReserveRate;      // 통행료 대비 현금 여유 (시작 자금 대비)
        public double LandAppetite;     // 땅을 살 확률
        public double BuildAppetite;    // 건물을 지을 확률
        public double IpoBudget;        // 공모주 청약 때 현금 중 주식 비중
        public double StockBudget;      // 평소 턴에 여유 현금 중 주식 비중
        public double RiskBase;         // 변동성 선호
        public double Stability;        // 투자자 많은 종목 선호
        public double Momentum;         // 지난 변동 추종(+) / 역행(-)
        public double Herd;             // 남들이 산 종목 따라 사기
        public double TakeProfit;       // 0 이면 익절 안 함
        public double StopLoss;         // 0 이면 손절 안 함
        public double PanicSell;        // 급락 다음 공포 매도 확률
        public double CoinAppetite;     // 코인 베팅 성향 (0~1)
        public double Noise;            // 판단 잡음
        public double MinScore;         // 매수 최소 점수

        public static AiProfile For(AiStyle style)
        {
            switch (style)
            {
                case AiStyle.Hodler:
                    return new AiProfile
                    {
                        ReserveRate = 0.20, LandAppetite = 0.75, BuildAppetite = 0.7, IpoBudget = 0.55, StockBudget = 0.6,
                        RiskBase = 0.0, Stability = 0.06, Momentum = 0.0, Herd = 0.0, TakeProfit = 0, StopLoss = 0,
                        PanicSell = 0, CoinAppetite = 0.1, Noise = 0.01, MinScore = 0.0,
                    };
                case AiStyle.Trader:
                    return new AiProfile
                    {
                        ReserveRate = 0.20, LandAppetite = 0.55, BuildAppetite = 0.5, IpoBudget = 0.5, StockBudget = 0.75,
                        RiskBase = 0.15, Stability = 0.0, Momentum = -0.3, Herd = 0.0, TakeProfit = 0.12, StopLoss = 0.15,
                        PanicSell = 0, CoinAppetite = 0.5, Noise = 0.02, MinScore = -0.01,
                    };
                case AiStyle.Landlord:
                    return new AiProfile
                    {
                        ReserveRate = 0.10, LandAppetite = 1.0, BuildAppetite = 1.0, IpoBudget = 0.25, StockBudget = 0.25,
                        RiskBase = 0.0, Stability = 0.04, Momentum = 0.0, Herd = 0.0, TakeProfit = 0.3, StopLoss = 0,
                        PanicSell = 0, CoinAppetite = 0.2, Noise = 0.01, MinScore = 0.005,
                    };
                default: // Ant
                    return new AiProfile
                    {
                        ReserveRate = 0.15, LandAppetite = 0.7, BuildAppetite = 0.6, IpoBudget = 0.6, StockBudget = 0.8,
                        RiskBase = 0.05, Stability = 0.0, Momentum = 0.5, Herd = 0.03, TakeProfit = 0.25, StopLoss = 0,
                        PanicSell = 0.7, CoinAppetite = 0.8, Noise = 0.06, MinScore = -0.03,
                    };
            }
        }
    }

    /// <summary>
    /// 규칙 기반 AI. 성향(존버/단타/땅부자/개미) + 순위 인식(뒤처질수록 위험한 선택).
    /// 남의 비밀 정보(남이 쓴 내부자 정보)는 보지 않는다.
    /// </summary>
    public sealed class AiAgent : IPlayerAgent
    {
        public readonly AiStyle Style;
        public readonly AiProfile Profile;
        private readonly Rng _rng;

        // 턴 단위 메모
        private int _turnKey = -1;
        private long _budgetLeft;
        private int _actionsThisPhase;
        private readonly HashSet<int> _insiderCompanies = new HashSet<int>();
        private int _insiderRound = -1;

        public AiAgent(AiStyle style, ulong seed, AiProfile profile = null)
        {
            Style = style;
            Profile = profile ?? AiProfile.For(style);
            _rng = new Rng(seed, 1013);
        }

        public GameAction Decide(GameEngine e, int me)
        {
            var s = e.State;
            switch (s.Phase)
            {
                case GamePhase.AwaitRoll: return DecideRoll(e, me);
                case GamePhase.AwaitLandPurchase: return GameAction.Decide(me, WantLand(e, me));
                case GamePhase.AwaitBuild: return GameAction.Decide(me, WantBuild(e, me));
                case GamePhase.AwaitCoinBet: return GameAction.Bet(me, ChooseCoinStake(e, me));
                case GamePhase.Ipo:
                case GamePhase.AwaitTrade:
                    return DecideTrade(e, me);
            }
            return GameAction.EndTurn(me);
        }

        // ------------------------------------------------------------------
        // 공통 판단 재료
        // ------------------------------------------------------------------

        /// <summary>0 = 선두, 1 = 크게 뒤처짐. 후반일수록 크게 반영.</summary>
        private double Behind(GameState s, int me)
        {
            long mine = Valuation.Total(s, s.Players[me]);
            long best = mine;
            foreach (var p in s.Players) best = Math.Max(best, Valuation.Total(s, p));
            if (best <= 0) return 0;
            return Math.Max(0, Math.Min(1, (double)(best - mine) / best));
        }

        private double Lateness(GameState s)
        {
            return s.Config.Rounds <= 0 ? 0 : Math.Min(1.0, (double)s.Round / s.Config.Rounds);
        }

        private bool IsLeader(GameState s, int me)
        {
            return Valuation.RankOf(s, me) == 1;
        }

        private double RiskAppetite(GameState s, int me)
        {
            double behind = Behind(s, me);
            double late = Lateness(s);
            double risk = Profile.RiskBase + behind * (0.4 + 0.8 * late);
            if (IsLeader(s, me) && late > 0.7) risk -= 0.15; // 막판 선두는 지키기
            return risk;
        }

        /// <summary>남의 땅 통행료 최댓값 기준으로 현금 여유를 잡는다.</summary>
        private long Reserve(GameEngine e, int me)
        {
            var s = e.State;
            long maxToll = 0;
            foreach (var land in s.Lands)
            {
                if (land == null || land.Owner < 0 || land.Owner == me) continue;
                maxToll = Math.Max(maxToll, e.Toll(land.TileIndex));
            }
            long baseReserve = (long)(s.Config.StartingCash * Profile.ReserveRate);
            // 남은 라운드가 적으면 여유를 줄인다 (막판 현금은 쓸모가 적다)
            if (s.RoundsLeft <= 1) baseReserve /= 2;
            return Math.Max(baseReserve, (long)(maxToll * (Style == AiStyle.Landlord ? 0.5 : 0.8)));
        }

        // ------------------------------------------------------------------
        // 주사위
        // ------------------------------------------------------------------

        private GameAction DecideRoll(GameEngine e, int me)
        {
            var s = e.State;
            var p = s.Players[me];
            if (p.Items.Contains(ItemType.DiceControl))
            {
                int best;
                double gain = DiceControlGain(e, me, out best);
                if (gain > s.Config.StartingCash * 0.08) return GameAction.UseItem(me, ItemType.DiceControl, -1, best);
            }
            return GameAction.Roll(me);
        }

        /// <summary>목적지 칸의 대략적인 가치 (원 단위).</summary>
        private double TileValue(GameEngine e, int me, int tileIndex, bool passStart)
        {
            var s = e.State;
            var tile = s.Tile(tileIndex);
            var p = s.Players[me];
            double v = passStart ? s.Config.Salary : 0;
            switch (tile.Type)
            {
                case TileType.Land:
                    var land = s.Lands[tileIndex];
                    if (land.Owner < 0) v += p.Cash >= tile.LandPrice ? tile.LandPrice * 0.4 * Profile.LandAppetite : 0;
                    else if (land.Owner == me) v += land.Level < s.Config.MaxBuildingLevel ? e.BuildCost(tileIndex) * 0.4 : 0;
                    else v -= e.Toll(tileIndex) * (p.Items.Contains(ItemType.TollWaiver) ? 0.2 : 1.0);
                    break;
                case TileType.Company:
                    v += s.Companies[tile.CompanyId].Listed ? 0 : 300;
                    break;
                case TileType.Item:
                    v += 500;
                    break;
                case TileType.Vacation:
                    v -= 300;
                    break;
                case TileType.TradeHalt:
                    v -= 600;
                    break;
                case TileType.CoinExchange:
                    v += 100 * Profile.CoinAppetite;
                    break;
            }
            return v;
        }

        private double DiceControlGain(GameEngine e, int me, out int bestTotal)
        {
            var s = e.State;
            int n = s.Board.TileCount;
            int pos = s.Players[me].Position;
            double expected = 0;
            double best = double.MinValue;
            bestTotal = 7;
            int sides = s.Config.DiceSides;
            for (int total = 2; total <= sides * 2; total++)
            {
                int ways = Math.Min(total - 1, 2 * sides + 1 - total);
                double prob = ways / (double)(sides * sides);
                int dest = (pos + total) % n;
                bool pass = pos + total >= n;
                double v = TileValue(e, me, dest, pass);
                expected += prob * v;
                if (v > best) { best = v; bestTotal = total; }
            }
            return best - expected;
        }

        // ------------------------------------------------------------------
        // 땅 / 건물 / 코인
        // ------------------------------------------------------------------

        private bool WantLand(GameEngine e, int me)
        {
            var s = e.State;
            long price = e.LandPrice(s.PendingTile);
            long after = s.Players[me].Cash - price;
            if (after < Reserve(e, me) * (s.RoundsLeft <= 2 ? 0.3 : 1.0)) return false;
            // 땅은 총자산에 매입가로 잡히니 막판에는 사도 손해가 없다.
            double appetite = Profile.LandAppetite + (s.RoundsLeft <= 2 ? 0.3 : 0);
            return _rng.Chance(Math.Min(1.0, appetite));
        }

        private bool WantBuild(GameEngine e, int me)
        {
            var s = e.State;
            long cost = e.BuildCost(s.PendingTile);
            long after = s.Players[me].Cash - cost;
            if (after < Reserve(e, me)) return false;
            return _rng.Chance(Profile.BuildAppetite);
        }

        private int ChooseCoinStake(GameEngine e, int me)
        {
            var s = e.State;
            double appetite = Profile.CoinAppetite + RiskAppetite(s, me) * 0.8;
            if (IsLeader(s, me) && Lateness(s) > 0.7) appetite -= 0.3;
            if (_rng.NextDouble() > appetite) return -1;
            int maxIndex = s.Config.CoinStakeRates.Length - 1;
            int idx = (int)Math.Floor(appetite * (maxIndex + 1) + _rng.Range(-0.5, 0.5));
            return Math.Max(0, Math.Min(maxIndex, idx));
        }

        // ------------------------------------------------------------------
        // 턴 종료 매매
        // ------------------------------------------------------------------

        private GameAction DecideTrade(GameEngine e, int me)
        {
            var s = e.State;
            var p = s.Players[me];
            int key = s.Round * 1000 + me * 10 + (s.Phase == GamePhase.Ipo ? 1 : 0) + p.TurnsTaken * 100000;
            if (key != _turnKey)
            {
                _turnKey = key;
                _actionsThisPhase = 0;
                long free = Math.Max(0, p.Cash - (s.Phase == GamePhase.Ipo ? (long)(s.Config.StartingCash * 0.25) : Reserve(e, me)));
                double share = s.Phase == GamePhase.Ipo ? Profile.IpoBudget : Profile.StockBudget;
                share += RiskAppetite(s, me) * 0.3;
                _budgetLeft = (long)(free * Math.Max(0.05, Math.Min(1.0, share)));
            }
            if (_insiderRound != s.Round)
            {
                _insiderRound = s.Round;
                _insiderCompanies.Clear();
            }

            if (++_actionsThisPhase > 16) return GameAction.EndTurn(me);

            GameAction a = PickItemUse(e, me);
            if (a == null && p.CanTrade && s.Phase == GamePhase.AwaitTrade) a = PickSell(e, me);
            if (a == null && s.Phase == GamePhase.AwaitTrade) a = PickTurnEndBuild(e, me);
            if (a == null && p.CanTrade) a = PickBuy(e, me);
            if (a != null && e.Validate(a) == null) return a;
            return GameAction.EndTurn(me);
        }

        private int KnownDirection(GameState s, int companyId)
        {
            return _insiderCompanies.Contains(companyId) ? s.Companies[companyId].ForcedDirection : 0;
        }

        /// <summary>한 라운드 기대 수익률 (주가 변동률 기준).</summary>
        private double ExpectedReturn(GameState s, StockOdds odds, int knownDirection)
        {
            double meanMove = odds.Volatility * (s.Config.MinMoveFactor + 1.0) * 0.5;
            if (knownDirection > 0) return meanMove;
            if (knownDirection < 0) return -meanMove;
            return (2 * odds.UpChance - 1) * meanMove;
        }

        private GameAction PickSell(GameEngine e, int me)
        {
            var s = e.State;
            var p = s.Players[me];
            long reserve = Reserve(e, me);
            bool leaderLate = IsLeader(s, me) && Lateness(s) > 0.85;

            for (int c = 0; c < s.Companies.Length; c++)
            {
                var company = s.Companies[c];
                if (!company.Listed) continue;
                var h = p.Holdings[c];
                if (h.UnlockedShares <= 0) continue;

                double gain = h.AveragePrice > 0 ? company.Price / h.AveragePrice - 1.0 : 0;
                int known = KnownDirection(s, c);
                var odds = StockMarket.GetOdds(s, c);
                double er = ExpectedReturn(s, odds, known);

                // 상장폐지 위험
                if (company.Price < company.ListingPrice * 0.25) return GameAction.Sell(me, c, h.UnlockedShares);
                // 내부자 정보로 하락이 확실
                if (known < 0) return GameAction.Sell(me, c, h.UnlockedShares);
                // 막판 선두: 기대수익이 음수인 위험주는 정리
                if (leaderLate && er < -0.01) return GameAction.Sell(me, c, h.UnlockedShares);

                switch (Style)
                {
                    case AiStyle.Hodler:
                        break;
                    case AiStyle.Trader:
                        if (known > 0) break;
                        if (Profile.TakeProfit > 0 && gain >= Profile.TakeProfit) return GameAction.Sell(me, c, h.UnlockedShares);
                        if (Profile.StopLoss > 0 && gain <= -Profile.StopLoss) return GameAction.Sell(me, c, h.UnlockedShares);
                        if (er < -0.02) return GameAction.Sell(me, c, h.UnlockedShares);
                        break;
                    case AiStyle.Landlord:
                        if (Profile.TakeProfit > 0 && gain >= Profile.TakeProfit) return GameAction.Sell(me, c, h.UnlockedShares);
                        break;
                    case AiStyle.Ant:
                        if (known > 0) break;
                        if (company.LastChangeRate <= -0.08 && _rng.Chance(Profile.PanicSell)) return GameAction.Sell(me, c, h.UnlockedShares);
                        if (gain >= Profile.TakeProfit) return GameAction.Sell(me, c, Math.Max(1, h.UnlockedShares / 2));
                        break;
                }
            }

            // 현금이 너무 적으면 기대수익 낮은 종목부터 일부 정리 (땅부자형은 건설 자금 마련)
            if (p.Cash < reserve / 2)
            {
                int worst = -1;
                double worstEr = double.MaxValue;
                for (int c = 0; c < s.Companies.Length; c++)
                {
                    if (!s.Companies[c].Listed || p.Holdings[c].UnlockedShares <= 0) continue;
                    double er = ExpectedReturn(s, StockMarket.GetOdds(s, c), KnownDirection(s, c));
                    if (er < worstEr) { worstEr = er; worst = c; }
                }
                if (worst >= 0)
                {
                    long price = s.Companies[worst].Price;
                    int qty = (int)Math.Min(p.Holdings[worst].UnlockedShares, (reserve - p.Cash + price - 1) / price);
                    if (qty > 0) return GameAction.Sell(me, worst, qty);
                }
            }
            return null;
        }

        /// <summary>선택 규칙: 턴 종료 건설. 통행료 증가분 대비 비용이 좋은 땅부터.</summary>
        private GameAction PickTurnEndBuild(GameEngine e, int me)
        {
            var s = e.State;
            if (!s.Config.BuildAtTurnEnd || s.TurnEndBuilds >= s.Config.MaxTurnEndBuilds) return null;
            var p = s.Players[me];
            long reserve = Reserve(e, me);
            int best = -1;
            double bestValue = 0;
            foreach (var land in s.Lands)
            {
                if (land == null || land.Owner != me || land.Level >= s.Config.MaxBuildingLevel) continue;
                long cost = e.BuildCost(land.TileIndex);
                if (p.Cash - cost < reserve) continue;
                double gain = e.TollAtLevel(land.TileIndex, land.Level + 1) - e.Toll(land.TileIndex);
                double value = gain / Math.Max(1, cost);
                if (value > bestValue) { bestValue = value; best = land.TileIndex; }
            }
            if (best < 0) return null;
            double appetite = Style == AiStyle.Landlord ? 1.0 : Profile.BuildAppetite * 0.6;
            return _rng.Chance(appetite) ? GameAction.Build(me, best) : null;
        }

        private double BuyScore(GameEngine e, int me, int c)
        {
            var s = e.State;
            var p = s.Players[me];
            var company = s.Companies[c];
            int investorsNow = StockMarket.CountInvestors(s, c);
            bool amInvestor = p.Holdings[c].TotalShares * company.Price >= s.Config.InvestorThreshold;
            int investorsAfter = amInvestor ? investorsNow : investorsNow + 1;
            var odds = StockMarket.GetOdds(s, c, investorsAfter);
            int known = KnownDirection(s, c);
            if (known < 0) return double.MinValue;

            double er = ExpectedReturn(s, odds, known);
            double risk = RiskAppetite(s, me);
            double ratioAfter = odds.InvestorRatio;

            double score = er;
            score += risk * odds.Volatility * 0.6;                 // 변동성 선호
            score += risk * (1.0 - ratioAfter) * 0.05;             // 혼자 가진 주식이 올라야 역전
            score += Profile.Stability * ratioAfter;                 // 안정주 선호
            score += Profile.Momentum * company.LastChangeRate * 0.3;
            score += Profile.Herd * investorsNow;
            if (known > 0) score += 0.2;
            if (company.Price < company.ListingPrice * 0.3) score -= 0.1; // 상장폐지 위험
            // 보호예수 때문에 마지막 라운드에 산 주식은 못 판다. 그래도 총자산엔 잡히니 기대값만 본다.
            score += _rng.Range(-Profile.Noise, Profile.Noise);
            return score;
        }

        private GameAction PickBuy(GameEngine e, int me)
        {
            var s = e.State;
            var p = s.Players[me];
            if (_budgetLeft <= 0) return null;

            int best = -1;
            double bestScore = Profile.MinScore;
            for (int c = 0; c < s.Companies.Length; c++)
            {
                if (!s.Companies[c].Listed) continue;
                double score = BuyScore(e, me, c);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (best < 0) { _budgetLeft = 0; return null; }

            long price = s.Companies[best].Price;
            long spend = Math.Min(_budgetLeft, p.Cash);
            // 존버형/땅부자형은 분산 투자: 한 종목에 예산의 60%까지
            if ((Style == AiStyle.Hodler || Style == AiStyle.Landlord) && _budgetLeft > s.Config.StartingCash * 0.15)
                spend = (long)(spend * 0.6);
            int qty = (int)(spend / price);
            // 투자자로 인정받을 만큼은 산다
            int minShares = StockMarket.SharesForInvestor(s, best);
            int held = p.Holdings[best].TotalShares;
            if (held + qty < minShares && (minShares - held) * price <= p.Cash) qty = Math.Max(qty, minShares - held);
            if (qty <= 0) { _budgetLeft = 0; return null; }
            _budgetLeft -= qty * price;
            return GameAction.Buy(me, best, qty);
        }

        // ------------------------------------------------------------------
        // 아이템
        // ------------------------------------------------------------------

        private GameAction PickItemUse(GameEngine e, int me)
        {
            var s = e.State;
            var p = s.Players[me];
            if (s.Phase != GamePhase.AwaitTrade) return null;
            var usable = e.UsableItems(me);
            if (usable.Count == 0) return null;

            foreach (var item in usable)
            {
                switch (item)
                {
                    case ItemType.Short:
                    {
                        int target = -1;
                        double lowest = 1.0;
                        for (int c = 0; c < s.Companies.Length; c++)
                        {
                            if (!s.Companies[c].Listed || p.Holdings[c].TotalShares > 0) continue;
                            bool already = false;
                            foreach (var sp in s.Shorts) if (sp.PlayerId == me && sp.CompanyId == c) already = true;
                            if (already) continue;
                            double up = StockMarket.GetOdds(s, c).UpChance;
                            if (up < lowest) { lowest = up; target = c; }
                        }
                        if (target >= 0 && lowest <= 0.5) return GameAction.UseItem(me, item, target);
                        break;
                    }
                    case ItemType.InsiderInfo:
                    {
                        int target = LargestHolding(s, me);
                        if (target < 0) target = BestBuyCandidate(e, me);
                        if (target >= 0 && !_insiderCompanies.Contains(target))
                        {
                            _insiderCompanies.Add(target);
                            return GameAction.UseItem(me, item, target);
                        }
                        break;
                    }
                    case ItemType.Rumor:
                    {
                        int mine = LargestHolding(s, me);
                        if (mine >= 0) return GameAction.UseItem(me, item, mine, 1);
                        break;
                    }
                    case ItemType.LockupRelease:
                    {
                        foreach (var h in p.Holdings)
                        {
                            if (h.LockedShares <= 0 || !s.Companies[h.CompanyId].Listed) continue;
                            var company = s.Companies[h.CompanyId];
                            double gain = h.AveragePrice > 0 ? company.Price / h.AveragePrice - 1 : 0;
                            if (gain >= 0.2 || company.Price < company.ListingPrice * 0.4) return GameAction.UseItem(me, item);
                        }
                        break;
                    }
                    case ItemType.TaxAudit:
                    {
                        int target = e.TaxTarget(me);
                        if (target >= 0 && s.Players[target].Cash >= s.Config.StartingCash * 0.3) return GameAction.UseItem(me, item);
                        break;
                    }
                }
            }
            return null;
        }

        private static int LargestHolding(GameState s, int me)
        {
            int best = -1;
            long bestValue = 0;
            var p = s.Players[me];
            for (int c = 0; c < s.Companies.Length; c++)
            {
                if (!s.Companies[c].Listed) continue;
                long v = p.Holdings[c].TotalShares * s.Companies[c].Price;
                if (v > bestValue) { bestValue = v; best = c; }
            }
            return best;
        }

        private int BestBuyCandidate(GameEngine e, int me)
        {
            var s = e.State;
            int best = -1;
            double bestScore = double.MinValue;
            for (int c = 0; c < s.Companies.Length; c++)
            {
                if (!s.Companies[c].Listed) continue;
                double score = BuyScore(e, me, c);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}
