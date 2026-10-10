using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public class ConstantArgumentTests
{
    [Fact]
    public void ConstantArgument_CreatesAndFormatsCorrectly()
    {
        var intArg = ConstantArgument.Create("T", 42);
        Assert.Equal(ConstantType.Int32, intArg.Type);
        Assert.Equal("T", intArg.Key);
        Assert.Equal("42", intArg.GetStringValue());

        var uintArg = ConstantArgument.Create("U", 42u);
        Assert.Equal(ConstantType.UInt32, uintArg.Type);
        Assert.Equal("42", uintArg.GetStringValue());

        var longArg = ConstantArgument.Create("L", -42L);
        Assert.Equal(ConstantType.Int64, longArg.Type);
        Assert.Equal("-42", longArg.GetStringValue());

        var ulongArg = ConstantArgument.Create("UL", 42ul);
        Assert.Equal(ConstantType.UInt64, ulongArg.Type);
        Assert.Equal("42", ulongArg.GetStringValue());

        var floatArg = ConstantArgument.Create("F", 3.14f);
        Assert.Equal(ConstantType.Single, floatArg.Type);
        Assert.Equal(3.14f.ToString("R"), floatArg.GetStringValue());

        var doubleArg = ConstantArgument.Create("D", 3.14159);
        Assert.Equal(ConstantType.Double, doubleArg.Type);
        Assert.Equal(3.14159.ToString("R"), doubleArg.GetStringValue());

        var boolArgTrue = ConstantArgument.Create("B", true);
        Assert.Equal(ConstantType.Boolean, boolArgTrue.Type);
        Assert.Equal("true", boolArgTrue.GetStringValue());

        var boolArgFalse = ConstantArgument.Create("B2", false);
        Assert.Equal("false", boolArgFalse.GetStringValue());
    }
}
