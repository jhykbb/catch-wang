using System.Collections;
using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>
    /// 게임 중 화면: 상단 바, 플레이어 카드, 주식 시장 패널, 행동 패널, 로그, 배너/토스트/떠오르는 글자.
    /// 기준 해상도 1920x1080 (CanvasScaler 로 늘고 줄어든다).
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private GameSession _s;
        private RectTransform _root;
        private RectTransform _popupLayer;
        private RectTransform _floatLayer;

        private Text _roundText;
        private Text _turnText;
        private Image _finalBadge;
        private Button _speedButton;
        private readonly List<PlayerCard> _cards = new List<PlayerCard>();
        private Text _logText;
        private readonly LinkedList<string> _logLines = new LinkedList<string>();
        private RectTransform _bannerRoot;
        private Image _bannerBg;
        private Text _bannerText;
        private RectTransform _toastRoot;
        private Text _toastText;
        private Image _toastBg;
        private Coroutine _toastRoutine;

        public StockPanel Market { get; private set; }
        public ActionPanel Actions { get; private set; }
        public GameSession Session { get { return _s; } }
        public RectTransform PopupLayer { get { return _popupLayer; } }

        public static HudView Create(RectTransform uiRoot, GameSession session)
        {
            var go = UIKit.Create("HUD", uiRoot);
            var hud = go.AddComponent<HudView>();
            hud._s = session;
            hud._root = (RectTransform)go.transform;
            UIKit.Fill(hud._root);
            hud.Build();
            return hud;
        }

        private void Build()
        {
            BuildTopBar();
            BuildCards();
            BuildLog();
            Market = StockPanel.Create(_root, this);
            Actions = ActionPanel.Create(_root, this);

            var floatGo = UIKit.Create("FloatLayer", _root);
            _floatLayer = UIKit.Fill(floatGo.GetComponent<RectTransform>());
            BuildBanner();
            BuildToast();
            var popupGo = UIKit.Create("PopupLayer", _root);
            _popupLayer = UIKit.Fill(popupGo.GetComponent<RectTransform>());
            Refresh();
        }

        // ------------------------------------------------------------------
        // 상단 바
        // ------------------------------------------------------------------

        private void BuildTopBar()
        {
            var bar = UIKit.Panel(_root, "TopBar", Palette.PanelDark, UIKit.RoundedSmall);
            UIKit.Place(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -70f), new Vector2(-12f, -8f));

            _roundText = UIKit.Label(bar.transform, "", 28, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Place(_roundText, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(24f, 0f), new Vector2(330f, 0f));

            _finalBadge = UIKit.Panel(bar.transform, "FinalBadge", Palette.Up, UIKit.RoundedSmall);
            UIKit.PlaceAt(_finalBadge, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(250f, 0f), new Vector2(150f, 40f));
            var badgeText = UIKit.Label(_finalBadge.transform, "마감 임박", 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Fill(badgeText);

            _turnText = UIKit.Label(bar.transform, "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(_turnText, new Vector2(0.25f, 0f), new Vector2(0.75f, 1f), Vector2.zero, Vector2.zero);

            var menu = UIKit.Button(bar.transform, "메뉴", Palette.ButtonDark, Color.white, OpenMenu, 22);
            UIKit.PlaceAt(menu, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(100f, 44f));
            _speedButton = UIKit.Button(bar.transform, "속도 x1", Palette.ButtonDark, Color.white, CycleSpeed, 22);
            UIKit.PlaceAt(_speedButton, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-124f, 0f), new Vector2(130f, 44f));
        }

        private void CycleSpeed()
        {
            _s.Speed = _s.Speed >= 4f ? 1f : _s.Speed * 2f;
            UIKit.ButtonLabel(_speedButton).text = "속도 x" + (int)_s.Speed;
        }

        private void OpenMenu()
        {
            Popup.Show(_popupLayer, "메뉴", "진행 중인 게임은 저장되지 않습니다.", new List<PopupChoice>
            {
                new PopupChoice("계속하기", null, Palette.ButtonPrimary),
                new PopupChoice("같은 설정으로 다시 시작", () => _s.Restart()),
                new PopupChoice("처음 화면으로", () => _s.QuitToMenu()),
            });
        }

        public void SetTurn(int playerId)
        {
            var st = _s.State;
            var p = st.Players[playerId];
            string phase = st.Phase == GamePhase.Ipo || st.Round == 0 ? " · 공모주 청약" : "";
            _turnText.text = UIKit.Colored("●", Palette.Player(playerId)) + " " + p.Name + "님의 차례" + phase;
            for (int i = 0; i < _cards.Count; i++) _cards[i].SetCurrent(i == playerId);
        }

        private void RefreshTop()
        {
            var st = _s.State;
            _roundText.text = st.Round <= 0 ? "공모주 청약" : string.Format("{0} / {1} 라운드", st.Round, st.Config.Rounds);
            _finalBadge.gameObject.SetActive(st.IsFinalStretch && st.Phase != GamePhase.GameOver);
            if (st.Phase != GamePhase.NotStarted) SetTurn(st.CurrentPlayer);
        }

        // ------------------------------------------------------------------
        // 플레이어 카드
        // ------------------------------------------------------------------

        private sealed class PlayerCard
        {
            public RectTransform Root;
            public Image Bg;
            public Image Outline;
            public Text Name;
            public Text Rank;
            public CountUpText Total;
            public CountUpText Cash;
            public Text Status;
            public bool Current;
            public float Pulse;

            public void SetCurrent(bool current)
            {
                Current = current;
                Outline.enabled = current;
            }
        }

        private void BuildCards()
        {
            int n = _s.State.PlayerCount;
            float available = 1080f - 86f - 210f;
            float h = Mathf.Min(150f, available / n - 10f);
            for (int i = 0; i < n; i++)
            {
                var card = new PlayerCard();
                var outline = UIKit.Panel(_root, "CardOutline" + i, Palette.Player(i));
                UIKit.PlaceAt(outline, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -84f - i * (h + 10f)), new Vector2(352f, h + 8f));
                card.Outline = outline;
                card.Root = outline.rectTransform;

                var bg = UIKit.Panel(outline.transform, "Card", Palette.PanelBg);
                UIKit.Fill(bg, 4, 4, 4, 4);
                card.Bg = bg;
                var stripe = UIKit.Panel(bg.transform, "Stripe", Palette.Player(i), UIKit.RoundedSmall);
                UIKit.Place(stripe, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 10f), new Vector2(18f, -10f));

                var setup = _s.Setups[i];
                string tag = setup.Kind == PlayerKind.Human ? "사람" : "AI · " + PlayerSetup.StyleName(setup.Style);
                card.Name = UIKit.Label(bg.transform, _s.State.Players[i].Name + "  " + UIKit.Colored(tag, Palette.InkSoft), 24, Palette.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
                UIKit.Place(card.Name, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -40f), new Vector2(-70f, -8f));

                var rankBg = UIKit.Panel(bg.transform, "Rank", Palette.ButtonDark, UIKit.RoundedSmall);
                UIKit.PlaceAt(rankBg, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(54f, 30f));
                card.Rank = UIKit.Label(rankBg.transform, "1위", 19, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Fill(card.Rank);

                var total = UIKit.Label(bg.transform, "", 28, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Place(total, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -76f), new Vector2(-10f, -40f));
                card.Total = total.gameObject.AddComponent<CountUpText>();
                card.Total.Target = total;
                card.Total.Format = v => "총자산 " + Fmt.Won(v);

                var cash = UIKit.Label(bg.transform, "", 19, Palette.InkSoft, TextAnchor.MiddleLeft);
                UIKit.Place(cash, new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(28f, -100f), new Vector2(0f, -76f));
                card.Cash = cash.gameObject.AddComponent<CountUpText>();
                card.Cash.Target = cash;
                card.Cash.Format = v => "현금 " + Fmt.Won(v);

                card.Status = UIKit.Label(bg.transform, "", 17, Palette.InkSoft, TextAnchor.UpperLeft);
                UIKit.Place(card.Status, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 4f), new Vector2(-10f, Mathf.Max(22f, h - 104f)));
                if (h < 110f) card.Status.fontSize = 15;

                card.SetCurrent(false);
                _cards.Add(card);
            }
        }

        public void RefreshPlayers()
        {
            var st = _s.State;
            var ranks = Valuation.Rankings(st);
            foreach (var r in ranks)
            {
                var card = _cards[r.PlayerId];
                var p = st.Players[r.PlayerId];
                card.Total.Set(r.Total);
                card.Cash.Set(p.Cash);
                card.Rank.text = r.Rank + "위";
                card.Rank.transform.parent.GetComponent<Image>().color = r.Rank == 1 ? Palette.GoldDark : Palette.ButtonDark;

                var parts = new List<string>();
                parts.Add("주식 " + Fmt.Won(r.Stocks) + " · 땅 " + Fmt.Won(r.Land));
                var flags = new List<string>();
                if (p.SkipTurns > 0) flags.Add(UIKit.Colored("휴가", Palette.Hex("#0CA678")));
                if (p.TradeHaltTurns > 0) flags.Add(UIKit.Colored("거래정지 " + p.TradeHaltTurns, Palette.Up));
                if (p.Debt > 0) flags.Add(UIKit.Colored("부채 " + Fmt.Won(p.Debt), Palette.Up));
                foreach (var item in p.Items) flags.Add(UIKit.Colored("[" + ItemInfo.Name(item) + "]", Palette.Hex("#D6336C")));
                if (flags.Count > 0) parts.Add(string.Join(" ", flags.ToArray()));
                card.Status.text = string.Join("\n", parts.ToArray());
            }
        }

        public void PulseCard(int playerId)
        {
            if (playerId < 0 || playerId >= _cards.Count) return;
            StartCoroutine(Tween.Punch(_cards[playerId].Root, 0.06f, 0.3f));
        }

        // ------------------------------------------------------------------
        // 로그
        // ------------------------------------------------------------------

        private void BuildLog()
        {
            var bg = UIKit.Panel(_root, "Log", new Color(1f, 1f, 1f, 0.78f));
            UIKit.PlaceAt(bg, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 12f), new Vector2(560f, 190f));
            _logText = UIKit.Label(bg.transform, "", 18, Palette.Ink, TextAnchor.LowerLeft);
            _logText.verticalOverflow = VerticalWrapMode.Truncate;
            UIKit.Fill(_logText, 16, 10, 16, 10);
        }

        public void Log(string line, GameEvent e = null)
        {
            if (string.IsNullOrEmpty(line)) return;
            Color c = Palette.Ink;
            if (e is MarketClosedEvent || e is NewsEvent) c = Palette.Hex("#E8590C");
            else if (e is RoundStartedEvent) c = Palette.InkSoft;
            else if (e is TollPaidEvent || e is BailoutEvent || e is DelistedEvent) c = Palette.Up;
            else if (e is CompanyListedEvent) c = Palette.Hex("#7048E8");
            _logLines.AddLast(UIKit.Colored(line, c));
            while (_logLines.Count > 7) _logLines.RemoveFirst();
            _logText.text = string.Join("\n", new List<string>(_logLines).ToArray());
        }

        // ------------------------------------------------------------------
        // 배너 / 토스트 / 떠오르는 글자 / 비밀 정보
        // ------------------------------------------------------------------

        private void BuildBanner()
        {
            var go = UIKit.Create("Banner", _root);
            _bannerRoot = UIKit.PlaceAt(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(2200f, 130f));
            _bannerBg = go.AddComponent<Image>();
            _bannerBg.raycastTarget = false;
            _bannerText = UIKit.Label(go.transform, "", 56, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Fill(_bannerText);
            UIKit.AddShadow(_bannerText, new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, -3f));
            go.SetActive(false);
        }

        public IEnumerator Banner(string text, Color color, float duration)
        {
            _bannerRoot.gameObject.SetActive(true);
            _bannerText.text = text;
            _bannerBg.color = Palette.WithAlpha(color, 0.92f);
            float inTime = Mathf.Min(0.25f, duration * 0.25f);
            float outTime = Mathf.Min(0.2f, duration * 0.2f);
            yield return Tween.Run(inTime, k =>
            {
                _bannerRoot.localScale = new Vector3(1f, Mathf.LerpUnclamped(0f, 1f, k), 1f);
                _bannerText.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-200f, 0f, k), 0f);
            }, Ease.OutBack);
            yield return Tween.Wait(Mathf.Max(0f, duration - inTime - outTime));
            yield return Tween.Run(outTime, k =>
            {
                _bannerRoot.localScale = new Vector3(1f, 1f - k, 1f);
                _bannerText.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(0f, 200f, k), 0f);
            }, Ease.InCubic);
            _bannerRoot.gameObject.SetActive(false);
        }

        private void BuildToast()
        {
            _toastBg = UIKit.Panel(_root, "Toast", Palette.PanelDark);
            _toastRoot = UIKit.PlaceAt(_toastBg, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(760f, 58f));
            _toastBg.raycastTarget = false;
            _toastText = UIKit.Label(_toastBg.transform, "", 24, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Fill(_toastText, 16, 4, 16, 4);
            _toastRoot.gameObject.SetActive(false);
        }

        public void Toast(string text, Color? accent = null, float duration = 1.8f)
        {
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine(text, accent ?? Palette.PanelDark, duration));
        }

        private IEnumerator ToastRoutine(string text, Color accent, float duration)
        {
            _toastRoot.gameObject.SetActive(true);
            _toastText.text = text;
            _toastBg.color = Palette.WithAlpha(Color.Lerp(Palette.PanelDark, accent, 0.65f), 0.95f);
            float width = Mathf.Clamp(_toastText.preferredWidth + 60f, 300f, 940f);
            _toastRoot.sizeDelta = new Vector2(width, 58f);
            yield return Tween.Run(0.15f, k => _toastRoot.anchoredPosition = new Vector2(0f, Mathf.Lerp(-40f, -86f, k)), Ease.OutBack);
            yield return Tween.Wait(duration);
            yield return Tween.Run(0.2f, k => _toastBg.color = Palette.WithAlpha(_toastBg.color, 0.95f * (1f - k)));
            _toastRoot.gameObject.SetActive(false);
            _toastRoutine = null;
        }

        /// <summary>월드 위치 위에 떠올랐다 사라지는 글자.</summary>
        public void Float(Vector3 world, string text, Color color, int size = 34)
        {
            var label = UIKit.Label(_floatLayer, text, size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.rectTransform.sizeDelta = new Vector2(600f, size * 1.5f);
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            UIKit.AddShadow(label, new Color(0f, 0f, 0f, 0.3f), new Vector2(0f, -3f));
            StartCoroutine(FloatRoutine(label, world));
        }

        private IEnumerator FloatRoutine(Text label, Vector3 world)
        {
            var cam = _s.Rig.Cam;
            var rt = label.rectTransform;
            Color baseColor = label.color;
            float duration = 1.25f;
            yield return Tween.Run(duration, k =>
            {
                if (label == null) return;
                Vector2 local;
                Vector3 screen = cam.WorldToScreenPoint(world);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_floatLayer, screen, null, out local);
                rt.anchoredPosition = local + new Vector2(0f, 30f + 70f * Ease.OutCubic(k));
                rt.localScale = Vector3.one * (k < 0.15f ? Ease.OutBack(k / 0.15f) : 1f);
                float a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                label.color = Palette.WithAlpha(baseColor, a);
                if (screen.z < 0f) label.color = Color.clear;
            });
            if (label != null) Destroy(label.gameObject);
        }

        /// <summary>그 플레이어에게만 보여줄 정보 (핫시트에서는 다른 사람이 잠깐 고개를 돌려야 함).</summary>
        public void ShowPrivate(string title, string message)
        {
            Popup.Message(_popupLayer, "[비밀] " + title, message, "확인했어요");
        }

        // ------------------------------------------------------------------
        // 장 마감 연출
        // ------------------------------------------------------------------

        public IEnumerator MarketClose(MarketClosedEvent ev, float speed)
        {
            var st = _s.State;
            var root = UIKit.Panel(_popupLayer, "MarketClose", Palette.PanelBg);
            root.raycastTarget = false;
            int rows = Mathf.Max(1, ev.Changes.Count);
            UIKit.PlaceAt(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(720f, 110f + rows * 58f));
            UIKit.AddShadow(root, new Color(0f, 0f, 0f, 0.25f), new Vector2(0f, -6f));
            var title = UIKit.Label(root.transform, ev.Round + "라운드 장 마감", 34, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -66f), new Vector2(0f, -12f));
            var cg = root.gameObject.AddComponent<CanvasGroup>();
            yield return Tween.Run(0.2f / speed, k => { root.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.8f, 1f, k); cg.alpha = k; }, Ease.OutBack);

            float y = -80f;
            bool bigMove = false;
            foreach (var ch in ev.Changes)
            {
                var row = UIKit.Create("Row", root.transform);
                var rrt = UIKit.Place(row.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, y - 52f), new Vector2(-24f, y));
                var rowBg = row.AddComponent<Image>();
                rowBg.sprite = UIKit.RoundedSmall;
                rowBg.type = Image.Type.Sliced;
                rowBg.color = Palette.WithAlpha(Palette.Change(ch.Rate), 0.12f);
                var def = st.CompanyDef(ch.CompanyId);
                var name = UIKit.Label(row.transform, UIKit.Colored("■ ", Palette.SectorColor(def.Sector)) + def.Name + UIKit.Colored("  투자자 " + ch.Investors + "명", Palette.InkSoft), 24, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.Place(name, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(14f, 0f), Vector2.zero);
                var price = UIKit.Label(row.transform, Fmt.Won(ch.OldPrice) + " → " + Fmt.Won(ch.NewPrice), 22, Palette.Ink, TextAnchor.MiddleRight);
                UIKit.Place(price, new Vector2(0.42f, 0f), new Vector2(0.78f, 1f), Vector2.zero, Vector2.zero);
                string tag = ch.Delisted ? "상장폐지" : ch.LimitUp ? "상한가!" : ch.LimitDown ? "하한가!" : Palette.ChangeArrow(ch.Rate) + " " + Fmt.Percent(ch.Rate);
                var rate = UIKit.Label(row.transform, tag, 24, Palette.Change(ch.Rate), TextAnchor.MiddleRight, FontStyle.Bold);
                UIKit.Place(rate, new Vector2(0.78f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-14f, 0f));
                y -= 58f;

                Market.FlashRow(ch.CompanyId, Palette.Change(ch.Rate));
                bool stamp = ch.LimitUp || ch.LimitDown || ch.Delisted;
                if (stamp)
                {
                    bigMove = true;
                    _s.Rig.Shake(0.18f, 0.3f);
                }
                yield return Tween.Run((stamp ? 0.35f : 0.18f) / speed, k => rrt.localScale = new Vector3(1f, Mathf.LerpUnclamped(0.2f, 1f, k), 1f), Ease.OutBack);
            }

            yield return Tween.Wait((bigMove ? 1.4f : 1.0f) / speed);
            yield return Tween.Run(0.2f / speed, k => cg.alpha = 1f - k);
            Destroy(root.gameObject);
        }

        // ------------------------------------------------------------------
        // 전체 갱신 / 입력
        // ------------------------------------------------------------------

        public void Refresh()
        {
            RefreshTop();
            RefreshPlayers();
            RefreshMarket();
        }

        public void RefreshMarket()
        {
            if (Market != null) Market.Refresh();
        }

        public void FlashStockRow(int companyId, Color color)
        {
            if (Market != null) Market.FlashRow(companyId, color);
        }

        public void ShowControls(int playerId)
        {
            Refresh();
            Actions.ShowFor(playerId);
            Market.SetInteractive(true);
        }

        public void HideControls()
        {
            Actions.ShowBusy();
            Market.SetInteractive(false);
        }

        public void ShowThinking(int playerId)
        {
            Actions.ShowThinking(playerId);
            Market.SetInteractive(false);
        }
    }
}
