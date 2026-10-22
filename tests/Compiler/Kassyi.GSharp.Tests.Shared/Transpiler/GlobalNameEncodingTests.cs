using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies that global C++ names encode C# paths injectively and decode back to the same path.</summary>
[Category(TestCategory.Unit)]
public class GlobalNameEncodingTests
{
    private const int PropertyCaseCount = 2000;

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(new[] { "Repro", "A", "B", "C" }, "Repro__A__B__C")]
    [InlineData(new[] { "Repro", "A", "B_C" }, "Repro__A__B_uC")]
    [InlineData(new[] { "N1", "Outer", "Inner" }, "N1__Outer__Inner")]
    [InlineData(new[] { "N2", "Outer_Inner" }, "N2__Outer_uInner")]
    [InlineData(new[] { "_Value" }, "_uValue")]
    public void Test_Encode_JoinsSegmentsAndEscapesUnderscores(string[] segments, string expected)
    {
        Assert.Equal(expected, GSharpNameEncoding.Encode(Path(segments)));
    }

    // The #402 report: these pairs used to share one C++ name and one include guard.
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(new[] { "Repro", "A", "B", "C" }, new[] { "Repro", "A", "B_C" })]
    [InlineData(new[] { "N1", "Outer", "Inner" }, new[] { "N1", "Outer_Inner" })]
    [InlineData(new[] { "A_", "B" }, new[] { "A", "_B" })]
    [InlineData(new[] { "Foo" }, new[] { "FooContext" })]
    public void Test_Encode_PreviouslyCollidingPaths_AreDistinct(string[] first, string[] second)
    {
        Assert.NotEqual(GSharpNameEncoding.Encode(Path(first)), GSharpNameEncoding.Encode(Path(second)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Encode_GenericArgumentListsWithMultiSegmentArguments_AreDistinct()
    {
        // G<A.B, C> and G<A, B.C> would be ambiguous if arguments were only separated by "__".
        var first = Generic("G", Path("A", "B"), Path("C"));
        var second = Generic("G", Path("A"), Path("B", "C"));

        Assert.NotEqual(GSharpNameEncoding.Encode(first), GSharpNameEncoding.Encode(second));
        Assert.Equal("G_g2_bA__B_bC_e", GSharpNameEncoding.Encode(first));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Encode_ArityOrdinalAndFileIdentity_AreSegmentMarkers()
    {
        var path = new GlobalNamePath([
            new GlobalNameSegment("N"),
            new GlobalNameSegment("G", arity: 2, fileId: "0a1b", overloadOrdinal: 3)
        ]);

        Assert.Equal("N__G_a2_f0a1b_o3", GSharpNameEncoding.Encode(path));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("TKey", "TKey")]
    [InlineData("T_Item", "T_uItem")]
    [InlineData("é", "_x0000e9")]
    public void Test_EscapeName_EscapesOnlyWhenNeeded(string name, string expected)
    {
        var escaped = GSharpNameEncoding.EscapeName(name);

        Assert.Equal(expected, escaped);
        if (name == expected)
        {
            Assert.Same(name, escaped);
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("é", "_x0000e9")]
    [InlineData("日本", "_x0065e5_x00672c")]
    [InlineData("a\U0001D6D1b", "a_x01d6d1b")]
    public void Test_Encode_NonAsciiCharacters_AreEscapedByCodePoint(string name, string expected)
    {
        Assert.Equal(expected, GSharpNameEncoding.Encode(Path(name)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_EncodeDecode_RoundTripsRandomPaths()
    {
        var random = new Random(402);
        for (var i = 0; i < PropertyCaseCount; i++)
        {
            var path = RandomPath(random, depth: 0);
            var encoded = GSharpNameEncoding.Encode(path);

            Assert.True(GSharpNameEncoding.TryDecodePath(encoded, out var decoded), $"Failed to decode '{encoded}'");
            Assert.Equal(path, decoded);
            Assert.True(IsCppIdentifierBody(encoded), $"'{encoded}' is not a valid identifier body");
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_EncodeDecode_DistinctRandomPathsGetDistinctNames()
    {
        var random = new Random(4021);
        var byName = new Dictionary<string, GlobalNamePath>(StringComparer.Ordinal);
        for (var i = 0; i < PropertyCaseCount; i++)
        {
            var path = RandomPath(random, depth: 0);
            var encoded = GSharpNameEncoding.Encode(path);
            if (byName.TryGetValue(encoded, out var existing))
            {
                Assert.Equal(existing, path);
            }

            byName[encoded] = path;
        }
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("")]
    [InlineData("A___B")]
    [InlineData("A__")]
    [InlineData("__A")]
    [InlineData("A_q")]
    [InlineData("A_g2_bB_e")]
    [InlineData("A_g1_bB")]
    [InlineData("A_x00e9")]
    [InlineData("A_h0123456789")]
    public void Test_TryDecodePath_MalformedName_ReturnsFalse(string encoded)
    {
        Assert.False(GSharpNameEncoding.TryDecodePath(encoded, out _));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("__gsharp_ty_Repro__A__B_uC", "Repro.A.B_C")]
    [InlineData("__gsharp_fn_Lib__Math__Clamp_o1", "Lib.Math.Clamp#1")]
    [InlineData("__gsharp_k_App__Kernels__Run", "App.Kernels.Run")]
    [InlineData("__gsharp_ty_N__G_a2", "N.G`2")]
    [InlineData("__gsharp_ty_N__G_g2_bSystem__Int32_bSystem__Single_r1_e", "N.G<System.Int32, System.Single[]>")]
    [InlineData("__gsharp_tp_T_uKey", "T_Key")]
    public void Test_TryDecode_GlobalName_ReturnsCSharpDisplayName(string identifier, string expected)
    {
        Assert.True(GSharpNameEncoding.TryDecode(identifier, out var name));
        Assert.Equal(expected, name);
    }

    private static GlobalNamePath Path(params string[] segments)
    {
        var list = new List<GlobalNameSegment>();
        foreach (var segment in segments)
        {
            list.Add(new GlobalNameSegment(segment));
        }

        return new GlobalNamePath(list);
    }

    private static GlobalNamePath Generic(string name, params GlobalNamePath[] arguments) =>
        new([new GlobalNameSegment(name, arguments)]);

    private static GlobalNamePath RandomPath(Random random, int depth)
    {
        var segments = new List<GlobalNameSegment>();
        var count = random.Next(1, 4);
        for (var i = 0; i < count; i++)
        {
            var arguments = new List<GlobalNamePath>();
            if (depth < 2 && random.Next(4) == 0)
            {
                var argumentCount = random.Next(1, 3);
                for (var j = 0; j < argumentCount; j++)
                {
                    arguments.Add(RandomPath(random, depth + 1));
                }
            }

            segments.Add(new GlobalNameSegment(
                RandomName(random),
                arguments,
                arity: random.Next(5) == 0 ? random.Next(1, 4) : 0,
                fileId: random.Next(8) == 0 ? random.Next(0x10000).ToString("x4") : null,
                overloadOrdinal: random.Next(5) == 0 ? random.Next(0, 12) : -1));
        }

        var modifiers = new List<int>();
        if (depth > 0)
        {
            var modifierCount = random.Next(3);
            for (var i = 0; i < modifierCount; i++)
            {
                modifiers.Add(random.Next(3));
            }
        }

        return new GlobalNamePath(segments, modifiers);
    }

    private static string RandomName(Random random)
    {
        const string Alphabet = "abcXYZ019_éα";
        var length = random.Next(1, 7);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        // C# identifiers never start with a digit and never contain "__" (GSH0055).
        var name = new string(chars).Replace("__", "_u");
        return char.IsDigit(name[0]) ? "n" + name : name;
    }

    private static bool IsCppIdentifierBody(string text)
    {
        foreach (var c in text)
        {
            if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            {
                return false;
            }
        }

        return text.Length > 0;
    }
}
