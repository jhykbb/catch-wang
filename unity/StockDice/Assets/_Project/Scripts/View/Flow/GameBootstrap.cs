using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && STOCKDICE_INPUTSYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace StockDice.View
{
    /// <summary>
    /// 씬 진입점. 빈 씬에 이 컴포넌트 하나만 있으면 카메라, 조명, UI, 보드를 모두 코드로 만든다.
    /// (메뉴: StockDice > 메인 씬 만들기 가 이 컴포넌트를 가진 씬을 만들어 준다.)
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public CameraRig Rig { get; private set; }
        public BoardView Board { get; private set; }
        public RectTransform UiRoot { get; private set; }

        private StartScreen _start;
        private GameSession _session;
        private ResultScreen _result;
        private Transform _worldRoot;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            SetupWorld();
            SetupUi();
            RebuildBoard();
            ShowStart();
        }

        // ------------------------------------------------------------------
        // 세팅
        // ------------------------------------------------------------------

        private void SetupWorld()
        {
            _worldRoot = new GameObject("World").transform;
            Rig = CameraRig.Create();

            // 기존 씬 카메라/조명이 있으면 끈다 (중복 방지)
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam != Rig.Cam) cam.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_worldRoot, false);
            sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.3f;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_worldRoot, false);
            fillGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.75f, 0.85f, 1f);
            fill.intensity = 0.3f;
            fill.shadows = LightShadows.None;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.Hex("#DCEEFF");
            RenderSettings.ambientEquatorColor = Palette.Hex("#F3E7DA");
            RenderSettings.ambientGroundColor = Palette.Hex("#B8A590");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Palette.Background;
            RenderSettings.fogStartDistance = 48f;
            RenderSettings.fogEndDistance = 120f;
            QualitySettings.shadowDistance = 70f;
            if (!MaterialLibrary.IsUrp) QualitySettings.antiAliasing = 4;

            UrpSetup.Apply(Rig.Cam, _worldRoot);
        }

        private void SetupUi()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && STOCKDICE_INPUTSYSTEM
                es.AddComponent<InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            var canvasGo = new GameObject("UI", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            UiRoot = (RectTransform)canvasGo.transform;
        }

        private void RebuildBoard()
        {
            if (Board != null) Destroy(Board.gameObject);
            Board = BoardView.Create(BoardDefinition.CreateDefault(), _worldRoot);
        }

        // ------------------------------------------------------------------
        // 화면 전환
        // ------------------------------------------------------------------

        private void ShowStart()
        {
            Rig.Orbit(true);
            _start = StartScreen.Create(UiRoot, this);
        }

        public void StartGame(GameConfig config, List<PlayerSetup> setups)
        {
            if (_start != null) Destroy(_start.gameObject);
            if (_result != null) Destroy(_result.gameObject);
            if (_session != null) _session.Teardown();
            RebuildBoard();
            _session = GameSession.Create(this, config, setups);
        }

        public void ShowResult(GameSession session)
        {
            if (_result != null) Destroy(_result.gameObject);
            var ranks = Valuation.Rankings(session.State);
            var winner = session.Tokens.Token(ranks[0].PlayerId);
            Rig.Focus(winner.transform.position, Board.SideOf(session.State.Players[ranks[0].PlayerId].Position), 13f);
            session.Fx.Confetti(winner.HeadPosition, 60, new[] { Palette.Gold, Palette.Up, Palette.Player(ranks[0].PlayerId), Color.white }, 7f);
            session.Fx.Sparkle(winner.HeadPosition, Palette.Gold);
            session.Hud.gameObject.SetActive(false);
            _result = ResultScreen.Create(UiRoot, session, () => Restart(session), BackToMenu);
        }

        public void Restart(GameSession session)
        {
            var cfg = session.Config.Clone();
            cfg.Seed = (ulong)System.DateTime.Now.Ticks;
            cfg.Board = BoardDefinition.CreateDefault();
            var setups = new List<PlayerSetup>(session.Setups);
            StartGame(cfg, setups);
        }

        public void BackToMenu()
        {
            if (_result != null) Destroy(_result.gameObject);
            if (_session != null) _session.Teardown();
            _session = null;
            RebuildBoard();
            ShowStart();
        }
    }
}
