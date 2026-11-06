using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using StockDice.Core;

namespace StockDice.Simulator
{
    /// <summary>
    /// 밸런스 시뮬레이터: AI끼리 수천 판을 돌려 평균 게임 시간, 땅/주식 유불리, 역전 빈도 등을 본다.
    ///
    /// 사용법:
    ///   dotnet run -c Release -- [--games 2000] [--seed 1] [--rounds 20] [--cash 10000] [--players 2,3,4] [--out report.md]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;

            var opt = Options.Parse(args);
            var sw = Stopwatch.StartNew();
            var report = new StringBuilder();

            report.AppendLine("# 밸런스 시뮬레이션 결과");
            report.AppendLine();
            report.AppendLine(string.Format("- 판 수: 인원별 {0}판 (혼합 성향) + 4성향 대결 {0}판", opt.Games));
            report.AppendLine(string.Format("- 설정: 시작 자금 {0}, {1}라운드, 시드 {2}", Fmt.Won(opt.Cash), opt.Rounds, opt.Seed));
            report.AppendLine("- AI 성향은 매 판 자리마다 무작위 (혼합), 또는 4성향을 한 명씩 (대결)");
            report.AppendLine("- 규칙 숫자는 `GameConfig.cs` 기본값 (RULES.md v0.2) 그대로");
            report.AppendLine(string.Format("- 다시 만들기: `cd tools/Simulator && dotnet run -c Release -- --games {0} --seed {1} --out ../../game-design/SIMULATION.md`", opt.Games, opt.Seed));
            report.AppendLine("- 주의: AI의 판단 수준에 따라 결과가 달라진다. 숫자는 \"규칙끼리 비교\"용으로 본다.");
            report.AppendLine();

            var allStats = new List<GameStats>();
            foreach (int players in opt.PlayerCounts)
            {
                var stats = RunBatch(opt, players, opt.Games, mixed: true);
                allStats.AddRange(stats);
                WriteSection(report, string.Format("{0}인 (혼합 성향)", players), stats, opt);
            }

            if (GameConfig.MaxPlayers >= 4)
            {
                var duel = RunBatch(opt, 4, opt.Games, mixed: false);
                WriteSection(report, "4인 성향 대결 (존버/단타/땅부자/개미 한 명씩, 자리 무작위)", duel, opt);
            }

            report.AppendLine(string.Format("_시뮬레이션 소요 시간: {0:0.0}초_", sw.Elapsed.TotalSeconds));

