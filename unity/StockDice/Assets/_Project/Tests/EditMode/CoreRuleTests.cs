using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StockDice.Core;

namespace StockDice.Tests
{
    /// <summary>
    /// RULES.md 규칙 검증. Unity Test Runner(EditMode)와 dotnet test 양쪽에서 돈다.
    /// </summary>
    public class CoreRuleTests
    {
        // ------------------------------------------------------------------
        // 도우미
        // ------------------------------------------------------------------

        private static GameEngine NewGame(int players = 4, ulong seed = 1, Action<GameConfig> tweak = null)
        {
            var cfg = new GameConfig { Seed = seed };
            if (tweak != null) tweak(cfg);
            var setups = new List<PlayerSetup>();
            for (int i = 0; i < players; i++) setups.Add(new PlayerSetup("P" + i, PlayerKind.Human));
            var e = GameEngine.Create(cfg, setups);
            e.Start();
            return e;
        }

        private static void Ok(GameEngine e, GameAction a)
        {
            var r = e.Apply(a);
            Assert.IsTrue(r.Success, "행동 실패: " + r.Error + " " + a);
        }

        private static void FinishIpo(GameEngine e)
        {
            while (e.Phase == GamePhase.Ipo) Ok(e, GameAction.EndTurn(e.CurrentPlayerId));
        }

        private static int FirstListed(GameEngine e)
        {
            return e.State.Companies.First(c => c.Listed).Id;
        }

        /// <summary>주사위 조작 아이템으로 정확히 steps 칸 이동시킨다.</summary>
        private static List<GameEvent> MoveExactly(GameEngine e, int steps)
        {
            var p = e.State.Current;
            p.Items.Add(ItemType.DiceControl);
            var r = e.Apply(GameAction.UseItem(p.Id, ItemType.DiceControl, -1, steps));
            Assert.IsTrue(r.Success, r.Error);
            return r.Events;
        }

        /// <summary>현재 플레이어를 target 칸 2칸 앞에 두고 2칸 이동해 도착시킨다.</summary>
        private static List<GameEvent> LandOn(GameEngine e, int target)
        {
            int n = e.State.Board.TileCount;
            e.State.Current.Position = (target - 2 + n) % n;
            return MoveExactly(e, 2);
        }

        /// <summary>현재 턴을 끝까지 넘긴다 (결정은 거절, 코인은 패스).</summary>
        private static void PassTurn(GameEngine e)
        {
            int pid = e.CurrentPlayerId;
            int guard = 0;
            while (e.CurrentPlayerId == pid && !e.IsGameOver && guard++ < 20)
            {
                switch (e.Phase)
                {
                    case GamePhase.AwaitRoll: Ok(e, GameAction.Roll(pid)); break;
                    case GamePhase.AwaitLandPurchase:
                    case GamePhase.AwaitBuild: Ok(e, GameAction.Decide(pid, false)); break;
                    case GamePhase.AwaitCoinBet: Ok(e, GameAction.Bet(pid, -1)); break;
                    default: Ok(e, GameAction.EndTurn(pid)); break;
                }
            }
        }

        /// <summary>지금 플레이어 차례가 다시 올 때까지 (남들 턴을 모두 넘긴다).</summary>
        private static void PassUntil(GameEngine e, int playerId)
        {
            int guard = 0;
            do { PassTurn(e); } while (e.CurrentPlayerId != playerId && guard++ < 20);
        }

        // ------------------------------------------------------------------
        // 시작 / 상장
        // ------------------------------------------------------------------

        [Test]
        public void InitialListing_IsPlayerCountPlusOne()
        {
            for (int players = GameConfig.MinPlayers; players <= GameConfig.MaxPlayers; players++)
            {
                var e = NewGame(players, (ulong)(players * 11));
                Assert.AreEqual(players + 1, e.State.Companies.Count(c => c.Listed), players + "인");
                Assert.AreEqual(GamePhase.Ipo, e.Phase);
            }
        }

        [Test]
        public void InitialListing_IsRandom()
        {
            var sets = new HashSet<string>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var e = NewGame(2, seed);
                sets.Add(string.Join(",", e.State.Companies.Where(c => c.Listed).Select(c => c.Id)));
            }
            Assert.Greater(sets.Count, 3);
        }

