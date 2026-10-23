using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

[Category(TestCategory.Unit)]
public class Fnv1A64Tests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Value_GivenNothingAppended_IsOffsetBasis()
    {
        Assert.Equal(0xcbf29ce484222325UL, new Fnv1A64().Value);
        Assert.Equal(0xcbf29ce484222325UL, Fnv1A64.Compute(""));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("a", 0xaf63dc4c8601ec8cUL)]
    [InlineData("foobar", 0x85944171f73967e8UL)]
    [InlineData("héllo", 0x0b25d480829d74bfUL)]
    public void Compute_GivenKnownVector_ReturnsPinnedHash(string text, ulong expected)
    {
        Assert.Equal(expected, Fnv1A64.Compute(text));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Append_GivenSplitInput_EqualsComputeOfWhole()
    {
        var hash = new Fnv1A64();
        hash.Append("foo");
        hash.Append('b');
        hash.Append("ar");

        Assert.Equal(Fnv1A64.Compute("foobar"), hash.Value);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AppendUInt_GivenValue_MixesInOneStepLikeAChar()
    {
        var viaUInt = new Fnv1A64();
        viaUInt.Append(0x41u);
        var viaChar = new Fnv1A64();
        viaChar.Append('A');

        Assert.Equal(viaChar.Value, viaUInt.Value);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AppendUInt_GivenWideValue_MixesAllBitsInOneStep()
    {
        // One xor of the whole value and one multiply, not four byte steps (SharedMemory site keys depend on this).
        var hash = new Fnv1A64();
        hash.Append(0xFFFFFFFFu);

        Assert.Equal(unchecked((0xcbf29ce484222325UL ^ 0xFFFFFFFFUL) * 1099511628211UL), hash.Value);
    }
}
