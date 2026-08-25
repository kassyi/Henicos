using Xunit;

namespace Kassyi.GSharp.Tests.Runtime;

public class Gsh06_07_RuntimeIssuesTests
{
    [Fact(Skip = "未実装: GSH-06 (PTX / C++ コンパイルエラーの行番号マッピング)")]
    [Trait("FeatureId", "GSH-06")]
    [Trait("Category", "Runtime")]
    public void Test_GSH06_LineNumberMappingInDiagnostics()
    {
    }

    [Fact(Skip = "未実装: GSH-07 (JIT ディスクキャッシュ機構)")]
    [Trait("FeatureId", "GSH-07")]
    [Trait("Category", "Runtime")]
    public void Test_GSH07_JitDiskCache()
    {
    }
}
