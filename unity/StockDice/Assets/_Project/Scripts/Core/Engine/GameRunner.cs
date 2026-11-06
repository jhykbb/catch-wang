using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    /// <summary>
    /// 화면 없이 게임을 끝까지 돌리는 도우미. 밸런스 시뮬레이터, 테스트, (나중에) 온라인 호스트 검증에 쓴다.
    /// </summary>
    public static class GameRunner
    {
        public static List<IPlayerAgent> CreateAiAgents(IList<PlayerSetup> setups, ulong seed)
        {
            var agents = new List<IPlayerAgent>();
            for (int i = 0; i < setups.Count; i++)
                agents.Add(new AiAgent(setups[i].Style, seed * 7919UL + (ulong)i * 104729UL + 17UL));
            return agents;
        }

        /// <summary>게임을 끝까지 진행한다. 진행 중 오류가 나면 예외. onEvents 는 이벤트를 받을 때마다 호출.</summary>
        public static void RunToEnd(GameEngine engine, IList<IPlayerAgent> agents, Action<List<GameEvent>> onEvents = null, int maxActions = 200000)
        {
            if (engine.Phase == GamePhase.NotStarted)
            {
                var start = engine.Start();
                if (onEvents != null) onEvents(start);
            }

            int count = 0;
            while (!engine.IsGameOver)
            {
                if (++count > maxActions) throw new InvalidOperationException("행동 수가 한도를 넘었습니다. (무한 루프 의심)");
                int pid = engine.CurrentPlayerId;
                var action = agents[pid].Decide(engine, pid);
                var result = engine.Apply(action);
                if (!result.Success)
                {
                    // AI 가 잘못된 행동을 하면 안전하게 턴을 넘긴다.
                    var fallback = engine.Phase == GamePhase.AwaitRoll ? GameAction.Roll(pid)
                        : engine.Phase == GamePhase.AwaitLandPurchase || engine.Phase == GamePhase.AwaitBuild ? GameAction.Decide(pid, false)
                        : engine.Phase == GamePhase.AwaitCoinBet ? GameAction.Bet(pid, -1)
                        : GameAction.EndTurn(pid);
                    result = engine.Apply(fallback);
                    if (!result.Success)
                        throw new InvalidOperationException("진행 불가: " + result.Error + " / " + action);
                }
                if (onEvents != null) onEvents(result.Events);
            }
        }
    }
}
