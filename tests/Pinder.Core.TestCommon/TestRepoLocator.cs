using System;
using Pinder.SessionRunner;

namespace Pinder.Core.TestCommon
{
    public static class TestRepoLocator
    {
        public static string RepoRoot =>
            DataFileLocator.FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
    }
}
