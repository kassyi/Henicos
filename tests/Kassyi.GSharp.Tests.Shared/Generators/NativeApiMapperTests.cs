using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Common;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Generators;

[Category(TestCategory.Compiler)]
public class NativeApiMapperTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryGetNativeName_ReturnsTrue_ForKnownApi()
    {
        var sig = "Kassyi.GSharp.Core.Gpu.AtomicAdd(ref int, int)";
        
        bool result = NativeApiMapper.TryGetNativeName(sig, out var nativeName);
        
        Assert.True(result);
        Assert.Equal("atomicAdd", nativeName);
    }
    
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryGetNativeName_ReturnsFalse_ForUnknownApi()
    {
        var sig = "Unknown.Api()";
        
        bool result = NativeApiMapper.TryGetNativeName(sig, out var nativeName);
        
        Assert.False(result);
        Assert.Empty(nativeName);
    }
}
