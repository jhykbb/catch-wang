using System;
using System.Collections.Generic;
using System.Globalization;

namespace StockDice.Core
{
    public static class Fmt
    {
        public static string Won(long amount)
        {
            return amount.ToString("#,0", CultureInfo.InvariantCulture) + "원";
        }

        public static string Num(long value)
        {
            return value.ToString("#,0", CultureInfo.InvariantCulture);
        }

        public static string SignedWon(long amount)
        {
            return (amount > 0 ? "+" : amount < 0 ? "-" : "") + Won(Math.Abs(amount));
        }

        public static string Percent(double rate)
        {
            double p = rate * 100.0;
            return (p > 0 ? "+" : "") + p.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
    }

    /// <summary>
    /// 로직이 내놓는 이벤트. 화면은 이 목록을 순서대로 애니메이션으로 재생한다.
    /// VisibleTo 가 -1 이면 모두에게, 아니면 그 플레이어에게만 보인다. (내부자 정보 등)
    /// </summary>
    [Serializable]
    public abstract class GameEvent
    {
        public int Seq;
        public int VisibleTo = -1;

        public bool IsVisibleTo(int playerId)
        {
            return VisibleTo < 0 || VisibleTo == playerId;
        }

        /// <summary>로그에 표시할 한 줄 설명.</summary>
        public abstract string Describe(GameState s);
    }

    public sealed class GameStartedEvent : GameEvent
    {
        public int PlayerCount;
        public int Rounds;
        public override string Describe(GameState s) { return string.Format("{0}인 게임 시작! {1}라운드 동안 총자산 1등을 노리세요.", PlayerCount, Rounds); }
    }

    public sealed class CompanyListedEvent : GameEvent
    {
        public int CompanyId;
        public long Price;
        public int ByPlayer = -1; // -1 = 게임 시작 시 상장
        public override string Describe(GameState s)
        {
            return ByPlayer < 0
                ? string.Format("{0} 상장 ({1})", s.CompanyName(CompanyId), Fmt.Won(Price))
                : string.Format("신규 상장! {0}님이 {1}을(를) 상장시켰다 ({2})", s.PlayerName(ByPlayer), s.CompanyName(CompanyId), Fmt.Won(Price));
        }
    }

    public sealed class IpoStartedEvent : GameEvent
    {
        public override string Describe(GameState s) { return "공모주 청약 시작! 시작 자금으로 첫 투자를 하세요."; }
    }

    public sealed class IpoTurnEvent : GameEvent
    {
        public int PlayerId;
        public override string Describe(GameState s) { return string.Format("{0}님의 공모주 청약", s.PlayerName(PlayerId)); }
    }

    public sealed class RoundStartedEvent : GameEvent
    {
        public int Round;
        public bool FinalStretch;
        public override string Describe(GameState s)
        {
            return FinalStretch
                ? string.Format("{0}라운드 시작 (마감 임박! 변동폭 확대)", Round)
                : string.Format("{0}라운드 시작", Round);
        }
    }

    public sealed class TurnStartedEvent : GameEvent
    {
        public int PlayerId;
        public int Round;
        public override string Describe(GameState s) { return string.Format("{0}님의 차례", s.PlayerName(PlayerId)); }
    }

    public sealed class LockupReleasedEvent : GameEvent
    {
        public int PlayerId;
        public int CompanyId;
        public int Shares;
        public override string Describe(GameState s) { return string.Format("{0}님의 {1} {2}주 보호예수 해제", s.PlayerName(PlayerId), s.CompanyName(CompanyId), Shares); }
    }

    public sealed class TurnSkippedEvent : GameEvent
    {
        public int PlayerId;
        public override string Describe(GameState s) { return string.Format("{0}님은 휴가 중이라 주사위를 쉰다", s.PlayerName(PlayerId)); }
    }

    public sealed class DiceRolledEvent : GameEvent
    {
        public int PlayerId;
        public int Die1;
        public int Die2;
        public bool IsDouble;
        public bool Controlled;
        public int Total { get { return Die1 + Die2; } }
        public override string Describe(GameState s)
        {
            if (Controlled) return string.Format("{0}님이 주사위를 조작해 {1}칸 이동", s.PlayerName(PlayerId), Total);
            return string.Format("{0}님 주사위 {1} + {2} = {3}{4}", s.PlayerName(PlayerId), Die1, Die2, Total, IsDouble ? " (더블!)" : "");
        }
    }

