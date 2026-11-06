using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    public enum TileType
    {
        Start,          // 출발 (월급)
        Land,           // 땅
        Company,        // 회사
        Item,           // 아이템 (행운 상자)
        Vacation,       // 휴가
        TradeHalt,      // 거래정지
        CoinExchange,   // 코인 거래소 (4번째 코너, 🟡)
    }

    public enum Sector
    {
        IT,
        Bio,
        Energy,
        Food,
        Entertainment,
    }

    [Serializable]
    public sealed class TileDef
    {
        public int Index;
        public TileType Type;
        public string Name;
        /// <summary>땅 칸만 사용.</summary>
        public int LandPrice;
        /// <summary>회사 칸만 사용. 아니면 -1.</summary>
        public int CompanyId = -1;
        /// <summary>보드의 몇 번째 변인지 (0~3). 화면 색상 구분용.</summary>
        public int Side;
    }

    [Serializable]
    public sealed class CompanyDef
    {
        public int Id;
        public string Name;
        public Sector Sector;
        /// <summary>신규 상장가 (🟡 업종별 기본가)</summary>
        public int ListingPrice;
        public int TileIndex;
    }

    [Serializable]
    public sealed class BoardDefinition
    {
        public List<TileDef> Tiles = new List<TileDef>();
        public List<CompanyDef> Companies = new List<CompanyDef>();
        public int StartIndex;

        public int TileCount { get { return Tiles.Count; } }

        public static string SectorName(Sector s)
        {
            switch (s)
            {
                case Sector.IT: return "IT";
                case Sector.Bio: return "바이오";
                case Sector.Energy: return "에너지";
                case Sector.Food: return "식품";
                case Sector.Entertainment: return "엔터";
                default: return s.ToString();
            }
        }

        public static string TileTypeName(TileType t)
        {
            switch (t)
            {
                case TileType.Start: return "출발";
                case TileType.Land: return "땅";
                case TileType.Company: return "회사";
                case TileType.Item: return "행운 상자";
                case TileType.Vacation: return "휴가";
                case TileType.TradeHalt: return "거래정지";
                case TileType.CoinExchange: return "코인 거래소";
                default: return t.ToString();
            }
        }

        /// <summary>
        /// 🟡 기본 보드 32칸 (한 변 8칸: 코너 1 + 일반 7).
        /// 각 변의 일반 칸 배치: 땅, 회사, 땅, 아이템, 땅, 회사, 땅 → 땅 16, 회사 8, 아이템 4.
        /// 회사, 땅 이름은 전부 가상이다.
        /// </summary>
        public static BoardDefinition CreateDefault()
        {
            var board = new BoardDefinition();

            string[] landNames =
            {
                "이끼골", "조약돌길", "민들레동", "개울목",
                "은행나무로", "노을언덕", "풍차마을", "등대곶",
                "별빛광장", "구름다리", "호수공원", "유리탑거리",
                "무지개타워", "왕관스퀘어", "다이아힐", "하늘정원",
            };

            // 🟡 회사 8개, 업종 5개. 상장가 600~1,400원.
            var companies = new[]
            {
                new CompanyDef { Name = "냠냠푸드", Sector = Sector.Food, ListingPrice = 600 },
                new CompanyDef { Name = "쑥쑥바이오", Sector = Sector.Bio, ListingPrice = 800 },
                new CompanyDef { Name = "쨍쨍에너지", Sector = Sector.Energy, ListingPrice = 1000 },
                new CompanyDef { Name = "반짝엔터", Sector = Sector.Entertainment, ListingPrice = 900 },
                new CompanyDef { Name = "두근소프트", Sector = Sector.IT, ListingPrice = 1200 },
                new CompanyDef { Name = "말끔제약", Sector = Sector.Bio, ListingPrice = 1000 },
                new CompanyDef { Name = "씽씽풍력", Sector = Sector.Energy, ListingPrice = 1100 },
                new CompanyDef { Name = "뚝딱코드", Sector = Sector.IT, ListingPrice = 1400 },
            };

            TileType[] corners = { TileType.Start, TileType.Vacation, TileType.TradeHalt, TileType.CoinExchange };
            TileType[] sidePattern =
            {
                TileType.Land, TileType.Company, TileType.Land, TileType.Item,
                TileType.Land, TileType.Company, TileType.Land,
            };

            int landCursor = 0;
            int companyCursor = 0;
            for (int side = 0; side < 4; side++)
            {
                var corner = new TileDef
                {
                    Index = board.Tiles.Count,
                    Type = corners[side],
                    Name = TileTypeName(corners[side]),
                    Side = side,
                };
                board.Tiles.Add(corner);

                for (int k = 0; k < sidePattern.Length; k++)
                {
                    var tile = new TileDef { Index = board.Tiles.Count, Type = sidePattern[k], Side = side };
                    switch (tile.Type)
                    {
                        case TileType.Land:
                            tile.Name = landNames[landCursor];
                            tile.LandPrice = 1000 + 200 * landCursor; // 🟡 1,000 ~ 4,000원
                            landCursor++;
                            break;
                        case TileType.Company:
                            var c = companies[companyCursor];
                            c.Id = companyCursor;
                            c.TileIndex = tile.Index;
                            board.Companies.Add(c);
                            tile.CompanyId = c.Id;
                            tile.Name = c.Name;
                            companyCursor++;
                            break;
                        case TileType.Item:
                            tile.Name = TileTypeName(TileType.Item);
                            break;
                    }
                    board.Tiles.Add(tile);
                }
            }

            board.StartIndex = 0;
            return board;
        }
    }
}
