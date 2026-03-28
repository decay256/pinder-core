using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Orchestrates a single Pinder conversation from match to outcome.
    /// Owns all mutable game state: interest, traps, momentum, history, turn count.
    /// Sequences calls to RollEngine (stateless), ILlmAdapter, and interest/trap tracking.
    /// </summary>
    public sealed class GameSession
    {
        private readonly CharacterProfile _player;
        private readonly CharacterProfile _opponent;
        private readonly ILlmAdapter _llm;
        private readonly IDiceRoller _dice;
        private readonly ITrapRegistry _trapRegistry;

        private readonly InterestMeter _interest;
        private readonly TrapState _traps;
        private readonly List<(string Sender, string Text)> _history;

        private int _momentumStreak;
        private int _turnNumber;
        private bool _ended;
        private GameOutcome? _outcome;

        // Stored between StartTurnAsync and ResolveTurnAsync
        private DialogueOption[]? _currentOptions;
        private bool _currentHasAdvantage;
        private bool _currentHasDisadvantage;

        public GameSession(
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _opponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _dice = dice ?? throw new ArgumentNullException(nameof(dice));
            _trapRegistry = trapRegistry ?? throw new ArgumentNullException(nameof(trapRegistry));

            _interest = new InterestMeter();
            _traps = new TrapState();
            _history = new List<(string Sender, string Text)>();
            _momentumStreak = 0;
            _turnNumber = 0;
            _ended = false;
            _outcome = null;
        }

        /// <summary>
        /// Start a new turn. Checks end conditions, determines advantage/disadvantage,
        /// and fetches dialogue options from the LLM adapter.
        /// </summary>
        /// <exception cref="GameEndedException">If the game has already ended.</exception>
        public async Task<TurnStart> StartTurnAsync()
        {
            // Check if game already ended
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            // Check end conditions: interest at 0 or 25
            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                throw new GameEndedException(GameOutcome.Unmatched);
            }

            if (_interest.IsMaxed)
            {
                _ended = true;
                _outcome = GameOutcome.DateSecured;
                throw new GameEndedException(GameOutcome.DateSecured);
            }

            // Ghost trigger: if Bored state, 25% chance per turn
            if (_interest.GetState() == InterestState.Bored)
            {
                int ghostRoll = _dice.Roll(4);
                if (ghostRoll == 1)
                {
                    _ended = true;
                    _outcome = GameOutcome.Ghosted;
                    throw new GameEndedException(GameOutcome.Ghosted);
                }
            }

            // Determine advantage/disadvantage from interest state + traps
            bool hasAdvantage = _interest.GrantsAdvantage;
            bool hasDisadvantage = _interest.GrantsDisadvantage;

            // Store for ResolveTurnAsync
            _currentHasAdvantage = hasAdvantage;
            _currentHasDisadvantage = hasDisadvantage;

            // Get trap names for context
            var activeTrapNames = GetActiveTrapNames();

            // Build dialogue context
            var context = new DialogueContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GetLastOpponentMessage(),
                activeTraps: activeTrapNames,
                currentInterest: _interest.Current);

            // Get dialogue options from LLM
            var options = await _llm.GetDialogueOptionsAsync(context).ConfigureAwait(false);
            _currentOptions = options;

            var snapshot = CreateSnapshot();
            return new TurnStart(options, snapshot);
        }

        /// <summary>
        /// Resolve a turn after the player selects an option.
        /// Sequences: roll → interest delta → momentum → trap advance → deliver → opponent response.
        /// </summary>
        /// <param name="optionIndex">Index into the options array from StartTurnAsync.</param>
        /// <exception cref="GameEndedException">If the game has already ended.</exception>
        /// <exception cref="InvalidOperationException">If StartTurnAsync was not called first or index is invalid.</exception>
        public async Task<TurnResult> ResolveTurnAsync(int optionIndex)
        {
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            if (_currentOptions == null)
                throw new InvalidOperationException("Must call StartTurnAsync before ResolveTurnAsync.");

            if (optionIndex < 0 || optionIndex >= _currentOptions.Length)
                throw new ArgumentOutOfRangeException(nameof(optionIndex),
                    $"Option index {optionIndex} is out of range. Valid range: 0–{_currentOptions.Length - 1}.");

            var chosenOption = _currentOptions[optionIndex];

            // 1. Roll dice
            var rollResult = RollEngine.Resolve(
                stat: chosenOption.Stat,
                attacker: _player.Stats,
                defender: _opponent.Stats,
                attackerTraps: _traps,
                level: _player.Level,
                trapRegistry: _trapRegistry,
                dice: _dice,
                hasAdvantage: _currentHasAdvantage,
                hasDisadvantage: _currentHasDisadvantage);

            // 2. Compute interest delta from roll outcome
            int interestDelta;
            if (rollResult.IsSuccess)
            {
                interestDelta = SuccessScale.GetInterestDelta(rollResult);
            }
            else
            {
                interestDelta = FailureScale.GetInterestDelta(rollResult);
            }

            // 3. Apply momentum
            if (rollResult.IsSuccess)
            {
                _momentumStreak++;
                interestDelta += GetMomentumBonus(_momentumStreak);
            }
            else
            {
                _momentumStreak = 0;
            }

            // 4. Record interest before applying delta
            int interestBefore = _interest.Current;
            InterestState stateBefore = _interest.GetState();

            // 5. Apply interest delta
            _interest.Apply(interestDelta);

            int interestAfter = _interest.Current;
            InterestState stateAfter = _interest.GetState();

            // 6. Advance trap timers
            _traps.AdvanceTurn();

            // 7. Deliver message via LLM
            var activeTrapInstructions = _traps.AllActive
                .Select(t => t.Definition.LlmInstruction)
                .ToList();

            int beatDcBy = rollResult.IsSuccess ? rollResult.Total - rollResult.DC : 0;

            var deliveryContext = new DeliveryContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GetLastOpponentMessage(),
                chosenOption: chosenOption,
                outcome: rollResult.Tier,
                beatDcBy: beatDcBy,
                activeTraps: activeTrapInstructions);

            string deliveredMessage = await _llm.DeliverMessageAsync(deliveryContext).ConfigureAwait(false);

            // 8. Append player message to history
            _history.Add((_player.DisplayName, deliveredMessage));

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (stateBefore != stateAfter)
            {
                var interestChangeContext = new InterestChangeContext(
                    opponentName: _opponent.DisplayName,
                    interestBefore: interestBefore,
                    interestAfter: interestAfter,
                    newState: stateAfter);

                narrativeBeat = await _llm.GetInterestChangeBeatAsync(interestChangeContext).ConfigureAwait(false);
            }

            // 10. Compute response delay
            double responseDelayMinutes = _opponent.Timing.ComputeDelay(_interest.Current, _dice);

            // 11. Generate opponent response
            var opponentContext = new OpponentContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GetLastOpponentMessage(),
                activeTraps: GetActiveTrapNames(),
                currentInterest: _interest.Current,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes);

            var opponentResponse = await _llm.GetOpponentResponseAsync(opponentContext).ConfigureAwait(false);
            string opponentMessage = opponentResponse.MessageText;

            // 12. Append opponent message to history
            _history.Add((_opponent.DisplayName, opponentMessage));

            // 13. Increment turn number
            _turnNumber++;

            // 14. Clear stored options
            _currentOptions = null;

            // 15. Check end conditions after this turn
            bool isGameOver = false;
            GameOutcome? outcome = null;

            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                isGameOver = true;
                outcome = GameOutcome.Unmatched;
            }
            else if (_interest.IsMaxed)
            {
                _ended = true;
                _outcome = GameOutcome.DateSecured;
                isGameOver = true;
                outcome = GameOutcome.DateSecured;
            }

            // 16. Build result
            var stateSnapshot = CreateSnapshot();

            return new TurnResult(
                roll: rollResult,
                deliveredMessage: deliveredMessage,
                opponentMessage: opponentMessage,
                narrativeBeat: narrativeBeat,
                interestDelta: interestDelta,
                stateAfter: stateSnapshot,
                isGameOver: isGameOver,
                outcome: outcome);
        }

        /// <summary>
        /// Get momentum bonus for the current streak length.
        /// 3-streak → +2, 4-streak → +2, 5+ → +3.
        /// </summary>
        private static int GetMomentumBonus(int streak)
        {
            if (streak >= 5) return 3;
            if (streak >= 3) return 2;
            return 0;
        }

        private GameStateSnapshot CreateSnapshot()
        {
            var trapNames = _traps.AllActive
                .Select(t => t.Definition.Id)
                .ToArray();

            return new GameStateSnapshot(
                interest: _interest.Current,
                state: _interest.GetState(),
                momentumStreak: _momentumStreak,
                activeTrapNames: trapNames,
                turnNumber: _turnNumber);
        }

        private string GetLastOpponentMessage()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].Sender == _opponent.DisplayName)
                    return _history[i].Text;
            }
            return string.Empty;
        }

        private List<string> GetActiveTrapNames()
        {
            return _traps.AllActive
                .Select(t => t.Definition.Id)
                .ToList();
        }
    }
}
