using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="StackLinqOperators"/> is the one list of the StackLinq operator names that a fused device chain can contain;
/// the chain extractor, the fusion rule and the parallel aggregate planner read the operator from it.
/// </summary>
public class StackLinqOperatorTests
{
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Where", false, false)]
    [InlineData("Select", false, false)]
    [InlineData("Take", false, true)]
    [InlineData("Skip", false, true)]
    [InlineData("TakeWhile", false, false)]
    [InlineData("SkipWhile", false, false)]
    [InlineData("TakeLast", false, true)]
    [InlineData("SkipLast", false, true)]
    [InlineData("Sum", true, false)]
    [InlineData("Count", true, false)]
    [InlineData("Any", true, false)]
    [InlineData("All", true, false)]
    [InlineData("First", true, false)]
    [InlineData("FirstOrDefault", true, false)]
    [InlineData("Last", true, false)]
    [InlineData("LastOrDefault", true, false)]
    [InlineData("Max", true, false)]
    [InlineData("Min", true, false)]
    [InlineData("Average", true, false)]
    [InlineData("Aggregate", true, false)]
    [InlineData("Contains", true, false)]
    public void TryParse_FusedOperatorName_ReadsMatchingKind(string name, bool isTerminal, bool isPositionStage)
    {
        // mutant: StackLinqOperators.TryParse maps a name to the operator next to it (nameof(Max) => Min)
        Assert.True(StackLinqOperators.TryParse(name, out var op));
        Assert.Equal(name, op.ToString());
        // mutant: a stage placed after Sum in StackLinqOperator (IsTerminal compares the order)
        Assert.Equal(isTerminal, op.IsTerminal());
        Assert.Equal(isPositionStage, op.IsPositionStage());
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("AsParallel")]
    [InlineData("AsStackEnumerable")]
    [InlineData("SelectMany")]
    [InlineData("where")]
    [InlineData("0")]
    [InlineData("")]
    public void TryParse_UnrecognizedOperatorName_ReturnsFalse(string name)
    {
        Assert.False(StackLinqOperators.TryParse(name, out _));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void TryParse_EveryOperatorKind_RoundTripsNameAndKind()
    {
        // A new operator without a TryParse arm cannot be read from a method name
        foreach (var op in Enum.GetValues<StackLinqOperator>())
        {
            Assert.True(StackLinqOperators.TryParse(op.ToString(), out var parsed), op.ToString());
            Assert.Equal(op, parsed);
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void FusionRule_EveryOperatorKind_HasStrategyOfItsKind()
    {
        // A new operator without a strategy fails the generator with an internal error when a chain uses it
        foreach (var op in Enum.GetValues<StackLinqOperator>())
        {
            bool isTerminal = op.IsTerminal();
            Assert.True(
                isTerminal ? StackLinqFusionRule.HasTerminalStrategy(op) : StackLinqFusionRule.HasIntermediateStrategy(op),
                op.ToString());
            Assert.False(
                isTerminal ? StackLinqFusionRule.HasIntermediateStrategy(op) : StackLinqFusionRule.HasTerminalStrategy(op),
                op.ToString());
        }
    }

    // The operators that the device header does not implement; a kernel reports GSH0003 for them
    private static readonly string[] s_unsupportedOnDevice = ["ToArray", "CopyTo"];

    private static readonly string[] s_hostOnly =
        ["AddTo", "AddIf", "Do", "DoIf", "SelectMany", "TryFirst", "IndexOf", "OfType"];

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceOperators_StackLinqHeader_AreDefinedInStackLinqCuh()
    {
        // An unfused call names a function of struct StackLinq; one the header lacks fails the C++ build (#1081)
        // mutant: StackLinq.cuh without TakeLast
        var header = File.ReadAllText(
            RepositoryPaths.Combine("src", "Compiler", "Kassyi.GSharp.Compiler", "Resources", "StackLinq.cuh"));
#pragma warning disable RS0030 // StackLinq.cuh is a C++ resource with no syntax tree; its definitions are matched against DeviceOperators
        var defined = Regex.Matches(header, @"__device__ static inline [\w:<>]+ (\w+)\(")
            .Select(static m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
#pragma warning restore RS0030

        Assert.All(StackLinqTranspilerRule.DeviceOperators, name => Assert.Contains(name, defined));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StackLinqOperator_OperatorCatalog_IsOnDeviceOrReported()
    {
        // A StackLinq operator added later is either given a device function (and listed) or reported as unsupported;
        // the host-only ones and the state overloads are reported by ProhibitedApiRule
        var names = typeof(Kassyi.ZeroAlloc.Linq.StackLinqExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static m => !s_hostOnly.Contains(m.Name) &&
                               !m.GetParameters().Any(static p => p.Name == "state"))
            .Select(static m => m.Name)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.True(
            StackLinqTranspilerRule.DeviceOperators.Contains(name) ^ s_unsupportedOnDevice.Contains(name),
            name));
    }
}
