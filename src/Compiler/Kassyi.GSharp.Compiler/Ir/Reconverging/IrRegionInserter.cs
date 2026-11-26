using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Reconverging;

/// <summary>
/// Inserts reconvergence regions (<c>enter</c>, <c>arrive</c>, and device-function <c>leave</c>) around split-holding
/// suspending calls, thread-dependent branches, and loops on a classified body.
/// </summary>
internal static class IrRegionInserter
{
    /// <summary>
    /// Returns <paramref name="body"/> with reconvergence regions around thread-dependent branches, loops, and
    /// suspending calls, and <c>leave</c> calls before device-function returns.
    /// </summary>
    public static IrReconvergedBody Insert(IrSplitLoweredBody body, Compilation compilation, bool isDeviceFunction) =>
        new Inserter(body, compilation, isDeviceFunction).Run();

    private sealed class Inserter(IrSplitLoweredBody body, Compilation compilation, bool isDeviceFunction)
    {
        private readonly SyntaxTree[] _trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
        private readonly ITypeSymbol _voidType = compilation.GetSpecialType(SpecialType.System_Void);
        private readonly IrLoopConditionTable _table = new(body.LoopConditions);
        private readonly ImmutableArray<int>.Builder _open = ImmutableArray.CreateBuilder<int>();
        private int _regionCount;

        public IrReconvergedBody Run()
        {
            var statements = RewriteList(body.Body.Statements, isKernelBody: !isDeviceFunction);
            var rewrittenBody = body.Body.Update(statements);
            return new IrReconvergedBody(new IrSplitLoweredBody(rewrittenBody, _table.ToImmutable()), _regionCount);
        }

        private ImmutableArray<IrStatement> RewriteList(ImmutableArray<IrStatement> statements, bool isKernelBody)
        {
            var builder = ImmutableArray.CreateBuilder<IrStatement>(statements.Length);
            for (var index = 0; index < statements.Length; index++)
            {
                var endsThread = isKernelBody && index == statements.Length - 1;
                AddRewritten(builder, statements[index], endsThread);
            }

            return SameInstances(statements, builder) ? statements : builder.ToImmutable();
        }

        private void AddRewritten(ImmutableArray<IrStatement>.Builder builder, IrStatement statement, bool endsThread)
        {
            if (statement is IrReturn @return)
            {
                AddReturn(builder, @return);
                return;
            }

            int? region = !endsThread && NeedsRegion(statement) ? OpenRegion() : null;
            if (region is not { } site)
            {
                builder.Add(RewriteStatement(statement, endsThread));
                return;
            }

            var placeSource = GetPlaceSource(statement);
            var regionSource = statement.Source.AsSynthesized();
            var enter = CreateRegionCall(IrRuntimeFunction.WarpRegionEnter, site, place: null, regionSource);
            if (!statement.CommentsBefore.IsEmpty)
            {
                enter = IrStatement.WithCommentsBefore(enter, statement.CommentsBefore);
            }

            builder.Add(enter);
            var rewritten = WithoutComments(RewriteStatement(statement, endsThread: false));
            _open.RemoveAt(_open.Count - 1);
            builder.Add(rewritten);
            var place = SourcePlace.Format(placeSource.ToLocation(compilation));
            builder.Add(CreateRegionCall(IrRuntimeFunction.WarpReconvergeArrive, site, place, regionSource));
        }

        private void AddReturn(ImmutableArray<IrStatement>.Builder builder, IrReturn @return)
        {
            if (!isDeviceFunction || _open.Count == 0)
            {
                builder.Add(@return);
                return;
            }

            var leaveSource = @return.Source.AsSynthesized();
            for (var index = _open.Count - 1; index >= 0; index--)
            {
                var leave = CreateRegionCall(IrRuntimeFunction.WarpRegionLeave, _open[index], place: null,
                    leaveSource);
                if (index == _open.Count - 1 && !@return.CommentsBefore.IsEmpty)
                {
                    leave = IrStatement.WithCommentsBefore(leave, @return.CommentsBefore);
                }

                builder.Add(leave);
            }

            builder.Add(WithoutComments(@return));
        }

        private int OpenRegion()
        {
            var site = _regionCount++;
            _open.Add(site);
            return site;
        }

