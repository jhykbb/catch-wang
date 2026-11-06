using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    public struct StockOdds
    {
        public double UpChance;
        public double Volatility;
        public int Investors;
        public double InvestorRatio;
    }

    /// <summary>주가 계산. 상태를 바꾸는 함수와 조회 함수를 모두 여기 둔다.</summary>
    public static class StockMarket
    {
        /// <summary>
        /// 🟡 투자자 수 = 보유 평가액이 기준(시작 자금의 10%) 이상인 플레이어 수.
        /// </summary>
        public static int CountInvestors(GameState s, int companyId)
        {
            var company = s.Companies[companyId];
            long threshold = s.Config.InvestorThreshold;
            int count = 0;
            for (int i = 0; i < s.Players.Length; i++)
            {
                int shares = s.Players[i].Holdings[companyId].TotalShares;
                if (shares > 0 && shares * company.Price >= threshold) count++;
            }
            return count;
        }

        /// <summary>투자자로 인정받는 데 필요한 최소 주식 수.</summary>
        public static int SharesForInvestor(GameState s, int companyId)
        {
            long price = Math.Max(1, s.Companies[companyId].Price);
            long threshold = s.Config.InvestorThreshold;
            return (int)Math.Max(1, (threshold + price - 1) / price);
        }

        /// <summary>표를 투자자 비율로 선형 보간한다. 🟡 2~3인 (그리고 5~6인)에도 그대로 적용.</summary>
        public static void Interpolate(List<StockOddsPoint> table, double ratio, out double up, out double vol)
        {
            if (ratio <= table[0].InvestorRatio)
            {
                up = table[0].UpChance;
                vol = table[0].Volatility;
                return;
            }
            for (int i = 1; i < table.Count; i++)
            {
                var a = table[i - 1];
                var b = table[i];
                if (ratio <= b.InvestorRatio)
                {
                    double t = (ratio - a.InvestorRatio) / Math.Max(1e-9, b.InvestorRatio - a.InvestorRatio);
                    up = a.UpChance + (b.UpChance - a.UpChance) * t;
                    vol = a.Volatility + (b.Volatility - a.Volatility) * t;
                    return;
                }
            }
            var last = table[table.Count - 1];
            up = last.UpChance;
            vol = last.Volatility;
        }

        /// <summary>
        /// 다음 장 마감의 상승 확률과 (최대) 변동폭. investorsOverride 로 "내가 사면 어떻게 되나"를 계산할 수 있다.
        /// </summary>
        public static StockOdds GetOdds(GameState s, int companyId, int investorsOverride = -1, bool includeRumor = true)
        {
            var cfg = s.Config;
            int investors = investorsOverride >= 0 ? investorsOverride : CountInvestors(s, companyId);
            double ratio = s.PlayerCount > 0 ? (double)investors / s.PlayerCount : 0;
            double up, vol;
            Interpolate(cfg.OddsTable, ratio, out up, out vol);

            var def = s.CompanyDef(companyId);
            int sector = (int)def.Sector;
            if (cfg.SectorVolatility != null && sector < cfg.SectorVolatility.Length) vol *= cfg.SectorVolatility[sector];
            if (s.IsFinalStretch) vol *= cfg.FinalStretchMultiplier;
            if (includeRumor) up += s.Companies[companyId].RumorBoost;
            up = Math.Max(0.05, Math.Min(0.95, up));

            return new StockOdds { UpChance = up, Volatility = vol, Investors = investors, InvestorRatio = ratio };
        }

        public static long RoundToTick(double price, int tick)
        {
            if (tick <= 1) return Math.Max(1, (long)Math.Round(price));
            long rounded = (long)Math.Round(price / tick) * tick;
            return Math.Max(tick, rounded);
        }

        /// <summary>
        /// 장 마감. 모든 상장 회사의 주가를 바꾸고 공매도 정산, 상장폐지를 처리한다.
        /// </summary>
        public static void CloseMarket(GameState s, Action<GameEvent> emit)
        {
            var cfg = s.Config;
            var rng = s.Rng;
            var closed = new MarketClosedEvent { Round = s.Round };

            // 1) 뉴스 (🟡)
            NewsEvent news = null;
            if (rng.Chance(cfg.NewsChance))
            {
                news = new NewsEvent();
                if (rng.Chance(cfg.NewsMarketWideShare))
                {
                    bool rally = rng.Chance(0.5);
                    news.Kind = rally ? NewsKind.MarketRally : NewsKind.MarketCrash;
                    news.Effect = rally ? cfg.NewsMarketWideEffect : -cfg.NewsMarketWideEffect;
                }
                else
                {
                    bool boom = rng.Chance(0.5);
                    news.Kind = boom ? NewsKind.SectorBoom : NewsKind.SectorBust;
                    var sectors = (Sector[])Enum.GetValues(typeof(Sector));
                    news.Sector = sectors[rng.Range(0, sectors.Length)];
                    double mag = rng.Range(cfg.NewsSectorMin, cfg.NewsSectorMax);
                    news.Effect = boom ? mag : -mag;
                }
                emit(news);
            }

            // 2) 회사별 변동
            for (int c = 0; c < s.Companies.Length; c++)
            {
                var company = s.Companies[c];
                if (!company.Listed) continue;

                var odds = GetOdds(s, c);
                bool up;
                if (company.ForcedDirection != 0) up = company.ForcedDirection > 0;
                else up = rng.Chance(odds.UpChance);

                double magnitude = odds.Volatility * rng.Range(cfg.MinMoveFactor, 1.0);
                double rate = up ? magnitude : -magnitude;

                if (news != null)
                {
                    bool applies = news.Kind == NewsKind.MarketCrash || news.Kind == NewsKind.MarketRally
                                   || s.CompanyDef(c).Sector == news.Sector;
                    if (applies)
                    {
                        rate += news.Effect;
                        // 내부자 정보로 정해진 방향은 뉴스가 있어도 지켜진다.
                        if (company.ForcedDirection > 0 && rate <= 0) rate = 0.01;
                        if (company.ForcedDirection < 0 && rate >= 0) rate = -0.01;
                    }
                }

                bool limitUp = false, limitDown = false;
                if (rate >= cfg.PriceLimit) { rate = cfg.PriceLimit; limitUp = true; }
                if (rate <= -cfg.PriceLimit) { rate = -cfg.PriceLimit; limitDown = true; }

                long oldPrice = company.Price;
                long newPrice = RoundToTick(oldPrice * (1.0 + rate), cfg.PriceTick);
                if (newPrice == oldPrice && rate != 0)
                {
                    // 저가주가 틱 때문에 멈추지 않도록 최소 한 틱은 움직인다.
                    newPrice = Math.Max(cfg.PriceTick, oldPrice + (rate > 0 ? cfg.PriceTick : -cfg.PriceTick));
                }

                company.PreviousPrice = oldPrice;
                company.Price = newPrice;
                company.LastChangeRate = oldPrice > 0 ? (double)(newPrice - oldPrice) / oldPrice : 0;
                company.PriceHistory.Add(newPrice);

                var change = new PriceChange
                {
                    CompanyId = c,
                    OldPrice = oldPrice,
                    NewPrice = newPrice,
                    Rate = company.LastChangeRate,
                    Investors = odds.Investors,
                    UpChance = odds.UpChance,
                    LimitUp = limitUp,
                    LimitDown = limitDown,
                };

                if (newPrice < company.ListingPrice * cfg.DelistRatio) change.Delisted = true;
                closed.Changes.Add(change);
            }

            emit(closed);

            // 3) 공매도 정산
            for (int i = 0; i < s.Shorts.Count; i++)
            {
                var sp = s.Shorts[i];
                bool success = false;
                foreach (var ch in closed.Changes)
                {
                    if (ch.CompanyId == sp.CompanyId) { success = ch.NewPrice < ch.OldPrice || ch.Delisted; break; }
                }
                long payout = success ? (long)Math.Round(cfg.StartingCash * cfg.ShortPayoutRate) : 0;
                var p = s.Players[sp.PlayerId];
                p.Cash += payout;
                p.Stats.ItemCash += payout;
                emit(new ShortSettledEvent { PlayerId = sp.PlayerId, CompanyId = sp.CompanyId, Success = success, Payout = payout });
            }
            s.Shorts.Clear();

            // 4) 상장폐지
            foreach (var ch in closed.Changes)
            {
                if (!ch.Delisted) continue;
                Delist(s, ch.CompanyId, emit);
            }

            // 5) 한 번만 적용되는 효과 초기화
            for (int c = 0; c < s.Companies.Length; c++)
            {
                s.Companies[c].RumorBoost = 0;
                s.Companies[c].ForcedDirection = 0;
            }
        }

        public static void Delist(GameState s, int companyId, Action<GameEvent> emit)
        {
            var company = s.Companies[companyId];
            long last = company.Price;
            for (int i = 0; i < s.Players.Length; i++) s.Players[i].Holdings[companyId].Clear();
            company.Listed = false;
            company.TimesDelisted++;
            company.Price = company.ListingPrice;
            company.PreviousPrice = company.ListingPrice;
            company.LastChangeRate = 0;
            company.RumorBoost = 0;
            company.ForcedDirection = 0;
            emit(new DelistedEvent { CompanyId = companyId, LastPrice = last });
        }

        /// <summary>회사 상장. 신규 상장가는 회사 정의의 기본가.</summary>
        public static void List(GameState s, int companyId, int byPlayer, Action<GameEvent> emit)
        {
            var company = s.Companies[companyId];
            var def = s.CompanyDef(companyId);
            company.Listed = true;
            company.ListingPrice = def.ListingPrice;
            company.Price = def.ListingPrice;
            company.PreviousPrice = def.ListingPrice;
            company.LastChangeRate = 0;
            company.ListedRound = s.Round;
            company.PriceHistory.Clear();
            company.PriceHistory.Add(company.Price);
            emit(new CompanyListedEvent { CompanyId = companyId, Price = company.Price, ByPlayer = byPlayer });
        }
    }

    public static class Valuation
    {
        public static long StockValue(GameState s, PlayerState p)
        {
            long sum = 0;
            for (int c = 0; c < s.Companies.Length; c++)
            {
                var company = s.Companies[c];
                if (!company.Listed) continue;
                sum += p.Holdings[c].TotalShares * company.Price;
            }
            return sum;
        }

        public static long LandValue(GameState s, PlayerState p)
        {
            long sum = 0;
            for (int i = 0; i < s.Lands.Length; i++)
            {
                var land = s.Lands[i];
                if (land != null && land.Owner == p.Id) sum += land.Invested;
            }
            return sum;
        }

        /// <summary>🟡 총자산 = 현금 + 주식 평가액(잠긴 주식 포함) + 땅/건물(매입가) - 구제금융 부채</summary>
        public static AssetBreakdown Breakdown(GameState s, PlayerState p)
        {
            return new AssetBreakdown
            {
                PlayerId = p.Id,
                Cash = p.Cash,
                Stocks = StockValue(s, p),
                Land = LandValue(s, p),
                Debt = p.Debt,
            };
        }

        public static long Total(GameState s, PlayerState p)
        {
            return Breakdown(s, p).Total;
        }

        /// <summary>🟡 순위: 총자산 → 현금 → 자리 순서.</summary>
        public static List<AssetBreakdown> Rankings(GameState s)
        {
            var list = new List<AssetBreakdown>();
            foreach (var p in s.Players) list.Add(Breakdown(s, p));
            list.Sort((a, b) =>
            {
                int cmp = b.Total.CompareTo(a.Total);
                if (cmp != 0) return cmp;
                cmp = b.Cash.CompareTo(a.Cash);
                if (cmp != 0) return cmp;
                return a.PlayerId.CompareTo(b.PlayerId);
            });
            for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
            return list;
        }

        public static int RankOf(GameState s, int playerId)
        {
            foreach (var r in Rankings(s)) if (r.PlayerId == playerId) return r.Rank;
            return s.PlayerCount;
        }
    }
}
