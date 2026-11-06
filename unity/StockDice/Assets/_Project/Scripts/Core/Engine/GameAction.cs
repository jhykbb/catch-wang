using System;

namespace StockDice.Core
{
    public enum ActionType
    {
        RollDice,
        Decide,     // 땅 매입 / 건물 건설 결정 (Accept)
        CoinBet,    // StakeIndex = Param (-1 이면 패스)
        BuyStock,
        SellStock,
        SellLand,
        Build,      // 턴 종료 때 내 땅에 건설 (선택 규칙 BuildAtTurnEnd)
        UseItem,
        EndTurn,
    }

    /// <summary>
    /// 플레이어 행동. 온라인 전송과 리플레이 저장이 쉽도록 평평한(flat) 구조로 둔다.
    /// </summary>
    [Serializable]
    public sealed class GameAction
    {
        public ActionType Type;
        public int PlayerId;
        public int CompanyId = -1;
        public int Quantity;
        public int TileIndex = -1;
        public bool Accept;
        public ItemType Item;
        /// <summary>코인 베팅 단계, 찌라시 방향(+1/-1), 주사위 조작 합 등.</summary>
        public int Param;

        public static GameAction Roll(int player)
        {
            return new GameAction { Type = ActionType.RollDice, PlayerId = player };
        }

        public static GameAction Decide(int player, bool accept)
        {
            return new GameAction { Type = ActionType.Decide, PlayerId = player, Accept = accept };
        }

        public static GameAction Bet(int player, int stakeIndex)
        {
            return new GameAction { Type = ActionType.CoinBet, PlayerId = player, Param = stakeIndex };
        }

        public static GameAction Buy(int player, int companyId, int quantity)
        {
            return new GameAction { Type = ActionType.BuyStock, PlayerId = player, CompanyId = companyId, Quantity = quantity };
        }

        public static GameAction Sell(int player, int companyId, int quantity)
        {
            return new GameAction { Type = ActionType.SellStock, PlayerId = player, CompanyId = companyId, Quantity = quantity };
        }

        public static GameAction SellLand(int player, int tileIndex)
        {
            return new GameAction { Type = ActionType.SellLand, PlayerId = player, TileIndex = tileIndex };
        }

        public static GameAction Build(int player, int tileIndex)
        {
            return new GameAction { Type = ActionType.Build, PlayerId = player, TileIndex = tileIndex };
        }

        public static GameAction UseItem(int player, ItemType item, int companyId = -1, int param = 0)
        {
            return new GameAction { Type = ActionType.UseItem, PlayerId = player, Item = item, CompanyId = companyId, Param = param };
        }

        public static GameAction EndTurn(int player)
        {
            return new GameAction { Type = ActionType.EndTurn, PlayerId = player };
        }

        public override string ToString()
        {
            return string.Format("[{0} p{1} c{2} q{3} t{4} a{5} i{6} x{7}]", Type, PlayerId, CompanyId, Quantity, TileIndex, Accept, Item, Param);
        }
    }
}
