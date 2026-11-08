namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Contains constants for xUnit test collections to prevent magic strings and hallucination.
/// </summary>
public static class TestCollections
{
    /// <summary>
    /// Enforces strictly sequential execution for tests that mutate global state (e.g. NativeLibrary.Load, Environment variables).
    /// </summary>
    public const string GlobalStateSequential = "GlobalStateSequential";

    /// <summary>
    /// Runs tests with no other test in parallel, for tests that stall CUDA driver-wide resources on purpose
    /// (e.g. a blocking host function, which delays every host function and device-wide synchronizing call of the context)
    /// and for tests that read a process-wide counter (e.g. the pinned object count of the GC).
    /// </summary>
    /// <remarks>
    /// xUnit only discovers collection definitions inside the test assembly, so each assembly that uses this collection
    /// must declare a <c>[CollectionDefinition(DeviceExclusive, DisableParallelization = true)]</c> class
    /// (see <c>DeviceExclusiveCollection</c> in <c>Kassyi.GSharp.Tests.Shared</c>).
    /// </remarks>
    public const string DeviceExclusive = "DeviceExclusive";

    /// <summary>
    /// Runs the tests that compile, transpile or parse the whole test corpus or the whole standard library one after
    /// another, so that their large transient heaps do not add up; the collection still runs in parallel with every
    /// other collection.
    /// </summary>
    /// <remarks>
    /// Each assembly that uses this collection must declare a <c>[CollectionDefinition(CorpusScan)]</c> class
    /// (see <c>CorpusScanCollection</c> in <c>Kassyi.GSharp.Tests.Shared</c>).
    /// </remarks>
    public const string CorpusScan = "CorpusScan";
}
