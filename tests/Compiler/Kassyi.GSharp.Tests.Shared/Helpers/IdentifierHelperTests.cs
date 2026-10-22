using Xunit;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

public class IdentifierHelperTests
{
    [Theory]
    [InlineData("MyNamespace.MyClass", "MyNamespace_MyClass")]
    [InlineData("System.Collections.Generic", "System_Collections_Generic")]
    [InlineData("NoDotsHere", "NoDotsHere")]
    [InlineData("", "")]
    [Category(TestCategory.Unit)]
    public void ToSafeIdentifier_ReplacesDotsWithUnderscores(string input, string expected)
    {
        var result = AotHostNames.ToSafeIdentifier(input);
        Assert.Equal(expected, result);
    }
}
