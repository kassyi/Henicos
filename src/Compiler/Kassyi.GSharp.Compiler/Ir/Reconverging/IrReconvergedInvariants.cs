using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Reconverging;

/// <summary>Checks the invariants of a reconverged IR body (<c>RC1</c>–<c>RC7</c>).</summary>
/// <remarks>
/// RC1 every <c>IrIf</c>/<c>IrWhile</c> uniformity is classified; RC2 uniformity matches <c>reachesActiveMask</c>;
/// RC3 every <c>enter(s)</c> wraps one non-region statement followed by <c>arrive(s)</c>; RC4 <c>enter</c> sites are
/// numbered from 0 in preorder and match <c>RegionCount</c>; RC5 <c>leave</c> appears only before device returns in
/// innermost-to-outermost order; RC6 runtime calls are only valid region calls at statement top level; RC7
/// <c>LoopConditions</c> keys match body <c>IrWhile</c> instances and cover every split-condition loop.
/// </remarks>
internal static class IrReconvergedInvariants
{
    /// <summary>
    /// Returns the invariant violations in <paramref name="result"/>; empty when all invariants hold.
    /// </summary>
    public static ImmutableArray<string> Verify(IrReconvergedBody result, Compilation compilation,
        bool isDeviceFunction, bool reachesActiveMask)
    {
        var checker = new Checker(result, compilation, isDeviceFunction, reachesActiveMask);
        checker.VerifyAll();
        return checker.Violations;
    }

