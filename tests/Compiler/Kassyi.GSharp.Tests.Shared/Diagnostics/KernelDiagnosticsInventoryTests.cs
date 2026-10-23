using System.Text;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Records every diagnostic that the kernel analyzers and <see cref="GpuKernelGenerator"/> report for the sad path inputs
/// of the G# restrictions, with its reporter and range. The snapshot is a measurement, not a target: moving a check from
/// the generator to an analyzer shows up as a reviewed snapshot diff.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelDiagnosticsInventoryTests
{
    // Test cases, relative to the TestCases directory
    internal static readonly string[] Inputs =
    [
        // GSH0001, GSH0002, GSH0004
        "Generator/SadPathDiagnostics.cu.cs",
        // GSH0001
        "Analyzers/InvalidMappingSadPath.cs",
        // GSH0002
        "Analyzers/GpuKernel/StringTernary_InGpuKernel.cu.cs",
        // GSH0001, GSH0003 (syntax without a rule)
        "Analyzers/DelegateEscapeSadPath.cs",
        // GSH0002 (implicitly typed variable), GSH0003 (syntax without a rule, function pointer parameter and variable)
        "Diagnostics/UnsupportedSyntaxNodes.cu.cs",
        "Diagnostics/UnsupportedSyntaxKinds.cu.cs",
        // GSH0003 (with on a class, with assigning a member the device cannot assign)
        "Diagnostics/WithExpressionUnsupported.cu.cs",
        // GSH0003 in the getter of a computed property (#471)
        "Diagnostics/ComputedPropertyGetterFailure.cu.cs",
        // GSH0003 (dynamic array size)
        "Analyzers/DynamicStackAllocSadPath.cs",
        // GSH0003 (switch expression)
        "Analyzers/GpuKernel/UnsupportedSwitch_InGpuKernel.cu.cs",
        "Diagnostics/UnsupportedSwitchPatterns.cu.cs",
        // GSH0003 (barrier in the scope of a using; a using of a class), GSH0001
        "Diagnostics/UsingBarrierScope.cu.cs",
        // GSH0003 (dynamic, TypedReference, GCHandle)
        "Diagnostics/RuntimeOnlyTypes.cu.cs",
        // GSH0003 (finalizer marked as GPU code)
        "Diagnostics/GpuFinalizer.cu.cs",
        // GSH0003 (operator macro target)
        "Diagnostics/OperatorMacroMissingTarget.cu.cs",
        // GSH0003 (operator macro context: none, or more than one)
        "Diagnostics/OperatorMacroContext.cu.cs",
        // GSH0003 (ForUnroll, SyncGrid), GSH0006, GSH0012
        "Analyzers/MacroArgumentSadPath.cs",
        // GSH0011
        "Diagnostics/MacroLambdaWithoutParameter.cu.cs",
        // GSH0016, GSH0017
        "Analyzers/UnrollHintSadPath.cs",
        // GSH0004
        "Generator/KernelReturnTypes_SadPath.cs",
        // GSH0029
        "Diagnostics/UnsafeMallocWarning.cu.cs",
        // GSH0013, GSH0064
        "Analyzers/StructSizeDiagnostics.cs",
        // GSH0054
        "Generator/DialectDiagnostics.cu.cs",
        // GSH0058
        "Generator/AotContextName_Namespaces.cs",
        "Generator/AotContextName_ContextSuffix.cs",
        "Generator/AotContextName_GenericConstructions.cs",
        // GSH0059
        "Generator/KernelOverload_Indistinguishable.cu.cs",
        // GSH0065
        "Analyzers/StaticMemberAccess_SadPath.cs",
        "Diagnostics/StaticMemberAccessTwice.cu.cs",
        // GSH0066
        "Analyzers/InstanceDeviceFunction_SadPath.cs"
    ];

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SadPathInputs_RecordEveryDiagnostic(string dialect)
    {
        var report = new StringBuilder();
        foreach (var input in Inputs)
        {
            report.AppendLine($"## {input}");
            var diagnostics = await KernelDiagnostics.RunTestCaseAsync(input, dialect);
            foreach (var diagnostic in diagnostics)
            {
                report.AppendLine(diagnostic.ToString());
            }

            report.AppendLine();
        }

        var snapshots = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Diagnostics", "Snapshots");
        Directory.CreateDirectory(snapshots);
        await Verifier.Verify(report.ToString(), "txt")
            .UseDirectory(snapshots)
            .UseFileName($"Snap.KernelDiagnosticsInventory_{dialect}");
    }
}