    public sealed class TokenMovedEvent : GameEvent
    {
        public int PlayerId;
        public int From;
        public int[] Path;
        public bool PassedStart;
        public int To { get { return Path.Length > 0 ? Path[Path.Length - 1] : From; } }
        public override string Describe(GameState s) { return string.Format("{0}님 → {1}", s.PlayerName(PlayerId), s.Tile(To).Name); }
    }

    public sealed class SalaryPaidEvent : GameEvent
    {
        public int PlayerId;
        public long Amount;
        public override string Describe(GameState s) { return string.Format("{0}님 월급 {1}", s.PlayerName(PlayerId), Fmt.SignedWon(Amount)); }
    }

    public sealed class LandBoughtEvent : GameEvent
    {
        public int PlayerId;
        public int TileIndex;
        public long Price;
        public override string Describe(GameState s) { return string.Format("{0}님이 {1}을(를) {2}에 매입", s.PlayerName(PlayerId), s.Tile(TileIndex).Name, Fmt.Won(Price)); }
    }

    public sealed class LandDeclinedEvent : GameEvent
    {
        public int PlayerId;
        public int TileIndex;
        public bool CouldNotAfford;
        public override string Describe(GameState s)
        {
            return CouldNotAfford
                ? string.Format("{0}님은 {1}을(를) 살 돈이 부족하다", s.PlayerName(PlayerId), s.Tile(TileIndex).Name)
                : string.Format("{0}님이 {1} 매입을 패스", s.PlayerName(PlayerId), s.Tile(TileIndex).Name);
        }
    }

    public sealed class BuildingBuiltEvent : GameEvent
    {
        public int PlayerId;
        public int TileIndex;
        public int Level;
        public long Cost;
        public override string Describe(GameState s) { return string.Format("{0}님이 {1}에 {2}단계 건물 건설 ({3})", s.PlayerName(PlayerId), s.Tile(TileIndex).Name, Level, Fmt.Won(Cost)); }
    }

    public sealed class TollPaidEvent : GameEvent
    {
        public int PayerId;
        public int OwnerId;
        public int TileIndex;
        public long Amount;
        public long Due;
        public override string Describe(GameState s)
        {
            return string.Format("{0}님이 {1}님에게 통행료 {2}{3}", s.PlayerName(PayerId), s.PlayerName(OwnerId), Fmt.Won(Amount),
                Amount < Due ? string.Format(" (청구 {0})", Fmt.Won(Due)) : "");
        }
    }

    public sealed class TollWaivedEvent : GameEvent
    {
        public int PlayerId;
        public int TileIndex;
        public long Amount;
        public override string Describe(GameState s) { return string.Format("{0}님이 통행료 면제권으로 {1} 면제", s.PlayerName(PlayerId), Fmt.Won(Amount)); }
    }

    public sealed class CompanyVisitedEvent : GameEvent
    {
        public int PlayerId;
        public int CompanyId;
        public override string Describe(GameState s) { return string.Format("{0}님이 {1} 본사 방문", s.PlayerName(PlayerId), s.CompanyName(CompanyId)); }
    }

    public sealed class ItemGainedEvent : GameEvent
    {
        public int PlayerId;
        public ItemType Item;
        public bool Discarded; // 가방이 가득 차서 버림
        public override string Describe(GameState s)
        {
            return Discarded
                ? string.Format("{0}님 아이템 {1}... 가방이 가득 차서 버렸다", s.PlayerName(PlayerId), ItemInfo.Name(Item))
                : string.Format("{0}님 아이템 획득: {1}", s.PlayerName(PlayerId), ItemInfo.Name(Item));
        }
    }

    public sealed class CashBonusEvent : GameEvent
    {
        public int PlayerId;
        public long Amount;
        public string Reason;
        public override string Describe(GameState s) { return string.Format("{0}님 {1} {2}", s.PlayerName(PlayerId), Reason, Fmt.SignedWon(Amount)); }
    }

