namespace Kassyi.GSharp.Tests.Shared.Differential
{
    // Stands in for the harness type: the generator finds it by its full metadata name
    public sealed record DifferentialCase(string Name);
}

namespace Other
{
    // Same simple name, another namespace: not a case
    public sealed record DifferentialCase(string Name);
}
