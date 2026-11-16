using Xunit;

namespace Slopwatch.Tests.Baseline;

/// <summary>
/// Serializes tests that redirect the process-wide <see cref="Console"/> streams.
/// </summary>
/// <remarks>
/// <see cref="Console.SetError"/> and <see cref="Console.SetOut"/> mutate global state, so two
/// tests that redirect concurrently can capture each other's output and restore each other's
/// writers. xUnit runs distinct test classes in parallel by default, which made
/// <c>BaselineFileTests.AnalyzeCommand_ForwardsConfigPathToBaselineLoad</c> and
/// <c>StaleBaselineUpgradeTests</c> race: a controlled overlap produced an empty capture, failed
/// the forwarding guard on a missing <c>--config</c>, and left <see cref="Console.Error"/> pointing
/// at a writer that had already been disposed. Tests in the same collection never run in parallel,
/// so putting both classes here removes the race without disabling parallelization elsewhere.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleRedirectCollection
{
    public const string Name = "ConsoleRedirect";
}
