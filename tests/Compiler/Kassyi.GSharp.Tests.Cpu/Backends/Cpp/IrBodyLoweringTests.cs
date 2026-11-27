#nullable enable
using Kassyi.GSharp.Compiler.Backends;
using Kassyi.GSharp.Compiler.Backends.Cpp;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Bodies;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

namespace Kassyi.GSharp.Tests.Cpu.Backends.Cpp;

/// <summary>
/// The two-stage IR pipeline (<see cref="IrBodyFronts.Create"/> and
/// <see cref="IrBodyLowering.TryLower(IrBodyFront, CppBodyProfile, CancellationToken)"/>), route profiles
/// (<see cref="CppBodyProfile"/>), route cache (<see cref="IrBodyCache"/>), policy-based scan results, and
/// zero-allocation probe measurement.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrBodyLoweringTests
{
    public const string ReasonCases = "Ir/IrBodyReasonCases.cs";
    public const string ReasonType = "IrBodyReasons.IrBodyReasonCases";
    public const string DeviceFunctionCases = "Ir/IrCppDeviceFunctions.cs";
    public const string DeviceFunctionType = "IrCppCases.IrCppDeviceFunctions";

    /// <summary>The methods of the device function input the C++ writer spells (all but the barrier).</summary>
    public static readonly string[] EligibleDeviceFunctions =
    [
        "Empty", "Declarations", "ReturnsConstant", "ReturnsLocal", "ExpressionBody", "IfElse", "IfWithoutBraces",
        "EarlyReturn", "WhileFlag", "NestedBlock", "MultiDeclarator", "ConstantForms", "VarDeclaration", "NullArray"
    ];

    // The reason and detail of each method of the reason cases, derived from the order of IrBodyIneligibility and the
    // builder: foreach, lambdas and a delegate argument are opaque (IrStatementBuilder, IrExpressionBuilder), a
    // [Conditional] call is removed (RemovedCalls), Gpu.NotNull is a macro (GpuMacroClassifier), SharedMemory<T> is a
    // struct creation (IrCreationBuilder), Gpu.SyncThreads is a block barrier (IrSplitClassifier); a removed call
    // evaluates nothing, so the shuffle in its argument is no split; Roslyn gives the target-typed default literal a
    // conversion around an operand of the target type, so the conversion comes first
    [Theory]
    [InlineData("Eligible", "None", "")]
    [InlineData("OpaqueForeach", "OpaqueNode", "UnsupportedOperation/ForEachStatement")]
    [InlineData("OpaqueLambda", "OpaqueNode", "UnsupportedOperation/ParenthesizedLambdaExpression")]
    [InlineData("RemovedCallStatement", "RemovedCall", "")]
    [InlineData("MacroForUnroll", "OpaqueNode", "UnsupportedOperation/SimpleLambdaExpression")]
    [InlineData("MacroNotNull", "Macro", "NotNull")]
    [InlineData("SharedMemoryLocalUnused", "SharedMemoryLocal", "")]
    [InlineData("UnsupportedAssignment", "UnsupportedForm", "NativePlain:IrAssignment/-")]
    [InlineData("BarrierInBody", "SplitCall", "BlockBarrier")]
    [InlineData("BarrierThenForeach", "SplitCall", "BlockBarrier")]
    [InlineData("SplitInsideRemovedCall", "RemovedCall", "")]
    [InlineData("DefaultConstant", "UnsupportedForm", "NativePlain:IrConversion/Transparent")]
    [Scenario(TestScenario.SadPath)]
    public void TryLower_ReasonCases_ReportTheFirstReasonInTheOrderOfTheTable(string name, string reason,
        string detail)
    {
        var result = Lower(ReasonCases, ReasonType, name);

        Assert.Equal(reason, result.Reason.ToString());
        Assert.Equal(detail, result.Detail);
        Assert.Equal(result.Reason == IrBodyIneligibility.None, result.Body is not null);
    }

    // A call and a sequence have no row of the C++ writer yet: their keys are listed
    [Theory]
    [InlineData("UnsupportedCall", "NativePlain:IrCall/")]
    [InlineData("UnsupportedSequence", "NativePlain:IrSequence/-")]
    [Scenario(TestScenario.SadPath)]
    public void TryLower_FormsWithoutARow_ListTheirKeys(string name, string key)
    {
        var result = Lower(ReasonCases, ReasonType, name);

        Assert.Equal(IrBodyIneligibility.UnsupportedForm, result.Reason);
        Assert.Null(result.Body);
        Assert.Contains(result.Unsupported, unsupported => unsupported.ToString().StartsWith(key,
            StringComparison.Ordinal));
        Assert.Equal(result.Unsupported[0].ToString(), result.Detail);
    }

    // The conversion of the default literal, then the three constants the syntax route spells by their position
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TryLower_DefaultConstants_ListTheConversionAndTheUnwrittenConstants()
    {
        var result = Lower(ReasonCases, ReasonType, "DefaultConstant");

        Assert.Equal(
        [
            "NativePlain:IrConversion/Transparent", "NativePlain:IrConstant/Unwritten",
            "NativePlain:IrConstant/Unwritten", "NativePlain:IrConstant/Unwritten"
        ], result.Unsupported.Select(static key => key.ToString()));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryLower_EligibleDeviceFunctions_ReturnABody()
    {
        foreach (var name in EligibleDeviceFunctions)
        {
            var result = Lower(DeviceFunctionCases, DeviceFunctionType, name);

            Assert.True(result.Reason == IrBodyIneligibility.None,
                $"{name}: {result.Reason} {result.Detail}");
            var body = Assert.IsType<IrBody>(result.Body);
            Assert.Equal(IrBodyRoute.NativePlain, result.Route);
            Assert.Equal(IrBodyRoute.NativePlain, body.Profile.Route);
            Assert.Empty(result.Unsupported);
        }

        var barrier = Lower(DeviceFunctionCases, DeviceFunctionType, "RecursiveBarrier");
        Assert.Equal(IrBodyIneligibility.SplitCall, barrier.Reason);
        Assert.Equal("BlockBarrier", barrier.Detail);
        Assert.Null(barrier.Body);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryLower_NothingToSequence_KeepsTheDecidedBody()
    {
        foreach (var name in EligibleDeviceFunctions)
        {
            var body = Assert.IsType<IrBody>(Lower(DeviceFunctionCases, DeviceFunctionType, name).Body);

            Assert.Same(body.Decided, body.Sequenced);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryLower_Body_KeepsTheProfileAndItsDialect()
    {
        var compilation = IrTestCases.Compile(DeviceFunctionCases);
        var method = IrTestCases.FindMethod(compilation, DeviceFunctionType, "Declarations");
        var front = IrBodyFronts.Create(method, compilation, CancellationToken.None);
        ITranspilerDialect[] dialects = [new StandardCppDialect(), new StackCppDialect(), new GSharpCudaDialect()];

        foreach (var dialect in dialects)
        {
            var profile = CppBodyProfile.For(dialect);
            var result = IrBodyLowering.TryLower(front, profile, CancellationToken.None);
            var body = Assert.IsType<IrBody>(result.Body);

            Assert.Equal(profile.Route, result.Route);
            Assert.Same(profile, body.Profile);
            Assert.Same(profile.Dialect, body.Decisions.Dialect);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryLower_StageOrder_FollowsThePipeline()
    {
        IrBodyStage[] frontStages = [IrBodyStage.Build, IrBodyStage.Classify, IrBodyStage.Scan];
        IrBodyStage[] routeStages = [IrBodyStage.Decide, IrBodyStage.Sequence, IrBodyStage.Support];

        var compilation = IrTestCases.Compile(DeviceFunctionCases);
        var declarations = IrTestCases.FindMethod(compilation, DeviceFunctionType, "Declarations");
        var cache = new IrBodyCache(declarations, compilation);
        var observer = new RecordingObserver();
        using (IrBodyProbe.Observe(observer))
        {
            _ = cache.For(CppBodyProfile.For(new StandardCppDialect()));
            _ = cache.For(CppBodyProfile.For(new StackCppDialect()));
        }

        Assert.Equal([.. frontStages, .. routeStages, .. routeStages], observer.Stages);

        var opaqueCompilation = IrTestCases.Compile(ReasonCases);
        var opaqueMethod = IrTestCases.FindMethod(opaqueCompilation, ReasonType, "OpaqueForeach");
        Assert.Equal(frontStages, StagesOf(opaqueMethod, opaqueCompilation));

        var mathType = opaqueCompilation.GetTypeByMetadataName("System.Math");
        Assert.NotNull(mathType);
        var mathAbs = mathType
            .GetMembers("Abs")
            .OfType<IMethodSymbol>()
            .First();
        Assert.Equal([IrBodyStage.Build], StagesOf(mathAbs, opaqueCompilation));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Profile_For_MapsEachDialectToItsRoute()
    {
        var plain = CppBodyProfile.For(new StandardCppDialect());
        var stack = CppBodyProfile.For(new StackCppDialect());
        var cuda = CppBodyProfile.For(new GSharpCudaDialect());

        Assert.Equal(IrBodyRoute.NativePlain, plain.Route);
        Assert.Equal(IrBodyRoute.NativeStack, stack.Route);
        Assert.Equal(IrBodyRoute.Cuda, cuda.Route);
        Assert.Equal(IrSplitPolicy.ExcludeEverySplit, plain.Splits);
        Assert.Equal(IrSplitPolicy.ExcludeEverySplit, stack.Splits);
        Assert.Equal(IrSplitPolicy.ExcludeEverySplit, cuda.Splits);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Cache_KeysTheResultByRoute()
    {
        var compilation = IrTestCases.Compile(DeviceFunctionCases);
        var method = IrTestCases.FindMethod(compilation, DeviceFunctionType, "Declarations");
        var cache = new IrBodyCache(method, compilation);
        var plain = CppBodyProfile.For(new StandardCppDialect());
        var stack = CppBodyProfile.For(new StackCppDialect());

        var firstPlain = cache.For(plain);
        var secondPlain = cache.For(plain);
        var firstStack = cache.For(stack);

        Assert.Same(firstPlain, secondPlain);
        Assert.NotSame(firstPlain, firstStack);
        Assert.Equal(IrBodyRoute.NativePlain, firstPlain.Route);
        Assert.Equal(IrBodyRoute.NativeStack, firstStack.Route);
        Assert.Same(cache.Front, cache.Front);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Cache_SameRouteWithAnotherDialectType_Throws()
    {
        var compilation = IrTestCases.Compile(DeviceFunctionCases);
        var method = IrTestCases.FindMethod(compilation, DeviceFunctionType, "Declarations");
        var cache = new IrBodyCache(method, compilation);
        _ = cache.For(CppBodyProfile.For(new StandardCppDialect()));

        Assert.Throws<InvalidOperationException>(() =>
            cache.For(CppBodyProfile.For(new AlternatePlainDialect())));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ScanResult_Reason_FollowsThePolicyAndTheOrder()
    {
        var withSplitAndOpaque = new IrBodyScanResult(
            hasStrykerMutant: false,
            splitCalls: [IrSplitKind.BlockBarrier],
            firstOpaqueDetail: "UnsupportedOperation/ForEachStatement",
            hasRemovedCall: false,
            firstMacroDetail: "",
            hasSharedMemoryLocal: false);

        Assert.Equal((IrBodyIneligibility.SplitCall, "BlockBarrier"),
            withSplitAndOpaque.Reason(IrSplitPolicy.ExcludeEverySplit));

        var withOpaqueOnly = new IrBodyScanResult(
            hasStrykerMutant: false,
            splitCalls: [],
            firstOpaqueDetail: "UnsupportedOperation/ForEachStatement",
            hasRemovedCall: false,
            firstMacroDetail: "",
            hasSharedMemoryLocal: false);

        Assert.Equal((IrBodyIneligibility.OpaqueNode, "UnsupportedOperation/ForEachStatement"),
            withOpaqueOnly.Reason(IrSplitPolicy.ExcludeEverySplit));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TryLower_DecisionInvariantViolation_Throws()
    {
        var compilation = IrTestCases.Compile(DeviceFunctionCases);
        var method = IrTestCases.FindMethod(compilation, DeviceFunctionType, "ReturnsConstant");
        var front = IrBodyFronts.Create(method, compilation, CancellationToken.None);
        var profile = CppBodyProfile.For(new StandardCppDialect());

        Assert.Throws<InvalidOperationException>(() =>
            IrBodyLowering.TryLower(front, profile, static (classified, _) => classified, CancellationToken.None));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Measure_WithoutAnObserver_AllocatesNothing()
    {
        Assert.Null(IrBodyProbe.Current);
        MeasureMany(10);

        var before = GC.GetAllocatedBytesForCurrentThread();
        MeasureMany(1000);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    /// <summary>
    /// The result of <see cref="IrBodyLowering.TryLower(IrBodyFront, CppBodyProfile, CancellationToken)"/> for a
    /// method of a test case file.
    /// </summary>
    internal static IrBodyResult Lower(string testCase, string type, string name,
        ITranspilerDialect? dialect = null)
    {
        var compilation = IrTestCases.Compile(testCase);
        var method = IrTestCases.FindMethod(compilation, type, name);
        var front = IrBodyFronts.Create(method, compilation, CancellationToken.None);
        return IrBodyLowering.TryLower(front, CppBodyProfile.For(dialect ?? HostDialects.Cpu), CancellationToken.None);
    }

    private static IrBodyStage[] StagesOf(IMethodSymbol method, Compilation compilation)
    {
        var observer = new RecordingObserver();
        using (IrBodyProbe.Observe(observer))
        {
            var front = IrBodyFronts.Create(method, compilation, CancellationToken.None);
            _ = IrBodyLowering.TryLower(front, CppBodyProfile.For(new StandardCppDialect()), CancellationToken.None);
        }

        return [.. observer.Stages];
    }

    private static void MeasureMany(int count)
    {
        for (var i = 0; i < count; i++)
        {
            using (IrBodyProbe.Measure(IrBodyStage.Scan))
            {
            }
        }
    }

    private sealed class RecordingObserver : IrBodyObserver
    {
        public List<IrBodyStage> Stages { get; } = [];

        public override void OnStage(IrBodyStage stage, long ticks) => Stages.Add(stage);
    }

    private sealed class AlternatePlainDialect : ITranspilerDialect
    {
        private readonly ITranspilerDialect _inner = HostDialects.Cpu;

        public string Name => "AlternatePlain";
        public bool IsCuda => _inner.IsCuda;
        public DialectCapabilities Capabilities => _inner.Capabilities;
        public TypeLayout ArrayViewLayout => _inner.ArrayViewLayout;
        public TypeLayout PointerLayout => _inner.PointerLayout;

        public bool TryMapType(string fullyQualifiedName, out string mappedType) =>
            _inner.TryMapType(fullyQualifiedName, out mappedType);

        public bool TryMapType(ITypeSymbol typeSymbol, out string mappedType) =>
            _inner.TryMapType(typeSymbol, out mappedType);

        public bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression) =>
            _inner.TryMapIntrinsic(symbol, out mappedExpression);

        public bool TryMapMemberAccess(ITypeSymbol typeSymbol, string memberName, out string? mappedName,
            out bool isMethodCall) =>
            _inner.TryMapMemberAccess(typeSymbol, memberName, out mappedName, out isMethodCall);

        public bool IsMappedType(ITypeSymbol typeSymbol) => _inner.IsMappedType(typeSymbol);

        public bool TryGetTypeLayout(ITypeSymbol typeSymbol, out TypeLayout layout) =>
            _inner.TryGetTypeLayout(typeSymbol, out layout);
    }
}
