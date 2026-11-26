#nullable enable
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Sequencing;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Over every GPU method of the tests: the sequences the IR sequence pass makes are those the syntax path writes as
/// <c>__gsharp_seq_&lt;n&gt;</c> temporaries, with the same first temporary of each, the same number of temporaries
/// and the same declarations, in the methods whose forms the pass covers (<see cref="IrSequenceAgreementScope"/>).
/// </summary>
/// <remarks>
/// The sequences are those of the native writer, so the syntax path writes the method in the native dialect the
/// decisions are made for (<see cref="HostDialects.Cpu"/>). It writes a method that waits at a barrier from a lowered
/// state machine; such a method is not compared.
/// </remarks>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrSequenceAgreementTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryComparableMethod_SequencesAgreeWithTheSyntaxPath()
    {
        var mismatches = new ConcurrentBag<string>();
        var excluded = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var compared = 0;
        var withSequences = 0;
        var nestedConversions = 0;
        var withCalls = 0;
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                switch (Compare(compilation, symbol, built, mismatches, ref nestedConversions, ref withCalls))
                {
                    case ({ } reason, _):
                        excluded.AddOrUpdate(reason, 1, static (_, count) => count + 1);
                        break;
                    case (_, var has):
                        Interlocked.Increment(ref compared);
                        if (has)
                        {
                            Interlocked.Increment(ref withSequences);
                        }

                        break;
                }
            }
        });

        output.WriteLine($"Compared {compared} methods, {withSequences} with sequences, " +
                         $"{nestedConversions} spilled operands below two implicit conversions, " +
                         $"{withCalls} with a call or a creation of two operands or more");
        foreach (var pair in excluded.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"Excluded ({pair.Key}): {pair.Value}");
        }

        foreach (var mismatch in mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100))
        {
            output.WriteLine(mismatch);
        }

        Assert.True(compared > 0 && withSequences > 0, "No method with sequences was compared.");
        Assert.True(mismatches.IsEmpty,
            string.Join("\n", mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void FormCases_SequencesAgreeWithTheSyntaxPath()
    {
        var compilation = IrTestCases.Compile(IrSequenceTestCases.Forms);
        var type = compilation.GetTypeByMetadataName(IrSequenceTestCases.FormsType) ??
                   throw new InvalidOperationException("The form cases are missing.");
        var mismatches = new List<string>();
        var compared = 0;
        foreach (var symbol in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (symbol.ReturnsVoid &&
                symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is MethodDeclarationSyntax declaration)
            {
                var sequenced = IrSequenceTestCases.Run(symbol.Name).Sequenced;
                var old = IrSequenceTemporaries.OfSyntaxPath(declaration,
                    compilation.GetSemanticModel(declaration.SyntaxTree));
                var ir = IrSequenceTemporaries.Of(sequenced);
                compared++;
                if (old.OtherLambdas > 0 || !old.SameAs(ir))
                {
                    mismatches.Add($"{symbol.Name}: old {old} other {old.OtherLambdas} IR {ir}");
                }
            }
        }

        Assert.True(compared > 20, $"Only {compared} form cases were compared.");
        Assert.Empty(mismatches);
    }

    private static (string? Excluded, bool HasSequences) Compare(Compilation compilation, IMethodSymbol symbol,
        IrMethod built, ConcurrentBag<string> mismatches, ref int nestedConversions, ref int withCalls)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax declaration)
        {
            return ("no declaration", false);
        }

        var where = IrCoverageInputs.Where(declaration.SyntaxTree, symbol);
        var decided = IrSequenceTestCases.Decide(compilation, built);
        var sequenced = IrSequenceTestCases.Sequence(compilation, decided);
        foreach (var violation in IrInvariants.Verify(sequenced, compilation)
                     .Concat(IrDecisionInvariants.Verify(sequenced.Body))
                     .Concat(IrSequenceInvariants.Verify(sequenced.Body)))
        {
            mismatches.Add($"{where}: invariant: {violation}");
        }

        if (IrSequenceAgreementScope.ExclusionOf(decided, sequenced) is { } reason)
        {
            return (reason, false);
        }

        // The native dialect lowers a method that waits at a barrier into a state machine before it writes it; the IR
        // is compared with the body as written
        if (SuspensionAnalysis.RequiresStateMachine(symbol, compilation))
        {
            return ("lowered on the native path", false);
        }

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        IrSequenceTemporaries old;
        try
        {
            old = IrSequenceTemporaries.OfSyntaxPath(declaration, model);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // A method the syntax path cannot write from a compilation of its file alone (a name it cannot build, a
            // shared memory site it cannot key) has nothing to compare
            return ($"syntax path fails: {exception.GetType().Name}", false);
        }

        if (old.OtherLambdas > 0)
        {
            return ("other lambda", false);
        }

        var ir = IrSequenceTemporaries.Of(sequenced);
        if (!old.SameAs(ir))
        {
            mismatches.Add($"{where}: old {old} IR {ir}");
        }

        Interlocked.Add(ref nestedConversions, NestedConversions(sequenced));
        if (HasCallOfTwoOperands(decided))
        {
            Interlocked.Increment(ref withCalls);
        }

        return (null, !ir.Firsts.IsEmpty);
    }

    // A call or a struct creation with two operands or more, whose arguments may need a sequence
    private static bool HasCallOfTwoOperands(IrMethod decided) =>
        IrNodeChildren.DescendantsAndSelf(decided.Body).Any(static node => node switch
        {
            IrCall call => (call.Receiver is null ? 0 : 1) +
                           call.Arguments.Count(static a => a.Kind != IrArgumentKind.DefaultValue) >= 2,
            IrStructCreation creation =>
                creation.Arguments.Count(static a => a.Kind != IrArgumentKind.DefaultValue) >= 2,
            _ => false
        });

    // Reads of deduced temporaries below two implicit conversions or more, which change the type the decisions
    // of the parent read
    private static int NestedConversions(IrMethod sequenced) =>
        IrNodeChildren.DescendantsAndSelf(sequenced.Body)
            .OfType<IrConversion>()
            .Count(static conversion => conversion is
            {
                IsImplicit: true, Conversion.Method: null,
                Operand: IrConversion
                {
                    IsImplicit: true, Conversion.Method: null,
                    Operand: IrVariableReference { Variable.Kind: IrVariableKind.Temporary }
                }
            });
}
