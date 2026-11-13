using Kassyi.GSharp.Core;

// A parameter named nameof, which a nameof(...) in the launcher would bind to
namespace NameofParameter;

public static partial class K
{
    [CudaImport("m.fatbin")]
    public static partial void Run(float[] data, int @nameof);
}
