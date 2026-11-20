#nullable enable
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;
using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Corpus-wide agreement of the IR getter, receiver copy and setter decisions with the syntax rules, and between the
/// decisions before and after the split (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrDecisionAccessorAgreementTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_AccessorDecisionsAgreeWithSyntax()
    {
        var counts = new Counts();
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                ComparePreSplit(compilation, symbol, built, counts);
            }
        });

        output.WriteLine(
            $"Compared accessor decisions: member {counts.Members}, Getter {counts.Getters}, " +
            $"CopyType {counts.Copies}, assignment {counts.Assignments}, CallsSetter {counts.Setters}, " +
            $"WritesBackingField {counts.BackingFields}");
        Assert.True(counts.Members > 0 && counts.Assignments > 0, "Expected property references and assignments.");
        Assert.True(counts.Mismatches.IsEmpty,
            string.Join("\n", counts.Mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryStructMember_AccessorDecisionsAgreeWithSyntax()
    {
        var counts = new Counts();
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.StructMembers(compilation, trees))
            {
                ComparePreSplit(compilation, symbol, built, counts);
            }
        });

        output.WriteLine(
            $"Compared struct member accessor decisions: member {counts.Members}, Getter {counts.Getters}, " +
            $"CopyType {counts.Copies}, assignment {counts.Assignments}, CallsSetter {counts.Setters}, " +
            $"WritesBackingField {counts.BackingFields}");
        Assert.True(counts.Members > 0 && counts.BackingFields > 0,
            "Expected property references and backing field stores in struct members.");
        Assert.True(counts.Mismatches.IsEmpty,
            string.Join("\n", counts.Mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EverySplitMethod_PostSplitAccessorDecisionsAgreeWithPreSplit()
    {
        var counts = new Counts();
        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                ComparePostSplit(compilation, symbol, built, counts);
            }
        });

        output.WriteLine(
            $"Post-split accessor decisions compared: member {counts.Members}, Getter {counts.Getters}, " +
            $"CopyType {counts.Copies}, assignment {counts.Assignments}, CallsSetter {counts.Setters}, " +
            $"receiver read through a temporary {counts.SpilledReceivers}");
        Assert.True(counts.Members > 0, "Expected post-split property references.");
        Assert.True(counts.Mismatches.IsEmpty,
            string.Join("\n", counts.Mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    private static void ComparePreSplit(Compilation compilation, IMethodSymbol symbol, IrMethod built, Counts counts)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            return;
        }

        var tree = syntaxRef.SyntaxTree;
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var where = IrCoverageInputs.Where(tree, symbol);
        var context = IrDecisionTestContext.Create(compilation, built);
        var decided = IrDecisions.Decide(built, context);
        foreach (var violation in IrDecisionInvariants.Verify(decided.Body, context.TemporaryInitializers))
        {
            counts.Mismatches.Add($"{where}: invariant: {violation}");
        }

        foreach (var node in IrNodeChildren.DescendantsAndSelf(decided.Body))
        {
            if (node is not IrExpression expression || expression.Source.Synthesized ||
                IrInvariants.ContainsOpaque(expression) ||
                IrClassificationEquivalence.FindExpression(root, expression.Source.Span) is not { } syntax)
            {
                continue;
            }

            var label = $"{where}: {syntax.Kind()} {syntax.Span}";
            switch (expression)
            {
                case IrPropertyReference member when NamedProperty(syntax, model) is { } property:
                    CompareMember(member, syntax, property, model, label, counts);
                    break;
                case IrAssignment assignment when syntax is AssignmentExpressionSyntax assignmentSyntax:
                    CompareAssignment(assignment, assignmentSyntax, model, label, counts);
                    break;
                case IrPropertyReference { Decided: var d }
                    when d.Form == IrMemberForm.Getter || d.CopyType is not null:
                    counts.Mismatches.Add($"{label}: IR decides {d.Form}/{d.CopyType}, the syntax names no property");
                    break;
                case IrAssignment { Decided.Accessor: not IrAccessorStore.None and var accessor }:
                    counts.Mismatches.Add($"{label}: IR decides {accessor}, the syntax is no assignment");
                    break;
            }
        }
    }

    // The symbol the syntax rules read for a name or a member access (MemberAccessExpressionTranspilerRule)
    private static IPropertySymbol? NamedProperty(ExpressionSyntax syntax, SemanticModel model) =>
        syntax switch
        {
            MemberAccessExpressionSyntax access =>
                (model.GetSymbol(access.Name) ?? model.GetSymbol(access)) as IPropertySymbol,
            IdentifierNameSyntax name => model.GetSymbol(name) as IPropertySymbol,
            _ => null
        };

    private static void CompareMember(IrPropertyReference member, ExpressionSyntax syntax, IPropertySymbol property,
        SemanticModel model, string label, Counts counts)
    {
        Interlocked.Increment(ref counts.Members);
        var dialect = HostDialects.Cpu;
        var d = member.Decided;
        var legacyGetter = member.Receiver is not null && PropertyAccessorCall.IsGetter(property, dialect) &&
                           !dialect.TryMapIntrinsic(property, out _) && !ArrayNullCheck.IsArrayMember(property);
        if (legacyGetter != (d.Form == IrMemberForm.Getter))
        {
            counts.Mismatches.Add($"{label}: Getter syntax {legacyGetter} IR {d.Form}");
        }

        if (legacyGetter)
        {
            Interlocked.Increment(ref counts.Getters);
        }

        var legacyCopy = PropertyAccessorCall.PlanCopyType(syntax, property, model, dialect, null);
        if (!SymbolEqualityComparer.Default.Equals(legacyCopy, d.CopyType))
        {
            counts.Mismatches.Add($"{label}: CopyType syntax {legacyCopy} IR {d.CopyType}");
        }

        if (legacyCopy is not null)
        {
            Interlocked.Increment(ref counts.Copies);
        }
    }

    private static void CompareAssignment(IrAssignment assignment, AssignmentExpressionSyntax syntax,
        SemanticModel model, string label, Counts counts)
    {
        Interlocked.Increment(ref counts.Assignments);
        var dialect = HostDialects.Cpu;
        var legacy = IrAccessorStore.None;
        if (!assignment.IsRef && PropertyAccessorCall.GetTargetProperty(syntax.Left, model) is { } property)
        {
            if (PropertyAccessorCall.IsSetter(property, dialect))
            {
                legacy = IrAccessorStore.CallsSetter;
            }
            else if (PropertyAccessorCall.WritesBackingField(syntax.Left.UnwrapParentheses(), property, dialect))
            {
                legacy = IrAccessorStore.WritesBackingField;
            }
        }

        var accessor = assignment.Decided.Accessor;
        if (legacy != accessor)
        {
            counts.Mismatches.Add($"{label}: Accessor syntax {legacy} IR {accessor}");
        }

        if (accessor == IrAccessorStore.CallsSetter)
        {
            Interlocked.Increment(ref counts.Setters);
        }
        else if (accessor == IrAccessorStore.WritesBackingField)
        {
            Interlocked.Increment(ref counts.BackingFields);
        }
    }

    private static void ComparePostSplit(Compilation compilation, IMethodSymbol symbol, IrMethod built, Counts counts)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef ||
            IrSplitLowering.Apply(IrClassifier.Classify(built, compilation), compilation, out _) is not { } lowered)
        {
            return;
        }

        var isDevice = !symbol.HasAttribute(KnownAttributes.GpuKernel);
        var linear = IrLinearizer.Linearize(lowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDevice, built.Body.Source, compilation);
        var where = IrCoverageInputs.Where(syntaxRef.SyntaxTree, symbol);
        var preDecided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
        var postContext = IrDecisionTestContext.Create(compilation, machine, built);
        var postDecided = IrDecisions.Decide(machine, postContext);
        foreach (var violation in IrDecisionInvariants.Verify(postDecided, postContext.TemporaryInitializers))
        {
            counts.Mismatches.Add($"{where}: post-split invariant: {violation}");
        }

        var preByKey = UniqueNodes(preDecided.Body);
        foreach (var node in IrNodeChildren.DescendantsAndSelf(postDecided))
        {
            if (node is not IrExpression post || post.Source.Synthesized ||
                !preByKey.TryGetValue(Key(post), out var pre) || pre is null)
            {
                continue;
            }

            var label = $"{where}: {post.GetType().Name} {post.Source.Span}";
            switch (pre, post)
            {
                case (IrPropertyReference pm, IrPropertyReference qm):
                    ComparePostMember(pm, qm, label, counts);
                    break;
                case (IrAssignment pa, IrAssignment qa) when pa.Decided.Accessor != qa.Decided.Accessor:
                    counts.Mismatches.Add($"{label}: post-split Accessor {pa.Decided.Accessor} -> " +
                                          qa.Decided.Accessor);
                    break;
                case (IrAssignment, IrAssignment):
                    Interlocked.Increment(ref counts.Assignments);
                    break;
            }
        }
    }

    private static void ComparePostMember(IrPropertyReference pre, IrPropertyReference post, string label,
        Counts counts)
    {
        Interlocked.Increment(ref counts.Members);
        if (post.Decided.Form == IrMemberForm.Getter)
        {
            Interlocked.Increment(ref counts.Getters);
        }

        if (post.Decided.CopyType is not null)
        {
            Interlocked.Increment(ref counts.Copies);
        }

        if (post.Receiver is IrVariableReference { Variable.Kind: IrVariableKind.Temporary })
        {
            Interlocked.Increment(ref counts.SpilledReceivers);
        }

        if (pre.Decided.Form != post.Decided.Form ||
            !SymbolEqualityComparer.Default.Equals(pre.Decided.CopyType, post.Decided.CopyType))
        {
            counts.Mismatches.Add($"{label}: post-split Form/CopyType {pre.Decided.Form}/{pre.Decided.CopyType} -> " +
                                  $"{post.Decided.Form}/{post.Decided.CopyType}");
        }
    }

    private static (int TreeOrdinal, int Start, int Length, Type Kind) Key(IrExpression expression) =>
        (expression.Source.TreeOrdinal, expression.Source.Start, expression.Source.Length, expression.GetType());

    private static Dictionary<(int TreeOrdinal, int Start, int Length, Type Kind), IrExpression?> UniqueNodes(
        IrNode root)
    {
        var map = new Dictionary<(int TreeOrdinal, int Start, int Length, Type Kind), IrExpression?>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            if (node is IrExpression { Source.Synthesized: false, Decision: not null } expression &&
                !map.TryAdd(Key(expression), expression))
            {
                map[Key(expression)] = null;
            }
        }

        return map;
    }

    private sealed class Counts
    {
        public ConcurrentBag<string> Mismatches { get; } = [];
        public int Members;
        public int Getters;
        public int Copies;
        public int Assignments;
        public int Setters;
        public int BackingFields;
        public int SpilledReceivers;
    }
}
