using System.Collections;
using System.Collections.Generic;
using StockDice.Core;
using UnityEngine;

namespace StockDice.View
{
    /// <summary>
    /// 한 판의 진행. 로직(GameEngine)에 행동을 넣고, 돌려받은 이벤트를 EventPlayer 로 순서대로 재생한다.
    /// 사람 차례면 HUD 입력을 기다리고, AI 차례면 AiAgent 가 고른 행동을 넣는다. (핫시트 + AI)
    /// 온라인을 붙일 때는 이 클래스의 Submit/이벤트 큐 부분을 네트워크로 대체하면 된다.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public GameEngine Engine { get; private set; }
        public GameConfig Config { get; private set; }
        public List<PlayerSetup> Setups { get; private set; }
        public BoardView Board { get; private set; }
        public TokenManager Tokens { get; private set; }
        public DiceView Dice { get; private set; }
        public FxManager Fx { get; private set; }
        public CameraRig Rig { get; private set; }
        public HudView Hud { get; private set; }

        /// <summary>1 = 기본, 2 = 빠르게, 4 = 아주 빠르게.</summary>
        public float Speed = 1f;
        /// <summary>AI 차례는 조금 더 빠르게 재생.</summary>
        public bool FastAi = true;

        private readonly Queue<GameEvent> _queue = new Queue<GameEvent>();
        private IPlayerAgent[] _agents;
        private EventPlayer _player;
        private bool _awaitingHuman;
        private GameBootstrap _boot;

        public bool AwaitingHuman { get { return _awaitingHuman; } }
        public GameState State { get { return Engine.State; } }

        public static GameSession Create(GameBootstrap boot, GameConfig config, List<PlayerSetup> setups)
        {
            var go = new GameObject("GameSession");
            var s = go.AddComponent<GameSession>();
            s.Init(boot, config, setups);
            return s;
        }

        private void Init(GameBootstrap boot, GameConfig config, List<PlayerSetup> setups)
        {
            _boot = boot;
            Config = config;
            Setups = setups;
            Engine = GameEngine.Create(config, setups);

            Rig = boot.Rig;
            Board = boot.Board;
            Fx = FxManager.Create(transform);
            Board.Fx = Fx;
            Dice = DiceView.Create(transform);
            Dice.Fx = Fx;
            Tokens = new TokenManager(Board, setups.Count, transform);
            var start = new int[setups.Count];
            for (int i = 0; i < start.Length; i++) start[i] = Engine.State.Board.StartIndex;
            Tokens.PlaceAll(start, true);

            _agents = new IPlayerAgent[setups.Count];
            for (int i = 0; i < setups.Count; i++)
            {
                if (setups[i].Kind == PlayerKind.Ai)
                    _agents[i] = new AiAgent(setups[i].Style, config.Seed * 7919UL + (ulong)i * 104729UL + 17UL);
            }

            Hud = HudView.Create(boot.UiRoot, this);
            _player = new EventPlayer(this);
            StartCoroutine(Run());
        }

        public bool IsHuman(int playerId)
        {
            return _agents[playerId] == null;
        }

        public bool AnyHuman
        {
            get
            {
                for (int i = 0; i < _agents.Length; i++) if (_agents[i] == null) return true;
                return false;
            }
        }

        /// <summary>현재 연출 속도 (AI 차례면 더 빠르게).</summary>
        public float EffectiveSpeed
        {
            get
            {
                float s = Speed;
                if (FastAi && !IsHuman(Engine.CurrentPlayerId)) s *= 1.6f;
                return s;
            }
        }

        private IEnumerator Run()
        {
            Enqueue(Engine.Start());
            Rig.Orbit(false);
            Rig.Overview();

            while (true)
            {
                while (_queue.Count > 0)
                {
                    var e = _queue.Dequeue();
                    yield return _player.Play(e);
                }
                _player.SyncAll();
                Hud.Refresh();

                if (Engine.IsGameOver)
                {
                    yield return Tween.Wait(1.2f);
                    _boot.ShowResult(this);
                    yield break;
                }

                int pid = Engine.CurrentPlayerId;
                if (!IsHuman(pid))
                {
                    Hud.ShowThinking(pid);
                    float think = Engine.Phase == GamePhase.AwaitTrade || Engine.Phase == GamePhase.Ipo ? 0.35f : 0.55f;
                    yield return Tween.Wait(think / EffectiveSpeed);
                    var action = _agents[pid].Decide(Engine, pid);
                    var result = Engine.Apply(action);
                    if (!result.Success)
                    {
                        Debug.LogWarning("[StockDice] AI 행동 실패: " + result.Error + " " + action);
                        result = Engine.Apply(Fallback(pid));
                    }
                    Enqueue(result.Events);
                }
                else
                {
                    _awaitingHuman = true;
                    Hud.ShowControls(pid);
                    while (_awaitingHuman) yield return null;
                }
            }
        }

        private GameAction Fallback(int pid)
        {
            switch (Engine.Phase)
            {
                case GamePhase.AwaitRoll: return GameAction.Roll(pid);
                case GamePhase.AwaitLandPurchase:
                case GamePhase.AwaitBuild: return GameAction.Decide(pid, false);
                case GamePhase.AwaitCoinBet: return GameAction.Bet(pid, -1);
                default: return GameAction.EndTurn(pid);
            }
        }

        private void Enqueue(List<GameEvent> events)
        {
            foreach (var e in events) _queue.Enqueue(e);
        }

        /// <summary>HUD 에서 사람의 행동을 넣는다. 실패하면 토스트로 이유를 보여준다.</summary>
        public bool Submit(GameAction action)
        {
            if (!_awaitingHuman) return false;
            if (!IsHuman(action.PlayerId)) return false;
            var result = Engine.Apply(action);
            if (!result.Success)
            {
                Hud.Toast(result.Error, Palette.Up);
                return false;
            }
            Enqueue(result.Events);
            _awaitingHuman = false;
            Hud.HideControls();
            return true;
        }

        public void QuitToMenu()
        {
            _boot.BackToMenu();
        }

        public void Restart()
        {
            _boot.Restart(this);
        }

        public void Teardown()
        {
            StopAllCoroutines();
            if (Hud != null) Destroy(Hud.gameObject);
            Destroy(gameObject);
        }
    }
}
