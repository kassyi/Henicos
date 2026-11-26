#nullable enable
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for a variable passed to an <c>in</c> (or <c>ref readonly</c>) parameter of a device function
/// that reaches a barrier: the callee reads the variable, not a copy taken before the call, and the location is the one
/// C# selected when it evaluated the argument, on every path (#1585, #1173).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public unsafe class InArgumentAliasDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_inlineElement = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchInlineElementKernel);

    internal static readonly DifferentialCase s_parenthesizedInlineElement = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchParenthesizedInlineElementKernel);

    internal static readonly DifferentialCase s_parenthesizedLocal = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchParenthesizedLocalKernel);

    internal static readonly DifferentialCase s_fixedBufferElement = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchFixedBufferElementKernel);

    internal static readonly DifferentialCase s_readOnlyReference = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchReadOnlyReferenceKernel);

    internal static readonly DifferentialCase s_structFieldInlineElement = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchStructFieldInlineElementKernel);

    internal static readonly DifferentialCase s_nestedInlineElement = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchNestedInlineElementKernel);

    internal static readonly DifferentialCase s_indexMovedByCallee = ArrayCase(
        InArgumentAliasDifferentialKernels.LaunchIndexMovedByCalleeKernel);

    // C# copies the element of a value once: the callee reads x, and MakeInline runs once
    internal static readonly DifferentialCase s_valueElement = DiffCases.Create(
        InArgumentAliasDifferentialKernels.LaunchValueElementKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, OutputPolicy.Exact)
            .Out<int>("calls", rows, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        firstInputName: "xs",
        secondInputName: "r",
        outputName: "calls",
        reference: static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            for (int tid = 0; tid < xs.Length; tid++)
            {
                b.Get<int>("r")[tid] = xs[tid] & 0x3FF;
                b.Get<int>("calls")[tid] = 1;
            }
        });

    internal static readonly DifferentialCase s_pointerIndirection = PointerCase(
        nameof(InArgumentAliasDifferentialKernels.PointerIndirectionKernel),
        static (g, b, s) => InArgumentAliasDifferentialKernels.LaunchPointerIndirectionKernel(
            g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_pointerElement = PointerCase(
        nameof(InArgumentAliasDifferentialKernels.PointerElementKernel),
        static (g, b, s) => InArgumentAliasDifferentialKernels.LaunchPointerElementKernel(
            g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_pointerMember = PointerCase(
        nameof(InArgumentAliasDifferentialKernels.PointerMemberKernel),
        static (g, b, s) => InArgumentAliasDifferentialKernels.LaunchPointerMemberKernel(
            g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_dereferencedMember = PointerCase(
        nameof(InArgumentAliasDifferentialKernels.DereferencedMemberKernel),
        static (g, b, s) => InArgumentAliasDifferentialKernels.LaunchDereferencedMemberKernel(
            g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_pointerMovedByLaterArgument = PointerCase(
        nameof(InArgumentAliasDifferentialKernels.PointerMovedByLaterArgumentKernel),
        static (g, b, s) => InArgumentAliasDifferentialKernels.LaunchPointerMovedByLaterArgumentKernel(
            g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX, blockDimX: s.BlockX),
        InArgumentAliasDifferentialKernels.MovedPointerScale);

    internal static readonly DifferentialCase s_pointerMovedByLaterKeywordArgument = PointerCase(
        nameof(InArgumentAliasDifferentialKernels.PointerMovedByLaterKeywordArgumentKernel),
        static (g, b, s) => InArgumentAliasDifferentialKernels.LaunchPointerMovedByLaterKeywordArgumentKernel(
            g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX, blockDimX: s.BlockX),
        InArgumentAliasDifferentialKernels.MovedPointerScale);

    [SkippableFact]
    public void InArgument_InlineArrayElementOfALocal_PassesTheElement() => DifferentialRunner.Run(s_inlineElement);

    [SkippableFact]
    public void InArgument_ParenthesizedInlineArrayElement_PassesTheElement() =>
        DifferentialRunner.Run(s_parenthesizedInlineElement);

    [SkippableFact]
    public void InArgument_ParenthesizedLocal_PassesTheLocal() => DifferentialRunner.Run(s_parenthesizedLocal);

    [SkippableFact]
    public void InArgument_FixedSizeBufferElement_PassesTheElement() => DifferentialRunner.Run(s_fixedBufferElement);

    [SkippableFact]
    public void RefReadOnlyArgument_InlineArrayElementWithoutTheKeyword_PassesTheElement() =>
        DifferentialRunner.Run(s_readOnlyReference);

    [SkippableFact]
    public void InArgument_InlineArrayElementOfAStructField_PassesTheElement() =>
        DifferentialRunner.Run(s_structFieldInlineElement);

    [SkippableFact]
    public void InArgument_ElementOfANestedInlineArray_PassesTheElement() =>
        DifferentialRunner.Run(s_nestedInlineElement);

    [SkippableFact]
    public void InArgument_IndexTheCalleeMoves_KeepsTheElementCSharpSelected() =>
        DifferentialRunner.Run(s_indexMovedByCallee);

    [SkippableFact]
    public void InArgument_ElementOfAnInlineArrayValue_IsCopiedOnce() => DifferentialRunner.Run(s_valueElement);

    [SkippableFact]
    public void InArgument_PointerIndirection_PassesThePointedVariable() =>
        DifferentialRunner.Run(s_pointerIndirection);

    [SkippableFact]
    public void InArgument_PointerElement_PassesThePointedVariable() => DifferentialRunner.Run(s_pointerElement);

    [SkippableFact]
    public void InArgument_PointerMemberAccess_PassesTheField() => DifferentialRunner.Run(s_pointerMember);

    [SkippableFact]
    public void InArgument_FieldOfADereferencedPointer_PassesTheField() =>
        DifferentialRunner.Run(s_dereferencedMember);

    [SkippableFact]
    public void InArgument_PointerMovedByALaterArgument_KeepsTheElementCSharpSelected() =>
        DifferentialRunner.Run(s_pointerMovedByLaterArgument);

    [SkippableFact]
    public void InKeywordArgument_PointerMovedByALaterArgument_KeepsTheElementCSharpSelected() =>
        DifferentialRunner.Run(s_pointerMovedByLaterKeywordArgument);

    // A kernel (int[] xs, int[] r) whose callee reads its in parameter after writing the same variable: x + 7
    private static DifferentialCase ArrayCase(
        Int32PairLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (rng, rows) => new DiffBuffers()
                .In("xs", DiffValues.Int32(rng, rows))
                .Out<int>("r", rows, OutputPolicy.Exact),
            shape: LaunchShape.ForRows(Rows, blockX: 64),
            inputName: "xs",
            outputName: "r",
            reference: static (b, _) =>
            {
                var xs = b.Get<int>("xs");
                for (int tid = 0; tid < xs.Length; tid++)
                {
                    b.Get<int>("r")[tid] = (xs[tid] & 0x3FF) + 7;
                }
            },
            launchExpression: launchExpression);

    // A kernel (int* g, int[] xs, int[] r) over two ints of g per thread: the callee reads and writes the first one
    // (x + 7), and the second one keeps its input; r adds moved, the scaled distance the kernel moved its pointer
    private static DifferentialCase PointerCase(string kernel, PointerLaunch launch, int moved = 0) =>
        new(DiffKernelNames.Of(typeof(InArgumentAliasDifferentialKernels), kernel))
        {
            Shape = LaunchShape.ForRows(Rows, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("xs", DiffValues.Int32(rng, rows))
                .In("g", DiffValues.Int32(rng, rows * 2), OutputPolicy.Exact)
                .Out<int>("r", rows, OutputPolicy.Exact),
            Launch = (b, s) =>
            {
                using var scope = new DiffBuffers().In("g", b.Get<int>("g"), OutputPolicy.Exact).PinOrUpload();
                launch((int*)scope.PinOrUpload<int>("g"), b, s);
            },
            Reference = (b, _) =>
            {
                var xs = b.Get<int>("xs");
                for (int tid = 0; tid < xs.Length; tid++)
                {
                    int x = xs[tid] & 0x3FF;
                    b.Get<int>("g")[tid * 2] = x + 7;
                    b.Get<int>("r")[tid] = x + 7 + moved;
                }
            },
        };

    private delegate void PointerLaunch(int* g, DiffBuffers buffers, LaunchShape shape);
}
