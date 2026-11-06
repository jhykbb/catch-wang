using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    public enum PlayerKind
    {
        Human,
        Ai,
    }

    public enum AiStyle
    {
        Hodler,     // 존버형
        Trader,     // 단타형
        Landlord,   // 땅부자형
        Ant,        // 개미형 (뇌동매매)
    }

    [Serializable]
    public sealed class PlayerSetup
    {
        public string Name;
        public PlayerKind Kind;
        public AiStyle Style;

        public PlayerSetup() { }

        public PlayerSetup(string name, PlayerKind kind, AiStyle style = AiStyle.Hodler)
        {
            Name = name;
            Kind = kind;
            Style = style;
        }

        public static string StyleName(AiStyle style)
        {
            switch (style)
            {
                case AiStyle.Hodler: return "존버형";
                case AiStyle.Trader: return "단타형";
                case AiStyle.Landlord: return "땅부자형";
                case AiStyle.Ant: return "개미형";
                default: return style.ToString();
            }
        }
    }

    /// <summary>주가 확률표의 한 점. 투자자 비율(투자자 수 ÷ 인원)에 대해 선형 보간한다.</summary>
    [Serializable]
    public sealed class StockOddsPoint
    {
        public double InvestorRatio;
        public double UpChance;
        public double Volatility;

        public StockOddsPoint() { }

        public StockOddsPoint(double ratio, double up, double vol)
        {
            InvestorRatio = ratio;
            UpChance = up;
            Volatility = vol;
        }
    }

    /// <summary>
    /// 게임 규칙의 모든 숫자. 기본값은 RULES.md 의 ✅/🟡 값과 같다.
    /// 인원 수는 여기서 하드코딩하지 않는다. (5~6인 확장 대비)
    /// </summary>
    [Serializable]
    public sealed class GameConfig
    {
        public const int MinPlayers = 2;
        /// <summary>현재 지원하는 최대 인원. 5~6인 업데이트 시 이 값만 올리면 된다.</summary>
        public static int MaxPlayers = 4;

        // ---- 기본 설정 (사설 방에서 변경 가능) ----
        public int StartingCash = 10000;            // ✅
        public int Rounds = 20;                     // ✅
        public double SalaryRate = 0.20;            // ✅ 시작 자금의 20%
        public ulong Seed = 1;

        // ---- 주사위 ----
        public int DiceSides = 6;
        /// <summary>🟡 더블이면 한 번 더 굴림. 턴당 최대 추가 횟수.</summary>
        public int MaxBonusRolls = 1;

        // ---- 주식 ----
        public int InitialListedExtra = 1;          // ✅ 시작 상장 수 = 인원 + 1
        public int LockupTurns = 2;                 // ✅ 다다음 자기 턴부터 매도
        /// <summary>🟡 투자자로 인정받는 최소 보유 평가액 (시작 자금 대비). 1주 무임승차 방지.</summary>
        public double InvestorThresholdRate = 0.10;
        /// <summary>🟡 4인 기준 표를 투자자 비율로 바꾼 것.</summary>
        public List<StockOddsPoint> OddsTable = new List<StockOddsPoint>
        {
            new StockOddsPoint(0.00, 0.40, 0.25),
            new StockOddsPoint(0.25, 0.45, 0.25),
            new StockOddsPoint(0.50, 0.55, 0.15),
            new StockOddsPoint(0.75, 0.65, 0.10),
            new StockOddsPoint(1.00, 0.75, 0.06),
        };
        /// <summary>🟡 실제 변동폭 = 표의 변동폭 × 랜덤(MinMoveFactor ~ 1.0)</summary>
        public double MinMoveFactor = 0.5;
        public double PriceLimit = 0.30;            // 🟡 상한가/하한가
        public int FinalStretchRounds = 3;          // 🟡 마감 임박
        public double FinalStretchMultiplier = 1.5; // 🟡
        public double DelistRatio = 0.10;           // 🟡 상장가의 10% 미만이면 상장폐지
        public int PriceTick = 10;                  // 🟡 주가 단위 (10원)
        /// <summary>🟡 업종별 변동폭 배율.</summary>
        public double[] SectorVolatility = { 1.0, 1.2, 0.9, 0.8, 1.1 }; // IT, 바이오, 에너지, 식품, 엔터

        // ---- 뉴스 (🟡) ----
        public double NewsChance = 0.20;
        public double NewsSectorMin = 0.10;
        public double NewsSectorMax = 0.20;
        public double NewsMarketWideShare = 0.15;   // 뉴스 중 전 종목 이벤트 비율
        public double NewsMarketWideEffect = 0.12;

        // ---- 땅 ----
        /// <summary>
        /// 🟡 통행료 = 땅값 × 비율. [빈 땅, 1단계, 2단계, 3단계]
        /// v0.1 의 10/30/70/120% 는 시뮬레이션에서 땅이 너무 약해서(땅 위주 승률 15%) 두 배로 올렸다.
        /// </summary>
        public double[] TollRates = { 0.20, 0.60, 1.20, 2.00 };
        public double BuildCostRate = 0.50;         // 🟡 건물 한 단계 = 땅값의 50%
        public double BankSellRate = 0.50;          // 🟡 은행 매각 = 투자액의 50%
        /// <summary>🟡 (선택 규칙) 턴 종료 때 내 땅 아무 곳에나 건물을 지을 수 있다. false 면 다시 밟았을 때만.</summary>
        public bool BuildAtTurnEnd = false;
        public int MaxTurnEndBuilds = 1;

        // ---- 현금 부족 / 파산 (🟡) ----
        public double LockedFireSaleRate = 0.50;    // 잠긴 주식 강제 처분가
        public double BailoutCashRate = 0.20;       // 구제금융 지급액 (시작 자금 대비)
        public double BailoutDebtRate = 0.30;       // 구제금융 부채 (총자산에서 차감)

        // ---- 특수 칸 (🟡) ----
        public int VacationSkipTurns = 1;
        public int TradeHaltTurns = 2;              // 이번 턴 + 다음 턴 매매 불가
        public double CoinWinChance = 0.40;         // 2배 확률 (나머지는 반토막)
        public double[] CoinStakeRates = { 0.10, 0.30, 0.50 };

        // ---- 아이템 (🟡) ----
        public int ItemHoldLimit = 3;
        public double ItemCashGrantRate = 0.10;     // 지원금
        public double RumorBoost = 0.25;            // 찌라시: 상승 확률 ±25%p
        public double ShortPayoutRate = 0.20;       // 공매도 성공 시 시작 자금의 20%
        public double TaxRate = 0.10;               // 세무조사: 대상 현금의 10%

        public long Salary { get { return (long)Math.Round(StartingCash * SalaryRate); } }
        public int MaxBuildingLevel { get { return TollRates.Length - 1; } }
        public long InvestorThreshold { get { return (long)Math.Round(StartingCash * InvestorThresholdRate); } }

        public BoardDefinition Board = BoardDefinition.CreateDefault();

        public GameConfig Clone()
        {
            var c = (GameConfig)MemberwiseClone();
            c.OddsTable = new List<StockOddsPoint>();
            foreach (var p in OddsTable) c.OddsTable.Add(new StockOddsPoint(p.InvestorRatio, p.UpChance, p.Volatility));
            c.SectorVolatility = (double[])SectorVolatility.Clone();
            c.TollRates = (double[])TollRates.Clone();
            c.CoinStakeRates = (double[])CoinStakeRates.Clone();
            return c;
        }

        public string Validate(int playerCount)
        {
            if (playerCount < MinPlayers || playerCount > MaxPlayers)
                return string.Format("인원은 {0}~{1}명이어야 합니다.", MinPlayers, MaxPlayers);
            if (StartingCash < 1000) return "시작 자금은 1,000원 이상이어야 합니다.";
            if (Rounds < 1 || Rounds > 200) return "라운드 수는 1~200 사이여야 합니다.";
            if (Board == null || Board.Tiles.Count < 8) return "보드 정의가 올바르지 않습니다.";
            if (playerCount + InitialListedExtra > Board.Companies.Count)
                return "회사 수가 시작 상장 수보다 적습니다.";
            if (OddsTable == null || OddsTable.Count < 2) return "주가 확률표가 비어 있습니다.";
            return null;
        }
    }
}
