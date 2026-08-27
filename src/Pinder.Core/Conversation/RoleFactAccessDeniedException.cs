using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Terminal pre-provider rejection carrying only text-free policy metadata.
    /// </summary>
    public sealed class RoleFactAccessDeniedException : RoleFactContractException
    {
        public RoleFactAccessDeniedException(RoleFactAccessDecision decision)
            : base("prompt_fact.access_denied", "A turn-local prompt fact was denied by role access policy.")
        {
            Decision = decision ?? throw new ArgumentNullException(nameof(decision));
            if (decision.Admitted)
                throw new ArgumentException("An admitted decision cannot produce an access-denied exception.", nameof(decision));
        }

        public RoleFactAccessDecision Decision { get; }
    }
}
