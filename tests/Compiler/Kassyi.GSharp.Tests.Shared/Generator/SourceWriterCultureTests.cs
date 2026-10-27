using System.Globalization;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Numbers written into a <see cref="SourceWriter"/> (an interpolation hole or <c>Append(int)</c>) become C++ or C#
/// source, so they must not follow the build host's culture: sv-SE writes a negative number with U+2212 MINUS SIGN, and
/// de-DE writes a decimal comma.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class SourceWriterCultureTests
{
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Test_NumericHoles_UseInvariantCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            using var writer = new SourceWriter();
            int negativeInt = -5;
            long negativeLong = -7L;
            short negativeShort = -3;
            sbyte negativeSByte = -2;
            float single = -1.5f;
            double number = -0.25;

            writer.Append($"{negativeInt} {negativeLong} {negativeShort} {negativeSByte} {single} {number}");
            writer.Append(' ').Append(negativeInt);

            Assert.Equal("-5 -7 -3 -2 -1.5 -0.25 -5", writer.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
