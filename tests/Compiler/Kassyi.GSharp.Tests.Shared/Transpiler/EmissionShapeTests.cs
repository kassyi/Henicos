namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The C++ that the generator writes for shapes the snapshot kernels do not cover: explicit struct layouts with gaps,
/// constructor chains, read-only members, value-returning kernels, inlining keywords, suspending device function
/// signatures, loop and return forms, and labels in copied iterator bodies. Each test checks the lines of one shape in
/// the generated source of <c>TestCases/Transpiler/Emission</c>.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public partial class EmissionShapeTests
{
    private const string Ns = "Kassyi__GSharp__Tests__TestCases__Emission__";
    private const string StructType = "__gsharp_ty_" + Ns;
    private const string Signatures = "__gsharp_fn_" + Ns + "SignatureShapeKernels__";
    private const string Statements = "__gsharp_fn_" + Ns + "StatementShapeKernels__";

    // Explicit layout: fields in offset order, the 4-byte gap and the 16 bytes up to Size = 32 as padding, and a union
    // for the fields that share an offset with the larger one first
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ExplicitLayout_PadsGapsAndTailAndGroupsSharedOffsets(string dialect)
    {
        var source = Generate("StructShapeKernels", dialect);

        AssertContainsLines(source,
            $"struct {StructType}GappedExplicit",
            "{",
            "int __gsharp_mb_A;",
            "unsigned char __gsharp_pad_0[4];",
            "long long __gsharp_mb_B;",
            "unsigned char __gsharp_pad_1[16];",
            "}");
        AssertContainsLines(source,
            $"struct {StructType}OverlapExplicit",
            "{",
            "union{",
            "long long __gsharp_mb_L;",
            "int __gsharp_mb_I;",
            "}",
            ";",
            "int __gsharp_mb_After;",
            "}");
    }

    // : this() to the implicit parameterless constructor starts from zero; : this(v) in a generic struct calls the
    // constructor of the same constructed type
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void ChainedConstructors_StartFromTheChainedValue(string dialect)
    {
        var source = Generate("StructShapeKernels", dialect);
        const string Generic = StructType + "ChainedGeneric_g1_bSystem__Int32_e";

        AssertContainsLines(source,
            $"__device__ inline {StructType}ChainToDefault __gsharp_ctor_{Ns}ChainToDefault(int __gsharp_id_0_a) {{",
            $"{StructType}ChainToDefault __gsharp_self{{}};",
            "__gsharp_self.__gsharp_mb_A=__gsharp_id_0_a;",
            "return __gsharp_self;",
            "}");
        AssertContainsLines(source,
            $"__device__ inline {Generic} __gsharp_ctor_{Ns}ChainedGeneric_g1_bSystem__Int32_e_o1(int __gsharp_id_0_v, int __gsharp_id_1_k) {{",
            $"{Generic} __gsharp_self = __gsharp_ctor_{Ns}ChainedGeneric_g1_bSystem__Int32_e_o0(__gsharp_id_0_v);",
            "__gsharp_self.__gsharp_mb_K=__gsharp_id_1_k;",
            "return __gsharp_self;",
            "}");
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void GpuReadOnlyMembers_AreConst(string dialect)
    {
        var source = Generate("StructShapeKernels", dialect);

        AssertContainsLines(source,
            "const int __gsharp_mb_Limit;",
            "GSharpArray<const int> __gsharp_mb_Table;",
            "int __gsharp_mb_Plain;");
    }

    // The result buffer follows the parameters with a comma only when something precedes it; an expression body
    // stores into the buffer instead of returning
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ValueReturningKernels_TakeTheResultBufferLast()
    {
        var cuda = Generate("SignatureShapeKernels", "Cuda");
        var cpu = Generate("SignatureShapeKernels", "Cpu");
        const string Kernel = "__gsharp_k_" + Ns + "SignatureShapeKernels__";
        const string Context = "__gsharp_context_" + Ns + "SignatureShapeKernels__";

        AssertContainsLines(cuda,
            $"{Kernel}ExpressionResultKernel(int* __restrict__ __gsharp_auto_result){{",
            "unsigned int __gsharp_global_tid = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x) * (blockDim.x * blockDim.y * blockDim.z) + threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;",
            "__gsharp_auto_result[__gsharp_global_tid] = 7;",
            "return;",
            "}");
        Assert.Contains(
            $"{Kernel}BlockResultKernel(GSharpArray<int> __gsharp_id_0_input, int* __restrict__ __gsharp_auto_result){{",
            cuda,
            StringComparison.Ordinal);
        Assert.Contains($"{Kernel}GridSyncResultKernel(int* __restrict__ __gsharp_auto_result){{", cuda, StringComparison.Ordinal);
        Assert.Contains(
            $"{Kernel}ExpressionResultKernel({Context}ExpressionResultKernel* __gsharp_ctx, int* __restrict__ __gsharp_auto_result){{",
            cpu,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{Kernel}GridSyncResultKernel({Context}GridSyncResultKernel* __gsharp_ctx, int* __restrict__ __gsharp_auto_result){{",
            cpu,
            StringComparison.Ordinal);

        // The managed state machine returns the local hoisted into its context
        Assert.Contains("\n__gsharp_auto_result = __gsharp_ctx.value;\n", cpu, StringComparison.Ordinal);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu", "")]
    [InlineData("Cuda", " __device__")]
    public void MethodImplOptions_SelectTheInliningKeyword(string dialect, string deviceKeyword)
    {
        var source = Generate("SignatureShapeKernels", dialect);

        Assert.Contains($"\ninline{deviceKeyword} int {Signatures}DefaultImpl(int __gsharp_id_0_x){{", source, StringComparison.Ordinal);
        Assert.Contains($"\n__noinline__{deviceKeyword} int {Signatures}ShortNoInlining(int __gsharp_id_0_x){{", source, StringComparison.Ordinal);
        Assert.Contains($"\n__forceinline__{deviceKeyword} int {Signatures}EnumAggressiveInlining(int __gsharp_id_0_x){{", source, StringComparison.Ordinal);
    }

    // in and ref readonly stay read-only references in both the C++ and the managed state machine; without parameters
    // the frame is the first parameter
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingDeviceFunctions_KeepReadOnlyParametersAndStartWithTheFrame()
    {
        var cpu = Generate("SignatureShapeKernels", "Cpu");
        const string Frame = "__gsharp_frame_" + Ns + "SignatureShapeKernels__";
        const string StateMachine = "__gsharp_statemachine_" + Ns + "SignatureShapeKernels__";

        Assert.Contains(
            $"template <typename TRoot> inline bool {Signatures}SuspendingReadOnlyParameters(const int* __gsharp_id_0_a, const int* __gsharp_id_1_b, {Frame}SuspendingReadOnlyParameters* __gsharp_ctx, TRoot* __gsharp_root, int* __gsharp_result){{",
            cpu,
            StringComparison.Ordinal);
        Assert.Contains(
            $"template <typename TRoot> inline bool {Signatures}SuspendingWithoutParameters({Frame}SuspendingWithoutParameters* __gsharp_ctx, TRoot* __gsharp_root, int* __gsharp_result){{",
            cpu,
            StringComparison.Ordinal);
        Assert.Contains(
            $"public static bool {StateMachine}SuspendingReadOnlyParameters(in int a, ref readonly int b, {Frame}SuspendingReadOnlyParameters __gsharp_ctx, ref int __gsharp_result) {{",
            cpu,
            StringComparison.Ordinal);
        Assert.Contains(
            $"public static bool {StateMachine}SuspendingWithoutParameters({Frame}SuspendingWithoutParameters __gsharp_ctx, ref int __gsharp_result) {{",
            cpu,
            StringComparison.Ordinal);
    }

    // A typed foreach keeps its type, a for loop keeps every initializer and incrementor, and an out var in a foreach
    // source or a return value is declared before the statement
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LoopAndReturnForms_KeepTypesSeparatorsAndHoistedDeclarations()
    {
        var cuda = Generate("StatementShapeKernels", "Cuda");
        var cpu = Generate("StatementShapeKernels", "Cpu");

        AssertContainsLines(cuda,
            "for(int __gsharp_id_5_typed : __gsharp_id_0_data)",
            "{",
            "__gsharp_id_4_sum=__gsharp_add_i32(__gsharp_id_4_sum,__gsharp_id_5_typed);",
            "}",
            "int __gsharp_id_6_i;",
            "int __gsharp_id_7_j;",
            "for(__gsharp_id_6_i=0,__gsharp_id_7_j=__gsharp_id_2_n;__gsharp_id_6_i<__gsharp_id_7_j;__gsharp_id_6_i++,__gsharp_id_7_j--)",
            "{",
            "__gsharp_id_4_sum=__gsharp_add_i32(__gsharp_id_4_sum,__gsharp_mul_i32(__gsharp_id_6_i,__gsharp_id_7_j));",
            "}",
            "int __gsharp_id_9_first;",
            $"for(auto __gsharp_id_8_picked : ({Statements}TryGet(__gsharp_id_0_data,&__gsharp_id_9_first)?__gsharp_id_0_data:__gsharp_id_1_output))");
        AssertContainsLines(cuda,
            $"inline __device__ int {Statements}ReturnWithOutVar(GSharpArray<int> __gsharp_id_0_data){{",
            "int __gsharp_id_1_v;",
            $"return ({Statements}TryGet(__gsharp_id_0_data,&__gsharp_id_1_v)?__gsharp_id_1_v:0);",
            "}");
        AssertContainsLines(cuda,
            "__gsharp_id_4_sum=__gsharp_add_i32(__gsharp_id_4_sum," + "__gsharp_ctor_" + Ns +
            "EarlyReturnConstructor(__gsharp_id_3_tid).__gsharp_mb_A);",
            "auto __gsharp_id_10_addFound = [&](int __gsharp_id_11_x) {",
            "int __gsharp_id_13_found;",
            $"int __gsharp_id_12_r = ({Statements}TryGet(__gsharp_id_0_data,&__gsharp_id_13_found)?__gsharp_add_i32(__gsharp_id_13_found,__gsharp_id_11_x):__gsharp_id_11_x);");
        Assert.Single(Regex.Matches(cuda, @"int __gsharp_id_13_found;"));
        AssertContainsLines(cpu,
            "int __gsharp_id_1_w;",
            $"*__gsharp_result = ({Statements}TryGet(__gsharp_ctx->__gsharp_id_0_data,&__gsharp_id_1_w)?__gsharp_id_1_w:0);");
    }

    // An early return in a struct constructor returns the value under construction
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void EarlyReturnInStructConstructor_ReturnsTheValue(string dialect)
    {
        AssertContainsLines(Generate("StatementShapeKernels", dialect),
            $"{StructType}EarlyReturnConstructor __gsharp_self{{}};",
            "__gsharp_self.__gsharp_mb_A=__gsharp_id_0_a;",
            "if(__gsharp_id_0_a>0)",
            "{",
            "return __gsharp_self;",
            "}",
            "__gsharp_self.__gsharp_mb_A=__gsharp_neg_i32(__gsharp_id_0_a);",
            "return __gsharp_self;",
            "}");
    }

    // Every copy of the loop body gets its own label, every goto names a label of the same kernel, and the labels of
    // the expansion are not renamed by Gpu.ForUnroll
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu", "LabelInIteratorBodyKernel", 2)]
    [InlineData("Cuda", "LabelInIteratorBodyKernel", 2)]
    [InlineData("Cpu", "LabelInUnrolledIteratorBodyKernel", 4)]
    [InlineData("Cuda", "LabelInUnrolledIteratorBodyKernel", 4)]
    public void LabelsInCopiedIteratorBodies_AreUniqueAndMatchTheirGotos(string dialect, string kernel, int copies)
    {
        var body = KernelSource(Generate("StatementShapeKernels", dialect), kernel);

        var labels = LabelRegex().Matches(body).Select(static m => m.Groups[1].Value).ToList();
        var gotos = GotoRegex().Matches(body).Select(static m => m.Groups[1].Value).ToList();

        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
        Assert.All(gotos, target => Assert.Contains(target, labels));
        var userLabels = labels.Where(static l => l.Contains("_Skip", StringComparison.Ordinal)).ToList();
        Assert.Equal(copies, userLabels.Count);
        Assert.Equal(copies, gotos.Count(static g => g.Contains("_Skip", StringComparison.Ordinal)));
        Assert.All(
            labels.Where(static l => l.StartsWith("__gsharp_iter_", StringComparison.Ordinal)),
            static label => Assert.DoesNotContain("_Unroll_", label, StringComparison.Ordinal));
        if (kernel == "LabelInUnrolledIteratorBodyKernel")
        {
            Assert.Equal(2, userLabels.Count(static l => l.EndsWith("_Skip_Unroll_0", StringComparison.Ordinal)));
            Assert.Equal(2, userLabels.Count(static l => l.EndsWith("_Skip_Unroll_1", StringComparison.Ordinal)));
        }
    }

    // The expansion depth is counted along one chain of nested expansions (16 allowed), not across siblings; one more
    // level fails with GSH0070 at the foreach that would expand it
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void IteratorExpansionDepth_AllowsSixteenNestedLevelsAndAnyNumberOfSiblings(string dialect)
    {
        var source = Generate("IteratorDepthKernels", dialect);

        Assert.Equal(16, Regex.Matches(KernelSource(source, "SixteenLevelsKernel"), @"__gsharp_iter_end_\d+:;").Count);
        Assert.Equal(17, Regex.Matches(KernelSource(source, "SiblingsKernel"), @"__gsharp_iter_end_\d+:;").Count);

        var overflow = GenerateAllowingFailures("IteratorDepthOverflowKernels", dialect);
        Assert.Contains(
            "SeventeenLevelsKernel_Source = @\"#error \"\"(148,9): GSH0070: Iterator method 'L17' is not supported on the GPU: inline expansion depth exceeds the maximum of 16.\"\"",
            overflow,
            StringComparison.Ordinal);
    }

    // What the transpiler cannot emit is stated as #error in the generated source, once per failure, at the place the
    // analyzer reports it: an instance device function, a function pointer parameter (of a suspending device function
    // on the CPU), a device function returning a managed type, a host static field read by a kernel and a ref local
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task UnsupportedDeclarations_AreStatedInTheGeneratedSource(string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/Emission/StatedFailureKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, dialect);

        Assert.Equal(
            [
                ("(12,16): GSH0066", true),
                ("(16,60): GSH0003", true),
                ("(24,26): GSH0004", true),
                ("(30,23): GSH0065", true),
                ("(31,9): GSH0002", true),
                ("(38,25): GSH0003", true)
            ],
            KernelDiagnostics.FindStatedFailures(run).OrderBy(static f => f.Failure, StringComparer.Ordinal));
        var generated = string.Join("\n", run.Output.SyntaxTrees.Where(t => !run.Input.SyntaxTrees.Contains(t)).Select(static t => t.ToString()));
        Assert.Contains(
            "(30,23): GSH0065: The static member 'StatedFailureKernels.s_hostCounter' cannot be accessed in GPU code",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "(12,16): GSH0066: The method 'StatedFailureKernels.InstanceHelper' is marked with [GpuDeviceFunction] but is an instance method.",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("(16,60): GSH0003: The syntax 'FunctionPointerType' is not supported", generated, StringComparison.Ordinal);
        var refLocal = Assert.Single(run.Analyzer, static d => d.Location.GetLineSpan().StartLinePosition.Line + 1 == 38);
        var span = refLocal.Location.GetLineSpan().Span;
        Assert.Equal(
            (GSharpDiagnostics.UnsupportedSyntax.Id, 38, 25, 38, 38),
            (refLocal.Id, span.Start.Line + 1, span.Start.Character + 1, span.End.Line + 1, span.End.Character + 1));
    }

    // A call qualified with the type name and a call in an expanded iterator pass the dynamic shared memory table
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DynamicSharedMemoryHelper_QualifiedOrInsideAnIterator_GetsTheTable()
    {
        var cuda = Generate("DynamicSharedMemoryCallKernels", "Cuda");
        const string Helper = "__gsharp_fn_" + Ns + "DynamicSharedMemoryCallKernels__PlainHelper";

        Assert.Contains($"={Helper}(__gsharp_id_1_n,__gsharp_id_2_tid,__gsharp_dsmem);", cuda, StringComparison.Ordinal);
        Assert.Contains($"int __gsharp_il_2_4_v = {Helper}(__gsharp_il_1_0_n,1,__gsharp_dsmem);", cuda, StringComparison.Ordinal);
        Assert.Contains($"int __gsharp_il_3_4_v = {Helper}(__gsharp_il_1_0_n,2,__gsharp_dsmem);", cuda, StringComparison.Ordinal);
    }

    // A method group of a helper that takes the table cannot become a delegate (GSH0003 on the method name)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task DynamicSharedMemoryHelper_AsMethodGroup_FailsWithGSH0003()
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/Emission/DynamicSharedMemoryDelegateKernels.cu.cs"));

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cuda");

        Assert.Equal([("(22,38): GSH0003", true)], KernelDiagnostics.FindStatedFailures(run));
        var diagnostic = Assert.Single(run.Analyzer);
        var span = diagnostic.Location.GetLineSpan().Span;
        Assert.Equal((22, 38, 22, 49), (span.Start.Line + 1, span.Start.Character + 1, span.End.Line + 1, span.End.Character + 1));
    }

    // An explicit `= default` is an initialization C# performs whatever [SkipLocalsInit] says (Roslyn emits initobj), so
    // the local keeps it with the attribute on the method, its type or its module (#622)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("SkipLocalsInitKernels", "ZeroInitializedKernel")]
    [InlineData("SkipLocalsInitKernels", "MethodSkipsKernel")]
    [InlineData("SkipLocalsInitKernels", "TypeSkipsKernel")]
    [InlineData("SkipLocalsInitModuleKernels", "ModuleSkipsKernel")]
    public void SkipLocalsInit_OnMethodTypeOrModule_KeepsTheExplicitInitializer(string testCase, string kernel)
    {
        var body = KernelSource(Generate(testCase, "Cuda"), kernel);

        Assert.Contains("\nint __gsharp_id_2_value = {};\n", body, StringComparison.Ordinal);
    }

    // A stack array declaration is zeroed ({}) unless it is a stackalloc without an initializer under [SkipLocalsInit].
    // The attribute is found where Roslyn looks for it: the method, the property of an accessor, a lambda or the method
    // around a local function, the containing types outward, the module. new T[N] (also as a Span) and a ReadOnlySpan
    // are always zeroed; each instantiation of a generic struct follows the same rule
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("SkipLocalsInitKernels", "int", "zeroedBuf", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "methodBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "methodArray", "{}")]
    [InlineData("SkipLocalsInitKernels", "const int", "methodReadOnly", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "methodSpanArray", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "lambdaSkipsBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "lambdaZeroedBuf", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "genericZeroedBuf", "{}")]
    [InlineData("SkipLocalsInitKernels", "float", "genericZeroedBuf", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "genericSkipsBuf", "")]
    [InlineData("SkipLocalsInitKernels", "float", "genericSkipsBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "outerSkipsBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "outerZeroedBuf", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "ownSkipsBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "propertyZeroedBuf", "{}")]
    [InlineData("SkipLocalsInitKernels", "int", "propertySkipsBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "typeBuf", "")]
    [InlineData("SkipLocalsInitKernels", "int", "nestedBuf", "")]
    [InlineData("SkipLocalsInitModuleKernels", "int", "moduleBuf", "")]
    [InlineData("SkipLocalsInitModuleKernels", "int", "moduleArray", "{}")]
    public void SkipLocalsInit_LeavesOutOnlyTheZeroFillOfAStackAlloc(
        string testCase, string elementType, string local, string initializer)
    {
        foreach (var dialect in (string[])["Cpu", "Cuda"])
        {
            var source = Generate(testCase, dialect);
            var declaration = new Regex($@"\n{elementType} \S*_{local}\[4\](.*);\n");

            var match = declaration.Match(source);
            Assert.True(match.Success, $"{local} is not declared as a stack array ({dialect}).");
            Assert.Equal(initializer, match.Groups[1].Value);
        }
    }

    // A stackalloc in an argument lives in a storage declared before the statement; it is zeroed where it is evaluated
    // unless [SkipLocalsInit] applies, as a declaration is
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ZeroInitializedKernel", "__gsharp_stackalloc_zero")]
    [InlineData("MethodSkipsKernel", "__gsharp_stackalloc_span")]
    public void StackAllocInAnArgument_IsZeroedUnlessSkipLocalsInitApplies(string kernel, string view)
    {
        var body = KernelSource(Generate("SkipLocalsInitKernels", "Cuda"), kernel);

        var storage = Regex.Match(body, @"\nint (__gsharp_stackalloc_\d+)\[4\];\n");
        Assert.True(storage.Success, "The storage of the stackalloc is not declared.");
        Assert.Contains($"ReadFirst({view}({storage.Groups[1].Value}))", body, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?m)^(\w+):;?$")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"goto (\w+);")]
    private static partial Regex GotoRegex();

    private static string KernelSource(string generated, string kernel)
    {
        var start = generated.IndexOf($"{kernel}_Source = @\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{kernel}_Source is not generated.");
        var end = generated.IndexOf("\n\";", start, StringComparison.Ordinal);
        return generated[start..end];
    }

    private static void AssertContainsLines(string source, params string[] lines)
    {
        var expected = string.Join("\n", lines);
        Assert.True(source.Contains(expected, StringComparison.Ordinal), "Missing lines:\n" + expected);
    }

    private static string GenerateAllowingFailures(string testCase, string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase($"Transpiler/Emission/{testCase}.cu.cs"));
        var (driver, _, _) = RoslynTestUtility.RunGenerator(compilation, dialect);
        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(static t => t.GetText().ToString()))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // Not cached across tests: a mutation test run activates one compiler mutant after another in the same process
    private static string Generate(string testCase, string dialect)
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase($"Transpiler/Emission/{testCase}.cu.cs"));
        var (driver, output, diagnostics) = RoslynTestUtility.RunGenerator(compilation, dialect);
        var errors = diagnostics
            .Concat(output.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        var source = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(static t => t.GetText().ToString()))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.DoesNotContain("#error", source, StringComparison.Ordinal);
        return source;
    }
}
