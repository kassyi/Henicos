using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

file struct FileLocalPair_MicroMod
{
    public int Left;
    public int Right;
}

public struct VolatileState_MicroMod
{
    public volatile int Flag;
}

public class MicroModifiersTarget
{
    public static void FileLocalTypes(int[] output)
    {
        FileLocalPair_MicroMod p = new FileLocalPair_MicroMod { Left = 10, Right = 20 };
        output[0] = p.Left + p.Right;
    }

    public static int ParamsSpan(params ReadOnlySpan<int> values)
    {
        return values.Length;
    }

    public static ref readonly int RefReadonlyReturns(ref int a, ref int b, bool first)
    {
        return ref (first ? ref a : ref b);
    }

    public static void VolatileModifier(VolatileState_MicroMod[] states)
    {
        VolatileState_MicroMod s = states[0];
        s.Flag = 1;
        states[0] = s;
    }

    [SkipLocalsInit]
    public static void SkipLocalsInitAttribute(int[] output)
    {
        Span<int> buf = stackalloc int[4];
        buf[0] = 77;
        output[0] = buf[0];
    }

    public static void ForeachTupleDeconstruction((int X, int Y)[] pairs, int[] output)
    {
        int sum = 0;
        foreach (var (x, y) in pairs)
        {
            sum += x + y;
        }
        output[0] = sum;
    }

    public static unsafe void FunctionPointerCallingConventions(delegate* unmanaged[Cdecl]<int, int> fn, int[] output)
    {
        output[0] = fn(10);
    }

    public static void LineDirectives(int[] output)
    {
#line 200 "CustomKernelFile.cu.cs"
        int val = 123;
#line default
        output[0] = val;
    }

    public static void ImplicitStackallocArray(int[] output)
    {
        Span<int> data = stackalloc[] { 10, 20, 30 };
        output[0] = data[1] + data.Length;
    }
}

public class MicroModifiersTests : TranspilerTestBase
{
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AddFive(int x) => x + 5;

    private static unsafe int InvokeFunctionPointerTarget()
    {
        int[] output = [0];
        MicroModifiersTarget.FunctionPointerCallingConventions(&AddFive, output);
        return output[0];
    }

    private static bool RefReadonlyReturnAliases(bool first)
    {
        int a = 1;
        int b = 2;
        ref readonly int selected = ref MicroModifiersTarget.RefReadonlyReturns(ref a, ref b, first);

        // Mutating the source must be observable through the returned reference (aliasing, not a copy).
        a = 100;
        b = 200;
        return selected == (first ? 100 : 200);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_FileLocalTypes_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.FileLocalTypes), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        int[] output = [0];
        MicroModifiersTarget.FileLocalTypes(output);
        Assert.Equal(30, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ParamsSpan_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.ParamsSpan), isDevice: true, dialect: CurrentDialect);

        var (model, methodSymbol) = RoslynTestUtility.GetMethodContext<MicroModifiersTarget>(nameof(MicroModifiersTarget.ParamsSpan));
        var methodDecl = (MethodDeclarationSyntax)methodSymbol.DeclaringSyntaxReferences[0].GetSyntax();
        var validationDiagnostics = new List<Diagnostic>();
        using var walker = GpuKernelValidationWalker.Rent(new ValidationContext(model, methodSymbol, validationDiagnostics.Add));
        walker.Visit(methodDecl);

        validationDiagnostics.AssertContains(GSharpDiagnostics.ParamsModifierNotSupported);
        await Verify(Report(cudaSource, diagnostics));

        Assert.Equal(0, MicroModifiersTarget.ParamsSpan());
        Assert.Equal(3, MicroModifiersTarget.ParamsSpan(1, 2, 3));
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_RefReadonlyReturns_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.RefReadonlyReturns), isDevice: true, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        Assert.True(RefReadonlyReturnAliases(first: true));
        Assert.True(RefReadonlyReturnAliases(first: false));
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_VolatileModifier_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.VolatileModifier), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        VolatileState_MicroMod[] states = [default];
        MicroModifiersTarget.VolatileModifier(states);
        Assert.Equal(1, states[0].Flag);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_SkipLocalsInitAttribute_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.SkipLocalsInitAttribute), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        int[] output = [0];
        MicroModifiersTarget.SkipLocalsInitAttribute(output);
        Assert.Equal(77, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ForeachTupleDeconstruction_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.ForeachTupleDeconstruction), isDevice: false, dialect: CurrentDialect);

        // Tuple deconstruction is unsupported by spec; it must be reported exactly once without leaking raw tokens.
        Assert.Single(diagnostics);
        diagnostics.AssertContains(GSharpDiagnostics.UnsupportedSyntax, "ForEachVariableStatement");
        await Verify(Report(cudaSource, diagnostics));

        int[] output = [0];
        MicroModifiersTarget.ForeachTupleDeconstruction([(1, 2), (3, 4), (5, 6)], output);
        Assert.Equal(21, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_FunctionPointerCallingConventions_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.FunctionPointerCallingConventions), isDevice: false, dialect: CurrentDialect);

        // Host function pointers cannot be dereferenced on the device, so the parameter is rejected.
        diagnostics.AssertContains(GSharpDiagnostics.UnsupportedSyntax, "FunctionPointerType");
        await Verify(Report(cudaSource, diagnostics));

        Assert.Equal(15, InvokeFunctionPointerTarget());
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_LineDirectives_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.LineDirectives), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        int[] output = [0];
        MicroModifiersTarget.LineDirectives(output);
        Assert.Equal(123, output[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ImplicitStackallocArray_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<MicroModifiersTarget>(
            nameof(MicroModifiersTarget.ImplicitStackallocArray), isDevice: false, dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        await Verify(Report(cudaSource, diagnostics));

        int[] output = [0];
        MicroModifiersTarget.ImplicitStackallocArray(output);
        Assert.Equal(23, output[0]);
    }
}
