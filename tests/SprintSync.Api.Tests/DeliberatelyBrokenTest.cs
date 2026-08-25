namespace SprintSync.Api.Tests;

/// <summary>
/// THROWAWAY — feature 002, task T054. This file exists to prove the CI pipeline
/// can go red, and must never be merged.
///
/// Every CI run recorded against this repository has been green. A pipeline
/// nobody has watched fail is indistinguishable from one that always passes, so
/// the "CI gates main" claim cannot be established by more green runs — only by
/// a red one. This is that red one.
///
/// Deliberately a failing assertion rather than a compile error: a build break
/// would prove the build step runs, not that the test step observes results and
/// propagates a non-zero exit. The distinction matters, because the backend job
/// runs `dotnet test --no-build`, so build and test are separate steps.
///
/// No fixture, no collection, no Docker: this must fail for exactly one reason.
/// </summary>
public sealed class DeliberatelyBrokenTest
{
    [Fact]
    public void CiPipelineCanFail()
    {
        Assert.Equal(1, 2);
    }
}
