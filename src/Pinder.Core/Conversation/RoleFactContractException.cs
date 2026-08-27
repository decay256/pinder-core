using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Raised when a role/fact prompt-access contract is malformed before any
    /// prompt compiler can ask to expose private character material.
    /// </summary>
    public sealed class RoleFactContractException : ArgumentException
    {
        public RoleFactContractException(string code, string message)
            : base(message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        /// <summary>
        /// Stable machine-readable validation code safe for diagnostics.
        /// </summary>
        public string Code { get; }
    }
}
