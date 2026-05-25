using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.SessionRunner;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Spec-driven tests for issue #346: IPlayerAgent interface and supporting DTOs.
    /// Tests verify acceptance criteria from docs/specs/issue-346-spec.md.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class PlayerDecisionSpecTests
    {
        #region Helpers

        private static OptionScore MakeScore(int index, float score = 1.0f)
        {
            return new OptionScore(index, score, 0.5f, 0.0f, Array.Empty<string>());
        }

        #endregion
    }
}
