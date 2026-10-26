namespace Kassyi.GSharp.Tests.Shared.Syntax;

// The second part of PcPartial (PrimaryConstructorPartialKernels.cu.cs): it reads the primary constructor parameter a,
// which Roslyn captures into a hidden field of the whole struct (#495)
public partial struct PcPartial
{
    public byte Y = 2;

    public long Read => a * 100 + X * 10 + Y;
}
