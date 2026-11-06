using System;
using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StockDice.View
{
    /// <summary>시작 화면: 인원, 자리별 사람/AI, 시작 자금, 라운드 수 (사설 방 설정).</summary>
    public sealed class StartScreen : MonoBehaviour
    {
        private sealed class Seat
        {
            public GameObject Root;
            public InputField Name;
            public Button Type;
            public int TypeIndex; // 0 = 사람, 1~4 = AI 성향
        }

        private static readonly string[] TypeLabels = { "사람", "AI · 존버형", "AI · 단타형", "AI · 땅부자형", "AI · 개미형" };
        private static readonly AiStyle[] Styles = { AiStyle.Hodler, AiStyle.Trader, AiStyle.Landlord, AiStyle.Ant };
        private static readonly int[] CashPresets = { 5000, 10000, 20000, 50000 };
        private static readonly int[] RoundPresets = { 12, 20, 30 };

        // 마지막 설정 기억
        private static int _savedCount = 4;
        private static int _savedCash = 10000;
        private static int _savedRounds = 20;
        private static string[] _savedNames;
        private static int[] _savedTypes;

        private GameBootstrap _boot;
        private readonly List<Seat> _seats = new List<Seat>();
        private readonly List<Button> _countButtons = new List<Button>();
        private readonly List<Button> _cashButtons = new List<Button>();
        private readonly List<Button> _roundButtons = new List<Button>();
        private InputField _cashInput;
        private InputField _roundInput;
        private Text _error;
        private int _count;
        private int _cash;
        private int _rounds;

        public static StartScreen Create(RectTransform parent, GameBootstrap boot)
        {
            var go = UIKit.Create("StartScreen", parent);
            UIKit.Fill(go.GetComponent<RectTransform>());
            var screen = go.AddComponent<StartScreen>();
            screen._boot = boot;
            screen.Build();
            return screen;
        }

        private void Build()
        {
            _count = Mathf.Clamp(_savedCount, GameConfig.MinPlayers, GameConfig.MaxPlayers);
            _cash = _savedCash;
            _rounds = _savedRounds;
            if (_savedNames == null || _savedNames.Length < GameConfig.MaxPlayers)
            {
                _savedNames = new string[GameConfig.MaxPlayers];
                _savedTypes = new int[GameConfig.MaxPlayers];
                for (int i = 0; i < GameConfig.MaxPlayers; i++)
                {
                    _savedTypes[i] = i == 0 ? 0 : 1 + (i - 1) % Styles.Length;
                    _savedNames[i] = i == 0 ? "나" : "AI " + PlayerSetup.StyleName(Styles[(i - 1) % Styles.Length]);
                }
            }

            var card = UIKit.Panel(transform, "Card", Palette.PanelBg);
            UIKit.PlaceAt(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 900f));
            UIKit.AddShadow(card, new Color(0f, 0f, 0f, 0.25f), new Vector2(0f, -8f));

            var title = UIKit.Label(card.transform, "떡상 다이스", 84, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Place(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -120f), new Vector2(0f, -20f));
            UIKit.AddShadow(title, Palette.WithAlpha(Palette.ButtonPrimary, 0.5f), new Vector2(4f, -4f));
            var sub = UIKit.Label(card.transform, "(가제)  주사위로 보드를 돌며 땅을 사고 주식에 투자하는 경제 보드게임", 22, Palette.InkSoft, TextAnchor.MiddleCenter);
            UIKit.Place(sub, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -160f), new Vector2(0f, -120f));

            float y = -190f;
            // 인원
            SectionLabel(card.transform, "인원", y);
            for (int n = GameConfig.MinPlayers; n <= GameConfig.MaxPlayers; n++)
            {
                int count = n;
                var b = UIKit.Button(card.transform, n + "인", Palette.ButtonNeutral, Palette.Ink, () => SetCount(count), 24);
                UIKit.Place(b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f + (n - GameConfig.MinPlayers) * 110f, y - 52f), new Vector2(320f + (n - GameConfig.MinPlayers) * 110f, y));
                _countButtons.Add(b);
            }

            // 자리
            y -= 72f;
            for (int i = 0; i < GameConfig.MaxPlayers; i++)
            {
                int idx = i;
                var seat = new Seat { TypeIndex = _savedTypes[i] };
                var row = UIKit.Create("Seat" + i, card.transform);
                seat.Root = row;
                UIKit.Place(row.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, y - 54f), new Vector2(-60f, y));
                var dot = UIKit.Panel(row.transform, "Color", Palette.Player(i), UIKit.Circle);
                UIKit.PlaceAt(dot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(36f, 36f));
                var label = UIKit.Label(row.transform, (i + 1) + "P", 22, Palette.InkSoft, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.PlaceAt(label, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(50f, 40f));
                seat.Name = UIKit.Input(row.transform, _savedNames[i], 24, InputField.ContentType.Standard, 10);
                UIKit.Place(seat.Name, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(100f, 4f), new Vector2(-10f, -4f));
                seat.Type = UIKit.Button(row.transform, TypeLabels[seat.TypeIndex], Palette.ButtonNeutral, Palette.Ink, () => CycleType(idx), 22);
                UIKit.Place(seat.Type, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(10f, 4f), new Vector2(0f, -4f));
                _seats.Add(seat);
                y -= 62f;
            }

            // 시작 자금
            y -= 14f;
            SectionLabel(card.transform, "시작 자금", y);
            for (int i = 0; i < CashPresets.Length; i++)
            {
                int value = CashPresets[i];
                var b = UIKit.Button(card.transform, Fmt.Num(value), Palette.ButtonNeutral, Palette.Ink, () => SetCash(value, true), 22);
                UIKit.Place(b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f + i * 130f, y - 50f), new Vector2(340f + i * 130f, y));
                _cashButtons.Add(b);
            }
            _cashInput = UIKit.Input(card.transform, "", 22, InputField.ContentType.IntegerNumber, 7);
            ((Text)_cashInput.placeholder).text = "직접 입력";
            UIKit.Place(_cashInput, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(740f, y - 50f), new Vector2(900f, y));
            _cashInput.onEndEdit.AddListener(v => { int c; if (int.TryParse(v, out c)) SetCash(c, false); });

            // 라운드
            y -= 70f;
            SectionLabel(card.transform, "라운드", y);
            string[] roundNames = { "빠른 판 12", "표준 20", "긴 판 30" };
            for (int i = 0; i < RoundPresets.Length; i++)
            {
                int value = RoundPresets[i];
                var b = UIKit.Button(card.transform, roundNames[i], Palette.ButtonNeutral, Palette.Ink, () => SetRounds(value, true), 22);
                UIKit.Place(b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(220f + i * 170f, y - 50f), new Vector2(380f + i * 170f, y));
                _roundButtons.Add(b);
            }
            _roundInput = UIKit.Input(card.transform, "", 22, InputField.ContentType.IntegerNumber, 3);
            ((Text)_roundInput.placeholder).text = "직접 입력";
            UIKit.Place(_roundInput, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(740f, y - 50f), new Vector2(900f, y));
            _roundInput.onEndEdit.AddListener(v => { int r; if (int.TryParse(v, out r)) SetRounds(r, false); });

            _error = UIKit.Label(card.transform, "", 20, Palette.Up, TextAnchor.MiddleCenter);
            UIKit.Place(_error, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 104f), new Vector2(-40f, 134f));

            var start = UIKit.Button(card.transform, "게임 시작", Palette.ButtonPrimary, Color.white, StartGame, 34);
            UIKit.PlaceAt(start, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(420f, 72f));

            var note = UIKit.Label(transform, "온라인 멀티는 준비 중 · 빈자리는 AI가 채웁니다 · 마우스 휠로 카메라 확대/축소", 18, Palette.WithAlpha(Palette.Ink, 0.7f), TextAnchor.MiddleCenter);
            UIKit.Place(note, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f), new Vector2(0f, 44f));

            SetCount(_count);
            SetCash(_cash, Array.IndexOf(CashPresets, _cash) >= 0);
            SetRounds(_rounds, Array.IndexOf(RoundPresets, _rounds) >= 0);
            StartCoroutine(Tween.Run(0.35f, k => card.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.9f, 1f, k), Ease.OutBack));
        }

        private static void SectionLabel(Transform parent, string text, float y)
        {
            var l = UIKit.Label(parent, text, 26, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Place(l, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, y - 52f), new Vector2(210f, y));
        }

        private static void Highlight(List<Button> buttons, int selected)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool on = i == selected;
                buttons[i].GetComponent<Image>().color = on ? Palette.ButtonPrimary : Palette.ButtonNeutral;
                UIKit.ButtonLabel(buttons[i]).color = on ? Color.white : Palette.Ink;
            }
        }

        private void SetCount(int count)
        {
            _count = count;
            Highlight(_countButtons, count - GameConfig.MinPlayers);
            for (int i = 0; i < _seats.Count; i++) _seats[i].Root.SetActive(i < count);
        }

        private void CycleType(int seat)
        {
            var s = _seats[seat];
            s.TypeIndex = (s.TypeIndex + 1) % TypeLabels.Length;
            UIKit.ButtonLabel(s.Type).text = TypeLabels[s.TypeIndex];
            s.Type.GetComponent<Image>().color = s.TypeIndex == 0 ? Palette.Lighten(Palette.ButtonPrimary, 0.7f) : Palette.ButtonNeutral;
            // 이름이 기본값이면 성향에 맞춰 바꿔 준다
            string current = s.Name.text;
            if (string.IsNullOrEmpty(current) || current.StartsWith("AI ") || current == "사람" + (seat + 1))
                s.Name.text = s.TypeIndex == 0 ? "사람" + (seat + 1) : "AI " + PlayerSetup.StyleName(Styles[s.TypeIndex - 1]);
        }

        private void SetCash(int value, bool preset)
        {
            _cash = Mathf.Clamp(value, 1000, 10000000);
            Highlight(_cashButtons, preset ? Array.IndexOf(CashPresets, _cash) : -1);
            if (preset) _cashInput.text = "";
        }

        private void SetRounds(int value, bool preset)
        {
            _rounds = Mathf.Clamp(value, 1, 200);
            Highlight(_roundButtons, preset ? Array.IndexOf(RoundPresets, _rounds) : -1);
            if (preset) _roundInput.text = "";
        }

        private void StartGame()
        {
            var setups = new List<PlayerSetup>();
            for (int i = 0; i < _count; i++)
            {
                var seat = _seats[i];
                string name = string.IsNullOrEmpty(seat.Name.text) ? "플레이어" + (i + 1) : seat.Name.text.Trim();
                var kind = seat.TypeIndex == 0 ? PlayerKind.Human : PlayerKind.Ai;
                var style = seat.TypeIndex == 0 ? AiStyle.Hodler : Styles[seat.TypeIndex - 1];
                setups.Add(new PlayerSetup(name, kind, style));
                _savedNames[i] = name;
                _savedTypes[i] = seat.TypeIndex;
            }
            _savedCount = _count;
            _savedCash = _cash;
            _savedRounds = _rounds;

            var cfg = new GameConfig
            {
                StartingCash = _cash,
                Rounds = _rounds,
                Seed = (ulong)DateTime.Now.Ticks,
            };
            string error = cfg.Validate(setups.Count);
            if (error != null)
            {
                _error.text = error;
                return;
            }
            _boot.StartGame(cfg, setups);
        }
    }
}
