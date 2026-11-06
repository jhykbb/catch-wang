namespace StockDice.Core
{
    /// <summary>🟡 아이템 8종.</summary>
    public enum ItemType
    {
        CashGrant,      // 지원금: 얻는 즉시 현금 (보관하지 않음)
        InsiderInfo,    // 내부자 정보: 한 종목의 다음 장 마감 방향 미리 보기
        Rumor,          // 찌라시: 한 종목의 다음 상승 확률 ±25%p
        LockupRelease,  // 보호예수 해제권: 내 잠긴 주식 전부 해제
        Short,          // 공매도권: 다음 장 마감에 그 종목이 떨어지면 돈을 받음
        TollWaiver,     // 통행료 면제권: 통행료를 낼 때 자동 사용
        TaxAudit,       // 세무조사: 나를 뺀 현금 1등이 현금의 10%를 세금으로 냄
        DiceControl,    // 주사위 조작: 원하는 합(2~12)으로 이동 (더블 판정 없음)
    }

    public enum ItemTiming
    {
        Instant,    // 얻는 즉시 효과
        Passive,    // 조건이 맞으면 자동
        Active,     // 자기 턴에 직접 사용
        BeforeRoll, // 주사위 굴리기 전에만
    }

    public static class ItemInfo
    {
        public static readonly ItemType[] All =
        {
            ItemType.CashGrant, ItemType.InsiderInfo, ItemType.Rumor, ItemType.LockupRelease,
            ItemType.Short, ItemType.TollWaiver, ItemType.TaxAudit, ItemType.DiceControl,
        };

        public static string Name(ItemType item)
        {
            switch (item)
            {
                case ItemType.CashGrant: return "지원금";
                case ItemType.InsiderInfo: return "내부자 정보";
                case ItemType.Rumor: return "찌라시";
                case ItemType.LockupRelease: return "보호예수 해제권";
                case ItemType.Short: return "공매도권";
                case ItemType.TollWaiver: return "통행료 면제권";
                case ItemType.TaxAudit: return "세무조사";
                case ItemType.DiceControl: return "주사위 조작";
                default: return item.ToString();
            }
        }

        public static string Description(ItemType item)
        {
            switch (item)
            {
                case ItemType.CashGrant: return "얻는 즉시 시작 자금의 10%를 받는다.";
                case ItemType.InsiderInfo: return "한 종목이 다음 장 마감에 오를지 내릴지 나만 미리 본다.";
                case ItemType.Rumor: return "한 종목의 다음 상승 확률을 25%p 올리거나 내린다.";
                case ItemType.LockupRelease: return "내 잠긴 주식을 전부 바로 팔 수 있게 한다.";
                case ItemType.Short: return "고른 종목이 다음 장 마감에 떨어지면 시작 자금의 20%를 받는다.";
                case ItemType.TollWaiver: return "통행료를 낼 때 자동으로 사용되어 한 번 면제된다.";
                case ItemType.TaxAudit: return "나를 뺀 현금 1등이 현금의 10%를 세금으로 낸다.";
                case ItemType.DiceControl: return "주사위를 굴리지 않고 원하는 칸 수(2~12)만큼 이동한다.";
                default: return "";
            }
        }

        public static ItemTiming Timing(ItemType item)
        {
            switch (item)
            {
                case ItemType.CashGrant: return ItemTiming.Instant;
                case ItemType.TollWaiver: return ItemTiming.Passive;
                case ItemType.DiceControl: return ItemTiming.BeforeRoll;
                default: return ItemTiming.Active;
            }
        }

        public static bool NeedsCompany(ItemType item)
        {
            return item == ItemType.InsiderInfo || item == ItemType.Rumor || item == ItemType.Short;
        }
    }
}