    public sealed class VacationEvent : GameEvent
    {
        public int PlayerId;
        public override string Describe(GameState s) { return string.Format("{0}님 휴가! 다음 턴은 주사위를 쉰다", s.PlayerName(PlayerId)); }
    }

    public sealed class TradeHaltEvent : GameEvent
    {
        public int PlayerId;
        public int Turns;
        public override string Describe(GameState s) { return string.Format("{0}님 거래정지! {1}턴 동안 주식 매매 불가", s.PlayerName(PlayerId), Turns); }
    }

    public sealed class CoinBetEvent : GameEvent
    {
        public int PlayerId;
        public long Stake;
        public bool Skipped;
        public bool Won;
        public long Delta;
        public override string Describe(GameState s)
        {
            if (Skipped) return string.Format("{0}님은 코인 거래소를 그냥 지나쳤다", s.PlayerName(PlayerId));
            return Won
                ? string.Format("{0}님 코인 대박! {1} → 2배 ({2})", s.PlayerName(PlayerId), Fmt.Won(Stake), Fmt.SignedWon(Delta))
                : string.Format("{0}님 코인 반토막... {1} ({2})", s.PlayerName(PlayerId), Fmt.Won(Stake), Fmt.SignedWon(Delta));
        }
    }

    public sealed class BonusRollEvent : GameEvent
    {
        public int PlayerId;
        public override string Describe(GameState s) { return string.Format("더블! {0}님 한 번 더", s.PlayerName(PlayerId)); }
    }

    public sealed class StockTradedEvent : GameEvent
    {
        public int PlayerId;
        public int CompanyId;
        public int Quantity;
        public long Price;
        public bool IsBuy;
        public bool Forced;     // 현금 부족으로 자동 처분
        public bool FireSale;   // 잠긴 주식 반값 처분
        /// <summary>Price 는 실제 체결 단가 (반값 처분이면 이미 절반 가격).</summary>
        public long Total { get { return Price * Quantity; } }
        public override string Describe(GameState s)
        {
            string verb = IsBuy ? "매수" : FireSale ? "강제 처분(반값)" : Forced ? "자동 매도" : "매도";
            return string.Format("{0}님 {1} {2}주 {3} @ {4}", s.PlayerName(PlayerId), s.CompanyName(CompanyId), Fmt.Num(Quantity), verb, Fmt.Won(Price));
        }
    }

    public sealed class LandSoldEvent : GameEvent
    {
        public int PlayerId;
        public int TileIndex;
        public long Amount;
        public bool Forced;
        public override string Describe(GameState s)
        {
            return string.Format("{0}님이 {1}을(를) 은행에 {2}에 매각{3}", s.PlayerName(PlayerId), s.Tile(TileIndex).Name, Fmt.Won(Amount), Forced ? " (현금 부족)" : "");
        }
    }

    public sealed class BailoutEvent : GameEvent
    {
        public int PlayerId;
        public long Unpaid;
        public long Cash;
        public long Debt;
        public override string Describe(GameState s)
        {
            return string.Format("{0}님 파산! 구제금융 {1} 지급, 부채 {2} (최종 총자산에서 차감)", s.PlayerName(PlayerId), Fmt.Won(Cash), Fmt.Won(Debt));
        }
    }

    public sealed class ItemUsedEvent : GameEvent
    {
        public int PlayerId;
        public ItemType Item;
        public int CompanyId = -1;
        public int Param;
        public override string Describe(GameState s)
        {
            string name = s.PlayerName(PlayerId);
            switch (Item)
            {
                case ItemType.Rumor:
                    return string.Format("{0}님이 {1} {2} 찌라시를 퍼뜨렸다", name, s.CompanyName(CompanyId), Param >= 0 ? "호재" : "악재");
                case ItemType.Short:
                    return string.Format("{0}님이 {1}에 공매도를 걸었다", name, s.CompanyName(CompanyId));
                case ItemType.InsiderInfo:
                    return string.Format("{0}님이 {1}의 내부자 정보를 입수했다", name, s.CompanyName(CompanyId));
                default:
                    return string.Format("{0}님 {1} 사용", name, ItemInfo.Name(Item));
            }
        }
    }