            string text = report.ToString();
            Console.WriteLine(text);
            if (!string.IsNullOrEmpty(opt.OutPath))
            {
                System.IO.File.WriteAllText(opt.OutPath, text, new UTF8Encoding(false));
                Console.WriteLine("저장: " + opt.OutPath);
            }
            return 0;
        }

        // ------------------------------------------------------------------

        private static readonly AiStyle[] Styles = { AiStyle.Hodler, AiStyle.Trader, AiStyle.Landlord, AiStyle.Ant };

        private static List<GameStats> RunBatch(Options opt, int players, int games, bool mixed)
        {
            var list = new List<GameStats>(games);
            var seatRng = new Rng(opt.Seed * 31UL + (ulong)players * 1000003UL + (mixed ? 0UL : 77UL));
            for (int g = 0; g < games; g++)
            {
                ulong seed = opt.Seed * 1000003UL + (ulong)g * 7919UL + (ulong)players * 13UL + (mixed ? 0UL : 999983UL);
                var setups = new List<PlayerSetup>();
                if (mixed)
                {
                    for (int i = 0; i < players; i++)
                    {
                        var st = Styles[seatRng.Range(0, Styles.Length)];
                        setups.Add(new PlayerSetup(PlayerSetup.StyleName(st) + (i + 1), PlayerKind.Ai, st));
                    }
                }
                else
                {
                    var order = new List<AiStyle>(Styles);
                    seatRng.Shuffle(order);
                    for (int i = 0; i < players; i++)
                        setups.Add(new PlayerSetup(PlayerSetup.StyleName(order[i]) + (i + 1), PlayerKind.Ai, order[i]));
                }

                var cfg = new GameConfig { Seed = seed, Rounds = opt.Rounds, StartingCash = opt.Cash };
                opt.Tweak?.Invoke(cfg);
                var engine = GameEngine.Create(cfg, setups);
                var agents = GameRunner.CreateAiAgents(setups, seed);
                var gs = new GameStats { Players = players };
                GameRunner.RunToEnd(engine, agents, evs => gs.Observe(engine.State, evs));
                gs.Finish(engine.State);
                list.Add(gs);
            }
            return list;
        }

        // ------------------------------------------------------------------
        // 리포트
        // ------------------------------------------------------------------

        private static void WriteSection(StringBuilder r, string title, List<GameStats> stats, Options opt)
        {
            int n = stats.Count;
            r.AppendLine("## " + title);
            r.AppendLine();

            // 1. 게임 시간
            double turns = stats.Average(s => s.Turns);
            double rolls = stats.Average(s => s.Rolls);
            double decisions = stats.Average(s => s.Decisions);
            double estHuman = stats.Average(s => s.EstimatedMinutes(true));
            double estAi = stats.Average(s => s.EstimatedMinutes(false));
            r.AppendLine("### 게임 시간");
            r.AppendLine();
            r.AppendLine("| 항목 | 평균 |");
            r.AppendLine("| --- | --- |");
            r.AppendLine(string.Format("| 턴 수 (전체) | {0:0.0} |", turns));
            r.AppendLine(string.Format("| 주사위 굴림 | {0:0.0} |", rolls));
            r.AppendLine(string.Format("| 땅/건물/코인 결정 | {0:0.0} |", decisions));
            r.AppendLine(string.Format("| 한 사람당 바퀴 수 | {0:0.0} |", stats.Average(s => s.AvgLaps)));
            r.AppendLine(string.Format("| 예상 플레이 시간 (전원 사람) | **{0:0}분** |", estHuman));
            r.AppendLine(string.Format("| 예상 플레이 시간 (사람 1 + AI) | {0:0}분 |", estAi));
            r.AppendLine();

            // 2. 성향별 성적
            r.AppendLine("### 성향별 성적");
            r.AppendLine();
            r.AppendLine("| 성향 | 참가 | 승률 | 평균 순위 | 평균 총자산 | 현금 | 주식 | 땅 | 구제금융/판 |");
            r.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var st in Styles)
            {
                var rows = stats.SelectMany(s => s.Results).Where(x => x.Style == st).ToList();
                if (rows.Count == 0) continue;
                double fair = 1.0 / stats[0].Players;
                r.AppendLine(string.Format("| {0} | {1} | {2:0.0}% (기준 {3:0}%) | {4:0.00} | {5} | {6:0}% | {7:0}% | {8:0}% | {9:0.00} |",
                    PlayerSetup.StyleName(st), rows.Count, 100.0 * rows.Count(x => x.Rank == 1) / rows.Count, fair * 100,
                    rows.Average(x => x.Rank), Fmt.Won((long)rows.Average(x => x.Total)),
                    100 * rows.Average(x => x.CashShare), 100 * rows.Average(x => x.StockShare), 100 * rows.Average(x => x.LandShare),
                    rows.Average(x => x.Bailouts)));
            }
            r.AppendLine();

            // 3. 땅 vs 주식
            var all = stats.SelectMany(s => s.Results).ToList();
            var winners = all.Where(x => x.Rank == 1).ToList();
            var others = all.Where(x => x.Rank != 1).ToList();
            r.AppendLine("### 땅 vs 주식");
            r.AppendLine();
            r.AppendLine("| 그룹 | 현금 비중 | 주식 비중 | 땅 비중 | 통행료 수입 | 주식 순매매 | 월급 |");
            r.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            WriteGroup(r, "우승자", winners);
            WriteGroup(r, "나머지", others);
            r.AppendLine();
            double corrLand = Correlation(all.Select(x => x.LandShare).ToList(), all.Select(x => x.Rank == 1 ? 1.0 : 0.0).ToList());
            double corrStock = Correlation(all.Select(x => x.StockShare).ToList(), all.Select(x => x.Rank == 1 ? 1.0 : 0.0).ToList());
            var landHeavy = all.Where(x => x.LandShare >= 0.5).ToList();
            var stockHeavy = all.Where(x => x.StockShare >= 0.5).ToList();
            r.AppendLine(string.Format("- 땅 비중 ↔ 우승 상관계수: **{0:+0.00;-0.00}**, 주식 비중 ↔ 우승 상관계수: **{1:+0.00;-0.00}**", corrLand, corrStock));
            r.AppendLine(string.Format("- 땅 위주(자산의 50%↑) 플레이어 승률: {0} / 주식 위주(50%↑) 승률: {1} (기준 {2:0}%)",
                Rate(landHeavy), Rate(stockHeavy), 100.0 / stats[0].Players));
            r.AppendLine(string.Format("- 땅 한 칸이 게임 동안 걷은 평균 통행료: {0} (평균 땅값 2,500원, 건물 포함 평균 투자액 {1})",
                Fmt.Won((long)stats.Average(s => s.TollPerOwnedLand)), Fmt.Won((long)stats.Average(s => s.InvestedPerOwnedLand))));
            r.AppendLine();

            // 4. 역전
            r.AppendLine("### 역전");
            r.AppendLine();
            r.AppendLine(string.Format("- 절반 시점 1등이 최종 우승하지 못한 비율 (역전): **{0:0.0}%**", 100.0 * stats.Count(s => s.LeaderAtHalf != s.Winner) / n));
            r.AppendLine(string.Format("- 75% 시점 1등이 최종 우승하지 못한 비율: **{0:0.0}%**", 100.0 * stats.Count(s => s.LeaderAt75 != s.Winner) / n));
            r.AppendLine(string.Format("- 마지막 라운드 직전 1등이 뒤집힌 비율: {0:0.0}%", 100.0 * stats.Count(s => s.LeaderBeforeLast != s.Winner) / n));
            r.AppendLine(string.Format("- 절반 시점 꼴찌가 최종 우승한 비율: {0:0.0}%", 100.0 * stats.Count(s => s.LastAtHalf == s.Winner) / n));
            r.AppendLine(string.Format("- 판당 1등 교체 횟수 (라운드 기준): {0:0.0}", stats.Average(s => s.LeadChanges)));
            r.AppendLine(string.Format("- 최종 1등과 2등 격차 (1등 대비): {0:0.0}%", 100 * stats.Average(s => s.FinalMargin)));
            r.AppendLine();

            // 5. 경제
            r.AppendLine("### 경제");
            r.AppendLine();
            r.AppendLine(string.Format("- 최종 평균 총자산: {0} (시작 {1}의 {2:0.0}배)", Fmt.Won((long)stats.Average(s => s.AvgFinalTotal)), Fmt.Won(opt.Cash), stats.Average(s => s.AvgFinalTotal) / opt.Cash));
            r.AppendLine(string.Format("- 판당 신규 상장 {0:0.0}회, 상장폐지 {1:0.00}회, 상한가 {2:0.0}회, 하한가 {3:0.0}회",
                stats.Average(s => s.Listings), stats.Average(s => s.Delistings), stats.Average(s => s.LimitUps), stats.Average(s => s.LimitDowns)));
            r.AppendLine(string.Format("- 판당 구제금융 {0:0.00}회, 강제 처분 발생 {1:0.0}회", stats.Average(s => s.Bailouts), stats.Average(s => s.ForcedSales)));
            r.AppendLine(string.Format("- 마지막 상장 종목 평균 주가 (상장가 대비): {0:0.00}배", stats.Average(s => s.AvgPriceRatio)));
            r.AppendLine(string.Format("- 투자자 수별 평균 라운드 수익률: {0}", string.Join(", ",
                Enumerable.Range(0, stats[0].Players + 1).Select(k =>
                {
                    var moves = stats.SelectMany(s => s.MovesByInvestors.ContainsKey(k) ? s.MovesByInvestors[k] : new List<double>()).ToList();
                    return moves.Count == 0 ? string.Format("{0}명 -", k) : string.Format("{0}명 {1:+0.0;-0.0}% (n={2})", k, 100 * moves.Average(), moves.Count);
                }))));
            r.AppendLine(string.Format("- 자리별 승률: {0}", string.Join(", ",
                Enumerable.Range(0, stats[0].Players).Select(i => string.Format("{0}번 {1:0.0}%", i + 1, 100.0 * stats.Count(s => s.Winner == i) / n)))));
            r.AppendLine();
        }

        private static string Rate(List<PlayerResult> rows)
        {
            if (rows.Count == 0) return "- (해당 없음)";
            return string.Format("{0:0.0}% (n={1})", 100.0 * rows.Count(x => x.Rank == 1) / rows.Count, rows.Count);
        }

        private static void WriteGroup(StringBuilder r, string name, List<PlayerResult> rows)
        {
            if (rows.Count == 0) return;
            r.AppendLine(string.Format("| {0} | {1:0}% | {2:0}% | {3:0}% | {4} | {5} | {6} |", name,
                100 * rows.Average(x => x.CashShare), 100 * rows.Average(x => x.StockShare), 100 * rows.Average(x => x.LandShare),
                Fmt.Won((long)rows.Average(x => x.TollIncome)), Fmt.SignedWon((long)rows.Average(x => x.StockNet)), Fmt.Won((long)rows.Average(x => x.Salary))));
        }

        private static double Correlation(List<double> a, List<double> b)
        {
            int n = a.Count;
            if (n < 2) return 0;
            double ma = a.Average(), mb = b.Average();
            double cov = 0, va = 0, vb = 0;
            for (int i = 0; i < n; i++)
            {
                cov += (a[i] - ma) * (b[i] - mb);
                va += (a[i] - ma) * (a[i] - ma);
                vb += (b[i] - mb) * (b[i] - mb);
            }
            return va <= 0 || vb <= 0 ? 0 : cov / Math.Sqrt(va * vb);
        }
    }

    internal sealed class Options
    {
        public int Games = 1000;
        public ulong Seed = 1;
        public int Rounds = 20;
        public int Cash = 10000;
        public List<int> PlayerCounts = new List<int> { 2, 3, 4 };
        public string OutPath;
        public Action<GameConfig> Tweak;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string v = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--games": o.Games = int.Parse(v); i++; break;
                    case "--seed": o.Seed = ulong.Parse(v); i++; break;
                    case "--rounds": o.Rounds = int.Parse(v); i++; break;
                    case "--cash": o.Cash = int.Parse(v); i++; break;
                    case "--players": o.PlayerCounts = v.Split(',').Select(int.Parse).ToList(); i++; break;
                    case "--out": o.OutPath = v; i++; break;
                    case "--toll":
                    {
                        var rates = v.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                        o.Tweak += c => c.TollRates = rates;
                        i++;
                        break;
                    }
                    case "--turnend-build":
                        o.Tweak += c => c.BuildAtTurnEnd = true;
                        break;
                    case "--build":
                    {
                        double rate = double.Parse(v, CultureInfo.InvariantCulture);
                        o.Tweak += c => c.BuildCostRate = rate;
                        i++;
                        break;
                    }
                }
            }
            return o;
        }
    }

    internal sealed class PlayerResult
    {
        public AiStyle Style;
        public int Seat;
        public int Rank;
        public long Total;
        public double CashShare, StockShare, LandShare;
        public long TollIncome;
        public long StockNet;
        public long Salary;
        public int Bailouts;
    }

    internal sealed class GameStats
    {
        public int Players;
        public int Turns, Rolls, Decisions, Moves, TradePhases, Closes;
        public int Listings, Delistings, LimitUps, LimitDowns, Bailouts, ForcedSales;
        public double AvgLaps, AvgFinalTotal, AvgPriceRatio, FinalMargin;
        public double TollPerOwnedLand, InvestedPerOwnedLand;
        public int Winner, LeaderAtHalf, LeaderAt75, LeaderBeforeLast, LastAtHalf, LeadChanges;
        public List<PlayerResult> Results = new List<PlayerResult>();
        public Dictionary<int, List<double>> MovesByInvestors = new Dictionary<int, List<double>>();

        public void Observe(GameState s, List<GameEvent> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case TurnStartedEvent _: Turns++; break;
                    case DiceRolledEvent d: Rolls++; break;
                    case TokenMovedEvent m: Moves += m.Path.Length; break;
                    case LandBoughtEvent _:
                    case LandDeclinedEvent ld when !ld.CouldNotAfford:
                    case BuildingBuiltEvent _:
                    case CoinBetEvent cb when !cb.Skipped:
                        Decisions++; break;
                    case CompanyListedEvent cl when cl.ByPlayer >= 0: Listings++; break;
                    case DelistedEvent _: Delistings++; break;
                    case BailoutEvent _: Bailouts++; break;
                    case StockTradedEvent st when st.Forced: ForcedSales++; break;
                    case MarketClosedEvent mc:
                        Closes++;
                        foreach (var ch in mc.Changes)
                        {
                            if (ch.LimitUp) LimitUps++;
                            if (ch.LimitDown) LimitDowns++;
                            List<double> list;
                            if (!MovesByInvestors.TryGetValue(ch.Investors, out list)) MovesByInvestors[ch.Investors] = list = new List<double>();
                            list.Add(ch.Delisted ? -1.0 : ch.Rate);
                        }
                        break;
                }
            }
        }

        public void Finish(GameState s)
        {
            var ranks = Valuation.Rankings(s);
            Winner = ranks[0].PlayerId;
            FinalMargin = ranks[0].Total > 0 && ranks.Count > 1 ? (double)(ranks[0].Total - ranks[1].Total) / ranks[0].Total : 0;
            AvgFinalTotal = ranks.Average(x => (double)x.Total);
            AvgLaps = s.Players.Average(p => (double)p.Stats.Laps);
            TradePhases = Turns;

            foreach (var r in ranks)
            {
                var p = s.Players[r.PlayerId];
                double gross = Math.Max(1, r.Cash + r.Stocks + r.Land);
                Results.Add(new PlayerResult
                {
                    Style = p.Style,
                    Seat = p.Id,
                    Rank = r.Rank,
                    Total = r.Total,
                    CashShare = r.Cash / gross,
                    StockShare = r.Stocks / gross,
                    LandShare = r.Land / gross,
                    TollIncome = p.Stats.TollReceived,
                    StockNet = p.Stats.StockSold - p.Stats.StockBought,
                    Salary = p.Stats.SalaryReceived,
                    Bailouts = p.BailoutCount,
                });
            }

            // 라운드별 1등 추적 (AssetHistory[0] = 시작, [k] = k라운드 마감 후)
            int rounds = s.Players[0].AssetHistory.Count - 1;
            int Leader(int k)
            {
                int best = 0;
                for (int i = 1; i < s.Players.Length; i++)
                    if (s.Players[i].AssetHistory[k] > s.Players[best].AssetHistory[k]) best = i;
                return best;
            }
            int Last(int k)
            {
                int worst = 0;
                for (int i = 1; i < s.Players.Length; i++)
                    if (s.Players[i].AssetHistory[k] < s.Players[worst].AssetHistory[k]) worst = i;
                return worst;
            }
            LeaderAtHalf = Leader(Math.Max(1, rounds / 2));
            LeaderAt75 = Leader(Math.Max(1, rounds * 3 / 4));
            LeaderBeforeLast = Leader(Math.Max(1, rounds - 1));
            LastAtHalf = Last(Math.Max(1, rounds / 2));
            int prev = Leader(1);
            for (int k = 2; k <= rounds; k++)
            {
                int l = Leader(k);
                if (l != prev) LeadChanges++;
                prev = l;
            }

            double ratioSum = 0;
            int listed = 0;
            foreach (var c in s.Companies)
            {
                if (!c.Listed) continue;
                ratioSum += (double)c.Price / c.ListingPrice;
                listed++;
            }
            AvgPriceRatio = listed > 0 ? ratioSum / listed : 0;

            long toll = 0, invested = 0;
            int owned = 0;
            foreach (var p in s.Players) toll += p.Stats.TollReceived;
            foreach (var land in s.Lands)
            {
                if (land == null || land.Owner < 0) continue;
                owned++;
                invested += land.Invested;
            }
            TollPerOwnedLand = owned > 0 ? (double)toll / owned : 0;
            InvestedPerOwnedLand = owned > 0 ? (double)invested / owned : 0;
        }

        /// <summary>
        /// 예상 플레이 시간 (분). 연출 시간 가정:
        /// 턴 시작 배너/카메라 2초, 주사위 2초, 한 칸 이동 0.3초, 칸 효과 1.5초, 결정 팝업 (사람 4초 / AI 1초),
        /// 턴 종료 매매 (사람 12초 / AI 2초), 장 마감 연출 6초, 공모주 청약 (사람 25초 / AI 3초).
        /// </summary>
        public double EstimatedMinutes(bool allHuman)
        {
            double humanShare = allHuman ? 1.0 : 1.0 / Players;
            double decisionSec = 4 * humanShare + 1 * (1 - humanShare);
            double tradeSec = 12 * humanShare + 2 * (1 - humanShare);
            double ipoSec = (25 * humanShare + 3 * (1 - humanShare)) * Players;
            double sec = Turns * 2.0 + Rolls * (2.0 + 1.5) + Moves * 0.3 + Decisions * decisionSec + Turns * tradeSec + Closes * 6.0 + ipoSec;
            return sec / 60.0;
        }
    }
}
