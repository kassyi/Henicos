namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.StackArrays;

/// <summary>A stack array storage: the C++ scope it lives in, and its name in the message.</summary>
internal readonly record struct Storage(SyntaxNode Scope, string Name);

/// <summary>A use that lets a storage outlive its scope: where to report it, the storage, and the use.</summary>
internal readonly record struct Finding(SyntaxNode Node, string Name, string Reason);

/// <summary>
/// The GSH0091 analysis of one method body, the only type <see cref="StackArrayEscapeRule"/> calls: collects the
/// declarations, assignments, calls and returns, propagates to each local the storages it may point into until nothing
/// changes, and returns the uses that let a storage outlive its scope.
/// </summary>
internal sealed class StackArrayEscapeAnalysis
{
    private readonly SemanticModel _model;

    // The storages that a local or a value parameter may point into
    private readonly Dictionary<ISymbol, ImmutableArray<Storage>.Builder> _held = new(SymbolEqualityComparer.Default);
    private readonly StorageGatherer _gatherer;
    private readonly ImmutableArray<VariableDeclaratorSyntax>.Builder _declarations = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
    private readonly ImmutableArray<AssignmentExpressionSyntax>.Builder _assignments = ImmutableArray.CreateBuilder<AssignmentExpressionSyntax>();
    private readonly ImmutableArray<ArgumentListSyntax>.Builder _callsWithReferences = ImmutableArray.CreateBuilder<ArgumentListSyntax>();
    private readonly ImmutableArray<InvocationExpressionSyntax>.Builder _calls = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();
    private readonly ImmutableArray<ExpressionSyntax>.Builder _returns = ImmutableArray.CreateBuilder<ExpressionSyntax>();

    public StackArrayEscapeAnalysis(SemanticModel model)
    {
        _model = model;
        _gatherer = new StorageGatherer(model, _held);
    }

    public ImmutableArray<Finding> Run(SyntaxNode body)
    {
        Collect(body);
        Propagate();

        var findings = ImmutableArray.CreateBuilder<Finding>();
        foreach (var assignment in _assignments)
        {
            CheckAssignment(assignment, findings);
        }

        foreach (var arguments in _callsWithReferences)
        {
            CheckCall(arguments, findings);
        }

        foreach (var value in _returns)
        {
            if (StorageGatherer.TryFirst(_gatherer.StorageOf(value), out var storage))
            {
                findings.Add(new Finding(value, storage.Name, StackArrayEscapeRule.Returned));
            }
        }

        findings.Sort(static (x, y) => x.Node.SpanStart.CompareTo(y.Node.SpanStart));
        return findings.ToImmutable();
    }

