using System;

namespace Pinder.Core.Conversation
{
    internal sealed class GameSessionTransactionTestHooks
    {
        internal Action? BeforeResolveCommit { get; set; }
        internal Action? BeforeAdoptCommit { get; set; }
        internal Action? AfterResolveCommit { get; set; }
    }

    public sealed partial class GameSession
    {
        private GameSessionTransactionTestHooks? _transactionTestHooks;

        internal GameSessionTransactionTestHooks? TransactionTestHooks
        {
            get => _transactionTestHooks;
            set => _transactionTestHooks = value;
        }
    }
}