    public sealed class InsiderInfoEvent : GameEvent
    {
        public int PlayerId;
        public int CompanyId;
        public int Direction;
        public override string Describe(GameState s)
        {
            return string.Format("[비밀] {0}은(는) 다음 장 마감에 {1}", s.CompanyName(CompanyId), Direction > 0 ? "오른다" : "내린다");
        }
    }

    public sealed class TaxAuditEvent : GameEvent
    {
        public int UserId;
        public int TargetId;
        public long Amount;
        public override string Describe(GameState s) { return string.Format("세무조사! {0}님이 세금 {1}을(를) 냈다", s.PlayerName(TargetId), Fmt.Won(Amount)); }
    }

    public sealed class TurnEndedEvent : GameEvent
    {
        public int PlayerId;
        public override string Describe(GameState s) { return string.Format("{0}님 턴 종료", s.PlayerName(PlayerId)); }
    }

    public enum NewsKind
    {
        SectorBoom,
        SectorBust,
        MarketRally,
        MarketCrash,
    }

    public sealed class NewsEvent : GameEvent
    {
        public NewsKind Kind;
        public Sector Sector;
        public double Effect;
        public string Headline
        {
            get
            {
                switch (Kind)
                {
                    case NewsKind.SectorBoom: return string.Format("[속보] {0} 업종 호황!", BoardDefinition.SectorName(Sector));
                    case NewsKind.SectorBust: return string.Format("[속보] {0} 업종 불황...", BoardDefinition.SectorName(Sector));
                    case NewsKind.MarketRally: return "[속보] 증시 대폭등! 전 종목 상승 압력";
                    case NewsKind.MarketCrash: return "[속보] 검은 월요일! 전 종목 폭락 압력";
                    default: return "[속보]";
                }
            }
        }
        public override string Describe(GameState s) { return Headline + " (" + Fmt.Percent(Effect) + ")"; }
    }

    [Serializable]
    public sealed class PriceChange
    {
        public int CompanyId;
        public long OldPrice;
        public long NewPrice;
        public double Rate;
        public int Investors;
        public double UpChance;
        public bool LimitUp;
        public bool LimitDown;
        public bool Delisted;
    }

    public sealed class MarketClosedEvent : GameEvent
    {
        public int Round;
        public List<PriceChange> Changes = new List<PriceChange>();
        public override string Describe(GameState s) { return string.Format("{0}라운드 장 마감", Round); }
    }

    public sealed class DelistedEvent : GameEvent
    {
        public int CompanyId;
        public long LastPrice;
        public override string Describe(GameState s) { return string.Format("{0} 상장폐지! 보유 주식은 휴지조각이 되었다", s.CompanyName(CompanyId)); }
    }

    public sealed class ShortSettledEvent : GameEvent
    {
        public int PlayerId;
        public int CompanyId;
        public bool Success;
        public long Payout;
        public override string Describe(GameState s)
        {
            return Success
                ? string.Format("{0}님 {1} 공매도 성공! {2}", s.PlayerName(PlayerId), s.CompanyName(CompanyId), Fmt.SignedWon(Payout))
                : string.Format("{0}님 {1} 공매도 실패", s.PlayerName(PlayerId), s.CompanyName(CompanyId));
        }
    }

    [Serializable]
    public sealed class AssetBreakdown
    {
        public int PlayerId;
        public int Rank;
        public long Cash;
        public long Stocks;
        public long Land;
        public long Debt;
        public long Total { get { return Cash + Stocks + Land - Debt; } }
    }

    public sealed class GameEndedEvent : GameEvent
    {
        public List<AssetBreakdown> Rankings = new List<AssetBreakdown>();
        public override string Describe(GameState s)
        {
            return Rankings.Count > 0
                ? string.Format("게임 종료! 우승: {0}님 ({1})", s.PlayerName(Rankings[0].PlayerId), Fmt.Won(Rankings[0].Total))
                : "게임 종료";
        }
    }
}
