#nullable enable
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Linear;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Builds the IR state machine (<see cref="IrStateMachineBuilder"/>) of every GPU method of the corpus and compares
/// its states, events, warp site count, and call frames against the syntax-based lowering
/// (<see cref="MethodEmissionPlan"/> / <see cref="StateMachineLowerer"/>).
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.CorpusScan)]
public class IrStateMachineEquivalenceTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrStateMachineDifferences.expected.yml";

    private static readonly string[] s_header =
    [
        "The status of every GPU method of the IR corpus (TestCases/ and every *.cu.cs kernel file) when comparing",
        "IrStateMachineBuilder with the syntax state machine lowering (MethodEmissionPlan.Lowered).",
        "IrStateMachineEquivalenceTests.EveryGpuMethod_MatchesTheSyntaxStateMachine builds this list on every run and",
        "fails on any difference; a received file goes to tmp/.",
        "",
        "  file:   the source file, relative to tests/Compiler/Kassyi.GSharp.Tests.Shared",
        "  method: the containing type and the method",
        "  status: Compared, NoStateMachine, Reconvergence, IrNotBuilt, Fallback:<kind>, " +
        "OpaqueStatement, or Skip:<kind>",
        "  count:  1 per method"
    ];

    private static readonly HashSet<string> s_readFunctions = BuildReadFunctions();

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void EveryGpuMethod_MatchesTheSyntaxStateMachine()
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new IrCorpusCounts();
        var unexplained = new ConcurrentBag<string>();
        var violations = new ConcurrentBag<string>();
        var compared = 0;

        IrCoverageInputs.ForEach((compilation, trees) =>
        {
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is not { } symbol ||
                        !GpuExecutionSpace.IsGpuFunction(symbol) ||
                        IrCoverageInputs.IsUnlowerableGenericDeclaration(symbol, compilation))
                    {
                        continue;
                    }

                    var where = IrCoverageInputs.Locate(tree, symbol);
                    var status = CompareMethod(declaration, symbol, model, compilation, where, violations,
                        unexplained);
                    if (status == "Compared")
                    {
                        Interlocked.Increment(ref compared);
                    }

                    counts.Add(where, ("status", status));
                }
            }
        }, everyKernelFile: true);

        output.WriteLine($"{compared} methods compared, {unexplained.Count} unexplained, " +
                         $"{violations.Count} invariant violations, {stopwatch.ElapsedMilliseconds} ms");
        foreach (var total in counts.Totals())
        {
            output.WriteLine(total);
        }

        Assert.True(compared > 0, "Expected at least one compared GPU method.");
        Assert.True(violations.IsEmpty,
            string.Join("\n", violations.OrderBy(static v => v, StringComparer.Ordinal).Take(50)));
        Assert.True(unexplained.IsEmpty,
            string.Join("\n", unexplained.OrderBy(static u => u, StringComparer.Ordinal).Take(50)));
        IrExpectedFiles.AssertMatches(ExpectedFile, counts.Render(s_header), output);
    }

    // The status of one GPU method; when both sides build a state machine, compares them and checks the invariants
    private static string CompareMethod(MethodDeclarationSyntax declaration, IMethodSymbol symbol, SemanticModel model,
        Compilation compilation, IrCorpusMethod where, ConcurrentBag<string> violations,
        ConcurrentBag<string> unexplained)
    {
        if (!declaration.HasBody())
        {
            return "NoStateMachine";
        }

        var isKernel = symbol.HasAttribute(KnownAttributes.GpuKernel);
        if (!isKernel &&
            (IteratorTypeResolver.IsDeviceIteratorMethod(symbol) &&
             !IteratorTypeResolver.IsRecursiveIterator(symbol, compilation) ||
             !SuspensionAnalysis.RequiresStateMachine(symbol, compilation)))
        {
            return "NoStateMachine";
        }

        var legacy = AstLoweringPipeline.Lower(declaration, model, symbol, isDeviceFunction: !isKernel);
        if (HasReconvergence(legacy.LoweredBody))
        {
            return "Reconvergence";
        }

        if (IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is not { } built)
        {
            return "IrNotBuilt";
        }

        var classified = IrClassifier.Classify(built, compilation);
        if (IrSplitLowering.Apply(classified, compilation, out var failure) is not { } splitLowered)
        {
            return failure.Kind switch
            {
                IrSplitLoweringFailureKind.Fallback => "Fallback:" + failure.Fallback,
                IrSplitLoweringFailureKind.OpaqueStatement => "OpaqueStatement",
                IrSplitLoweringFailureKind.Skip => "Skip:" + failure.Skip,
                _ => throw new InvalidOperationException($"Unexpected failure kind: {failure.Kind}")
            };
        }

        var linear = IrLinearizer.Linearize(splitLowered, compilation);
        var machine = IrStateMachineBuilder.Build(linear, isDeviceFunction: !isKernel, built.Body.Source, compilation);
        foreach (var violation in IrStateMachineInvariants.Verify(machine))
        {
            violations.Add($"{where.File} {where.Method}: {violation}");
        }

        Compare(where, legacy, machine, model, unexplained);
        return "Compared";
    }

    private static void Compare(IrCorpusMethod where, LoweredStateMachine legacy, IrStateMachine ir,
        SemanticModel model, ConcurrentBag<string> unexplained)
    {
        var label = $"{where.File} {where.Method}";
        if (legacy.WarpSiteCount != ir.WarpSiteCount)
        {
            unexplained.Add($"{label}: WarpSiteCount legacy={legacy.WarpSiteCount} ir={ir.WarpSiteCount}");
            return;
        }

        if (legacy.CallSites.Count != ir.CallFrames.Length)
        {
            unexplained.Add($"{label}: CallFrames count legacy={legacy.CallSites.Count} ir={ir.CallFrames.Length}");
            return;
        }

        for (var i = 0; i < ir.CallFrames.Length; i++)
        {
            var legacyCallee = model.GetSymbol(legacy.CallSites[i]) is IMethodSymbol callee
                ? callee.GetConstructedReducedFrom() ?? callee
                : null;
            if (!SymbolEqualityComparer.Default.Equals(legacyCallee, ir.CallFrames[i]))
            {
                unexplained.Add($"{label}: CallFrame[{i}] legacy={legacyCallee?.Name ?? "<null>"} " +
                                $"ir={ir.CallFrames[i].Name}");
                return;
            }
        }

        var legacyStates = ExtractLegacyStates(legacy.LoweredBody);
        if (legacyStates.Count != ir.States.Length)
        {
            unexplained.Add($"{label}: state count legacy={legacyStates.Count} ir={ir.States.Length}");
            return;
        }

        for (var i = 0; i < ir.States.Length; i++)
        {
            var (legacyNumber, legacyEvents) = legacyStates[i];
            var irState = ir.States[i];
            var irEvents = ExtractIrEvents(irState, ir.IsDeviceFunction);
            if (legacyNumber != irState.Number || !legacyEvents.SequenceEqual(irEvents, StringComparer.Ordinal))
            {
                unexplained.Add($"{label}: first differing state legacy={legacyNumber} ir={irState.Number}\n" +
                                $"    legacy: [{string.Join(", ", legacyEvents)}]\n" +
                                $"    ir:     [{string.Join(", ", irEvents)}]");
                return;
            }
        }
    }

    private static List<(int Number, List<string> Events)> ExtractLegacyStates(BlockSyntax loweredBody)
    {
        var @switch = loweredBody.Statements.OfType<SwitchStatementSyntax>().Single();
        var states = new List<(int Number, List<string> Events)>(@switch.Sections.Count);
        foreach (var section in @switch.Sections)
        {
            var caseLabel = section.Labels.OfType<CaseSwitchLabelSyntax>().Single();
            if (!TryReadIntLiteral(caseLabel.Value, out var stateNumber))
            {
                throw new InvalidOperationException("Legacy state switch label has non-integer value.");
            }

            var statements = section.Statements
                .OfType<BlockSyntax>()
                .SelectMany(static block => block.Statements)
                .ToList();
            states.Add((stateNumber, LegacyEvents(statements)));
        }

        return states;
    }

    // The events of one legacy state section, in order, in the words of ExtractIrEvents
    private static List<string> LegacyEvents(List<StatementSyntax> statements)
    {
        var events = new List<string>();
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (statement is GotoStatementSyntax gotoStmt &&
                gotoStmt.IsKind(SyntaxKind.GotoCaseStatement) &&
                TryReadIntLiteral(gotoStmt.Expression, out var jumpTarget))
            {
                events.Add($"jump {jumpTarget}");
            }
            else if (statement is IfStatementSyntax ifStmt &&
                     ifStmt.Else is null &&
                     TryGetGotoCaseTarget(ifStmt.Statement, out var condTarget))
            {
                var jumpIfTrue = !ifStmt.Condition.IsKind(SyntaxKind.LogicalNotExpression);
                events.Add(jumpIfTrue ? $"jump-if-true {condTarget}" : $"jump-if-false {condTarget}");
            }
            else if (statement is IfStatementSyntax resumeIf &&
                     TryGetResumeFrame(resumeIf.Condition, out var frameIndex))
            {
                events.Add($"resume {frameIndex}");
            }
            else if (TryGetFlagEvent(statement, out var flagEvent))
            {
                events.Add(flagEvent);
            }
            else if (i + 1 < statements.Count &&
                     TryGetStateAssignment(statement, out var nextState) &&
                     statements[i + 1] is ReturnStatementSyntax nextRet &&
                     nextRet.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
            {
                events.Add(nextState == -1 ? "exit" : $"suspend {nextState}");
                i++;
            }
            else if (statement is ReturnStatementSyntax ret &&
                     !ret.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
            {
                events.Add("return");
            }
            else if (TryGetWarpPublishSite(statement, out var publishSite))
            {
                events.Add($"publish {publishSite}");
            }
            else if (TryGetWarpReadSite(statement, out var readSite))
            {
                events.Add($"read {readSite}");
            }
        }

        return events;
    }

    private static List<string> ExtractIrEvents(IrState state, bool isDeviceFunction)
    {
        var events = new List<string>();
        foreach (var statement in state.Statements)
        {
            switch (statement)
            {
                case IrStateJump { Condition: null } jump:
                    events.Add($"jump {jump.State}");
                    break;
                case IrStateJump { Condition: not null } jump:
                    events.Add(jump.JumpIfTrue ? $"jump-if-true {jump.State}" : $"jump-if-false {jump.State}");
                    break;
                case IrSuspend { Kind: IrSuspendKind.BlockBarrier } suspend:
                    events.Add("flag block");
                    events.Add($"suspend {suspend.ResumeState}");
                    break;
                case IrSuspend { Kind: IrSuspendKind.GridBarrier } suspend:
                    events.Add("flag grid");
                    events.Add($"suspend {suspend.ResumeState}");
                    break;
                case IrSuspend { Kind: IrSuspendKind.WarpPublish } suspend:
                    events.Add($"suspend {suspend.ResumeState}");
                    break;
                case IrResumeCall resume:
                    events.Add($"resume {resume.CallFrame}");
                    break;
                case IrExpressionStatement
                {
                    Expression: IrRuntimeCall { Function: IrRuntimeFunction.WarpPublish } pub
                }:
                    events.Add($"publish {pub.Site}");
                    break;
                case var s when IrNodeChildren.DescendantsAndSelf(s)
                    .OfType<IrRuntimeCall>()
                    .FirstOrDefault(static c => c.Function == IrRuntimeFunction.WarpRead) is { } read:
                    events.Add($"read {read.Site}");
                    break;
                case IrReturn:
                    events.Add("return");
                    break;
                case IrStateMachineExit:
                    events.Add(isDeviceFunction ? "return" : "exit");
                    break;
            }
        }

        return events;
    }

    private static bool HasReconvergence(BlockSyntax loweredBody) =>
        loweredBody.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(static invocation =>
                invocation.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == GSharpInternalNames.Prelude.WarpReconvergeArrive);

    private static bool TryGetGotoCaseTarget(StatementSyntax statement, out int target)
    {
        var unwrapped = statement is BlockSyntax { Statements.Count: 1 } block ? block.Statements[0] : statement;
        if (unwrapped is GotoStatementSyntax gotoStmt &&
            gotoStmt.IsKind(SyntaxKind.GotoCaseStatement) &&
            TryReadIntLiteral(gotoStmt.Expression, out target))
        {
            return true;
        }

        target = 0;
        return false;
    }

    private static bool TryGetResumeFrame(ExpressionSyntax condition, out int frame)
    {
        if (condition is PrefixUnaryExpressionSyntax
            {
                Operand: InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax id,
                    ArgumentList.Arguments: var arguments
                }
            } &&
            condition.IsKind(SyntaxKind.LogicalNotExpression) &&
            id.Identifier.ValueText == GSharpInternalNames.SuspendingCall.ResumeCall &&
            arguments.Count >= 2 &&
            TryReadIntLiteral(arguments[1].Expression, out frame))
        {
            return true;
        }

        frame = 0;
        return false;
    }

    private static bool TryGetFlagEvent(StatementSyntax statement, out string flagEvent)
    {
        if (statement is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax id,
                    Right: LiteralExpressionSyntax right
                } assignment
            } &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            right.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            if (id.Identifier.ValueText == GSharpInternalNames.StateMachine.YieldedForBlockBarrier)
            {
                flagEvent = "flag block";
                return true;
            }

            if (id.Identifier.ValueText == GSharpInternalNames.StateMachine.YieldedForGridSync)
            {
                flagEvent = "flag grid";
                return true;
            }
        }

        flagEvent = "";
        return false;
    }

    private static bool TryGetStateAssignment(StatementSyntax statement, out int nextState)
    {
        if (statement is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    Left: IdentifierNameSyntax id,
                    Right: var right
                } assignment
            } &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            id.Identifier.ValueText == GSharpInternalNames.StateMachine.State &&
            TryReadIntLiteral(right, out nextState))
        {
            return true;
        }

        nextState = 0;
        return false;
    }

    private static bool TryGetWarpPublishSite(StatementSyntax statement, out int site)
    {
        if (statement is ExpressionStatementSyntax
            {
                Expression: InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax id,
                    ArgumentList.Arguments: var arguments
                }
            } &&
            id.Identifier.ValueText == GSharpInternalNames.Prelude.WarpPublish &&
            arguments.Count >= 3 &&
            TryReadIntLiteral(arguments[2].Expression, out site))
        {
            return true;
        }

        site = 0;
        return false;
    }

    private static bool TryGetWarpReadSite(StatementSyntax statement, out int site)
    {
        foreach (var invocation in statement.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is IdentifierNameSyntax id &&
                s_readFunctions.Contains(id.Identifier.ValueText) &&
                invocation.ArgumentList.Arguments.Count >= 3 &&
                TryReadIntLiteral(invocation.ArgumentList.Arguments[2].Expression, out site))
            {
                return true;
            }
        }

        site = 0;
        return false;
    }

    private static bool TryReadIntLiteral(ExpressionSyntax? expression, out int value)
    {
        if (expression is LiteralExpressionSyntax { Token.Value: int number })
        {
            value = number;
            return true;
        }

        if (expression is PrefixUnaryExpressionSyntax
            {
                Operand: LiteralExpressionSyntax { Token.Value: int pos }
            } unary &&
            unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            value = -pos;
            return true;
        }

        value = 0;
        return false;
    }

    private static HashSet<string> BuildReadFunctions()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<WarpShuffleKind>())
        {
            if (kind != WarpShuffleKind.None)
            {
                names.Add(WarpShuffleShape.ForKind(kind).ReadFunction);
            }
        }

        return names;
    }
}
