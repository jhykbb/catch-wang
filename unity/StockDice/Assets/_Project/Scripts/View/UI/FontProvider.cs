using System.Collections.Generic;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>
    /// 한글 폰트 처리.
    /// 1순위: Assets/_Project/Resources/Fonts/MainFont.ttf (출시 빌드에는 반드시 OFL 한글 폰트를 넣는다. 예: Pretendard, Noto Sans KR)
    /// 2순위: OS 에 설치된 한글 폰트 (맑은 고딕, Apple SD 산돌고딕 Neo, Noto Sans CJK KR, 나눔고딕)
    /// 3순위: Unity 기본 폰트 (한글이 깨질 수 있음 → 경고 로그)
    /// </summary>
    public static class FontProvider
    {
        private static Font _font;

        private static readonly string[] KoreanOsFonts =
        {
            "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleSDGothicNeo-Regular", "AppleGothic",
            "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Nanum Gothic", "나눔고딕", "Gulim", "굴림", "Dotum", "돋움",
        };

        public static Font Get()
        {
            if (_font != null) return _font;

            _font = Resources.Load<Font>("Fonts/MainFont");
            if (_font != null) return _font;

            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            var available = new List<string>();
            foreach (var name in KoreanOsFonts) if (installed.Contains(name)) available.Add(name);
            if (available.Count > 0)
            {
                _font = Font.CreateDynamicFontFromOSFont(available.ToArray(), 32);
                if (_font != null) return _font;
            }

            Debug.LogWarning("[StockDice] 한글 폰트를 찾지 못했습니다. Assets/_Project/Resources/Fonts/MainFont.ttf 에 한글 폰트를 넣어 주세요.");
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 32);
            return _font;
        }
    }
}