        private IrStatement RewriteStatement(IrStatement statement, bool endsThread)
        {
            switch (statement)
            {
                case IrBlock block:
                    return block.Update(RewriteList(block.Statements, isKernelBody: endsThread));
                case IrIf @if:
                    return @if.Update(@if.Condition, RewriteEmbedded(@if.Then),
                        @if.Else is { } @else ? RewriteEmbedded(@else) : null);
                case IrWhile @while:
                    var rebuilt = @while.Update(@while.Condition, RewriteEmbedded(@while.Body));
                    _table.Rebuilt(@while, rebuilt);
                    return rebuilt;
                case IrExpressionStatement or IrLocalDeclaration or IrReturn:
                    return statement;
                default:
                    throw new InvalidOperationException(
                        $"Cannot rewrite reconvergence regions for statement of kind {statement.GetType().Name}.");
            }
        }

        private IrStatement RewriteEmbedded(IrStatement statement)
        {
            var builder = ImmutableArray.CreateBuilder<IrStatement>();
            AddRewritten(builder, statement, endsThread: false);
            return builder.Count == 1
                ? builder[0]
                : new IrBlock(builder.ToImmutable(), ImmutableArray<IrVariable>.Empty,
                    statement.Source.AsSynthesized());
        }

        private bool NeedsRegion(IrStatement statement) => statement switch
        {
            IrExpressionStatement or IrLocalDeclaration => FindSuspendingCall(statement, _trees) is not null,
            IrIf @if => (IrSplitLowering.HoldsSplit(@if.Then, compilation) ||
                         (@if.Else is { } @else && IrSplitLowering.HoldsSplit(@else, compilation))) &&
                        IsThreadVariant(@if.Uniformity),
            IrWhile @while => IrSplitLowering.HoldsSplit(@while, compilation) &&
                              (IsThreadVariant(@while.Uniformity) || ContainsReturn(@while.Body)),
            IrBlock or IrReturn => false,
            _ => throw new InvalidOperationException(
                $"Cannot decide reconvergence region for statement of kind {statement.GetType().Name}.")
        };

        private static bool IsThreadVariant(IrUniformity uniformity) => uniformity switch
        {
            IrUniformity.ThreadVariant => true,
            IrUniformity.Uniform => false,
            _ => throw new InvalidOperationException(
                $"Uniformity {uniformity.ToString()} is not valid when inserting reconvergence regions.")
        };

        private IrSource GetPlaceSource(IrStatement statement) => statement switch
        {
            IrExpressionStatement or IrLocalDeclaration =>
                (FindSuspendingCall(statement, _trees) ??
                 throw new InvalidOperationException("Expected a suspending call in the region statement.")).Source,
            IrIf or IrWhile => statement.Source,
            _ => throw new InvalidOperationException(
                $"No region place source for statement of kind {statement.GetType().Name}.")
        };

        private static IrCall? FindSuspendingCall(IrNode node, SyntaxTree[] trees)
        {
            if (node is IrCall call)
            {
                if (RemovedCalls.IsRemoved(call.Method, trees[call.Source.TreeOrdinal]))
                {
                    return null;
                }

                if (call.Classification.Split.Kind == IrSplitKind.SuspendingCall)
                {
                    return call;
                }
            }

            return IrNodeChildren.Of(node)
                .AsStackEnumerable()
                .Select(trees, static (child, syntaxTrees) => FindSuspendingCall(child, syntaxTrees))
                .OfType<IrCall>()
                .FirstOrDefault();
        }

        private static bool ContainsReturn(IrNode node) =>
            IrNodeChildren.Exists(node, static child => child is IrReturn);

        private IrExpressionStatement CreateRegionCall(IrRuntimeFunction function, int site, string? place,
            IrSource source) =>
            new(new IrRuntimeCall(function, WarpShuffleKind.None, site, ImmutableArray<IrRuntimeArgument>.Empty,
                place, _voidType, source), source);

        private T WithoutComments<T>(T statement) where T : IrStatement
        {
            var updated = IrStatement.WithCommentsBefore(statement, ImmutableArray<string>.Empty);
            if (statement is IrWhile previous && updated is IrWhile rebuilt)
            {
                _table.Rebuilt(previous, rebuilt);
            }

            return updated;
        }

        private static bool SameInstances(ImmutableArray<IrStatement> original,
            ImmutableArray<IrStatement>.Builder rewritten)
        {
            if (original.Length != rewritten.Count)
            {
                return false;
            }

            for (var index = 0; index < original.Length; index++)
            {
                if (original[index] != rewritten[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
