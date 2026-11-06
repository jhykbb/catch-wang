using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    public sealed class ActionResult
    {
        public bool Success;
        public string Error;
        public List<GameEvent> Events = new List<GameEvent>();

        public static ActionResult Fail(string error)
        {
            return new ActionResult { Success = false, Error = error };
        }
    }

    /// <summary>
    /// 게임 규칙 엔진. UnityEngine 에 의존하지 않는다.
    /// 사용법: Create → Start() 로 초기 이벤트 → 매 행동마다 Apply(action) 로 이벤트 목록을 받는다.
    /// 화면, AI, 온라인 호스트, 시뮬레이터가 모두 이 클래스 하나를 공유한다.
    /// </summary>
    public sealed class GameEngine
    {
        public GameState State { get; private set; }

        private List<GameEvent> _out = new List<GameEvent>();

        private GameEngine() { }

        public GamePhase Phase { get { return State.Phase; } }
        public int CurrentPlayerId { get { return State.CurrentPlayer; } }
        public bool IsGameOver { get { return State.Phase == GamePhase.GameOver; } }
        public GameConfig Config { get { return State.Config; } }

        // ------------------------------------------------------------------
        // 생성
        // ------------------------------------------------------------------

        public static GameEngine Create(GameConfig config, IList<PlayerSetup> setups)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (setups == null) throw new ArgumentNullException("setups");
            string error = config.Validate(setups.Count);
            if (error != null) throw new ArgumentException(error);

            var board = config.Board;
            var s = new GameState
            {
                Config = config,
                Players = new PlayerState[setups.Count],
                Companies = new CompanyState[board.Companies.Count],
                Lands = new LandState[board.Tiles.Count],
                Rng = new Rng(config.Seed),
            };

            for (int i = 0; i < setups.Count; i++)
            {
                var p = new PlayerState
                {
                    Id = i,
                    Name = string.IsNullOrEmpty(setups[i].Name) ? "플레이어 " + (i + 1) : setups[i].Name,
                    Kind = setups[i].Kind,
                    Style = setups[i].Style,
                    Cash = config.StartingCash,
                    Position = board.StartIndex,
                    Holdings = new Holding[board.Companies.Count],
                };
                for (int c = 0; c < p.Holdings.Length; c++) p.Holdings[c] = new Holding { CompanyId = c };
                s.Players[i] = p;
            }

            for (int c = 0; c < s.Companies.Length; c++)
            {
                var def = board.Companies[c];
                s.Companies[c] = new CompanyState
                {
                    Id = c,
                    Price = def.ListingPrice,
                    ListingPrice = def.ListingPrice,
                    PreviousPrice = def.ListingPrice,
                };
            }

            foreach (var tile in board.Tiles)
            {
                if (tile.Type == TileType.Land) s.Lands[tile.Index] = new LandState { TileIndex = tile.Index };
            }

            return new GameEngine { State = s };
        }

        /// <summary>저장된 상태에서 이어서 진행 (온라인 재접속, 세이브 로드용).</summary>
        public static GameEngine FromState(GameState state)
        {
            return new GameEngine { State = state };
        }

        /// <summary>게임 시작: 초기 상장 → 공모주 청약.</summary>
        public List<GameEvent> Start()
        {
            if (State.Phase != GamePhase.NotStarted) throw new InvalidOperationException("이미 시작된 게임입니다.");
            _out = new List<GameEvent>();
            var s = State;

            Emit(new GameStartedEvent { PlayerCount = s.PlayerCount, Rounds = s.Config.Rounds });

            // ✅ 시작 상장 수 = 인원 + 1, 랜덤
            var ids = new List<int>();
            for (int c = 0; c < s.Companies.Length; c++) ids.Add(c);
            s.Rng.Shuffle(ids);
            int count = Math.Min(ids.Count, s.PlayerCount + s.Config.InitialListedExtra);
            var chosen = ids.GetRange(0, count);
            chosen.Sort();
            foreach (int c in chosen) StockMarket.List(s, c, -1, Emit);

            foreach (var p in s.Players) p.AssetHistory.Add(Valuation.Total(s, p));

            s.Round = 0;
            s.CurrentPlayer = 0;
            s.Phase = GamePhase.Ipo;
            Emit(new IpoStartedEvent());
            Emit(new IpoTurnEvent { PlayerId = 0 });
            return _out;
        }

        // ------------------------------------------------------------------
        // 행동 처리
        // ------------------------------------------------------------------

        public ActionResult Apply(GameAction a)
        {
            if (a == null) return ActionResult.Fail("행동이 비어 있습니다.");
            string error = Validate(a);
            if (error != null) return ActionResult.Fail(error);

            _out = new List<GameEvent>();
            switch (a.Type)
            {
                case ActionType.RollDice: DoRoll(0); break;
                case ActionType.Decide: DoDecide(a.Accept); break;
                case ActionType.CoinBet: DoCoinBet(a.Param); break;
                case ActionType.BuyStock: DoBuy(a.CompanyId, a.Quantity); break;
                case ActionType.SellStock: DoSell(a.CompanyId, a.Quantity); break;
                case ActionType.SellLand: DoSellLand(a.TileIndex); break;
                case ActionType.Build: DoTurnEndBuild(a.TileIndex); break;
                case ActionType.UseItem: DoUseItem(a); break;
                case ActionType.EndTurn: DoEndTurn(); break;
            }
            return new ActionResult { Success = true, Events = _out };
        }

        /// <summary>행동이 지금 가능한지 검사. 가능하면 null, 아니면 이유.</summary>
        public string Validate(GameAction a)
        {
            var s = State;
            if (s.Phase == GamePhase.NotStarted) return "게임이 아직 시작되지 않았습니다.";
            if (s.Phase == GamePhase.GameOver) return "게임이 끝났습니다.";
            if (a.PlayerId != s.CurrentPlayer) return string.Format("지금은 {0}님의 차례입니다.", s.Current.Name);
            var p = s.Current;

            switch (a.Type)
            {
                case ActionType.RollDice:
                    return s.Phase == GamePhase.AwaitRoll ? null : "지금은 주사위를 굴릴 수 없습니다.";

                case ActionType.Decide:
                    if (s.Phase == GamePhase.AwaitLandPurchase)
                        return !a.Accept || p.Cash >= LandPrice(s.PendingTile) ? null : "현금이 부족합니다.";
                    if (s.Phase == GamePhase.AwaitBuild)
                        return !a.Accept || p.Cash >= BuildCost(s.PendingTile) ? null : "현금이 부족합니다.";
                    return "지금은 결정할 것이 없습니다.";

                case ActionType.CoinBet:
                    if (s.Phase != GamePhase.AwaitCoinBet) return "지금은 코인 거래를 할 수 없습니다.";
                    if (a.Param < 0) return null;
                    if (a.Param >= s.Config.CoinStakeRates.Length) return "잘못된 베팅입니다.";
                    return CoinStake(p.Id, a.Param) > 0 ? null : "걸 돈이 없습니다.";

                case ActionType.BuyStock:
                    if (s.Phase != GamePhase.AwaitTrade && s.Phase != GamePhase.Ipo) return "매매는 턴 종료 때만 할 수 있습니다.";
                    if (!p.CanTrade) return "거래정지 중입니다.";
                    if (!IsListed(a.CompanyId)) return "상장되지 않은 회사입니다.";
                    if (a.Quantity <= 0) return "수량을 확인하세요.";
                    if (s.Companies[a.CompanyId].Price * a.Quantity > p.Cash) return "현금이 부족합니다.";
                    return null;

                case ActionType.SellStock:
                    if (s.Phase != GamePhase.AwaitTrade && s.Phase != GamePhase.Ipo) return "매매는 턴 종료 때만 할 수 있습니다.";
                    if (!p.CanTrade) return "거래정지 중입니다.";
                    if (!IsListed(a.CompanyId)) return "상장되지 않은 회사입니다.";
                    if (a.Quantity <= 0) return "수량을 확인하세요.";
                    if (p.Holdings[a.CompanyId].UnlockedShares < a.Quantity) return "팔 수 있는 주식이 부족합니다. (보호예수)";
                    return null;

                case ActionType.SellLand:
                    if (s.Phase != GamePhase.AwaitTrade) return "땅은 턴 종료 때만 팔 수 있습니다.";
                    if (a.TileIndex < 0 || a.TileIndex >= s.Lands.Length || s.Lands[a.TileIndex] == null) return "땅이 아닙니다.";
                    if (s.Lands[a.TileIndex].Owner != p.Id) return "내 땅이 아닙니다.";
                    return null;

                case ActionType.Build:
                    if (!s.Config.BuildAtTurnEnd) return "턴 종료 건설 규칙이 꺼져 있습니다.";
                    if (s.Phase != GamePhase.AwaitTrade) return "건물은 턴 종료 때 지을 수 있습니다.";
                    if (s.TurnEndBuilds >= s.Config.MaxTurnEndBuilds) return "이번 턴에는 더 지을 수 없습니다.";
                    if (a.TileIndex < 0 || a.TileIndex >= s.Lands.Length || s.Lands[a.TileIndex] == null) return "땅이 아닙니다.";
                    if (s.Lands[a.TileIndex].Owner != p.Id) return "내 땅이 아닙니다.";
                    if (s.Lands[a.TileIndex].Level >= s.Config.MaxBuildingLevel) return "이미 최고 단계입니다.";
                    if (p.Cash < BuildCost(a.TileIndex)) return "현금이 부족합니다.";
                    return null;

                case ActionType.UseItem:
                    return ValidateItem(p, a);

                case ActionType.EndTurn:
                    return s.Phase == GamePhase.AwaitTrade || s.Phase == GamePhase.Ipo ? null : "아직 턴을 끝낼 수 없습니다.";
            }
            return "알 수 없는 행동입니다.";
        }

        private string ValidateItem(PlayerState p, GameAction a)
        {
            var s = State;
            if (!p.Items.Contains(a.Item)) return "그 아이템을 가지고 있지 않습니다.";
            string timingError = ItemTimingError(a.Item);
            if (timingError != null) return timingError;
            if (ItemInfo.NeedsCompany(a.Item) && !IsListed(a.CompanyId)) return "상장된 회사를 골라야 합니다.";

            switch (a.Item)
            {
                case ItemType.Rumor:
                    return a.Param == 1 || a.Param == -1 ? null : "호재(+1) 또는 악재(-1)를 골라야 합니다.";
                case ItemType.DiceControl:
                    return a.Param >= 2 && a.Param <= s.Config.DiceSides * 2 ? null : "2~12 사이의 수를 골라야 합니다.";
                case ItemType.LockupRelease:
                    foreach (var h in p.Holdings) if (h.LockedShares > 0) return null;
                    return "잠긴 주식이 없습니다.";
                case ItemType.TaxAudit:
                    return TaxTarget(p.Id) >= 0 ? null : "세금을 낼 사람이 없습니다.";
                case ItemType.Short:
                    foreach (var sp in s.Shorts)
                        if (sp.PlayerId == p.Id && sp.CompanyId == a.CompanyId) return "이미 공매도를 건 종목입니다.";
                    return null;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 조회 (UI, AI 용)
        // ------------------------------------------------------------------

        public bool IsListed(int companyId)
        {
            return companyId >= 0 && companyId < State.Companies.Length && State.Companies[companyId].Listed;
        }

        public long LandPrice(int tileIndex)
        {
            return tileIndex >= 0 ? State.Tile(tileIndex).LandPrice : 0;
        }

        public long BuildCost(int tileIndex)
        {
            return (long)Math.Round(LandPrice(tileIndex) * State.Config.BuildCostRate);
        }

        /// <summary>현재 건물 단계 기준 통행료.</summary>
        public long Toll(int tileIndex)
        {
            var land = State.Lands[tileIndex];
            if (land == null) return 0;
            return TollAtLevel(tileIndex, land.Level);
        }

        public long TollAtLevel(int tileIndex, int level)
        {
            var rates = State.Config.TollRates;
            level = Math.Max(0, Math.Min(rates.Length - 1, level));
            return (long)Math.Round(LandPrice(tileIndex) * rates[level]);
        }

        public long BankSellValue(int tileIndex)
        {
            var land = State.Lands[tileIndex];
            return land == null ? 0 : (long)Math.Floor(land.Invested * State.Config.BankSellRate);
        }

        public long CoinStake(int playerId, int stakeIndex)
        {
            var rates = State.Config.CoinStakeRates;
            if (stakeIndex < 0 || stakeIndex >= rates.Length) return 0;
            long raw = (long)Math.Floor(State.Players[playerId].Cash * rates[stakeIndex]);
            return raw / 10 * 10;
        }

        public int MaxBuyable(int playerId, int companyId)
        {
            if (!IsListed(companyId)) return 0;
            long price = State.Companies[companyId].Price;
            return price <= 0 ? 0 : (int)Math.Min(int.MaxValue, State.Players[playerId].Cash / price);
        }

        public bool CanTradeNow(int playerId)
        {
            var s = State;
            return s.CurrentPlayer == playerId && (s.Phase == GamePhase.AwaitTrade || s.Phase == GamePhase.Ipo) && s.Players[playerId].CanTrade;
        }

        /// <summary>지금 직접 사용할 수 있는 아이템 (중복 제거). 회사를 고르는 아이템은 고를 회사가 있으면 포함.</summary>
        public List<ItemType> UsableItems(int playerId)
        {
            var list = new List<ItemType>();
            if (State.CurrentPlayer != playerId) return list;
            var p = State.Players[playerId];
            foreach (var item in p.Items)
            {
                if (list.Contains(item)) continue;
                if (ItemTimingError(item) != null) continue;
                switch (item)
                {
                    case ItemType.LockupRelease:
                        bool anyLocked = false;
                        foreach (var h in p.Holdings) if (h.LockedShares > 0) anyLocked = true;
                        if (!anyLocked) continue;
                        break;
                    case ItemType.TaxAudit:
                        if (TaxTarget(playerId) < 0) continue;
                        break;
                    default:
                        if (ItemInfo.NeedsCompany(item) && FirstListedCompany() < 0) continue;
                        break;
                }
                list.Add(item);
            }
            return list;
        }

        private string ItemTimingError(ItemType item)
        {
            var timing = ItemInfo.Timing(item);
            if (timing == ItemTiming.Instant || timing == ItemTiming.Passive) return "직접 사용하는 아이템이 아닙니다.";
            if (timing == ItemTiming.BeforeRoll && State.Phase != GamePhase.AwaitRoll) return "주사위를 굴리기 전에만 쓸 수 있습니다.";
            if (timing == ItemTiming.Active && State.Phase != GamePhase.AwaitRoll && State.Phase != GamePhase.AwaitTrade)
                return "지금은 아이템을 쓸 수 없습니다.";
            return null;
        }

        private int FirstListedCompany()
        {
            for (int c = 0; c < State.Companies.Length; c++) if (State.Companies[c].Listed) return c;
            return -1;
        }

        /// <summary>세무조사 대상: 나를 뺀 현금 1등 (현금이 0보다 커야 함).</summary>
        public int TaxTarget(int userId)
        {
            int best = -1;
            long bestCash = 0;
            foreach (var p in State.Players)
            {
                if (p.Id == userId) continue;
                if (p.Cash > bestCash) { best = p.Id; bestCash = p.Cash; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // 내부 처리
        // ------------------------------------------------------------------

        private void Emit(GameEvent e)
        {
            e.Seq = ++State.EventSequence;
            _out.Add(e);
        }

        private void DoRoll(int controlledTotal)
        {
            var s = State;
            var p = s.Current;
            int d1, d2;
            bool controlled = controlledTotal > 0;
            if (controlled)
            {
                d1 = controlledTotal / 2;
                d2 = controlledTotal - d1;
            }
            else
            {
                d1 = s.Rng.Range(1, s.Config.DiceSides + 1);
                d2 = s.Rng.Range(1, s.Config.DiceSides + 1);
            }
            bool isDouble = !controlled && d1 == d2;
            s.LastRollWasDouble = isDouble;
            s.LastDie1 = d1;
            s.LastDie2 = d2;
            p.Stats.Rolls++;
            if (isDouble) p.Stats.Doubles++;

            Emit(new DiceRolledEvent { PlayerId = p.Id, Die1 = d1, Die2 = d2, IsDouble = isDouble, Controlled = controlled });
            MoveBy(p, d1 + d2);
            ResolveTile(p);
        }

        private void MoveBy(PlayerState p, int steps)
        {
            var s = State;
            int n = s.Board.TileCount;
            int from = p.Position;
            var path = new int[steps];
            bool passedStart = false;
            int pos = from;
            for (int i = 0; i < steps; i++)
            {
                pos = (pos + 1) % n;
                path[i] = pos;
                if (pos == s.Board.StartIndex) passedStart = true;
            }
            p.Position = pos;
            Emit(new TokenMovedEvent { PlayerId = p.Id, From = from, Path = path, PassedStart = passedStart });

            if (passedStart)
            {
                long salary = s.Config.Salary;
                p.Cash += salary;
                p.Stats.SalaryReceived += salary;
                p.Stats.Laps++;
                Emit(new SalaryPaidEvent { PlayerId = p.Id, Amount = salary });
            }
        }

        private void ResolveTile(PlayerState p)
        {
            var s = State;
            var tile = s.Tile(p.Position);
            s.PendingTile = tile.Index;

            switch (tile.Type)
            {
                case TileType.Land:
                {
                    var land = s.Lands[tile.Index];
                    if (land.Owner < 0)
                    {
                        if (p.Cash >= LandPrice(tile.Index))
                        {
                            s.Phase = GamePhase.AwaitLandPurchase;
                            return;
                        }
                        Emit(new LandDeclinedEvent { PlayerId = p.Id, TileIndex = tile.Index, CouldNotAfford = true });
                    }
                    else if (land.Owner == p.Id)
                    {
                        if (land.Level < s.Config.MaxBuildingLevel && p.Cash >= BuildCost(tile.Index))
                        {
                            s.Phase = GamePhase.AwaitBuild;
                            return;
                        }
                    }
                    else
                    {
                        PayToll(p, land);
                    }
                    break;
                }

                case TileType.Company:
                    if (!s.Companies[tile.CompanyId].Listed) StockMarket.List(s, tile.CompanyId, p.Id, Emit);
                    else Emit(new CompanyVisitedEvent { PlayerId = p.Id, CompanyId = tile.CompanyId });
                    break;

                case TileType.Item:
                    GainItem(p);
                    break;

                case TileType.Vacation:
                    p.SkipTurns = s.Config.VacationSkipTurns;
                    Emit(new VacationEvent { PlayerId = p.Id });
                    break;

                case TileType.TradeHalt:
                    p.TradeHaltTurns = s.Config.TradeHaltTurns;
                    Emit(new TradeHaltEvent { PlayerId = p.Id, Turns = s.Config.TradeHaltTurns });
                    break;

                case TileType.CoinExchange:
                    if (CoinStake(p.Id, 0) > 0)
                    {
                        s.Phase = GamePhase.AwaitCoinBet;
                        return;
                    }
                    Emit(new CoinBetEvent { PlayerId = p.Id, Skipped = true });
                    break;
            }

            AfterTileResolved();
        }

        private void AfterTileResolved()
        {
            var s = State;
            var p = s.Current;
            var tile = s.Tile(p.Position);
            s.PendingTile = -1;

            bool canBonus = s.LastRollWasDouble && s.BonusRollsUsed < s.Config.MaxBonusRolls && tile.Type != TileType.Vacation;
            if (canBonus)
            {
                s.BonusRollsUsed++;
                s.LastRollWasDouble = false;
                Emit(new BonusRollEvent { PlayerId = p.Id });
                s.Phase = GamePhase.AwaitRoll;
            }
            else
            {
                s.LastRollWasDouble = false;
                s.Phase = GamePhase.AwaitTrade;
            }
        }

        private void DoDecide(bool accept)
        {
            var s = State;
            var p = s.Current;
            int tile = s.PendingTile;
            var land = s.Lands[tile];

            if (s.Phase == GamePhase.AwaitLandPurchase)
            {
                if (accept)
                {
                    long price = LandPrice(tile);
                    p.Cash -= price;
                    land.Owner = p.Id;
                    land.Level = 0;
                    land.Invested = price;
                    p.Stats.LandSpent += price;
                    p.Stats.LandsBought++;
                    Emit(new LandBoughtEvent { PlayerId = p.Id, TileIndex = tile, Price = price });
                }
                else
                {
                    Emit(new LandDeclinedEvent { PlayerId = p.Id, TileIndex = tile });
                }
            }
            else if (s.Phase == GamePhase.AwaitBuild && accept)
            {
                BuildInternal(p, land);
            }

            AfterTileResolved();
        }

        private void BuildInternal(PlayerState p, LandState land)
        {
            long cost = BuildCost(land.TileIndex);
            p.Cash -= cost;
            land.Level++;
            land.Invested += cost;
            p.Stats.LandSpent += cost;
            p.Stats.BuildingsBuilt++;
            Emit(new BuildingBuiltEvent { PlayerId = p.Id, TileIndex = land.TileIndex, Level = land.Level, Cost = cost });
        }

        private void DoTurnEndBuild(int tileIndex)
        {
            State.TurnEndBuilds++;
            BuildInternal(State.Current, State.Lands[tileIndex]);
        }

        private void PayToll(PlayerState p, LandState land)
        {
            var s = State;
            long due = Toll(land.TileIndex);
            if (due <= 0) return;

            int waiver = p.Items.IndexOf(ItemType.TollWaiver);
            if (waiver >= 0)
            {
                p.Items.RemoveAt(waiver);
                Emit(new TollWaivedEvent { PlayerId = p.Id, TileIndex = land.TileIndex, Amount = due });
                return;
            }

            var owner = s.Players[land.Owner];
            long paid = Collect(p, due);
            owner.Cash += paid;
            owner.Stats.TollReceived += paid;
            p.Stats.TollPaid += paid;
            Emit(new TollPaidEvent { PayerId = p.Id, OwnerId = owner.Id, TileIndex = land.TileIndex, Amount = paid, Due = due });

            if (paid < due) Bailout(p, due - paid);
        }

        /// <summary>
        /// amount 만큼 현금을 걷는다. 모자라면 🟡 순서대로 자동 처분한다.
        /// 1) 잠금 풀린 주식 2) 땅/건물 은행 매각 3) 잠긴 주식 반값 처분. 실제로 낸 금액을 돌려준다.
        /// </summary>
        private long Collect(PlayerState p, long amount)
        {
            if (p.Cash < amount) RaiseCash(p, amount);
            long paid = Math.Min(p.Cash, amount);
            p.Cash -= paid;
            return paid;
        }

        private void RaiseCash(PlayerState p, long target)
        {
            var s = State;
            p.Stats.ForcedSales++;

            // 1) 잠금 풀린 주식 (평가액 큰 종목부터, 필요한 만큼만)
            var order = new List<int>();
            for (int c = 0; c < s.Companies.Length; c++)
                if (s.Companies[c].Listed && p.Holdings[c].UnlockedShares > 0) order.Add(c);
            order.Sort((a, b) =>
            {
                long va = p.Holdings[a].UnlockedShares * s.Companies[a].Price;
                long vb = p.Holdings[b].UnlockedShares * s.Companies[b].Price;
                int cmp = vb.CompareTo(va);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
            foreach (int c in order)
            {
                long need = target - p.Cash;
                if (need <= 0) return;
                long price = s.Companies[c].Price;
                var h = p.Holdings[c];
                int qty = (int)Math.Min(h.UnlockedShares, (need + price - 1) / price);
                if (qty <= 0) continue;
                ReduceBasis(h, qty);
                h.UnlockedShares -= qty;
                p.Cash += qty * price;
                p.Stats.StockSold += qty * price;
                Emit(new StockTradedEvent { PlayerId = p.Id, CompanyId = c, Quantity = qty, Price = price, Forced = true });
            }

            // 2) 땅 매각 (부족분을 한 번에 메우는 가장 싼 땅, 없으면 가장 비싼 땅부터)
            while (p.Cash < target)
            {
                long need = target - p.Cash;
                LandState pick = null;
                LandState largest = null;
                foreach (var land in s.Lands)
                {
                    if (land == null || land.Owner != p.Id) continue;
                    long value = BankSellValue(land.TileIndex);
                    if (value >= need && (pick == null || value < BankSellValue(pick.TileIndex))) pick = land;
                    if (largest == null || value > BankSellValue(largest.TileIndex)) largest = land;
                }
                if (pick == null) pick = largest;
                if (pick == null) break;
                SellLandInternal(p, pick, true);
            }
            if (p.Cash >= target) return;

            // 3) 잠긴 주식 반값 강제 처분
            for (int c = 0; c < s.Companies.Length && p.Cash < target; c++)
            {
                if (!s.Companies[c].Listed) continue;
                var h = p.Holdings[c];
                if (h.LockedShares <= 0) continue;
                long firePrice = Math.Max(1, (long)Math.Floor(s.Companies[c].Price * s.Config.LockedFireSaleRate));
                long need = target - p.Cash;
                int qty = (int)Math.Min(h.LockedShares, (need + firePrice - 1) / firePrice);
                if (qty <= 0) continue;
                ReduceBasis(h, qty);
                int remaining = qty;
                for (int i = h.LockedLots.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var lot = h.LockedLots[i];
                    int take = Math.Min(lot.Shares, remaining);
                    lot.Shares -= take;
                    remaining -= take;
                    if (lot.Shares <= 0) h.LockedLots.RemoveAt(i);
                }
                p.Cash += qty * firePrice;
                p.Stats.StockSold += qty * firePrice;
                Emit(new StockTradedEvent { PlayerId = p.Id, CompanyId = c, Quantity = qty, Price = firePrice, Forced = true, FireSale = true });
            }
        }

        private static void ReduceBasis(Holding h, int soldShares)
        {
            int total = h.TotalShares;
            if (total <= 0) { h.CostBasis = 0; return; }
            if (soldShares >= total) { h.CostBasis = 0; return; }
            h.CostBasis -= h.CostBasis * soldShares / total;
        }

        private void SellLandInternal(PlayerState p, LandState land, bool forced)
        {
            long amount = BankSellValue(land.TileIndex);
            p.Cash += amount;
            land.Owner = -1;
            land.Level = 0;
            land.Invested = 0;
            Emit(new LandSoldEvent { PlayerId = p.Id, TileIndex = land.TileIndex, Amount = amount, Forced = forced });
        }

        /// <summary>🟡 파산 → 구제금융: 현금 지급, 부채 기록 (최종 총자산에서 차감). 탈락은 없다.</summary>
        private void Bailout(PlayerState p, long unpaid)
        {
            var cfg = State.Config;
            long cash = (long)Math.Round(cfg.StartingCash * cfg.BailoutCashRate);
            long debt = (long)Math.Round(cfg.StartingCash * cfg.BailoutDebtRate);
            p.Cash += cash;
            p.Debt += debt;
            p.BailoutCount++;
            Emit(new BailoutEvent { PlayerId = p.Id, Unpaid = unpaid, Cash = cash, Debt = debt });
        }

        private void GainItem(PlayerState p)
        {
            var s = State;
            var item = s.Rng.Pick(ItemInfo.All);
            if (item == ItemType.CashGrant)
            {
                long amount = (long)Math.Round(s.Config.StartingCash * s.Config.ItemCashGrantRate);
                p.Cash += amount;
                p.Stats.ItemCash += amount;
                Emit(new ItemGainedEvent { PlayerId = p.Id, Item = item });
                Emit(new CashBonusEvent { PlayerId = p.Id, Amount = amount, Reason = "지원금" });
                return;
            }
            if (p.Items.Count >= s.Config.ItemHoldLimit)
            {
                Emit(new ItemGainedEvent { PlayerId = p.Id, Item = item, Discarded = true });
                return;
            }
            p.Items.Add(item);
            Emit(new ItemGainedEvent { PlayerId = p.Id, Item = item });
        }

        private void DoCoinBet(int stakeIndex)
        {
            var s = State;
            var p = s.Current;
            if (stakeIndex < 0)
            {
                Emit(new CoinBetEvent { PlayerId = p.Id, Skipped = true });
            }
            else
            {
                long stake = CoinStake(p.Id, stakeIndex);
                bool won = s.Rng.Chance(s.Config.CoinWinChance);
                long delta = won ? stake : -(stake / 2);
                p.Cash += delta;
                p.Stats.CoinNet += delta;
                Emit(new CoinBetEvent { PlayerId = p.Id, Stake = stake, Won = won, Delta = delta });
            }
            AfterTileResolved();
        }

        private void DoBuy(int companyId, int qty)
        {
            var s = State;
            var p = s.Current;
            long price = s.Companies[companyId].Price;
            long cost = price * qty;
            var h = p.Holdings[companyId];
            p.Cash -= cost;
            h.CostBasis += cost;
            p.Stats.StockBought += cost;
            p.Stats.TradeActions++;

            int lockTurns = s.Config.LockupTurns;
            if (lockTurns <= 0)
            {
                h.UnlockedShares += qty;
            }
            else
            {
                StockLot lot = null;
                foreach (var l in h.LockedLots) if (l.TurnsLeft == lockTurns) { lot = l; break; }
                if (lot == null)
                {
                    lot = new StockLot { TurnsLeft = lockTurns };
                    h.LockedLots.Add(lot);
                }
                lot.Shares += qty;
            }
            Emit(new StockTradedEvent { PlayerId = p.Id, CompanyId = companyId, Quantity = qty, Price = price, IsBuy = true });
        }

        private void DoSell(int companyId, int qty)
        {
            var s = State;
            var p = s.Current;
            long price = s.Companies[companyId].Price;
            var h = p.Holdings[companyId];
            ReduceBasis(h, qty);
            h.UnlockedShares -= qty;
            p.Cash += price * qty;
            p.Stats.StockSold += price * qty;
            p.Stats.TradeActions++;
            Emit(new StockTradedEvent { PlayerId = p.Id, CompanyId = companyId, Quantity = qty, Price = price });
        }

        private void DoSellLand(int tileIndex)
        {
            SellLandInternal(State.Current, State.Lands[tileIndex], false);
        }

        private void DoUseItem(GameAction a)
        {
            var s = State;
            var p = s.Current;
            p.Items.Remove(a.Item);
            Emit(new ItemUsedEvent { PlayerId = p.Id, Item = a.Item, CompanyId = a.CompanyId, Param = a.Param });

            switch (a.Item)
            {
                case ItemType.InsiderInfo:
                {
                    var company = s.Companies[a.CompanyId];
                    if (company.ForcedDirection == 0)
                    {
                        var odds = StockMarket.GetOdds(s, a.CompanyId);
                        company.ForcedDirection = s.Rng.Chance(odds.UpChance) ? 1 : -1;
                    }
                    Emit(new InsiderInfoEvent { PlayerId = p.Id, CompanyId = a.CompanyId, Direction = company.ForcedDirection, VisibleTo = p.Id });
                    break;
                }
                case ItemType.Rumor:
                    s.Companies[a.CompanyId].RumorBoost += a.Param * s.Config.RumorBoost;
                    break;
                case ItemType.LockupRelease:
                    foreach (var h in p.Holdings)
                    {
                        int locked = h.LockedShares;
                        if (locked <= 0) continue;
                        h.UnlockedShares += locked;
                        h.LockedLots.Clear();
                        Emit(new LockupReleasedEvent { PlayerId = p.Id, CompanyId = h.CompanyId, Shares = locked });
                    }
                    break;
                case ItemType.Short:
                    s.Shorts.Add(new ShortPosition { PlayerId = p.Id, CompanyId = a.CompanyId });
                    break;
                case ItemType.TaxAudit:
                {
                    int target = TaxTarget(p.Id);
                    var t = s.Players[target];
                    long tax = (long)Math.Floor(t.Cash * s.Config.TaxRate) / 10 * 10;
                    t.Cash -= tax;
                    Emit(new TaxAuditEvent { UserId = p.Id, TargetId = target, Amount = tax });
                    break;
                }
                case ItemType.DiceControl:
                    DoRoll(a.Param);
                    break;
            }
        }

        private void DoEndTurn()
        {
            var s = State;
            var p = s.Current;
            Emit(new TurnEndedEvent { PlayerId = p.Id });

            if (s.Phase == GamePhase.Ipo)
            {
                if (s.CurrentPlayer + 1 < s.PlayerCount)
                {
                    s.CurrentPlayer++;
                    Emit(new IpoTurnEvent { PlayerId = s.CurrentPlayer });
                }
                else
                {
                    s.Round = 1;
                    s.CurrentPlayer = 0;
                    Emit(new RoundStartedEvent { Round = 1, FinalStretch = s.IsFinalStretch });
                    StartTurn();
                }
                return;
            }

            if (p.TradeHaltTurns > 0) p.TradeHaltTurns--;

            if (s.CurrentPlayer + 1 < s.PlayerCount)
            {
                s.CurrentPlayer++;
                StartTurn();
                return;
            }

            // 라운드 끝 → 장 마감
            StockMarket.CloseMarket(s, Emit);
            foreach (var pl in s.Players) pl.AssetHistory.Add(Valuation.Total(s, pl));

            if (s.Round >= s.Config.Rounds)
            {
                s.Phase = GamePhase.GameOver;
                var ended = new GameEndedEvent { Rankings = Valuation.Rankings(s) };
                Emit(ended);
                return;
            }

            s.Round++;
            s.CurrentPlayer = 0;
            Emit(new RoundStartedEvent { Round = s.Round, FinalStretch = s.IsFinalStretch });
            StartTurn();
        }

        private void StartTurn()
        {
            var s = State;
            var p = s.Current;
            p.TurnsTaken++;
            s.BonusRollsUsed = 0;
            s.LastRollWasDouble = false;
            s.PendingTile = -1;
            s.TurnEndBuilds = 0;
            Emit(new TurnStartedEvent { PlayerId = p.Id, Round = s.Round });

            // 보호예수: 자기 턴이 시작될 때마다 1씩 줄어든다.
            foreach (var h in p.Holdings)
            {
                int released = 0;
                for (int i = h.LockedLots.Count - 1; i >= 0; i--)
                {
                    var lot = h.LockedLots[i];
                    lot.TurnsLeft--;
                    if (lot.TurnsLeft <= 0)
                    {
                        released += lot.Shares;
                        h.LockedLots.RemoveAt(i);
                    }
                }
                if (released > 0)
                {
                    h.UnlockedShares += released;
                    Emit(new LockupReleasedEvent { PlayerId = p.Id, CompanyId = h.CompanyId, Shares = released });
                }
            }

            if (p.SkipTurns > 0)
            {
                p.SkipTurns--;
                Emit(new TurnSkippedEvent { PlayerId = p.Id });
                s.Phase = GamePhase.AwaitTrade;
            }
            else
            {
                s.Phase = GamePhase.AwaitRoll;
            }
        }
    }
}
