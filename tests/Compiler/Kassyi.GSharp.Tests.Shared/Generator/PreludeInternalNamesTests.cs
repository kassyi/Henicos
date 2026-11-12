using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Keeps the hand-written C++ prelude in sync with the internal names that the C# emitters produce.
/// </summary>
public partial class PreludeInternalNamesTests
{
    [GeneratedRegex(@"(?<![A-Za-z0-9_])_(ctx|state|yieldedForGridSync|warpExchange|all_contexts)(?![A-Za-z0-9_])")]
    private static partial Regex LegacyInternalNameRegex();

#pragma warning disable RS0030 // The C++ prelude has no syntax tree; finds an InternalNames.yml name as an identifier
    private static bool ContainsIdentifier(string source, string identifier) =>
        Regex.IsMatch(source, $"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])");
#pragma warning restore RS0030

    // The names that InternalNames.yml marks as declared by the Prelude.
    public static TheoryData<string, string> PreludeDeclaredNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var (path, value, preludeFile) in GSharpInternalNames.All)
        {
            if (preludeFile != null)
            {
                data.Add(path, value);
            }
        }

        return data;
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(PreludeDeclaredNames))]
    public void Prelude_DeclaresInternalName(string path, string identifier)
    {
        Assert.True(ContainsIdentifier(GSharpPreludeResources.Cpp, identifier) || ContainsIdentifier(GSharpPreludeResources.Cuda, identifier),
            $"No prelude declares '{identifier}' ({path}); update Resources/Prelude or InternalNames.yml.");
    }

    // The runtime cannot see GSharpInternalNames, so it spells the exported error accessor itself.
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void CpuKernelLoader_LoadsThePreludeErrorAccessor()
    {
        var loader = File.ReadAllText(RepositoryPaths.Combine("src", "Runtime", "Kassyi.GSharp.Runtime", "Aot", "CpuKernelLoader.cs"));

        Assert.Contains($"\"{GSharpInternalNames.Prelude.ConsumeLastError}\"", loader);
        Assert.Contains($"\"{GSharpInternalNames.Prelude.ConsumeLastErrorDetails}\"", loader);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void Preludes_ContainNoLegacyInternalNames()
    {
        Assert.DoesNotMatch(LegacyInternalNameRegex(), GSharpPreludeResources.Cpp);
        Assert.DoesNotMatch(LegacyInternalNameRegex(), GSharpPreludeResources.Cuda);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Preludes_DeclareIdenticalGSharpErrorCodeEnumValues()
    {
        // The runtime reads a device error code as the C# enum, so both preludes declare exactly its values
        // mutant: SwitchExpression = 11 in Prelude/Types.hpp
        AssertDeclaresErrorCodes(GSharpPreludeResources.Cpp, "int32_t");
        AssertDeclaresErrorCodes(GSharpPreludeResources.Cuda, "int");
    }

    // enum class GSharpErrorCode : <type> {<line break><name> = <value>,<line break>...<name> = <value><line break>};
    // The prelude generator removes the indentation and keeps the line breaks of the checkout.
    private static void AssertDeclaresErrorCodes(string prelude, string underlyingType)
    {
        var lineBreak = prelude.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var entries = string.Join(
            "," + lineBreak,
            Enum.GetValues<GSharpErrorCode>().Select(static code => $"{code} = {(int)code}")
        );

        Assert.Contains(
            $"enum class GSharpErrorCode : {underlyingType} {{{lineBreak}{entries}{lineBreak}}};",
            prelude,
            StringComparison.Ordinal
        );
    }
}
