namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Declares <see cref="TestCollections.DeviceExclusive"/> for the assemblies that import the shared test sources.
/// </summary>
/// <remarks>
/// <c>DisableParallelization</c> makes xUnit run this collection only after every parallel collection has finished,
/// so no other test can contend for the CUDA driver while a member test deliberately stalls it, or move a process-wide
/// counter that a member test reads.
/// </remarks>
[CollectionDefinition(TestCollections.DeviceExclusive, DisableParallelization = true)]
public sealed class DeviceExclusiveCollection
{
}