    private void Collect(SyntaxNode body)
    {
        if (body is ArrowExpressionClauseSyntax arrow)
        {
            _returns.Add(arrow.Expression);
        }

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Initializer: not null } declarator:
                    _declarations.Add(declarator);
                    break;
                // A member of an object initializer is part of the value it initializes (StorageGatherer)
                case AssignmentExpressionSyntax { Parent: not InitializerExpressionSyntax } assignment:
                    _assignments.Add(assignment);
                    break;
                case ArgumentListSyntax arguments when arguments.Arguments
                    .AsStackEnumerable()
                    .Any(static a => a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword)):
                    _callsWithReferences.Add(arguments);
                    break;
                case ReturnStatementSyntax { Expression: { } value }:
                    _returns.Add(value);
                    break;
                case InvocationExpressionSyntax invocation:
                    _calls.Add(invocation);
                    break;
                case ArrowExpressionClauseSyntax { Parent: LocalFunctionStatementSyntax } localArrow:
                    _returns.Add(localArrow.Expression);
                    break;
                case AnonymousFunctionExpressionSyntax { ExpressionBody: { } lambdaValue }:
                    _returns.Add(lambdaValue);
                    break;
            }
        }
    }

    // Spreads the storages through the locals until no local gets a new one
    private void Propagate()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var declarator in _declarations)
            {
                if (_model.GetDeclaredSymbol(declarator) is ILocalSymbol local &&
                    declarator.Initializer is { } initializer)
                {
                    changed |= Hold(local, _gatherer.StorageOf(initializer.Value));
                }
            }

            foreach (var assignment in _assignments)
            {
                foreach (var (target, value) in AssignmentPairs.Pairs(assignment))
                {
                    if (RootLocal(target) is { } local)
                    {
                        changed |= Hold(local, _gatherer.StorageOf(value));
                    }
                }
            }

            // The value parameters of a local function or lambda that the method calls hold what the arguments refer to,
            // so that a write to a captured variable in its body is checked like one in the method
            foreach (var invocation in _calls)
            {
                if (_gatherer.CalleeOfLocalCode(invocation) is not { } callee)
                {
                    continue;
                }

                var arguments = invocation.ArgumentList.Arguments;
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (callee.FindParameter(arguments, i) is { RefKind: RefKind.None } parameter)
                    {
                        changed |= Hold(parameter, _gatherer.StorageOf(arguments[i].Expression));
                    }
                }
            }

            foreach (var arguments in _callsWithReferences)
            {
                var passed = StoragesOfArguments(arguments, except: null);
                foreach (var argument in arguments.Arguments)
                {
                    if (IsWritableReference(argument) && RootLocal(argument.Expression) is { } local)
                    {
                        changed |= Hold(local, passed);
                    }
                }
            }
        }
        while (changed);
    }

    private bool Hold(ISymbol local, ImmutableArray<Storage> storages)
    {
        if (storages.Length == 0)
        {
            return false;
        }

        if (!_held.TryGetValue(local, out var held))
        {
            held = ImmutableArray.CreateBuilder<Storage>();
            _held.Add(local, held);
        }

        var added = false;
        foreach (var storage in storages)
        {
            if (!held
                    .AsStackEnumerable()
                    .Any(storage.Scope, static (h, scope) => h.Scope == scope))
            {
                held.Add(storage);
                added = true;
            }
        }

        return added;
    }

    private void CheckAssignment(AssignmentExpressionSyntax assignment, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var (target, value) in AssignmentPairs.Pairs(assignment))
        {
            if (LocalOf(AssignmentPairs.Unwrap(target)) is { } targetLocal && IsStackArrayLocal(targetLocal))
            {
                findings.Add(new Finding(assignment, targetLocal.Name, StackArrayEscapeRule.Reassigned));
                return;
            }

            if (FindOutlived(_gatherer.StorageOf(value), CppScopes.TargetScope(target, _model, _gatherer), out var storage))
            {
                var reason = LocalOf(AssignmentPairs.Unwrap(target)) is { IsRef: false }
                    ? StackArrayEscapeRule.AssignedToOuterVariable
                    : StackArrayEscapeRule.StoredOutside;
                findings.Add(new Finding(assignment, storage.Name, reason));
                return;
            }
        }
    }

    private void CheckCall(ArgumentListSyntax arguments, ImmutableArray<Finding>.Builder findings)
    {
        foreach (var argument in arguments.Arguments)
        {
            if (!IsWritableReference(argument))
            {
                continue;
            }

            if (_model.GetSymbol(argument.Expression) is ILocalSymbol local && IsStackArrayLocal(local))
            {
                findings.Add(new Finding(argument, local.Name, StackArrayEscapeRule.PassedByReference));
                continue;
            }

            if (_gatherer.IsCarrier(_gatherer.TypeOf(argument.Expression)) &&
                FindOutlived(StoragesOfArguments(arguments, except: argument),
                    CppScopes.TargetScope(argument.Expression, _model, _gatherer), out var storage))
            {
                findings.Add(new Finding(argument, storage.Name, StackArrayEscapeRule.PassedWithLongerLivedReference));
            }
        }
    }

    // What a callee may store into a ref / out argument: any argument that carries a reference
    private ImmutableArray<Storage> StoragesOfArguments(ArgumentListSyntax arguments, ArgumentSyntax? except)
    {
        var storages = ImmutableArray.CreateBuilder<Storage>();
        foreach (var argument in arguments.Arguments)
        {
            if (argument != except && !argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                _gatherer.Gather(argument.Expression, storages, Carry.Value);
            }
        }

        return storages.ToImmutable();
    }

    // The first storage that a target of the given scope outlives; a null scope is outside the method
    private static bool FindOutlived(ImmutableArray<Storage> storages, SyntaxNode? targetScope, out Storage storage)
    {
        foreach (var candidate in storages)
        {
            if (targetScope == null || CppScopes.IsProperAncestor(targetScope, candidate.Scope))
            {
                storage = candidate;
                return true;
            }
        }

        storage = default;
        return false;
    }

    // The variable that a write to target changes: the local or value parameter itself, or the struct local whose field
    // it is
    private ISymbol? RootLocal(SyntaxNode target)
    {
        var current = AssignmentPairs.Unwrap(target);
        while (current is MemberAccessExpressionSyntax member &&
               _gatherer.TypeOf(member.Expression) is { IsValueType: true })
        {
            current = AssignmentPairs.Unwrap(member.Expression);
        }

        if (LocalOf(current) is { IsRef: false } local)
        {
            return local;
        }

        return _model.GetSymbol(current) is IParameterSymbol { RefKind: RefKind.None } parameter ? parameter : null;
    }

    // The local that a name or a designation stands for
    private ILocalSymbol? LocalOf(SyntaxNode node) => AssignmentPairs.LocalOf(node, _model);

    private static bool IsWritableReference(ArgumentSyntax argument) =>
        argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword);

    // The stack array of an array or span local is a C++ array, which cannot be assigned or passed by reference
    private static bool IsStackArrayLocal(ILocalSymbol local) => StackArrayLocals.IsStackArray(local, out _);
}
