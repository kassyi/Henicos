#nullable enable
using System.IO;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Decisions;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Literals;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Corpus-wide agreement checks between IR numeric decisions and syntax-based <see cref="IOperation"/> helpers, and
/// between pre-split and post-split IR decisions (#1561).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrDecisionAgreementTests(ITestOutputHelper output)
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_NumericDecisionsAgreeWithOperations()
    {
        var mismatches = new ConcurrentBag<string>();
        int binaryCount = 0;
        int unaryCount = 0;
        int stepCount = 0;
        int compoundCount = 0;
        int conversionCount = 0;
        int conditionalCount = 0;
        int constantCount = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null)
                {
                    continue;
                }

                var tree = syntaxRef.SyntaxTree;
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                var where = $"{Path.GetFileName(tree.FilePath)} {symbol.ContainingType?.Name}.{symbol.Name}";

                var decided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));

                foreach (var violation in IrDecisionInvariants.Verify(decided.Body))
                {
                    mismatches.Add($"{where}: invariant: {violation}");
                }

                foreach (var node in IrNodeChildren.DescendantsAndSelf(decided.Body))
                {
                    if (node is not IrExpression expression || expression.Source.Synthesized ||
                        IrInvariants.ContainsOpaque(expression))
                    {
                        continue;
                    }

                    if (IrClassificationEquivalence.FindExpression(root, expression.Source.Span) is not { } syntax)
                    {
                        continue;
                    }

                    CompareNode(expression, syntax, model, where, mismatches,
                        ref binaryCount, ref unaryCount, ref stepCount, ref compoundCount,
                        ref conversionCount, ref conditionalCount, ref constantCount);
                }
            }
        });

        output.WriteLine(
            $"Compared decisions: binary {binaryCount}, unary {unaryCount}, step {stepCount}, " +
            $"compound {compoundCount}, conversion {conversionCount}, conditional {conditionalCount}, " +
            $"constant {constantCount}");

        Assert.True(binaryCount > 0 && unaryCount > 0 && stepCount > 0 && compoundCount > 0 &&
                    conversionCount > 0 && conditionalCount > 0 && constantCount > 0,
            "Expected non-zero comparison counts for every decision kind.");
        Assert.True(mismatches.IsEmpty,
            string.Join("\n", mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EverySplitMethod_PostSplitDecisionsAgreeWithPreSplit()
    {
        var mismatches = new ConcurrentBag<string>();
        int comparedMethods = 0;
        int comparedNodes = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var (_, symbol, built) in IrCoverageInputs.GpuMethods(compilation, trees))
            {
                var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null)
                {
                    continue;
                }

                var classified = IrClassifier.Classify(built, compilation);
                if (IrSplitLowering.Apply(classified, compilation, out _) is not { } splitLowered)
                {
                    continue;
                }

                var isDevice = !symbol.HasAttribute(KnownAttributes.GpuKernel);
                var linear = IrLinearizer.Linearize(splitLowered, compilation);
                var machine = IrStateMachineBuilder.Build(linear, isDevice, built.Body.Source, compilation);
                var where = IrCoverageInputs.Where(syntaxRef.SyntaxTree, symbol);

                var preDecided = IrDecisions.Decide(built, IrDecisionTestContext.Create(compilation, built));
                var postContext = IrDecisionTestContext.Create(compilation, machine, built);
                var postDecided = IrDecisions.Decide(machine, postContext);

                foreach (var violation in IrDecisionInvariants.Verify(postDecided))
                {
                    mismatches.Add($"{where}: post-split invariant: {violation}");
                }

                var preByKey = CollectUniqueNonSynthesized(preDecided.Body);
                Interlocked.Increment(ref comparedMethods);

                foreach (var node in IrNodeChildren.DescendantsAndSelf(postDecided))
                {
                    if (node is not IrExpression postExpr || postExpr.Source.Synthesized ||
                        postExpr.Decision is null)
                    {
                        continue;
                    }

                    var key = (postExpr.Source.TreeOrdinal, postExpr.Source.Start, postExpr.Source.Length,
                        postExpr.GetType());
                    if (!preByKey.TryGetValue(key, out var preExpr) || preExpr is null)
                    {
                        continue;
                    }

                    Interlocked.Increment(ref comparedNodes);
                    ComparePreAndPost(preExpr, postExpr, where, mismatches);
                }
            }
        });

        output.WriteLine($"Post-split compared: {comparedMethods} methods, {comparedNodes} nodes");
        Assert.True(comparedMethods > 0 && comparedNodes > 0, "Expected non-zero post-split comparisons.");
        Assert.True(mismatches.IsEmpty,
            string.Join("\n", mismatches.OrderBy(static m => m, StringComparer.Ordinal).Take(100)));
    }

    private static void CompareNode(IrExpression expression, ExpressionSyntax syntax, SemanticModel model,
        string where, ConcurrentBag<string> mismatches,
        ref int binaryCount, ref int unaryCount, ref int stepCount, ref int compoundCount,
        ref int conversionCount, ref int conditionalCount, ref int constantCount)
    {
        switch (expression)
        {
            case IrBinary binary when model.GetOperation(syntax) is IBinaryOperation op:
                Interlocked.Increment(ref binaryCount);
                CompareBinary(binary, op, syntax, model, where, mismatches);
                break;

            case IrUnary unary when model.GetOperation(syntax) is IUnaryOperation op:
                Interlocked.Increment(ref unaryCount);
                CompareUnary(unary, op, syntax, model, where, mismatches);
                break;

            case IrIncrement increment when model.GetOperation(syntax) is IIncrementOrDecrementOperation op:
                Interlocked.Increment(ref stepCount);
                CompareStep(increment, op, syntax, where, mismatches);
                break;

            case IrCompoundAssignment compound when model.GetOperation(syntax) is ICompoundAssignmentOperation op:
                Interlocked.Increment(ref compoundCount);
                CompareCompound(compound, op, syntax, where, mismatches);
                break;

            case IrConversion conversion when !conversion.IsImplicit && syntax is CastExpressionSyntax &&
                                              model.GetOperation(syntax) is IConversionOperation op:
                Interlocked.Increment(ref conversionCount);
                CompareConversion(conversion, op, syntax, model, where, mismatches);
                break;

            case IrConditional conditional when model.GetOperation(syntax) is IConditionalOperation op:
                Interlocked.Increment(ref conditionalCount);
                CompareConditional(conditional, op, syntax, where, mismatches);
                break;

            case IrConstant constant when constant.Origin is not null &&
                                          syntax is IdentifierNameSyntax or MemberAccessExpressionSyntax &&
                                          model.GetSymbol(syntax) is IFieldSymbol { IsConst: true } field:
                Interlocked.Increment(ref constantCount);
                CompareConstant(constant, field, syntax, model, where, mismatches);
                break;
        }
    }

    private static void CompareBinary(IrBinary binary, IBinaryOperation op, ExpressionSyntax syntax,
        SemanticModel model, string where, ConcurrentBag<string> mismatches)
    {
        var d = binary.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        var oldFolded = IntegerSemantics.GetConstant(op);
        if (oldFolded is var (oldConstType, oldConstVal))
        {
            var oldTypeAtUse = ConstantAtUse.GetTypeAtUse(syntax, oldConstType, model);
            if (d.Folded is not { } irFolded ||
                !SymbolEqualityComparer.Default.Equals(oldTypeAtUse, irFolded.Type) ||
                !Equals(oldConstVal, irFolded.Value))
            {
                mismatches.Add($"{label}: Folded syntax ({oldTypeAtUse.Name},{oldConstVal}) IR {d.Folded}");
            }
            return;
        }

        if (d.Folded is not null)
        {
            mismatches.Add($"{label}: Folded syntax null IR {d.Folded}");
            return;
        }

        var oldRemainder = FloatingPointRemainder.GetHelper(op);
        if (oldRemainder != d.FloatingRemainder)
        {
            mismatches.Add($"{label}: FloatingRemainder syntax {oldRemainder} IR {d.FloatingRemainder}");
        }

        if (oldRemainder is not null)
        {
            return;
        }

        var oldHelper = IntegerSemantics.GetBinaryHelper(op);
        if (oldHelper != d.Helper)
        {
            mismatches.Add($"{label}: Helper syntax {oldHelper} IR {d.Helper}");
        }

        var oldHasSite = oldHelper is not null && IntegerSemantics.TakesErrorSite(oldHelper);
        if (oldHasSite != (d.Site is not null))
        {
            mismatches.Add($"{label}: Site syntax {oldHasSite} IR {d.Site is not null}");
        }

        var oldManaged = ManagedDivision.Plan(syntax, model);
        if (!Equals(oldManaged, d.ManagedDivision))
        {
            mismatches.Add($"{label}: ManagedDivision syntax {oldManaged} IR {d.ManagedDivision}");
        }

        if (oldHelper is not null)
        {
            return;
        }

        var oldShift = IntegerSemantics.GetShiftMask(op);
        if (oldShift != d.ShiftMask)
        {
            mismatches.Add($"{label}: ShiftMask syntax {oldShift} IR {d.ShiftMask}");
        }

        if (oldShift != 0)
        {
            return;
        }

        var oldNarrow = IntegerSemantics.TryGetNarrowEnumResultType(op, out var narrowType) ? narrowType : null;
        if (!SymbolEqualityComparer.Default.Equals(oldNarrow, d.NarrowEnumType))
        {
            mismatches.Add($"{label}: NarrowEnumType syntax {oldNarrow?.Name} IR {d.NarrowEnumType?.Name}");
        }

        if (oldNarrow is not null)
        {
            return;
        }

        var oldPromoted = ArithmeticConversions.TryGetPromotedType(op, out var promotedType) ? promotedType : null;
        if (!SymbolEqualityComparer.Default.Equals(oldPromoted, d.PromotedType))
        {
            mismatches.Add($"{label}: PromotedType syntax {oldPromoted?.Name} IR {d.PromotedType?.Name}");
        }

        var oldConvertsLeft = oldPromoted is not null && ArithmeticConversions.IsConverted(op.LeftOperand, oldPromoted);
        var oldConvertsRight = oldPromoted is not null &&
                               ArithmeticConversions.IsConverted(op.RightOperand, oldPromoted);
        if (oldConvertsLeft != d.ConvertsLeft || oldConvertsRight != d.ConvertsRight)
        {
            mismatches.Add(
                $"{label}: Converts syntax ({oldConvertsLeft},{oldConvertsRight}) " +
                $"IR ({d.ConvertsLeft},{d.ConvertsRight})");
        }
    }

    private static void CompareUnary(IrUnary unary, IUnaryOperation op, ExpressionSyntax syntax,
        SemanticModel model, string where, ConcurrentBag<string> mismatches)
    {
        var d = unary.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        if (syntax is PrefixUnaryExpressionSyntax prefix &&
            OperatorSpacing.IsNegatedNumericLiteral(prefix) &&
            model.GetSafeConstantValue(prefix) is { HasValue: true, Value: { } litVal } &&
            model.GetTypeSymbol(prefix) is { } litType)
        {
            var oldTypeAtUse = ConstantAtUse.GetTypeAtUse(prefix, litType, model);
            if (d.Folded is not { } irFolded ||
                !SymbolEqualityComparer.Default.Equals(oldTypeAtUse, irFolded.Type) ||
                !Equals(litVal, irFolded.Value))
            {
                mismatches.Add($"{label}: NegatedLiteralFolded syntax ({oldTypeAtUse.Name},{litVal}) IR {d.Folded}");
            }
            return;
        }

        var oldFolded = IntegerSemantics.GetConstant(op);
        if (oldFolded is var (oldConstType, oldConstVal))
        {
            var oldTypeAtUse = ConstantAtUse.GetTypeAtUse(syntax, oldConstType, model);
            if (d.Folded is not { } irFolded ||
                !SymbolEqualityComparer.Default.Equals(oldTypeAtUse, irFolded.Type) ||
                !Equals(oldConstVal, irFolded.Value))
            {
                mismatches.Add($"{label}: Folded syntax ({oldTypeAtUse.Name},{oldConstVal}) IR {d.Folded}");
            }
            return;
        }

        if (d.Folded is not null)
        {
            mismatches.Add($"{label}: Folded syntax null IR {d.Folded}");
            return;
        }

        var oldHelper = IntegerSemantics.GetNegationHelper(op);
        if (oldHelper != d.Helper)
        {
            mismatches.Add($"{label}: Helper syntax {oldHelper} IR {d.Helper}");
        }

        if (oldHelper is not null)
        {
            return;
        }

        var oldNarrow = IntegerSemantics.TryGetNarrowEnumResultType(op, out var narrowType) ? narrowType : null;
        if (!SymbolEqualityComparer.Default.Equals(oldNarrow, d.NarrowEnumType))
        {
            mismatches.Add($"{label}: NarrowEnumType syntax {oldNarrow?.Name} IR {d.NarrowEnumType?.Name}");
        }

        if (oldNarrow is not null)
        {
            return;
        }

        var oldPromoted = ArithmeticConversions.TryGetPromotedType(op, out var promotedType) ? promotedType : null;
        if (!SymbolEqualityComparer.Default.Equals(oldPromoted, d.PromotedType))
        {
            mismatches.Add($"{label}: PromotedType syntax {oldPromoted?.Name} IR {d.PromotedType?.Name}");
        }

        var oldConverts = oldPromoted is not null && ArithmeticConversions.IsConverted(op.Operand, oldPromoted);
        if (oldConverts != d.ConvertsOperand)
        {
            mismatches.Add($"{label}: ConvertsOperand syntax {oldConverts} IR {d.ConvertsOperand}");
        }
    }

    private static void CompareStep(IrIncrement increment, IIncrementOrDecrementOperation op, ExpressionSyntax syntax,
        string where, ConcurrentBag<string> mismatches)
    {
        var d = increment.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";
        var facts = IntegerStepFacts.Of(op);

        var oldHelper = IntegerSemantics.GetStepHelper(in facts, undo: false);
        var oldUndo = IntegerSemantics.GetStepHelper(in facts, undo: true);
        var oldNarrow = IntegerSemantics.GetStepNarrowing(op);

        if (oldHelper != d.Helper || oldUndo != d.UndoHelper ||
            !SymbolEqualityComparer.Default.Equals(oldNarrow, d.Narrowing))
        {
            mismatches.Add(
                $"{label}: Step syntax ({oldHelper},{oldUndo},{oldNarrow?.Name}) " +
                $"IR ({d.Helper},{d.UndoHelper},{d.Narrowing?.Name})");
        }
    }

    private static void CompareCompound(IrCompoundAssignment compound, ICompoundAssignmentOperation op,
        ExpressionSyntax syntax, string where, ConcurrentBag<string> mismatches)
    {
        var d = compound.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        var oldHelper = IntegerSemantics.GetCompoundHelper(op);
        var oldShift = oldHelper is null ? IntegerSemantics.GetCompoundShiftMask(op) : 0;
        var oldNarrow = IntegerSemantics.GetCompoundNarrowing(op);
        var oldOpType = IntegerSemantics.GetCompoundOperatorType(op);
        var oldRemainder = FloatingPointRemainder.GetCompoundHelper(op);
        var oldHasSite = oldHelper is not null && IntegerSemantics.TakesErrorSite(oldHelper);

        if (oldHelper != d.Helper || oldShift != d.ShiftMask ||
            !SymbolEqualityComparer.Default.Equals(oldNarrow, d.Narrowing) ||
            oldOpType != d.OperatorType || oldRemainder != d.FloatingRemainder ||
            oldHasSite != (d.Site is not null))
        {
            mismatches.Add(
                $"{label}: Compound syntax ({oldHelper},{oldShift},{oldNarrow?.Name},{oldOpType},{oldRemainder}," +
                $"{oldHasSite}) IR ({d.Helper},{d.ShiftMask},{d.Narrowing?.Name},{d.OperatorType}," +
                $"{d.FloatingRemainder},{d.Site is not null})");
        }
    }

    private static void CompareConversion(IrConversion conversion, IConversionOperation op, ExpressionSyntax syntax,
        SemanticModel model, string where, ConcurrentBag<string> mismatches)
    {
        var d = conversion.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        var oldConst = IntegerSemantics.GetConstant(op);
        if ((d.Form == IrConversionForm.Folded) != (oldConst is not null))
        {
            mismatches.Add($"{label}: ConversionFolded syntax {oldConst is not null} IR {d.Form}");
        }

        if (oldConst is var (oldConstType, oldConstVal))
        {
            var oldTypeAtUse = ConstantAtUse.GetTypeAtUse(syntax, oldConstType, model);
            if (d.Folded is not { } irFolded ||
                !SymbolEqualityComparer.Default.Equals(oldTypeAtUse, irFolded.Type) ||
                !Equals(oldConstVal, irFolded.Value))
            {
                mismatches.Add(
                    $"{label}: ConversionFoldedValue syntax ({oldTypeAtUse.Name},{oldConstVal}) IR {d.Folded}");
            }
            return;
        }

        var oldHelper = IntegerSemantics.GetConversionHelper(op, out var oldNeedsCast);
        if ((d.Form == IrConversionForm.FloatToInteger) != (oldHelper is not null) ||
            oldHelper != d.Helper || oldNeedsCast != d.NeedsCast)
        {
            mismatches.Add(
                $"{label}: FloatToInteger syntax ({oldHelper},{oldNeedsCast}) " +
                $"IR ({d.Form},{d.Helper},{d.NeedsCast})");
        }
    }

    private static void CompareConditional(IrConditional conditional, IConditionalOperation op,
        ExpressionSyntax syntax, string where, ConcurrentBag<string> mismatches)
    {
        var d = conditional.Decided;
        var label = $"{where}: {syntax.Kind()} {syntax.Span}";

        var oldPromoted = ArithmeticConversions.TryGetPromotedType(op, out var promotedType) ? promotedType : null;
        var oldTrue = oldPromoted is not null && ArithmeticConversions.IsConverted(op.WhenTrue, oldPromoted);
        var oldFalse = oldPromoted is not null && op.WhenFalse is not null &&
                       ArithmeticConversions.IsConverted(op.WhenFalse, oldPromoted);

        if (!SymbolEqualityComparer.Default.Equals(oldPromoted, d.PromotedType) ||
            oldTrue != d.ConvertsWhenTrue || oldFalse != d.ConvertsWhenFalse)
        {
            mismatches.Add(
                $"{label}: Conditional syntax ({oldPromoted?.Name},{oldTrue},{oldFalse}) " +
                $"IR ({d.PromotedType?.Name},{d.ConvertsWhenTrue},{d.ConvertsWhenFalse})");
        }
    }

    private static void CompareConstant(IrConstant constant, IFieldSymbol field, ExpressionSyntax syntax,
        SemanticModel model, string where, ConcurrentBag<string> mismatches)
    {
        var d = constant.Decided;
        var oldTypeAtUse = ConstantAtUse.GetTypeAtUse(syntax, field.Type, model);
        if (!SymbolEqualityComparer.Default.Equals(oldTypeAtUse, d.TypeAtUse))
        {
            mismatches.Add(
                $"{where}: {syntax.Kind()} {syntax.Span}: TypeAtUse syntax {oldTypeAtUse.Name} IR {d.TypeAtUse.Name}");
        }
    }

    private static Dictionary<(int TreeOrdinal, int Start, int Length, Type Kind), IrExpression?>
        CollectUniqueNonSynthesized(IrNode root)
    {
        var map = new Dictionary<(int TreeOrdinal, int Start, int Length, Type Kind), IrExpression?>();
        foreach (var node in IrNodeChildren.DescendantsAndSelf(root))
        {
            if (node is not IrExpression expr || expr.Source.Synthesized || expr.Decision is null)
            {
                continue;
            }

            var key = (expr.Source.TreeOrdinal, expr.Source.Start, expr.Source.Length, expr.GetType());
            if (!map.TryAdd(key, expr))
            {
                map[key] = null;
            }
        }

        return map;
    }

    private static void ComparePreAndPost(IrExpression pre, IrExpression post, string where,
        ConcurrentBag<string> mismatches)
    {
        var label = $"{where}: {pre.GetType().Name} {pre.Source.Span}";
        switch (pre, post)
        {
            case (IrBinary pb, IrBinary qb):
            {
                var pd = pb.Decided;
                var qd = qb.Decided;
                if (pd.Helper != qd.Helper ||
                    pd.ShiftMask != qd.ShiftMask ||
                    !SymbolEqualityComparer.Default.Equals(pd.NarrowEnumType, qd.NarrowEnumType) ||
                    !SymbolEqualityComparer.Default.Equals(pd.PromotedType, qd.PromotedType) ||
                    pd.ConvertsLeft != qd.ConvertsLeft ||
                    pd.ConvertsRight != qd.ConvertsRight ||
                    pd.FloatingRemainder != qd.FloatingRemainder ||
                    !Equals(pd.Folded, qd.Folded) ||
                    (pd.Site is not null) != (qd.Site is not null) ||
                    !Equals(pd.ManagedDivision, qd.ManagedDivision))
                {
                    mismatches.Add($"{label}: post-split IrBinary decision mismatch");
                }
                break;
            }
            case (IrUnary pu, IrUnary qu):
            {
                var pd = pu.Decided;
                var qd = qu.Decided;
                if (pd.Helper != qd.Helper ||
                    !SymbolEqualityComparer.Default.Equals(pd.NarrowEnumType, qd.NarrowEnumType) ||
                    !SymbolEqualityComparer.Default.Equals(pd.PromotedType, qd.PromotedType) ||
                    pd.ConvertsOperand != qd.ConvertsOperand ||
                    !Equals(pd.Folded, qd.Folded))
                {
                    mismatches.Add($"{label}: post-split IrUnary decision mismatch");
                }
                break;
            }
            case (IrIncrement pi, IrIncrement qi):
            {
                var pd = pi.Decided;
                var qd = qi.Decided;
                if (pd.Helper != qd.Helper ||
                    pd.UndoHelper != qd.UndoHelper ||
                    !SymbolEqualityComparer.Default.Equals(pd.Narrowing, qd.Narrowing))
                {
                    mismatches.Add($"{label}: post-split IrIncrement decision mismatch");
                }
                break;
            }
            case (IrCompoundAssignment pc, IrCompoundAssignment qc):
            {
                var pd = pc.Decided;
                var qd = qc.Decided;
                if (pd.Helper != qd.Helper ||
                    pd.ShiftMask != qd.ShiftMask ||
                    !SymbolEqualityComparer.Default.Equals(pd.Narrowing, qd.Narrowing) ||
                    pd.OperatorType != qd.OperatorType ||
                    pd.FloatingRemainder != qd.FloatingRemainder ||
                    pd.ConvertsValue != qd.ConvertsValue ||
                    (pd.Site is not null) != (qd.Site is not null))
                {
                    mismatches.Add($"{label}: post-split IrCompoundAssignment decision mismatch");
                }
                break;
            }
            case (IrConversion pv, IrConversion qv):
            {
                var pd = pv.Decided;
                var qd = qv.Decided;
                if (pd.Form != qd.Form ||
                    pd.Helper != qd.Helper ||
                    pd.NeedsCast != qd.NeedsCast ||
                    !Equals(pd.Folded, qd.Folded))
                {
                    mismatches.Add($"{label}: post-split IrConversion decision mismatch");
                }
                break;
            }
            case (IrConditional pcond, IrConditional qcond):
            {
                var pd = pcond.Decided;
                var qd = qcond.Decided;
                if (!SymbolEqualityComparer.Default.Equals(pd.PromotedType, qd.PromotedType) ||
                    pd.ConvertsWhenTrue != qd.ConvertsWhenTrue ||
                    pd.ConvertsWhenFalse != qd.ConvertsWhenFalse)
                {
                    mismatches.Add($"{label}: post-split IrConditional decision mismatch");
                }
                break;
            }
            case (IrConstant pconst, IrConstant qconst):
            {
                if (!SymbolEqualityComparer.Default.Equals(pconst.Decided.TypeAtUse, qconst.Decided.TypeAtUse))
                {
                    mismatches.Add($"{label}: post-split IrConstant TypeAtUse mismatch");
                }
                break;
            }
        }
    }
}
