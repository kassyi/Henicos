#nullable enable
using K = Kassyi.GSharp.Tests.Shared.Differential.InArgumentAliasDifferentialKernels;

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
    private const string Kernels = nameof(InArgumentAliasDifferentialKernels);

    private static readonly DifferentialCase s_inlineElement = ArrayCase(
        nameof(K.InlineElementKernel),
        static (b, s) => K.LaunchInlineElementKernel(b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_parenthesizedInlineElement = ArrayCase(
        nameof(K.ParenthesizedInlineElementKernel),
        static (b, s) => K.LaunchParenthesizedInlineElementKernel(b.Get<int>("xs"), b.Get<int>("r"),
            gridDimX: s.GridX, blockDimX: s.BlockX));

    private static readonly DifferentialCase s_parenthesizedLocal = ArrayCase(
        nameof(K.ParenthesizedLocalKernel),
        static (b, s) => K.LaunchParenthesizedLocalKernel(b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_fixedBufferElement = ArrayCase(
        nameof(K.FixedBufferElementKernel),
        static (b, s) => K.LaunchFixedBufferElementKernel(b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_readOnlyReference = ArrayCase(
        nameof(K.ReadOnlyReferenceKernel),
        static (b, s) => K.LaunchReadOnlyReferenceKernel(b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_structFieldInlineElement = ArrayCase(
        nameof(K.StructFieldInlineElementKernel),
        static (b, s) => K.LaunchStructFieldInlineElementKernel(b.Get<int>("xs"), b.Get<int>("r"),
            gridDimX: s.GridX, blockDimX: s.BlockX));

    private static readonly DifferentialCase s_nestedInlineElement = ArrayCase(
        nameof(K.NestedInlineElementKernel),
        static (b, s) => K.LaunchNestedInlineElementKernel(b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_indexMovedByCallee = ArrayCase(
        nameof(K.IndexMovedByCalleeKernel),
        static (b, s) => K.LaunchIndexMovedByCalleeKernel(b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    // C# copies the element of a value once: the callee reads x, and MakeInline runs once
    private static readonly DifferentialCase s_valueElement = new(Kernels + "." + nameof(K.ValueElementKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, OutputPolicy.Exact)
            .Out<int>("calls", rows, OutputPolicy.Exact),
        Launch = static (b, s) => K.LaunchValueElementKernel(b.Get<int>("xs"), b.Get<int>("r"), b.Get<int>("calls"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Reference = static (b, _) =>
        {
            var xs = b.Get<int>("xs");
            for (int tid = 0; tid < xs.Length; tid++)
            {
                b.Get<int>("r")[tid] = xs[tid] & 0x3FF;
                b.Get<int>("calls")[tid] = 1;
            }
        },
    };

    private static readonly DifferentialCase s_pointerIndirection = PointerCase(
        nameof(K.PointerIndirectionKernel),
        static (g, b, s) => K.LaunchPointerIndirectionKernel(g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_pointerElement = PointerCase(
        nameof(K.PointerElementKernel),
        static (g, b, s) => K.LaunchPointerElementKernel(g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_pointerMember = PointerCase(
        nameof(K.PointerMemberKernel),
        static (g, b, s) => K.LaunchPointerMemberKernel(g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_dereferencedMember = PointerCase(
        nameof(K.DereferencedMemberKernel),
        static (g, b, s) => K.LaunchDereferencedMemberKernel(g, b.Get<int>("xs"), b.Get<int>("r"), gridDimX: s.GridX,
            blockDimX: s.BlockX));

    private static readonly DifferentialCase s_pointerMovedByLaterArgument = PointerCase(
        nameof(K.PointerMovedByLaterArgumentKernel),
        static (g, b, s) => K.LaunchPointerMovedByLaterArgumentKernel(g, b.Get<int>("xs"), b.Get<int>("r"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        K.MovedPointerScale);

    private static readonly DifferentialCase s_pointerMovedByLaterKeywordArgument = PointerCase(
        nameof(K.PointerMovedByLaterKeywordArgumentKernel),
        static (g, b, s) => K.LaunchPointerMovedByLaterKeywordArgumentKernel(g, b.Get<int>("xs"), b.Get<int>("r"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        K.MovedPointerScale);

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
    private static DifferentialCase ArrayCase(string kernel, Action<DiffBuffers, LaunchShape> launch) =>
        new(Kernels + "." + kernel)
        {
            Shape = LaunchShape.ForRows(Rows, blockX: 64),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("xs", DiffValues.Int32(rng, rows))
                .Out<int>("r", rows, OutputPolicy.Exact),
            Launch = launch,
            Reference = static (b, _) =>
            {
                var xs = b.Get<int>("xs");
                for (int tid = 0; tid < xs.Length; tid++)
                {
                    b.Get<int>("r")[tid] = (xs[tid] & 0x3FF) + 7;
                }
            },
        };

    // A kernel (int* g, int[] xs, int[] r) over two ints of g per thread: the callee reads and writes the first one
    // (x + 7), and the second one keeps its input; r adds moved, the scaled distance the kernel moved its pointer
    private static DifferentialCase PointerCase(string kernel, PointerLaunch launch, int moved = 0) =>
        new(Kernels + "." + kernel)
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
