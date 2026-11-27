#nullable enable
using Kassyi.GSharp.Compiler.Backends;
using Kassyi.GSharp.Compiler.Backends.Cpp;
using Kassyi.GSharp.Compiler.Backends.Cpp.Support;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;
using static Kassyi.GSharp.Tests.Cpu.Backends.Cpp.IrBodyLoweringTests;

namespace Kassyi.GSharp.Tests.Cpu.Backends.Cpp;

/// <summary>
/// The table of the forms the C++ writer spells (<see cref="IrCppSupport"/>): the statement and leaf rows, the nodes
/// that never reach a body, the keys of the nodes without a row, route-uniform initial answers, route-tagged keys, and
/// the walk that lists them in pre-order with their parents.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrCppSupportTests
{
    private static readonly CppBodyProfile s_plain = CppBodyProfile.For(new StandardCppDialect());

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_StatementRows_AreSupported()
    {
        IrNode[] nodes =
        [
            .. Nodes<IrBlock>(Decided(DeviceFunctionCases, DeviceFunctionType, "NestedBlock")),
            .. Nodes<IrIf>(Decided(DeviceFunctionCases, DeviceFunctionType, "IfElse")),
            .. Nodes<IrWhile>(Decided(DeviceFunctionCases, DeviceFunctionType, "WhileFlag")),
            .. Nodes<IrReturn>(Decided(DeviceFunctionCases, DeviceFunctionType, "ReturnsLocal")),
            .. Nodes<IrExpressionStatement>(Decided(ReasonCases, ReasonType, "UnsupportedCall"))
        ];

        Assert.Equal(6, nodes.Length);
        Assert.All(nodes, static node => Assert.True(IrCppSupport.Of(node, null, s_plain).IsSupported,
            node.GetType().Name));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_LeafRows_AreSupportedOrNameTheirShape()
    {
        var declarations = Decided(DeviceFunctionCases, DeviceFunctionType, "Declarations");
        var constants = Nodes<IrConstant>(Decided(DeviceFunctionCases, DeviceFunctionType, "ConstantForms"));
        var references = Nodes<IrVariableReference>(declarations);
        var temporary = IrVariable.CreateTemporary(references[0].Type, RefKind.None, "Sequence",
            references[0].Source);
        var reference = IrVariable.CreateTemporary(references[0].Type, RefKind.Ref, "Sequence", references[0].Source);
        var compilation = IrTestCases.Compile(ReasonCases);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var source = references[0].Source;

        AssertSupported(Nodes<IrConstant>(declarations)[0], "Literal");
        AssertSupported(constants[0], "Value");
        AssertSupported(Nodes<IrConstant>(Decided(DeviceFunctionCases, DeviceFunctionType, "NullArray"))[0], "Null");
        AssertSupported(references.Single(static r => r.Variable.Kind == IrVariableKind.Local), "Local");
        AssertSupported(references.Single(static r => r.Variable.Kind == IrVariableKind.Parameter), "Parameter");
        AssertSupported(Read(temporary, isDeclaration: false), "Temporary");
        AssertUnsupported(Read(temporary, isDeclaration: true), "Declaration");
        AssertUnsupported(Read(reference, isDeclaration: false), "Ref");
        AssertUnsupported(new IrLocalDeclaration(reference, null, source), "Ref");
        Assert.All(Nodes<IrConstant>(Decided(ReasonCases, ReasonType, "DefaultConstant")),
            static constant => AssertUnsupported(constant, "Unwritten"));
        AssertUnsupported(new IrConstant("s", null, stringType, source, "\"s\""), "NonDeviceValue");
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Of_StateMachineNodes_AreNotInTheBody()
    {
        var source = Decided(DeviceFunctionCases, DeviceFunctionType, "Empty").Source;
        var voidType = IrTestCases.Compile(ReasonCases).GetSpecialType(SpecialType.System_Void);
        IrNode[] nodes =
        [
            new IrRuntimeCall(IrRuntimeFunction.WarpRegionEnter, WarpShuffleKind.None, 0, [], null, voidType, source)
        ];

        Assert.All(nodes, static node => AssertUnsupported(node, "NotInBody"));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Of_NodesWithoutARow_ReportTheDecisionShape()
    {
        var shapes = Decided(ReasonCases, ReasonType, "DecidedShapes");
        var conversion = Nodes<IrConversion>(shapes).First();
        var property = Nodes<IrPropertyReference>(shapes).Single();
        var call = Nodes<IrCall>(shapes).Single();
        var sequence = Nodes<IrSequence>(Sequenced(ReasonCases, ReasonType, "UnsupportedSequence")).First();

        Assert.Equal("NativePlain:IrConversion/" + conversion.Decided.Form, Key(conversion));
        Assert.Equal("NativePlain:IrPropertyReference/" + property.Decided.Form, Key(property));
        Assert.Equal("NativePlain:IrCall/" + (call.Decided.Target?.ToString() ?? "TargetNull"), Key(call));
        Assert.Equal("NativePlain:IrSequence/-", Key(sequence));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Of_UnspellableTypes_AreNamed()
    {
        var compilation = IrTestCases.Compile(ReasonCases);
        var source = Decided(DeviceFunctionCases, DeviceFunctionType, "Empty").Source;
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
        var typeParameter = IrTestCases.FindMethod(compilation, ReasonType, "Generic").TypeParameters[0];
        var functionPointer = compilation.CreateFunctionPointerTypeSymbol(int32, RefKind.None,
            ImmutableArray<ITypeSymbol>.Empty, ImmutableArray<RefKind>.Empty);
        (ITypeSymbol Type, string Shape)[] cases =
        [
            (compilation.GetSpecialType(SpecialType.System_String), "Type:String"),
            (functionPointer, "Type:FunctionPointer"),
            (typeParameter, "Type:TypeParameter"),
            (compilation.CreatePointerTypeSymbol(int32), "Type:Pointer")
        ];

        foreach (var (type, shape) in cases)
        {
            var variable = IrVariable.CreateTemporary(type, RefKind.None, "Sequence", source);
            AssertUnsupported(new IrLocalDeclaration(variable, null, source), shape);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_UnsupportedNodes_AreListedInPreOrder()
    {
        var keys = IrCppSupport.Check(Decided(ReasonCases, ReasonType, "UnsupportedAssignment"), s_plain);

        Assert.NotEmpty(keys);
        Assert.Equal("NativePlain:IrAssignment/-", keys[0].ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Of_W1Rows_AnswerTheSameOnEveryRoute()
    {
        var declarations = Decided(DeviceFunctionCases, DeviceFunctionType, "Declarations");
        var constants = Nodes<IrConstant>(Decided(DeviceFunctionCases, DeviceFunctionType, "ConstantForms"));
        var shapes = Decided(ReasonCases, ReasonType, "DecidedShapes");
        var compilation = IrTestCases.Compile(ReasonCases);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
        var pointerDecl = new IrLocalDeclaration(
            IrVariable.CreateTemporary(compilation.CreatePointerTypeSymbol(int32), RefKind.None, "Sequence",
                declarations.Source),
            null, declarations.Source);

        IrNode[] nodes =
        [
            .. Nodes<IrBlock>(Decided(DeviceFunctionCases, DeviceFunctionType, "NestedBlock")),
            .. Nodes<IrIf>(Decided(DeviceFunctionCases, DeviceFunctionType, "IfElse")),
            .. Nodes<IrWhile>(Decided(DeviceFunctionCases, DeviceFunctionType, "WhileFlag")),
            .. Nodes<IrReturn>(Decided(DeviceFunctionCases, DeviceFunctionType, "ReturnsLocal")),
            .. Nodes<IrExpressionStatement>(Decided(ReasonCases, ReasonType, "UnsupportedCall")),
            Nodes<IrConstant>(declarations)[0],
            constants[0],
            Nodes<IrConstant>(Decided(DeviceFunctionCases, DeviceFunctionType, "NullArray"))[0],
            new IrConstant("s", null, stringType, declarations.Source, "\"s\""),
            .. Nodes<IrVariableReference>(declarations),
            pointerDecl,
            Nodes<IrConversion>(shapes).First(),
            Nodes<IrPropertyReference>(shapes).Single(),
            Nodes<IrCall>(shapes).Single()
        ];

        var stack = CppBodyProfile.For(new StackCppDialect());
        var cuda = CppBodyProfile.For(new GSharpCudaDialect());
        foreach (var node in nodes)
        {
            var expected = IrCppSupport.Of(node, null, s_plain);
            Assert.Equal(expected, IrCppSupport.Of(node, null, stack));
            Assert.Equal(expected, IrCppSupport.Of(node, null, cuda));
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Check_KeysCarryTheRoute()
    {
        var assignment = Decided(ReasonCases, ReasonType, "UnsupportedAssignment");

        var stackKeys = IrCppSupport.Check(assignment, CppBodyProfile.For(new StackCppDialect()));
        var cudaKeys = IrCppSupport.Check(assignment, CppBodyProfile.For(new GSharpCudaDialect()));

        Assert.NotEmpty(stackKeys);
        Assert.NotEmpty(cudaKeys);
        Assert.Equal("NativeStack:IrAssignment/-", stackKeys[0].ToString());
        Assert.Equal("Cuda:IrAssignment/-", cudaKeys[0].ToString());
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Check_PassesTheParentToTheGroups()
    {
        var method = Decided(ReasonCases, ReasonType, "UnsupportedCall");
        var parents = new Dictionary<IrNode, IrNode?>(ReferenceEqualityComparer.Instance);
        var seenProfiles = new List<CppBodyProfile>();
        SupportGroup recorder = (IrNode node, IrNode? parent, CppBodyProfile profile, out IrSupport support) =>
        {
            parents.Add(node, parent);
            seenProfiles.Add(profile);
            support = new IrSupport(true, "Recorded");
            return true;
        };

        var keys = IrCppSupport.Check(method, s_plain, [recorder]);

        var statement = Assert.IsType<IrExpressionStatement>(Assert.Single(method.Body.Statements));
        var call = Assert.IsType<IrCall>(statement.Expression);
        Assert.Empty(keys);
        Assert.All(seenProfiles, profile => Assert.Same(s_plain, profile));
        Assert.Null(parents[method.Body]);
        Assert.Same(method.Body, parents[statement]);
        Assert.Same(statement, parents[call]);
        Assert.Same(call, parents[call.Arguments[0].Value]);
    }

    /// <summary>The decided body of a method of a test case file.</summary>
    internal static IrMethod Decided(string testCase, string type, string name)
    {
        var compilation = IrTestCases.Compile(testCase);
        var built = IrTestCases.Build(testCase, type, name);
        var classified = IrClassifier.Classify(built, compilation);
        return IrDecisions.Decide(classified, IrDecisionTestContext.Create(compilation, classified, HostDialects.Cpu));
    }

    /// <summary>The body of a method of a test case file after the sequence pass.</summary>
    internal static IrMethod Sequenced(string testCase, string type, string name)
    {
        var compilation = IrTestCases.Compile(testCase);
        var classified = IrClassifier.Classify(IrTestCases.Build(testCase, type, name), compilation);
        var context = IrDecisionTestContext.Create(compilation, classified, HostDialects.Cpu);
        return IrSequencer.Sequence(IrDecisions.Decide(classified, context), new IrSequenceContext(context));
    }

    private static T[] Nodes<T>(IrMethod method) where T : IrNode =>
        [.. IrNodeChildren.DescendantsAndSelf(method.Body).OfType<T>()];

    private static string Key(IrNode node) =>
        IrSupportKey.Of(IrBodyRoute.NativePlain, node, IrCppSupport.Of(node, null, s_plain)).ToString();

    private static IrVariableReference Read(IrVariable variable, bool isDeclaration) =>
        new(variable, isDeclaration, IrCategory.Value, default, variable.Source);

    private static void AssertSupported(IrNode node, string shape)
    {
        var support = IrCppSupport.Of(node, null, s_plain);
        Assert.True(support.IsSupported, $"{node.GetType().Name}: {support.Shape}");
        Assert.Equal(shape, support.Shape);
    }

    private static void AssertUnsupported(IrNode node, string shape)
    {
        var support = IrCppSupport.Of(node, null, s_plain);
        Assert.False(support.IsSupported, $"{node.GetType().Name}: {support.Shape}");
        Assert.Equal(shape, support.Shape);
    }
}