        [Test]
        public void PlayerCountLimits()
        {
            var cfg = new GameConfig();
            Assert.Throws<ArgumentException>(() => GameEngine.Create(cfg, new List<PlayerSetup> { new PlayerSetup("a", PlayerKind.Human) }));
            var many = Enumerable.Range(0, GameConfig.MaxPlayers + 1).Select(i => new PlayerSetup("p" + i, PlayerKind.Human)).ToList();
            Assert.Throws<ArgumentException>(() => GameEngine.Create(cfg, many));
        }

        [Test]
        public void LandingOnUnlistedCompany_ListsIt_AndHasNoToll()
        {
            var e = NewGame(2, 3);
            FinishIpo(e);
            var unlisted = e.State.Companies.First(c => !c.Listed);
            long cash = e.State.Current.Cash;
            var events = LandOn(e, e.State.CompanyDef(unlisted.Id).TileIndex);
            Assert.IsTrue(unlisted.Listed);
            Assert.IsTrue(events.OfType<CompanyListedEvent>().Any(x => x.CompanyId == unlisted.Id && x.ByPlayer == 0));
            Assert.AreEqual(cash, e.State.Current.Cash, "회사 칸에는 통행료가 없다");
        }

        // ------------------------------------------------------------------
        // 매매 / 보호예수
        // ------------------------------------------------------------------

        [Test]
        public void Ipo_BuyThenLockedUntilSecondOwnTurn()
        {
            var e = NewGame(2, 5);
            int c = FirstListed(e);
            Ok(e, GameAction.Buy(0, c, 3));
            var h = e.State.Players[0].Holdings[c];
            Assert.AreEqual(3, h.LockedShares);
            Assert.AreEqual(2, h.LockedLots[0].TurnsLeft, "🔒2");
            FinishIpo(e);

            // 1턴: 🔒1, 매도 불가
            Assert.AreEqual(0, e.CurrentPlayerId);
            Assert.AreEqual(1, h.LockedLots[0].TurnsLeft);
            PassTurnUntilTrade(e);
            Assert.IsFalse(e.Apply(GameAction.Sell(0, c, 1)).Success);
            Ok(e, GameAction.EndTurn(0));
            PassUntil(e, 0);

            // 2턴: 해제
            Assert.AreEqual(0, h.LockedShares);
            Assert.AreEqual(3, h.UnlockedShares);
            PassTurnUntilTrade(e);
            if (e.State.Current.CanTrade) Ok(e, GameAction.Sell(0, c, 3));
        }

        [Test]
        public void AdditionalPurchase_LocksOnlyNewShares()
        {
            var e = NewGame(2, 7);
            int c = FirstListed(e);
            Ok(e, GameAction.Buy(0, c, 2));
            FinishIpo(e);
            PassTurn(e); // 0의 1턴
            PassUntil(e, 0); // 0의 2턴 시작 → IPO 물량 해제
            var h = e.State.Players[0].Holdings[c];
            Assert.AreEqual(2, h.UnlockedShares);
            PassTurnUntilTrade(e);
            e.State.Players[0].TradeHaltTurns = 0;
            Ok(e, GameAction.Buy(0, c, 5));
            Assert.AreEqual(2, h.UnlockedShares);
            Assert.AreEqual(5, h.LockedShares);
            Ok(e, GameAction.Sell(0, c, 2)); // 예전 물량은 바로 팔 수 있다
            Assert.IsFalse(e.Apply(GameAction.Sell(0, c, 1)).Success);
        }

        private static void PassTurnUntilTrade(GameEngine e)
        {
            int pid = e.CurrentPlayerId;
            int guard = 0;
            while (e.Phase != GamePhase.AwaitTrade && guard++ < 20)
            {
                switch (e.Phase)
                {
                    case GamePhase.AwaitRoll: Ok(e, GameAction.Roll(pid)); break;
                    case GamePhase.AwaitLandPurchase:
                    case GamePhase.AwaitBuild: Ok(e, GameAction.Decide(pid, false)); break;
                    case GamePhase.AwaitCoinBet: Ok(e, GameAction.Bet(pid, -1)); break;
                }
            }
        }

        [Test]
        public void Trading_OnlyAtTurnEnd_AndLimitedByCash()
        {
            var e = NewGame(2, 9);
            int c = FirstListed(e);
            long price = e.State.Companies[c].Price;
            int max = e.MaxBuyable(0, c);
            Assert.AreEqual((int)(e.State.Players[0].Cash / price), max);
            Assert.IsFalse(e.Apply(GameAction.Buy(0, c, max + 1)).Success, "매수 한도 = 현금");
            FinishIpo(e);
            Assert.AreEqual(GamePhase.AwaitRoll, e.Phase);
            Assert.IsFalse(e.Apply(GameAction.Buy(0, c, 1)).Success, "주사위 전에는 매매 불가");
        }

