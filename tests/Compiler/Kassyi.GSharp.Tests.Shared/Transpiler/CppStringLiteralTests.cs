using System;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="CppStringLiteral"/> writes a C++ string literal whose bytes do not depend on the compiler's character sets:
/// a quote, a backslash, a control character and each UTF-8 byte of a non-ASCII character as a three-digit octal escape.
/// </summary>
[Category(TestCategory.Compiler)]
public class CppStringLiteralTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Kernels.cu.cs(12,17)", "\"Kernels.cu.cs(12,17)\"")]
    [InlineData("a\"b\\c", "\"a\\042b\\134c\"")]
    [InlineData("tab\there", "\"tab\\011here\"")]
    // é is C3 A9; カ is E3 82 AB
    [InlineData("é.cs", "\"\\303\\251.cs\"")]
    [InlineData("カ", "\"\\343\\202\\253\"")]
    public void Test_Append_EscapesOnlyWhatTheCompilerCouldReadOtherwise(string value, string expected)
    {
        using var writer = new SourceWriter();

        CppStringLiteral.Append(writer, value);

        Assert.Equal(expected, writer.ToString());
    }

    // U+1F600 is the pair D83D DE00, F0 9F 98 80 in UTF-8; a surrogate without its pair becomes U+FFFD (EF BF BD). The
    // strings are built here: the test framework does not keep a lone surrogate of [InlineData] as it is
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(new[] { 0xD83D, 0xDE00 }, "\"\\360\\237\\230\\200\"")]
    [InlineData(new[] { 'a', 0xD800, 'b' }, "\"a\\357\\277\\275b\"")]
    [InlineData(new[] { 0xDC00 }, "\"\\357\\277\\275\"")]
    [InlineData(new[] { 0xD800 }, "\"\\357\\277\\275\"")]
    [InlineData(new[] { 0xDC00, 0xD800 }, "\"\\357\\277\\275\\357\\277\\275\"")]
    public void Test_Append_SurrogatePairAndLoneSurrogate(int[] codeUnits, string expected)
    {
        var value = new string(Array.ConvertAll(codeUnits, static u => (char)u));
        using var writer = new SourceWriter();

        CppStringLiteral.Append(writer, value);

        Assert.Equal(expected, writer.ToString());
    }
}
