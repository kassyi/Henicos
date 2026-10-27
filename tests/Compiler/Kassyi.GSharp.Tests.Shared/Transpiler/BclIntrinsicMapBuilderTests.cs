using System.Numerics;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The <c>nuint</c> overloads of <see cref="BitOperations"/> take the mapping of the <c>uint</c> overload on a 32-bit
/// platform and of the <c>ulong</c> overload otherwise.
/// </summary>
public class BclIntrinsicMapBuilderTests
{
    public static TheoryData<string, Platform> NativeSizedOperations()
    {
        var data = new TheoryData<string, Platform>();
        foreach (var method in new[]
                 {
                     nameof(BitOperations.RotateLeft), nameof(BitOperations.RotateRight),
                     nameof(BitOperations.LeadingZeroCount), nameof(BitOperations.TrailingZeroCount),
                     nameof(BitOperations.Log2), nameof(BitOperations.PopCount)
                 })
        {
            foreach (var platform in new[] { Platform.X86, Platform.Arm, Platform.X64, Platform.AnyCpu })
            {
                data.Add(method, platform);
            }
        }

        return data;
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(NativeSizedOperations))]
    public void NativeSizedOverload_MapsLikeTheFixedSizeOverloadOfThePlatform(string methodName, Platform platform)
    {
        var baseCompilation = MegaCompilationCache.BaseCompilation;
        var compilation = baseCompilation.WithOptions(baseCompilation.Options.WithPlatform(platform));
        var map = BclIntrinsicMapBuilder.GetOrBuildMap(compilation);

        var is32Bit = platform is Platform.X86 or Platform.Arm;
        var nativeSized = FindOverload(compilation, methodName, SpecialType.System_UIntPtr);
        var fixedSize = FindOverload(compilation, methodName, is32Bit ? SpecialType.System_UInt32 : SpecialType.System_UInt64);

        Assert.True(map.TryGetValue(nativeSized, out var actual), $"{methodName}(nuint) has no mapping.");
        Assert.True(map.TryGetValue(fixedSize, out var expected), $"{methodName}({fixedSize.Parameters[0].Type}) has no mapping.");
        Assert.Equal(expected.CudaIntrinsicName, actual.CudaIntrinsicName);
        Assert.Equal(is32Bit ? "unsigned int" : "unsigned long long int", actual.CastFirstArgumentTo);
    }

    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(Platform.X86)]
    [InlineData(Platform.X64)]
    public void GSharpMappingTargets_AreDeclaredByThePrelude(Platform platform)
    {
        var baseCompilation = MegaCompilationCache.BaseCompilation;
        var map = BclIntrinsicMapBuilder.GetOrBuildMap(baseCompilation.WithOptions(baseCompilation.Options.WithPlatform(platform)));
        Assert.NotEmpty(map);

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var mapping in map.Values)
        {
            var name = mapping.CudaIntrinsicName;
            if (name.StartsWith("__gsharp_", StringComparison.Ordinal) &&
                !DeclaresIdentifier(GSharpPreludeResources.Cpp, name) &&
                !DeclaresIdentifier(GSharpPreludeResources.Cuda, name))
            {
                missing.Add(name);
            }
        }

        Assert.True(missing.Count == 0, "No prelude declares: " + string.Join(", ", missing));
    }

    // The user's assembly maps only the overloads whose parameter types all resolve and match one by one; an attribute
    // of the same shape that is not BclIntrinsicMap maps nothing
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Platform.X64, "unsigned long long int")]
    [InlineData(Platform.X86, "unsigned int")]
    public void UserAssemblyMappings_MapOnlyTheMatchingOverloads(Platform platform, string nativeSizedCast)
    {
        var source = RoslynTestUtility.ReadTestCase("Transpiler/BclIntrinsicMap/UserMappings.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source);
        compilation = compilation.WithOptions(compilation.Options.WithPlatform(platform));
        Assert.Empty(compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var map = BclIntrinsicMapBuilder.GetOrBuildMap(compilation);

        var userMappings = map
            .Where(static entry => entry.Value.CudaIntrinsicName.StartsWith("user_", StringComparison.Ordinal) ||
                                   entry.Value.CudaIntrinsicName.StartsWith("fake_", StringComparison.Ordinal))
            .Select(static entry => $"{entry.Key.ToDisplayString()} => {entry.Value.CudaIntrinsicName} ({entry.Value.CastFirstArgumentTo ?? "-"})")
            .OrderBy(static line => line, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "System.Math.Abs(double) => user_fabs (-)",
                "System.Math.Round(double) => user_round (-)",
                $"System.Numerics.BitOperations.IsPow2(nint) => user_ispow2_nint ({nativeSizedCast})"
            ],
            userMappings);
    }

    private static bool DeclaresIdentifier(string source, string identifier) =>
        Regex.IsMatch(source, $"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])");

    private static IMethodSymbol FindOverload(Compilation compilation, string methodName, SpecialType firstParameter)
    {
        var bitOperations = compilation.GetTypeByMetadataName(typeof(BitOperations).FullName ?? nameof(BitOperations))
            ?? throw new InvalidOperationException("System.Numerics.BitOperations is not referenced.");

        foreach (var member in bitOperations.GetMembers(methodName))
        {
            if (member is IMethodSymbol { Parameters: [{ Type.SpecialType: var type }, ..] } method && type == firstParameter)
            {
                return method.OriginalDefinition;
            }
        }

        throw new InvalidOperationException($"BitOperations.{methodName} has no overload taking {firstParameter}.");
    }
}