        [Test]
        public void NotYourTurn_IsRejected()
        {
            var e = NewGame(3, 1);
            Assert.IsFalse(e.Apply(GameAction.EndTurn(1)).Success);
        }

        // ------------------------------------------------------------------
        // 이동 / 월급 / 특수 칸
        // ------------------------------------------------------------------

        [Test]
        public void Salary_WhenPassingOrLandingOnStart()
        {
            var e = NewGame(2, 2);
            FinishIpo(e);
            var p = e.State.Current;
            int n = e.State.Board.TileCount;
            p.Position = n - 3;
            long cash = p.Cash;
            var events = MoveExactly(e, 3); // 정확히 출발 칸
            Assert.AreEqual(1, events.OfType<SalaryPaidEvent>().Count());
            Assert.AreEqual(e.Config.Salary, 2000);
            Assert.GreaterOrEqual(p.Cash, cash + 2000 - 0);
        }

        [Test]
        public void Vacation_SkipsRollButAllowsTrade()
        {
            var e = NewGame(2, 4);
            FinishIpo(e);
            int vac = e.State.Board.Tiles.First(t => t.Type == TileType.Vacation).Index;
            LandOn(e, vac);
            Assert.AreEqual(1, e.State.Players[0].SkipTurns);
            PassTurn(e);
            PassUntil(e, 0);
            Assert.AreEqual(GamePhase.AwaitTrade, e.Phase, "휴가: 주사위 없이 바로 턴 종료 매매");
        }

        [Test]
        public void TradeHalt_BlocksTradingThisTurnAndNext()
        {
            var e = NewGame(2, 6);
            FinishIpo(e);
            int halt = e.State.Board.Tiles.First(t => t.Type == TileType.TradeHalt).Index;
            LandOn(e, halt);
            PassTurnUntilTrade(e);
            int c = FirstListed(e);
            Assert.IsFalse(e.Apply(GameAction.Buy(0, c, 1)).Success);
            Ok(e, GameAction.EndTurn(0));
            PassUntil(e, 0);
            PassTurnUntilTrade(e);
            if (e.State.Players[0].TradeHaltTurns > 0)
                Assert.IsFalse(e.Apply(GameAction.Buy(0, c, 1)).Success, "다음 턴도 매매 불가");
            Ok(e, GameAction.EndTurn(0));
            Assert.AreEqual(0, e.State.Players[0].TradeHaltTurns);
        }

        [Test]
        public void Doubles_GiveOneBonusRoll()
        {
            // 첫 굴림이 더블인 시드를 찾는다
            for (ulong seed = 1; seed < 500; seed++)
            {
                var e = NewGame(2, seed);
                FinishIpo(e);
                var probe = e.State.Rng.Clone();
                int d1 = probe.Range(1, 7), d2 = probe.Range(1, 7);
                if (d1 != d2) continue;
                var r = e.Apply(GameAction.Roll(0));
                Assert.IsTrue(r.Success);
                var tile = e.State.Tile(e.State.Players[0].Position);
                if (tile.Type == TileType.Vacation) continue;
                // 결정이 필요한 칸이면 거절
                while (e.Phase == GamePhase.AwaitLandPurchase || e.Phase == GamePhase.AwaitBuild) Ok(e, GameAction.Decide(0, false));
                if (e.Phase == GamePhase.AwaitCoinBet) Ok(e, GameAction.Bet(0, -1));
                Assert.AreEqual(GamePhase.AwaitRoll, e.Phase, "더블이면 한 번 더");
                return;
            }
            Assert.Fail("더블 시드를 찾지 못했다");
        }

        // ------------------------------------------------------------------
        // 땅 / 통행료 / 현금 부족
        // ------------------------------------------------------------------

