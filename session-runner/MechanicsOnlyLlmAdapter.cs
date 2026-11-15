using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Model-free adapter for <c>--mechanics-only</c> runs. It offers one placeholder option
    /// per stat the engine made available this turn, so option draws, rolls, checks, traps,
    /// steering and interest all resolve through the real GameSession while no model is
    /// called. Mechanics that depend on model output (tells, weakness windows, callbacks,
    /// datee signals) never fire, because there is no model output to derive them from.
    /// </summary>
    public sealed class MechanicsOnlyLlmAdapter : NullLlmAdapter
    {
        public override Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            StatType[]? stats = context.AvailableStats;
            if (stats == null || stats.Length == 0)
                return base.GetDialogueOptionsAsync(context, ct);

            int max = context.MaxDialogueOptions > 0 ? context.MaxDialogueOptions : stats.Length;
            DialogueOption[] options = stats
                .Take(max)
                .Select(s => new DialogueOption(s, "[" + s + " option]"))
                .ToArray();
            return Task.FromResult(options);
        }
    }
}
