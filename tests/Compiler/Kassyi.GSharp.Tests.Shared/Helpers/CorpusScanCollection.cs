namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Declares <see cref="TestCollections.CorpusScan"/> for the assemblies that import the shared test sources.
/// </summary>
/// <remarks>
/// Parallelization stays on: the member classes run one after another, and the collection runs beside every other
/// collection.
/// </remarks>
[CollectionDefinition(TestCollections.CorpusScan)]
public sealed class CorpusScanCollection
{
}
