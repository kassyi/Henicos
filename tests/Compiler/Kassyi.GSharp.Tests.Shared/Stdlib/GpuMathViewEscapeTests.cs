#nullable enable
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// The GpuMath views (<c>GpuBigUIntView</c> and the other <see langword="ref struct"/> views) cannot leave the call that
/// obtained them: C# itself rejects storing, capturing, boxing and disposing them (#630, design 6.3 RS1-RS10).
/// </summary>
/// <remarks>
/// The inputs are C# that does not compile, so they are test-case files compiled here against the GpuMath assembly of
/// the test output. The valid file shows that the reference works, so each error comes from the escape it tests.
/// Mutants: make <c>GpuBigUIntArray.GetElement</c> return <c>GpuBigUInt</c> again (the RS7 lines compile); drop
/// <see langword="ref"/> from <c>GpuBigUIntArrayView</c> (RS4 of the array view compiles); add an implicit
/// <c>GSharpArrayProxy</c> conversion to <c>GpuBigUIntView</c> (RS4 of the view compiles by boxing the proxy).
/// Dropping <see langword="ref"/> from <c>GpuBigUIntView</c> does not compile GpuMath itself (its
/// <see langword="scoped"/> parameters).
/// </remarks>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuMathViewEscapeTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Escapes_AreCompileErrors()
    {
        Assert.Equal(
            [
                ("CS8345", 10, 13, 10, 27),   // RS1: field of a class
                ("CS8345", 15, 13, 15, 27),   // RS1: field of a non-ref struct
                ("CS8175", 25, 42, 25, 46),   // RS3: captured by a lambda
                ("CS8175", 26, 25, 26, 29),   // RS3: captured by a local function
                ("CS0029", 33, 55, 33, 74),   // RS4b: converted to an interface it does not implement
                ("CS9244", 39, 29, 39, 43),   // RS5: generic type argument without allows ref struct
                ("CS0611", 45, 25, 45, 39),   // RS6: array element
                ("CS1061", 51, 29, 51, 36),   // RS7: no Dispose
                ("CS1674", 52, 9, 52, 46),    // RS7: not disposable in using
                ("CS0019", 57, 13, 57, 40),   // RS8: no expression operators
                ("CS9244", 62, 9, 62, 24),    // RS9: Nullable<T>
                ("CS0029", 69, 24, 69, 29),   // RS4: boxing an array view
                ("CS0029", 70, 26, 70, 45),   // RS4: boxing a view (no implicit GSharpArrayProxy conversion)
            ],
            Errors("Stdlib/GpuMathViewEscape.cs")
        );
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void UseAcrossAwait_IsCompileError()
    {
        // RS2: the error is reported when the async method is lowered, so the test case is emitted
        Assert.Equal([("CS4007", 14, 13, 14, 17)], Errors("Stdlib/GpuMathViewEscapeAsync.cs", emit: true));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ValidUses_Compile()
    {
        // Local views, ToOwned, scoped parameters, ref struct fields and returning a view of a local owner (RS10: no
        // ref field ties a view to its owner, so this compiles and the generation stops the use at run time)
        Assert.Empty(Errors("Stdlib/GpuMathViewEscapeValid.cs", emit: true));
    }

    // The errors of the test case, sorted by position. With emit, also the errors reported while method bodies are
    // lowered (an async method), which the semantic model does not report
    private static (string Id, int StartLine, int StartColumn, int EndLine, int EndColumn)[] Errors(
        string testCase, bool emit = false)
    {
        var (compilation, tree, model) = RoslynTestUtility.GetTestCaseContext(testCase);
        var diagnostics = emit
            ? compilation.Emit(Stream.Null).Diagnostics.Where(d => d.Location.SourceTree == tree)
            : model.GetDiagnostics();
        return diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d =>
            {
                var span = d.Location.GetLineSpan();
                return (d.Id, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                    span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
            })
            .OrderBy(static e => e.Item2)
            .ThenBy(static e => e.Item3)
            .ToArray();
    }
}
