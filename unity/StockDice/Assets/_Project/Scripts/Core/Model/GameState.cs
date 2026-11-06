using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    public enum GamePhase
    {
        NotStarted,
        Ipo,                // 공모주 청약 (첫 턴 전 매수만)
        AwaitRoll,          // 주사위 굴리기 (또는 아이템 사용)
        AwaitLandPurchase,  // 빈 땅 매입 여부
        AwaitBuild,         // 내 땅 건물 건설 여부
        AwaitCoinBet,       // 코인 거래소 베팅
        AwaitTrade,         // 턴 종료 매매
        GameOver,
    }

    /// <summary>잠긴 주식 묶음. TurnsLeft 는 자기 턴이 시작될 때마다 1씩 줄고 0이 되면 풀린다.</summary>
    [Serializable]
    public sealed class StockLot
    {
        public int Shares;
        public int TurnsLeft;
    }

    [Serializable]
    public sealed class Holding
    {
        public int CompanyId;
        public int UnlockedShares;
        public List<StockLot> LockedLots = new List<StockLot>();
        /// <summary>보유 중인 주식의 총 매입 원가 (평균 단가, 수익률 표시용).</summary>
        public long CostBasis;

        public int LockedShares
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < LockedLots.Count; i++) sum += LockedLots[i].Shares;
                return sum;
            }
        }

        public int TotalShares { get { return UnlockedShares + LockedShares; } }

        public double AveragePrice
        {
            get { int t = TotalShares; return t > 0 ? (double)CostBasis / t : 0; }
        }

        public void Clear()
        {
            UnlockedShares = 0;
            LockedLots.Clear();
            CostBasis = 0;
        }
    }

    /// <summary>시뮬레이터와 결과 화면용 누적 통계.</summary>
    [Serializable]
    public sealed class PlayerStats
    {
        public long SalaryReceived;
        public long TollReceived;
        public long TollPaid;
        public long StockBought;
        public long StockSold;
        public long LandSpent;
        public long CoinNet;
        public long ItemCash;
        public int Laps;
        public int LandsBought;
        public int BuildingsBuilt;
        public int Rolls;
        public int Doubles;
        public int TradeActions;
        public int ForcedSales;
    }

    [Serializable]
    public sealed class PlayerState
    {
        public int Id;
        public string Name;
        public PlayerKind Kind;
        public AiStyle Style;

        public long Cash;
        public int Position;
        public Holding[] Holdings;
        public List<ItemType> Items = new List<ItemType>();

        public int SkipTurns;       // 휴가
        public int TradeHaltTurns;  // 거래정지 (턴 종료 시 1씩 감소)
        public long Debt;           // 구제금융 부채
        public int BailoutCount;
        public int TurnsTaken;

        public PlayerStats Stats = new PlayerStats();
        /// <summary>라운드가 끝날 때마다의 총자산 (0 = 게임 시작 시점).</summary>
        public List<long> AssetHistory = new List<long>();

        public bool CanTrade { get { return TradeHaltTurns <= 0; } }

        public Holding GetHolding(int companyId)
        {
            return Holdings[companyId];
        }
    }

    [Serializable]
    public sealed class CompanyState
    {
        public int Id;
        public bool Listed;
        public long Price;
        public long ListingPrice;
        public long PreviousPrice;
        public double LastChangeRate;
        public int ListedRound = -1;
        public int TimesDelisted;
        public List<long> PriceHistory = new List<long>();

        // 다음 장 마감에만 적용되는 효과 (장 마감 후 초기화)
        public double RumorBoost;
        /// <summary>내부자 정보로 미리 정해진 방향. 0 = 없음, +1 상승, -1 하락.</summary>
        public int ForcedDirection;
    }

    [Serializable]
    public sealed class LandState
    {
        public int TileIndex;
        public int Owner = -1;
        public int Level;           // 0 = 건물 없음
        public long Invested;       // 땅값 + 건설비 (총자산, 은행 매각 기준)
    }

    [Serializable]
    public sealed class ShortPosition
    {
        public int PlayerId;
        public int CompanyId;
    }

    [Serializable]
    public sealed class GameState
    {
        public GameConfig Config;
        public PlayerState[] Players;
        public CompanyState[] Companies;
        /// <summary>타일 인덱스로 접근. 땅이 아닌 칸은 null.</summary>
        public LandState[] Lands;
        public List<ShortPosition> Shorts = new List<ShortPosition>();

        public GamePhase Phase = GamePhase.NotStarted;
        /// <summary>0 = 공모주 청약, 1부터 실제 라운드.</summary>
        public int Round;
        public int CurrentPlayer;
        public int BonusRollsUsed;
        public bool LastRollWasDouble;
        public int LastDie1;
        public int LastDie2;
        public int PendingTile = -1;
        public int TurnEndBuilds;
        public int EventSequence;

        public Rng Rng;

        public int PlayerCount { get { return Players.Length; } }
        public BoardDefinition Board { get { return Config.Board; } }
        public PlayerState Current { get { return Players[CurrentPlayer]; } }

        public bool IsFinalStretch
        {
            get { return Round > Config.Rounds - Config.FinalStretchRounds && Round >= 1; }
        }

        public int RoundsLeft { get { return Math.Max(0, Config.Rounds - Round); } }

        public TileDef Tile(int index)
        {
            return Config.Board.Tiles[index];
        }

        public CompanyDef CompanyDef(int companyId)
        {
            return Config.Board.Companies[companyId];
        }

        public string CompanyName(int companyId)
        {
            return Config.Board.Companies[companyId].Name;
        }

        public string PlayerName(int playerId)
        {
            return playerId >= 0 && playerId < Players.Length ? Players[playerId].Name : "은행";
        }
    }
}