        [Test]
        public void Land_BuyBuildAndToll()
        {
            var e = NewGame(2, 8);
            FinishIpo(e);
            var landTile = e.State.Board.Tiles.First(t => t.Type == TileType.Land);
            LandOn(e, landTile.Index);
            Assert.AreEqual(GamePhase.AwaitLandPurchase, e.Phase);
            Ok(e, GameAction.Decide(0, true));
            var land = e.State.Lands[landTile.Index];
            Assert.AreEqual(0, land.Owner);
            Assert.AreEqual(landTile.LandPrice, land.Invested);

            // 다시 밟으면 건설
            if (e.Phase == GamePhase.AwaitRoll) PassTurn(e); else Ok(e, GameAction.EndTurn(0));
            PassUntil(e, 0);
            Assert.AreEqual(GamePhase.AwaitRoll, e.Phase);
            LandOn(e, landTile.Index);
            Assert.AreEqual(GamePhase.AwaitBuild, e.Phase);
            Ok(e, GameAction.Decide(0, true));
            Assert.AreEqual(1, land.Level);
            Assert.AreEqual(landTile.LandPrice + e.BuildCost(landTile.Index), land.Invested);

            // 상대가 밟으면 통행료 (1단계)
            if (e.CurrentPlayerId == 0) PassTurn(e);
            Assert.AreEqual(1, e.CurrentPlayerId);
            long due = e.Toll(landTile.Index);
            Assert.AreEqual((long)Math.Round(landTile.LandPrice * e.Config.TollRates[1]), due);
            long before0 = e.State.Players[0].Cash, before1 = e.State.Players[1].Cash;
            var events = LandOn(e, landTile.Index);
            var toll = events.OfType<TollPaidEvent>().Single();
            Assert.AreEqual(due, toll.Amount);
            Assert.AreEqual(before0 + due, e.State.Players[0].Cash);
        }

        [Test]
        public void TollWaiver_IsUsedAutomatically()
        {
            var e = NewGame(2, 8);
            FinishIpo(e);
            var landTile = e.State.Board.Tiles.Where(t => t.Type == TileType.Land).Last();
            e.State.Lands[landTile.Index].Owner = 1;
            e.State.Lands[landTile.Index].Invested = landTile.LandPrice;
            var p = e.State.Players[0];
            p.Items.Add(ItemType.TollWaiver);
            long cash = p.Cash;
            var events = LandOn(e, landTile.Index);
            Assert.IsTrue(events.OfType<TollWaivedEvent>().Any());
            Assert.IsFalse(p.Items.Contains(ItemType.TollWaiver));
            Assert.AreEqual(cash, p.Cash);
        }

        [Test]
        public void CashShortage_LiquidationOrder_ThenBailout()
        {
            var e = NewGame(2, 12);
            FinishIpo(e);
            var s = e.State;
            var lands = s.Board.Tiles.Where(t => t.Type == TileType.Land).ToList();
            var expensive = lands.Last();
            var mine = lands.First();

            // 상대의 비싼 3단계 땅
            var target = s.Lands[expensive.Index];
            target.Owner = 1;
            target.Level = 3;
            target.Invested = expensive.LandPrice * 3;
            long due = e.Toll(expensive.Index);

            // 나: 현금 조금, 풀린 주식 조금, 땅 1, 잠긴 주식 조금
            var p = s.Players[0];
            int c = FirstListed(e);
            long price = s.Companies[c].Price;
            p.Cash = 100;
            p.Holdings[c].UnlockedShares = 1;
            p.Holdings[c].LockedLots.Add(new StockLot { Shares = 2, TurnsLeft = 2 });
            s.Lands[mine.Index].Owner = 0;
            s.Lands[mine.Index].Invested = mine.LandPrice;

            var events = LandOn(e, expensive.Index);
            var order = events.Where(x => x is StockTradedEvent || x is LandSoldEvent || x is TollPaidEvent || x is BailoutEvent).ToList();
            // 1) 풀린 주식 → 2) 땅 → 3) 잠긴 주식 반값 → 통행료 → 구제금융
            Assert.IsInstanceOf<StockTradedEvent>(order[0]);
            Assert.IsFalse(((StockTradedEvent)order[0]).FireSale);
            Assert.IsInstanceOf<LandSoldEvent>(order[1]);
            Assert.IsInstanceOf<StockTradedEvent>(order[2]);
            Assert.IsTrue(((StockTradedEvent)order[2]).FireSale);
            Assert.AreEqual(price / 2, ((StockTradedEvent)order[2]).Price);
            Assert.IsInstanceOf<TollPaidEvent>(order[3]);
            Assert.IsInstanceOf<BailoutEvent>(order[4]);

            var paid = (TollPaidEvent)order[3];
            Assert.Less(paid.Amount, due);
            Assert.AreEqual(1, p.BailoutCount);
            Assert.Greater(p.Debt, 0);
            Assert.AreEqual(-1, s.Lands[mine.Index].Owner);
            Assert.AreEqual(0, p.Holdings[c].TotalShares);
        }

        // ------------------------------------------------------------------
        // 주가
        // ------------------------------------------------------------------