    private sealed class Checker(
        IrReconvergedBody result,
        Compilation compilation,
        bool isDeviceFunction,
        bool reachesActiveMask)
    {
        private readonly ImmutableArray<string>.Builder _violations = ImmutableArray.CreateBuilder<string>();
        private readonly HashSet<IrWhile> _bodyLoops = [];
        private readonly ImmutableArray<int>.Builder _openRegions = ImmutableArray.CreateBuilder<int>();
        private int _expectedSite;

        public ImmutableArray<string> Violations => _violations.ToImmutable();

        public void VerifyAll()
        {
            var root = result.Body.Body;
            CheckStatementList(root.Statements);
            if (result.RegionCount != _expectedSite)
            {
                Add("RC4", root.Source,
                    $"region count {result.RegionCount} does not match enter count {_expectedSite}");
            }

            CheckNode(root, allowDirectRuntimeCall: false);
            CheckLoopConditions();
        }

        private void CheckStatementList(ImmutableArray<IrStatement> statements)
        {
            for (var index = 0; index < statements.Length; index++)
            {
                var current = statements[index];
                if (TryGetRegionCall(current, out var call))
                {
                    index = CheckRegionCallInList(statements, index, current, call);
                    continue;
                }

                CheckEmbeddedStatement(current);
            }
        }

        private int CheckRegionCallInList(ImmutableArray<IrStatement> statements, int index, IrStatement current,
            IrRuntimeCall call)
        {
            if (call.Function == IrRuntimeFunction.WarpRegionEnter)
            {
                return CheckEnterTuple(statements, index, current, call);
            }

            if (call.Function == IrRuntimeFunction.WarpRegionLeave)
            {
                return CheckLeaveRun(statements, index);
            }

            Add("RC3", current.Source, $"unpaired region call {call.Function.ToString()} in statement list");
            return index;
        }

        private int CheckEnterTuple(ImmutableArray<IrStatement> statements, int index, IrStatement enterStmt,
            IrRuntimeCall enter)
        {
            if (enter.Site != _expectedSite)
            {
                Add("RC4", enterStmt.Source,
                    $"enter site {enter.Site} does not match expected preorder site {_expectedSite}");
            }

            _expectedSite++;
            if (index + 2 >= statements.Length)
            {
                Add("RC3", enterStmt.Source, "enter statement is not followed by a body statement and arrive");
                return index;
            }

            var inner = statements[index + 1];
            var arriveStmt = statements[index + 2];
            if (TryGetRegionCall(inner, out _))
            {
                Add("RC3", inner.Source, "region body statement cannot be a region call");
            }

            if (!TryGetRegionCall(arriveStmt, out var arrive) ||
                arrive.Function != IrRuntimeFunction.WarpReconvergeArrive ||
                arrive.Site != enter.Site)
            {
                Add("RC3", arriveStmt.Source,
                    $"enter({enter.Site}) must be followed after one statement by arrive({enter.Site})");
                return index;
            }

            _openRegions.Add(enter.Site);
            CheckEmbeddedStatement(inner);
            _openRegions.RemoveAt(_openRegions.Count - 1);
            return index + 2;
        }

        private int CheckLeaveRun(ImmutableArray<IrStatement> statements, int startIndex)
        {
            var end = startIndex;
            while (end < statements.Length &&
                   TryGetRegionCall(statements[end], out var leave) &&
                   leave.Function == IrRuntimeFunction.WarpRegionLeave)
            {
                if (!isDeviceFunction)
                {
                    Add("RC5", statements[end].Source, "leave is not allowed in a kernel");
                }

                end++;
            }

            if (!isDeviceFunction)
            {
                return end - 1;
            }

            if (end >= statements.Length || statements[end] is not IrReturn)
            {
                Add("RC5", statements[startIndex].Source, "leave sequence must be immediately followed by return");
                return end - 1;
            }

            VerifyLeaveOrder(statements, startIndex, end);
            return end;
        }

        private void VerifyLeaveOrder(ImmutableArray<IrStatement> statements, int startIndex, int endIndex)
        {
            var count = endIndex - startIndex;
            if (count != _openRegions.Count)
            {
                Add("RC5", statements[endIndex].Source,
                    $"return has {count} leave calls but {_openRegions.Count} open regions");
                return;
            }

            for (var offset = 0; offset < count; offset++)
            {
                TryGetRegionCall(statements[startIndex + offset], out var leave);
                var expectedSite = _openRegions[_openRegions.Count - 1 - offset];
                if (leave is not null && leave.Site != expectedSite)
                {
                    Add("RC5", statements[startIndex + offset].Source,
                        $"leave site {leave.Site} does not match expected innermost site {expectedSite}");
                }
            }
        }

        private void CheckEmbeddedStatement(IrStatement statement)
        {
            switch (statement)
            {
                case IrBlock block:
                    CheckStatementList(block.Statements);
                    break;
                case IrIf @if:
                    CheckSingleSlot(@if.Then);
                    if (@if.Else is { } @else)
                    {
                        CheckSingleSlot(@else);
                    }

                    break;
                case IrWhile @while:
                    _bodyLoops.Add(@while);
                    CheckSingleSlot(@while.Body);
                    break;
                case IrReturn @return:
                    CheckReturn(@return);
                    break;
                case IrExpressionStatement or IrLocalDeclaration:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected statement in reconverged body: {statement.GetType().Name}.");
            }
        }

        private void CheckReturn(IrReturn @return)
        {
            if (isDeviceFunction && _openRegions.Count > 0)
            {
                Add("RC5", @return.Source, "device return is missing preceding leave calls");
            }
        }

        private void CheckSingleSlot(IrStatement statement)
        {
            if (TryGetRegionCall(statement, out var call))
            {
                var code = call.Function == IrRuntimeFunction.WarpRegionLeave ? "RC5" : "RC3";
                Add(code, statement.Source,
                    $"region call {call.Function.ToString()} cannot sit in a single-statement slot");
                return;
            }

            CheckEmbeddedStatement(statement);
        }

        private void CheckNode(IrNode node, bool allowDirectRuntimeCall)
        {
            switch (node)
            {
                case IrIf @if:
                    CheckUniformity(@if.Uniformity, @if.Source);
                    break;
                case IrWhile @while:
                    CheckUniformity(@while.Uniformity, @while.Source);
                    break;
                case IrRuntimeCall runtime:
                    CheckRuntimeCall(runtime, allowDirectRuntimeCall);
                    break;
            }

            if (node is IrExpressionStatement exprStatement)
            {
                CheckNode(exprStatement.Expression, allowDirectRuntimeCall: true);
                return;
            }

            foreach (var child in IrNodeChildren.Of(node))
            {
                CheckNode(child, allowDirectRuntimeCall: false);
            }
        }

        private void CheckUniformity(IrUniformity uniformity, IrSource source)
        {
            if (uniformity == IrUniformity.Unclassified)
            {
                Add("RC1", source, "branch or loop uniformity is Unclassified");
                return;
            }

            if (!reachesActiveMask && uniformity != IrUniformity.NotRequired)
            {
                Add("RC2", source,
                    $"method does not reach Warp.ActiveMask() so uniformity must be NotRequired, not " +
                    $"{uniformity.ToString()}");
            }
            else if (reachesActiveMask && uniformity is not (IrUniformity.Uniform or IrUniformity.ThreadVariant))
            {
                Add("RC2", source,
                    $"method reaches Warp.ActiveMask() so uniformity must be Uniform or ThreadVariant, not " +
                    $"{uniformity.ToString()}");
            }
        }

        private void CheckRuntimeCall(IrRuntimeCall runtime, bool allowDirectRuntimeCall)
        {
            if (!allowDirectRuntimeCall)
            {
                Add("RC6", runtime.Source, "runtime call is not the direct expression of a statement");
            }

            switch (runtime.Function)
            {
                case IrRuntimeFunction.WarpRegionEnter or IrRuntimeFunction.WarpRegionLeave:
                    if (runtime.Place is not null)
                    {
                        Add("RC6", runtime.Source, $"{runtime.Function.ToString()} must have a null place");
                    }

                    break;
                case IrRuntimeFunction.WarpReconvergeArrive:
                    if (runtime.Place is null)
                    {
                        Add("RC6", runtime.Source, "WarpReconvergeArrive must have a non-null place");
                    }

                    break;
                default:
                    Add("RC6", runtime.Source,
                        $"runtime function {runtime.Function.ToString()} is not allowed in reconverged IR");
                    break;
            }

            if (runtime.Shuffle != WarpShuffleKind.None || !runtime.Arguments.IsEmpty)
            {
                Add("RC6", runtime.Source, "region runtime call must have Shuffle == None and no arguments");
            }
        }

        private void CheckLoopConditions()
        {
            foreach (var key in result.Body.LoopConditions.Keys)
            {
                if (!_bodyLoops.Contains(key))
                {
                    Add("RC7", key.Source, "LoopConditions key is not an IrWhile instance in the body");
                }
            }

            foreach (var loop in _bodyLoops)
            {
                if (IrSplitLowering.HoldsSplit(loop.Condition, compilation) &&
                    !result.Body.LoopConditions.ContainsKey(loop))
                {
                    Add("RC7", loop.Source, "IrWhile with a split condition is missing from LoopConditions");
                }
            }
        }

        private static bool TryGetRegionCall(IrStatement statement, [NotNullWhen(true)] out IrRuntimeCall? call)
        {
            if (statement is IrExpressionStatement { Expression: IrRuntimeCall runtime })
            {
                call = runtime;
                return true;
            }

            call = null;
            return false;
        }

        private void Add(string code, IrSource source, string text) =>
            _violations.Add($"{code} at {source.Start}..{source.Start + source.Length}: {text}");
    }
}
