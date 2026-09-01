
namespace Kassyi.GSharp.Tests.Helpers;

public abstract class TranspilerTestBase
{
    public static TheoryData<ITranspilerDialect> Dialects => new()
    {
        new GSharpCudaDialect(),
        new StandardCppDialect()
    };
}