        [Test]
        public void OddsTable_MatchesRulesFor4Players_AndInterpolates()
        {
            var cfg = new GameConfig();
            double up, vol;
            StockMarket.Interpolate(cfg.OddsTable, 0.0, out up, out vol);
            Assert.AreEqual(0.40, up, 1e-9); Assert.AreEqual(0.25, vol, 1e-9);
            StockMarket.Interpolate(cfg.OddsTable, 0.25, out up, out vol);
            Assert.AreEqual(0.45, up, 1e-9); Assert.AreEqual(0.25, vol, 1e-9);
            StockMarket.Interpolate(cfg.OddsTable, 0.5, out up, out vol);
            Assert.AreEqual(0.55, up, 1e-9); Assert.AreEqual(0.15, vol, 1e-9);
            StockMarket.Interpolate(cfg.OddsTable, 0.75, out up, out vol);
            Assert.AreEqual(0.65, up, 1e-9); Assert.AreEqual(0.10, vol, 1e-9);
            StockMarket.Interpolate(cfg.OddsTable, 1.0, out up, out vol);
            Assert.AreEqual(0.75, up, 1e-9); Assert.AreEqual(0.06, vol, 1e-9);
            // 3인 1명 = 1/3
            StockMarket.Interpolate(cfg.OddsTable, 1.0 / 3.0, out up, out vol);
            Assert.AreEqual(0.45 + 0.10 / 3.0, up, 1e-9);
        }

        [Test]
        public void InvestorCount_RequiresThresholdValue()
        {
            var e = NewGame(4, 1);
            int c = FirstListed(e);
            long price = e.State.Companies[c].Price;
            int need = StockMarket.SharesForInvestor(e.State, c);
            Assert.GreaterOrEqual(need * price, e.Config.InvestorThreshold);
            e.State.Players[0].Holdings[c].UnlockedShares = 1;
            if (price < e.Config.InvestorThreshold) Assert.AreEqual(0, StockMarket.CountInvestors(e.State, c), "1주 무임승차 방지");
            e.State.Players[0].Holdings[c].UnlockedShares = need;
            e.State.Players[1].Holdings[c].UnlockedShares = need;
            Assert.AreEqual(2, StockMarket.CountInvestors(e.State, c));
        }

        [Test]
        public void MarketClose_RespectsPriceLimit_AndFinalStretch()
        {
            var e = NewGame(2, 99);
            var s = e.State;
            s.Round = s.Config.Rounds; // 마감 임박
            Assert.IsTrue(s.IsFinalStretch);
            var odds = StockMarket.GetOdds(s, FirstListed(e));
            Assert.Greater(odds.Volatility, 0.25, "마지막 3라운드는 변동폭 1.5배");
            for (int i = 0; i < 50; i++)
            {
                var events = new List<GameEvent>();
                StockMarket.CloseMarket(s, events.Add);
                var closed = events.OfType<MarketClosedEvent>().Single();
                foreach (var ch in closed.Changes)
                {
                    // 틱 반올림 오차 정도만 허용
                    Assert.LessOrEqual(Math.Abs(ch.NewPrice - ch.OldPrice), ch.OldPrice * s.Config.PriceLimit + s.Config.PriceTick, "상한가/하한가 ±30%");
                }
            }
        }

        [Test]
        public void Delisting_WipesHoldings_AndCanRelist()
        {
            var e = NewGame(2, 5);
            FinishIpo(e);
            var s = e.State;
            int c = FirstListed(e);
            var company = s.Companies[c];
            s.Players[1].Holdings[c].UnlockedShares = 10;
            company.Price = s.Config.PriceTick; // 상장가의 10% 미만으로
            company.ForcedDirection = -1;
            var events = new List<GameEvent>();
            StockMarket.CloseMarket(s, events.Add);
            Assert.IsTrue(events.OfType<DelistedEvent>().Any(x => x.CompanyId == c));
            Assert.IsFalse(company.Listed);
            Assert.AreEqual(0, s.Players[1].Holdings[c].TotalShares);

            // 회사 칸을 밟으면 재상장
            var r = LandOn(e, s.CompanyDef(c).TileIndex);
            Assert.IsTrue(company.Listed);
            Assert.AreEqual(s.CompanyDef(c).ListingPrice, company.Price);
        }

