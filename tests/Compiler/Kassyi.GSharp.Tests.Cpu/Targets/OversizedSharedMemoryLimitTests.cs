#nullable enable
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Targets;

/// <summary>
/// Verifies the emitted launch checks and Prepare calls for kernels with and without
/// <c>[GpuKernel(AllowOversizedSharedMemory = true)]</c> across target architectures.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class OversizedSharedMemoryLimitTests
{
    private const string ArchitecturesKey = "build_property.GSharpTargetArchitectures";
    private const string TestCasePath = "Generator/OversizedSharedMemoryKernelCases.cs";

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu", "sm_107a")]
    [InlineData("Cpu", "compute_107,sm_107")]
    [InlineData("Cuda", "sm_107a")]
    [InlineData("Cuda", "compute_107,sm_107")]
    public void Generator_TargetHasOnlyComputeCapability107_EmitsOversizedLimitAndHint(
        string dialect,
        string architectures)
    {
        var trees = RunGenerator(dialect, architectures);

        var checks = FindInvocations(trees, "CheckTargetSharedMemory");
        Assert.NotEmpty(checks);

        var markedChecks = checks.Where(static inv => IsKernelInvocation(inv, 4, "Marked")).ToList();
        Assert.NotEmpty(markedChecks);
        Assert.All(markedChecks, static check =>
        {
            Assert.Equal(5, check.ArgumentList.Arguments.Count);
            Assert.Equal(334848, GetIntLiteral(check.ArgumentList.Arguments[2]));
            Assert.Equal("sm_107", GetStringLiteral(check.ArgumentList.Arguments[3]));
        });

        var unmarkedChecks = checks.Where(static inv => IsKernelInvocation(inv, 4, "Unmarked")).ToList();
        Assert.NotEmpty(unmarkedChecks);
        Assert.All(unmarkedChecks, static check =>
        {
            Assert.Equal(6, check.ArgumentList.Arguments.Count);
            Assert.Equal(232448, GetIntLiteral(check.ArgumentList.Arguments[2]));
            Assert.Equal("sm_107", GetStringLiteral(check.ArgumentList.Arguments[3]));
            Assert.Equal(334848, GetIntLiteral(check.ArgumentList.Arguments[5]));
        });

        if (dialect == "Cuda")
        {
            var prepares = FindInvocations(trees, "Prepare");
            Assert.NotEmpty(prepares);

            // Marked kernel uses 4-argument Prepare with allowsOversizedSharedMemory: true
            var markedPrepares = prepares.Where(static inv => IsKernelInvocation(inv, 2, "Marked")).ToList();
            Assert.NotEmpty(markedPrepares);
            Assert.All(markedPrepares, static p =>
            {
                Assert.Equal(4, p.ArgumentList.Arguments.Count);
                var fourthArg = p.ArgumentList.Arguments[3];
                Assert.Equal("allowsOversizedSharedMemory", fourthArg.NameColon?.Name.Identifier.Text);
                Assert.True(GetBoolLiteral(fourthArg));
            });

            // Unmarked kernel uses 3-argument Prepare
            var unmarkedPrepares = prepares.Where(static inv => IsKernelInvocation(inv, 2, "Unmarked")).ToList();
            Assert.NotEmpty(unmarkedPrepares);
            Assert.All(unmarkedPrepares, static p =>
            {
                Assert.Equal(3, p.ArgumentList.Arguments.Count);
            });
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Generator_DefaultArchitectures_EmitsSameCheckWithoutHintForMarkedAndUnmarked()
    {
        var trees = RunGenerator("Cpu", architectures: null);

        var checks = FindInvocations(trees, "CheckTargetSharedMemory");
        Assert.NotEmpty(checks);

        var markedChecks = checks.Where(static inv => IsKernelInvocation(inv, 4, "Marked")).ToList();
        Assert.NotEmpty(markedChecks);
        Assert.All(markedChecks, static check =>
        {
            Assert.Equal(5, check.ArgumentList.Arguments.Count);
            Assert.Equal(101376, GetIntLiteral(check.ArgumentList.Arguments[2]));
            Assert.Equal("sm_86", GetStringLiteral(check.ArgumentList.Arguments[3]));
        });

        var unmarkedChecks = checks.Where(static inv => IsKernelInvocation(inv, 4, "Unmarked")).ToList();
        Assert.NotEmpty(unmarkedChecks);
        Assert.All(unmarkedChecks, static check =>
        {
            Assert.Equal(5, check.ArgumentList.Arguments.Count);
            Assert.Equal(101376, GetIntLiteral(check.ArgumentList.Arguments[2]));
            Assert.Equal("sm_86", GetStringLiteral(check.ArgumentList.Arguments[3]));
        });
    }

    private static IEnumerable<SyntaxTree> RunGenerator(string dialect, string? architectures)
    {
        var source = RoslynTestUtility.ReadTestCase(TestCasePath);
        var options = architectures is null
            ? null
            : new Dictionary<string, string> { [ArchitecturesKey] = architectures };

        var (driver, _, _) = GeneratorTestRunner.RunGenerator(source, dialect, options);
        return driver.GetRunResult().GeneratedTrees;
    }

    private static List<InvocationExpressionSyntax> FindInvocations(
        IEnumerable<SyntaxTree> trees,
        string methodName)
    {
        var list = new List<InvocationExpressionSyntax>();
        foreach (var tree in trees)
        {
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null
                };

                if (name == methodName)
                {
                    list.Add(invocation);
                }
            }
        }

        return list;
    }

    private static bool IsKernelInvocation(InvocationExpressionSyntax inv, int nameArgIndex, string kernelNameSubstr)
    {
        if (inv.ArgumentList.Arguments.Count <= nameArgIndex)
        {
            return false;
        }

        var arg = inv.ArgumentList.Arguments[nameArgIndex];
        return arg.Expression is LiteralExpressionSyntax lit &&
               lit.Token.ValueText.Contains(kernelNameSubstr);
    }

    private static int GetIntLiteral(ArgumentSyntax arg) =>
        Assert.IsType<int>(((LiteralExpressionSyntax)arg.Expression).Token.Value);

    private static string GetStringLiteral(ArgumentSyntax arg) =>
        ((LiteralExpressionSyntax)arg.Expression).Token.ValueText;

    private static bool GetBoolLiteral(ArgumentSyntax arg) =>
        Assert.IsType<bool>(((LiteralExpressionSyntax)arg.Expression).Token.Value);
}
