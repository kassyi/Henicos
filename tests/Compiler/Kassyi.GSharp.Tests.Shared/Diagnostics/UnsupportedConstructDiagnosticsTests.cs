using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The analyzer reports the constructs that the transpiler cannot emit (GSH0001, GSH0002, GSH0003, GSH0013) once, at
/// the construct; the generator reports none of them (S-46).
/// </summary>
[Category(TestCategory.Compiler)]
public class UnsupportedConstructDiagnosticsTests
{
    [Theory]
    [InlineData("Analyzers/InvalidMappingSadPath.cs", "Cpu", 28, 19, 28, 31)]
    [InlineData("Analyzers/InvalidMappingSadPath.cs", "Cuda", 28, 19, 28, 31)]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cpu", 12, 26, 12, 38)]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cuda", 12, 26, 12, 38)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ClassCreation_ReportsGsh0001Once(string testCase, string dialect, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(testCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.ObjectCreationNotSupported.Id));
        // The transpiler used to add GSH0003 'ObjectCreationExpression' at the same place
        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cpu", 11, 13, 11, 19)]
    [InlineData("Generator/SadPathDiagnostics.cu.cs", "Cuda", 11, 13, 11, 19)]
    [InlineData("Analyzers/GpuKernel/StringTernary_InGpuKernel.cu.cs", "Cpu", 12, 9, 12, 15)]
    [InlineData("Analyzers/GpuKernel/StringTernary_InGpuKernel.cu.cs", "Cuda", 12, 9, 12, 15)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_StringVariable_ReportsGsh0002AtTheType(string testCase, string dialect, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(testCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    // Line 38 is 'var text = "abc";'
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ImplicitlyTypedStringVariable_ReportsGsh0002AtVar(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSyntaxNodes.cu.cs", dialect);

        Assert.Equal([(Analyzer, 38, 9, 38, 12)], diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    // default(string) on line 13 and the type argument on line 14; the lambda parameter and the local function's return
    // type are a signature
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_StringTypesInABody_ReportGsh0002OutsideSignatures(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/StringTypeUsages.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 13, 27, 13, 33),
                (Analyzer, 14, 14, 14, 20)
            ],
            diagnostics.Ranges(GSharpDiagnostics.StringNotSupported.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_StackArrayWithInitializer_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/SupportedMacroAndArrayForms.cu.cs", dialect);

        Assert.Empty(diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DynamicStackAllocSize_ReportsGsh0003AtTheSize(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/DynamicStackAllocSadPath.cs", dialect);

        Assert.Equal(
            [(Analyzer, 13, 43, 13, 54)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'dynamic array size'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_SwitchExpressionArms_ReportGsh0003AtTheClauseOrPattern(string dialect)
    {
        var whenAndRelational =
            await KernelDiagnostics.RunTestCaseAsync("Analyzers/GpuKernel/UnsupportedSwitch_InGpuKernel.cu.cs", dialect);
        var propertyAndList = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/UnsupportedSwitchPatterns.cu.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 16, 15, 16, 28),
                (Analyzer, 23, 13, 23, 16)
            ],
            whenAndRelational.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
        Assert.Equal(
            [
                (Analyzer, 21, 13, 21, 27),
                (Analyzer, 32, 13, 32, 23)
            ],
            propertyAndList.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroWithoutTarget_ReportsGsh0003AtTheAssignment(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroMissingTarget.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 36, 9, 36, 18)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "'OperatorMacroTargets.Missing'"));
    }

    // NoContextKernel has no variable of the context type; TwoContextsKernel has two before the call (innermost first)
    // and one after it
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroWithoutSingleContext_ReportsGsh0003AtTheAssignment(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroContext.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 43, 9, 43, 18)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "no parameter or local of that type"));
        Assert.Equal(
            [(Analyzer, 55, 9, 55, 18)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "2 parameters or locals of that type are in scope ('second', 'first')"));
        Assert.Equal(2, diagnostics.Length);
    }

    // #478: no overload applies (each candidate with its reason), two apply equally well (with and without a context, or
    // by conversions that are not better than each other),
    // a ref result parameter needs the operator's own type, and the assignment target does not convert to the result
    // parameter of the chosen overload
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroOverload_ReportsGsh0003AtTheAssignment(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroOverload.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 109, 9, 109, 19)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro target 'NoOverloadOps.Shift' has no overload that takes (OverloadValue result, OverloadValue value, int shift): " +
                "Shift(OverloadCtx, int, int): 'OverloadValue' does not convert to 'OverloadCtx' for 'ctx'; " +
                "Shift(ref OverloadCtx, uint[], uint[], int): 'OverloadValue' does not convert to 'uint[]' for 'result'"));
        Assert.Equal(
            [(Analyzer, 121, 9, 121, 18)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro target 'AmbiguousOps.Add' is ambiguous between Add(uint[], uint[], uint[]) and Add(ref OverloadCtx, uint[], uint[], uint[])"));
        Assert.Equal(
            [(Analyzer, 132, 9, 132, 18)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Add(ref uint[], uint[], uint[]): 'ref' parameter 'result' takes only 'uint[]', not 'OverloadRef'"));
        Assert.Equal(
            [(Analyzer, 143, 9, 143, 20)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "The target 'raw' of operator macro 'TargetOps.Add' does not convert to 'OneWayRef' of parameter 'result'"));
        // Better conversions (C# spec §12.6.4.5): a built-in conversion is not better than a user-defined one, and two
        // types that convert to each other are not better conversion targets than each other
        Assert.Equal(
            [(Analyzer, 245, 9, 245, 19)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro target 'ShlOps.Shl' is ambiguous between Shl(uint[], uint[], long) and Shl(uint[], uint[], ShiftCount)"));
        Assert.Equal(
            [(Analyzer, 256, 9, 256, 18)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro target 'MutualOps.Add' is ambiguous between Add(uint[], uint[], uint[]) and Add(MutualRef, MutualRef, MutualRef)"));
        Assert.Equal(
            [(Analyzer, 267, 9, 267, 18)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Add(uint[], in uint[], uint[]): 'in' parameter 'left' takes only 'uint[]', not 'ConversionValue'"));
        Assert.Equal(
            [(Analyzer, 268, 9, 268, 18)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Add(uint[], ref readonly uint[], uint[]): 'ref readonly' parameter 'left' takes only 'uint[]', not 'ConversionValue'"));
        Assert.Equal(8, diagnostics.Length);
    }

    // c += a + b would call the target of + with c as the result and compute c = a + b; c += a has no call to expand into
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroInCompoundAssignment_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroUnsupportedForms.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 67, 9, 67, 19), (Analyzer, 68, 9, 68, 15)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "The compound assignment '+=' does not expand operator macros: give each operator macro a simple assignment of its own, as in 't = a + b; c = c + t;'"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_IncrementOperatorMacro_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroUnsupportedForms.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 78, 9, 78, 16)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "Operator macro '++' is an increment or decrement operator"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_LiftedOperatorMacro_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroUnsupportedForms.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 89, 9, 89, 31)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "Operator macro '+' is applied to nullable operands"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_GenericOperatorMacroTarget_IsNotACandidate(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroUnsupportedForms.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 101, 9, 101, 18)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro target 'FormOps.Mul' has no overload that takes (FormRef result, FormRef left, FormRef right): Mul(T, T, T): generic methods are not candidates"));
        // The five forms above, and the type parameter T in the signature of Mul<T> (a generic device function, reported on its own)
        Assert.Equal(6, diagnostics.Length);
    }

    // An operator macro nested in another one is computed into a temporary only when its target takes the result as a ref or
    // out value; BufferRef's target writes into the buffer of the assignment target
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NestedOperatorMacroThatCannotBeExpanded_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroPositions.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 47, 9, 47, 24)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro '+' is an operand of another operator macro, but its target 'PositionOps.Add' takes the result as 'uint[] result', not as a ref or out parameter of an unmanaged type, so it has no temporary to compute into; assign it to a variable first, as in 't = a + b; c = c + t;'"));

        // The temporary is passed as it is, so its type must be the type of the outer operand
        Assert.Equal(
            [(Analyzer, 105, 9, 105, 24)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro '-' returns 'NarrowValue', but the operand of '-' takes 'WideValue'; assign it to a variable first, as in 't = a + b; c = c + t;'"));
    }

    // ++a; and a++; are statements of their own, and a + b initializes a declaration: no assignment expands them
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroOutsideAssignment_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroPositions.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 57, 9, 57, 12), (Analyzer, 58, 9, 58, 12)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro '++' is expanded only as the right side of an assignment statement, or as an operand of an operator macro there; assign it to a variable first, as in 't = a + b;'"));
        Assert.Equal(
            [(Analyzer, 59, 23, 59, 28)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "Operator macro '+' is expanded only as the right side of an assignment statement"));
        Assert.Equal(7, diagnostics.Length);
    }

    // #759 item 6: the expansion passes the operator's own result on, so a user-defined conversion around it would be dropped;
    // an identity cast is transparent (OperatorMacroNestingTests)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OperatorMacroUnderUserDefinedConversion_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/OperatorMacroPositions.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 146, 40, 146, 45), (Analyzer, 147, 36, 147, 41)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "Operator macro '-' is under a user-defined conversion, which its expansion would drop; assign it to a variable first, as in 't = a + b;', and convert the variable"));
    }

    // StructSizeDiagnostics: SharedMemory<double>(5000) is 40000 bytes; 4096 doubles (exactly 32768 bytes) and 10000
    // bytes are not reported. ComprehensiveSpecKernel: SharedMemory<int>(10000); the dynamic size is not checked.
    [Theory]
    [InlineData("Analyzers/StructSizeDiagnostics.cs", "Cpu", 104, 63, 104, 67)]
    [InlineData("Analyzers/StructSizeDiagnostics.cs", "Cuda", 104, 63, 104, 67)]
    [InlineData("ComprehensiveSpecKernel.cu.cs", "Cpu", 107, 63, 107, 68)]
    [InlineData("ComprehensiveSpecKernel.cu.cs", "Cuda", 107, 63, 107, 68)]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_LargeStaticSharedMemory_ReportsGsh0013AtTheSize(string testCase, string dialect, int startLine,
        int startColumn, int endLine, int endColumn)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(testCase, dialect);

        Assert.Equal(
            [(Analyzer, startLine, startColumn, endLine, endColumn)],
            diagnostics.Ranges(GSharpDiagnostics.ExcessiveSharedMemoryWarning.Id));
    }

    // #439: the getter of a computed property (line 12) is a member of the emitted struct and the method group (line 37)
    // becomes a delegate; neither can pass the table of dynamic shared memory regions that Scratch takes on CUDA. The
    // direct call of DirectCallKernel passes it. The analyzer runs the CUDA transpilation on both dialects.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DynamicSharedMemoryReachedWithoutTable_ReportsGsh0003(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/DynamicSharedMemoryWithoutTable.cu.cs", dialect);

        Assert.Equal(
            [(Analyzer, 12, 27, 12, 80)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, DynamicSharedMemoryAnalysis.ReachedWithoutTable));
        // The method group is in a kernel, so the message names the delegate rather than the code around it
        Assert.Equal(
            [(Analyzer, 37, 34, 37, 41)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "passed as a delegate"));
    }
}