        [Test]
        public void InsiderInfo_RevealsTrueDirection()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var e = NewGame(2, seed);
                FinishIpo(e);
                PassTurnUntilTrade(e);
                var s = e.State;
                int c = FirstListed(e);
                s.Current.Items.Add(ItemType.InsiderInfo);
                var r = e.Apply(GameAction.UseItem(s.CurrentPlayer, ItemType.InsiderInfo, c));
                Assert.IsTrue(r.Success, r.Error);
                var info = r.Events.OfType<InsiderInfoEvent>().Single();
                Assert.AreEqual(s.CurrentPlayer, info.VisibleTo, "본인에게만 보인다");
                var events = new List<GameEvent>();
                StockMarket.CloseMarket(s, events.Add);
                var ch = events.OfType<MarketClosedEvent>().Single().Changes.First(x => x.CompanyId == c);
                Assert.AreEqual(info.Direction > 0, ch.NewPrice > ch.OldPrice, "seed " + seed);
            }
        }

        // ------------------------------------------------------------------
        // 점수 / 전체 진행
        // ------------------------------------------------------------------

        [Test]
        public void TotalAssets_CashStocksLandMinusDebt()
        {
            var e = NewGame(2, 1);
            var s = e.State;
            var p = s.Players[0];
            int c = FirstListed(e);
            p.Cash = 1000;
            p.Holdings[c].UnlockedShares = 2;
            p.Holdings[c].LockedLots.Add(new StockLot { Shares = 3, TurnsLeft = 1 });
            var tile = s.Board.Tiles.First(t => t.Type == TileType.Land);
            s.Lands[tile.Index].Owner = 0;
            s.Lands[tile.Index].Invested = 1500;
            p.Debt = 300;
            long expected = 1000 + 5 * s.Companies[c].Price + 1500 - 300;
            Assert.AreEqual(expected, Valuation.Total(s, p));
        }

        [Test]
        public void Tie_BrokenByCash()
        {
            var e = NewGame(2, 1);
            var s = e.State;
            foreach (var p in s.Players) foreach (var h in p.Holdings) h.Clear();
            var tile = s.Board.Tiles.First(t => t.Type == TileType.Land);
            s.Players[0].Cash = 5000;
            s.Lands[tile.Index].Owner = 0;
            s.Lands[tile.Index].Invested = 5000;
            s.Players[1].Cash = 10000;
            var ranks = Valuation.Rankings(s);
            Assert.AreEqual(1, ranks[0].PlayerId, "총자산 같으면 현금 많은 쪽");
        }

        [Test]
        public void FullAiGames_RunToEnd_ForAllPlayerCounts()
        {
            var styles = new[] { AiStyle.Hodler, AiStyle.Trader, AiStyle.Landlord, AiStyle.Ant };
            for (int players = GameConfig.MinPlayers; players <= GameConfig.MaxPlayers; players++)
            {
                for (ulong seed = 1; seed <= 15; seed++)
                {
                    var setups = Enumerable.Range(0, players).Select(i => new PlayerSetup("AI" + i, PlayerKind.Ai, styles[(i + (int)seed) % 4])).ToList();
                    var cfg = new GameConfig { Seed = seed, Rounds = 12 };
                    var e = GameEngine.Create(cfg, setups);
                    GameRunner.RunToEnd(e, GameRunner.CreateAiAgents(setups, seed));
                    Assert.IsTrue(e.IsGameOver);
                    Assert.AreEqual(cfg.Rounds, e.State.Round);
                    foreach (var p in e.State.Players)
                    {
                        Assert.GreaterOrEqual(p.Cash, 0);
                        Assert.AreEqual(cfg.Rounds + 1, p.AssetHistory.Count);
                    }
                }
            }
        }

        [Test]
        public void SameSeed_SameGame()
        {
            string Run()
            {
                var setups = Enumerable.Range(0, 4).Select(i => new PlayerSetup("AI" + i, PlayerKind.Ai, (AiStyle)(i % 4))).ToList();
                var e = GameEngine.Create(new GameConfig { Seed = 42 }, setups);
                int events = 0;
                GameRunner.RunToEnd(e, GameRunner.CreateAiAgents(setups, 42), evs => events += evs.Count);
                return events + ":" + string.Join(",", e.State.Players.Select(p => Valuation.Total(e.State, p)));
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void Rng_IsDeterministicAndUnbiasedEnough()
        {
            var a = new Rng(123);
            var b = new Rng(123);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
            var counts = new int[6];
            var r = new Rng(7);
            for (int i = 0; i < 60000; i++) counts[r.Range(1, 7) - 1]++;
            foreach (int count in counts) Assert.That(count, Is.InRange(9400, 10600));
        }
    }
}
