#nullable enable
using Kassyi.GSharp.Compiler.Backends;
using Kassyi.GSharp.Compiler.Backends.Cpp;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Bodies;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Naming;
using Kassyi.GSharp.Tests.Shared.Transpiler.Ir;
using static Kassyi.GSharp.Tests.Cpu.Backends.Cpp.IrBodyLoweringTests;

namespace Kassyi.GSharp.Tests.Cpu.Backends.Cpp;

/// <summary>
/// The plan of the C++ names of the variables of an IR body (<see cref="IrCppNames"/>): user variables take the name
/// the syntax path gives them through <see cref="IIrCppNameSource"/>, the plan carries the body's route, sequence
/// temporaries are numbered in the order a writer spells them, and a temporary no namer takes or two variables with one
/// name are internal errors.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrCppNamesTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_UserLocalsAndParameters_UseTheCppNameResolver()
    {
        var body = Assert.IsType<IrBody>(Lower(DeviceFunctionCases, DeviceFunctionType, "Declarations").Body);
        var names = IrCppNames.Create(body, new TranspilerCppNameSource());
        var locals = body.Sequenced.Body.Statements
            .OfType<IrLocalDeclaration>()
            .Select(static declaration => declaration.Variable)
            .ToArray();
        var x = locals.Single(static local => local.Name == "x");
        var flag = Assert.Single(body.Sequenced.Parameters);

        Assert.Equal(3, locals.Length);
        foreach (var variable in new[] { x, flag })
        {
            var expected = CppNameResolver.Resolve(variable.Symbol, variable.Name, HostDialects.Cpu, 0).ToString();
            Assert.Equal(expected, names[variable]);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_Route_IsTheRouteOfTheBody()
    {
        var body = Assert.IsType<IrBody>(Lower(DeviceFunctionCases, DeviceFunctionType, "Declarations").Body);
        var names = IrCppNames.Create(body, new TranspilerCppNameSource());

        Assert.Equal(IrBodyRoute.NativePlain, names.Route);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_SequenceTemporaries_AreNumberedInSpellingOrder()
    {
        var body = SequencedBody(ReasonCases, ReasonType, "UnsupportedSequence");
        var names = IrCppNames.Create(body, new TranspilerCppNameSource());
        var temporaries = IrSequenceNumbering.Of(body.Sequenced.Body);

        Assert.Equal(2, temporaries.Length);
        Assert.Equal(GSharpInternalNames.Lowering.SequenceTempPrefix + "0", names[temporaries[0]]);
        Assert.Equal(GSharpInternalNames.Lowering.SequenceTempPrefix + "1", names[temporaries[1]]);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_UnknownTemporary_Throws()
    {
        var body = WithTemporaries(1);

        Assert.Throws<InvalidOperationException>(() => IrCppNames.Create(body, new TranspilerCppNameSource()));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_SameCppName_Throws()
    {
        var one = WithTemporaries(1);
        var two = WithTemporaries(2);
        var single = Assert.Single(DeclaredVariables(one));

        // One temporary takes the name; the second one with the same name is the error
        Assert.Equal("same", IrCppNames.Create(one, new TranspilerCppNameSource(), [new FixedNamer("same")])[single]);
        Assert.Throws<InvalidOperationException>(() =>
            IrCppNames.Create(two, new TranspilerCppNameSource(), [new FixedNamer("same")]));
    }

    private static IrVariable[] DeclaredVariables(IrBody body) =>
    [
        .. body.Sequenced.Body.Statements
            .OfType<IrLocalDeclaration>()
            .Select(static declaration => declaration.Variable)
    ];

    // The body of a method with its sequences, as TryLower builds it before the support check
    private static IrBody SequencedBody(string testCase, string type, string name)
    {
        var compilation = IrTestCases.Compile(testCase);
        var method = IrTestCases.FindMethod(compilation, type, name);
        var front = IrBodyFronts.Create(method, compilation, CancellationToken.None);
        var classified = front.Classified ?? throw new InvalidOperationException("Expected classified IR.");
        var profile = CppBodyProfile.For(HostDialects.Cpu);
        var context = IrDecisionTestContext.Create(compilation, classified, profile.Dialect);
        var decided = IrDecisions.Decide(classified, context);
        var sequenced = IrSequencer.Sequence(decided, new IrSequenceContext(context));
        return new IrBody(front, profile, decided, sequenced, context);
    }

    // The body of Empty with declarations of temporaries that no sequence holds (an argument spill of a split)
    private static IrBody WithTemporaries(int count)
    {
        var body = Assert.IsType<IrBody>(Lower(DeviceFunctionCases, DeviceFunctionType, "Empty").Body);
        var method = body.Sequenced;
        var type = IrTestCases.Compile(DeviceFunctionCases).GetSpecialType(SpecialType.System_Int32);
        var variables = Enumerable.Range(0, count)
            .Select(_ => IrVariable.CreateTemporary(type, RefKind.None, "Argument", method.Body.Source))
            .ToImmutableArray();
        var statements = variables
            .Select(variable => (IrStatement)new IrLocalDeclaration(variable, null, method.Body.Source))
            .ToImmutableArray();
        var changed = new IrMethod(method.Symbol, method.Parameters, method.This,
            new IrBlock(statements, variables, method.Body.Source), method.Source);
        return new IrBody(body.Front, body.Profile, body.Decided, changed, body.Decisions);
    }

    // A namer that gives every temporary the same name
    private sealed class FixedNamer(string name) : IIrCppTemporaryNamer
    {
        public bool TryName(IrVariable variable, IrCppNamingState state, out string cpp)
        {
            cpp = name;
            return true;
        }
    }
}
