using StockDice.Core;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>색상표. 한국식 주식 색: 상승 빨강, 하락 파랑.</summary>
    public static class Palette
    {
        public static readonly Color Up = Hex("#E5484D");
        public static readonly Color Down = Hex("#2F7BF5");
        public static readonly Color Flat = Hex("#868E96");

        public static readonly Color Background = Hex("#BFE3F5");
        public static readonly Color Table = Hex("#8DC7E8");
        public static readonly Color BoardBase = Hex("#FFF8EC");
        public static readonly Color BoardCenter = Hex("#F3EADB");
        public static readonly Color TileTop = Hex("#FFFFFF");
        public static readonly Color TileSide = Hex("#E9E2D6");
        public static readonly Color Ink = Hex("#1F2937");
        public static readonly Color InkSoft = Hex("#6B7280");
        public static readonly Color Gold = Hex("#FFC93C");
        public static readonly Color GoldDark = Hex("#E0A100");
        public static readonly Color Gain = Hex("#12B886");

        public static readonly Color PanelBg = new Color(1f, 1f, 1f, 0.94f);
        public static readonly Color PanelDark = new Color(0.12f, 0.16f, 0.23f, 0.92f);
        public static readonly Color ButtonPrimary = Hex("#FF7A45");
        public static readonly Color ButtonNeutral = Hex("#E9ECEF");
        public static readonly Color ButtonDark = Hex("#343A40");

        private static readonly Color[] PlayerColors =
        {
            Hex("#FF6B6B"), Hex("#4DABF7"), Hex("#51CF66"), Hex("#FCC419"), Hex("#B197FC"), Hex("#FF922B"),
        };

        private static readonly Color[] SideColors =
        {
            Hex("#FFC9A3"), Hex("#A9E5B4"), Hex("#9FD3FF"), Hex("#CDB8FF"),
        };

        public static Color Player(int index)
        {
            return PlayerColors[((index % PlayerColors.Length) + PlayerColors.Length) % PlayerColors.Length];
        }

        public static Color Side(int side)
        {
            return SideColors[((side % SideColors.Length) + SideColors.Length) % SideColors.Length];
        }

        public static Color SectorColor(Sector sector)
        {
            switch (sector)
            {
                case StockDice.Core.Sector.IT: return Hex("#5C7CFA");
                case StockDice.Core.Sector.Bio: return Hex("#20C997");
                case StockDice.Core.Sector.Energy: return Hex("#FAB005");
                case StockDice.Core.Sector.Food: return Hex("#FF8787");
                case StockDice.Core.Sector.Entertainment: return Hex("#CC5DE8");
                default: return Color.gray;
            }
        }

        public static Color Change(double rate)
        {
            if (rate > 0.0001) return Up;
            if (rate < -0.0001) return Down;
            return Flat;
        }

        public static string ChangeArrow(double rate)
        {
            if (rate > 0.0001) return "▲";
            if (rate < -0.0001) return "▼";
            return "-";
        }

        public static string ToHex(Color c)
        {
            return ColorUtility.ToHtmlStringRGB(c);
        }

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        public static Color Lighten(Color c, float t)
        {
            return Color.Lerp(c, Color.white, t);
        }

        public static Color Darken(Color c, float t)
        {
            return Color.Lerp(c, Color.black, t);
        }
    }
}
