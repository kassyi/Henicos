namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// A set of variables that an expression reads or writes: locals and by-value parameters by symbol (or by name, for an
/// identifier that lowering synthesized and the semantic model does not know), and two coarse members:
/// <see cref="Memory"/> (array elements, pointers, shared memory, <c>ref</c> parameters, static fields) and
/// <see cref="AllLocals"/> (a call that may touch any local: a local function or a delegate).
/// </summary>
internal struct VariableSet
{
    private ImmutableArray<object>.Builder? _variables;

    /// <summary>Memory that is not a local: two sets with <see cref="Memory"/> overlap.</summary>
    public bool Memory;

    /// <summary>Every local: overlaps any set that names a local.</summary>
    public bool AllLocals;

    public readonly bool IsEmpty => !Memory && !AllLocals && _variables is not { Count: > 0 };

    private readonly bool HasVariables => AllLocals || _variables is { Count: > 0 };

    /// <summary>Adds a local or by-value parameter (<see cref="ISymbol"/>) or the name of an unresolved identifier.</summary>
    public void Add(object variable)
    {
        _variables ??= ImmutableArray.CreateBuilder<object>();
        if (!Contains(variable))
        {
            _variables.Add(variable);
        }
    }

    /// <summary>Whether a variable of this set may be the same as a variable of <paramref name="other"/>.</summary>
    public readonly bool Overlaps(in VariableSet other)
    {
        if ((Memory && other.Memory) || (AllLocals && other.HasVariables) || (other.AllLocals && HasVariables))
        {
            return true;
        }

        if (_variables == null)
        {
            return false;
        }

        foreach (var variable in _variables)
        {
            if (other.Contains(variable))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the set names <paramref name="variable"/> (a symbol or a name).</summary>
    public readonly bool Contains(object variable)
    {
        if (_variables == null)
        {
            return false;
        }

        foreach (var existing in _variables)
        {
            if (existing is ISymbol symbol ? variable is ISymbol other && symbol.IsEqualTo(other) : existing.Equals(variable))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// What evaluating an expression reads (<see cref="Reads"/>) and writes (<see cref="Writes"/>), as far as the order of
/// the operands of an enclosing expression can tell (<see cref="EvaluationOrder"/>).
/// </summary>
/// <remarks>
/// <para>
/// A local or by-value parameter is one variable; every other location (array and span elements, pointers, shared
/// memory, <c>ref</c> / <c>out</c> / <c>in</c> parameters, ref locals, static fields, fields of <c>this</c>) is
/// <see cref="VariableSet.Memory"/>. A field of a struct local is the local. A call reads <see cref="VariableSet.Memory"/>
/// (its callee may read anything) unless it is pure math over scalars, and writes what
/// <see cref="DeviceFunctionEffects"/> says: nothing, the variables of its <c>ref</c> / <c>out</c> arguments, or
/// <see cref="VariableSet.Memory"/>. A <c>ref</c> / <c>out</c> argument only names its location: the variable itself is
/// read or written by the call, after every argument has been evaluated. A lambda is not evaluated.
/// </para>
/// <para>
/// [WHY] The same rule as Roslyn's <c>LocalRewriter.IsSafeForReordering</c> and <c>CanChangeValueBetweenReads</c>: a
/// read of a local is safe to reorder unless another operand writes that local. Unlike Roslyn, a call is classified by
/// its callee, since C++ does not keep the order even of positional arguments and most calls would otherwise need
/// temporaries.
/// </para>
/// </remarks>
internal readonly struct ExpressionEffects(VariableSet reads, VariableSet writes)
{
    public VariableSet Reads { get; } = reads;

    public VariableSet Writes { get; } = writes;

    /// <summary>Neither reads nor writes anything (a constant, <c>ThreadIndex.X</c>, a lambda).</summary>
    public bool IsEmpty => Reads.IsEmpty && Writes.IsEmpty;

    /// <summary>The effects of evaluating <paramref name="expression"/> for its value.</summary>
    public static ExpressionEffects Of(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new Walker(model);
        walker.AddValue(expression);
        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names (the target of an assignment, a
    /// <c>ref</c> / <c>out</c> / <c>in</c> argument): the reads of its index and receiver, without reading the location.
    /// With <paramref name="readsValue"/> the location is also read (the target of a compound assignment).
    /// </summary>
    public static ExpressionEffects OfLocation(ExpressionSyntax expression, SemanticModel model, bool readsValue = false)
    {
        var walker = new Walker(model);
        var root = walker.AddLocation(expression);
        if (readsValue)
        {
            walker.AddRead(root);
        }

        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>Whether this operand, evaluated before <paramref name="later"/>, could see or change what it does.</summary>
    public bool InterferesWith(in ExpressionEffects later) =>
        Writes.Overlaps(later.Reads) || Writes.Overlaps(later.Writes) || Reads.Overlaps(later.Writes);

    // The variable that a location belongs to: a symbol or name, or memory
    private readonly struct Root(object? variable)
    {
        public object? Variable { get; } = variable;

        public bool IsMemory => Variable == null;
    }

    private struct Walker(SemanticModel model)
    {
        public VariableSet Reads;
        public VariableSet Writes;

        public void AddRead(Root root)
        {
            if (root.Variable is { } variable)
            {
                Reads.Add(variable);
            }
            else
            {
                Reads.Memory = true;
            }
        }

        private void AddWrite(Root root)
        {
            if (root.Variable is { } variable)
            {
                Writes.Add(variable);
            }
            else
            {
                Writes.Memory = true;
            }
        }

        private void AddUnknown()
        {
            Reads.Memory = Reads.AllLocals = true;
            Writes.Memory = Writes.AllLocals = true;
        }

        public void AddValue(ExpressionSyntax expression)
        {
            if (model.GetSafeConstantValue(expression).HasValue)
            {
                return;
            }

            switch (expression)
            {
                case LiteralExpressionSyntax or DefaultExpressionSyntax or SizeOfExpressionSyntax or
                    TypeOfExpressionSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or
                    DeclarationExpressionSyntax or BaseExpressionSyntax or PredefinedTypeSyntax:
                    return;
                case ParenthesizedExpressionSyntax parenthesized:
                    AddValue(parenthesized.Expression);
                    return;
                case CastExpressionSyntax cast:
                    AddValue(cast.Expression);
                    return;
                case CheckedExpressionSyntax @checked:
                    AddValue(@checked.Expression);
                    return;
                case PostfixUnaryExpressionSyntax postfix:
                    if (postfix.IsKind(SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression))
                    {
                        AddIncrement(postfix.Operand);
                    }
                    else
                    {
                        AddValue(postfix.Operand);
                    }

                    return;
                case PrefixUnaryExpressionSyntax prefix:
                    AddPrefix(prefix);
                    return;
                case IdentifierNameSyntax identifier:
                    AddIdentifier(identifier);
                    return;
                case MemberAccessExpressionSyntax access:
                    AddMemberAccess(access);
                    return;
                case ElementAccessExpressionSyntax element:
                    AddValue(element.Expression);
                    AddArgumentValues(element.ArgumentList.Arguments);
                    Reads.Memory = true;
                    return;
                case AssignmentExpressionSyntax assignment:
                    AddAssignment(assignment);
                    return;
                case InvocationExpressionSyntax invocation:
                    AddInvocation(invocation);
                    return;
                case BaseObjectCreationExpressionSyntax creation:
                    AddObjectCreation(creation);
                    return;
                case ThisExpressionSyntax:
                    Reads.Memory = true;
                    return;
                case RefExpressionSyntax reference:
                    AddLocation(reference.Expression);
                    return;
                default:
                    AddChildren(expression);
                    return;
            }
        }

        // Every expression under node, for the kinds whose operands are all evaluated (?:, binary operators, tuples,
        // switch and with expressions, patterns, initializers, collection expressions ...)
        private void AddChildren(SyntaxNode node)
        {
            foreach (var child in node.ChildNodes())
            {
                if (child is ExpressionSyntax expression)
                {
                    AddValue(expression);
                }
                else if (child is not TypeSyntax)
                {
                    AddChildren(child);
                }
            }
        }

        private void AddArgumentValues(SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            foreach (var argument in arguments)
            {
                AddValue(argument.Expression);
            }
        }

        private void AddPrefix(PrefixUnaryExpressionSyntax prefix)
        {
            switch (prefix.Kind())
            {
                case SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression:
                    AddIncrement(prefix.Operand);
                    return;
                case SyntaxKind.PointerIndirectionExpression:
                    AddValue(prefix.Operand);
                    Reads.Memory = true;
                    return;
                case SyntaxKind.AddressOfExpression:
                    AddLocation(prefix.Operand);
                    return;
                default:
                    AddValue(prefix.Operand);
                    return;
            }
        }

        private void AddIncrement(ExpressionSyntax operand)
        {
            var root = AddLocation(operand);
            AddRead(root);
            AddWrite(root);
        }

        private void AddIdentifier(IdentifierNameSyntax identifier)
        {
            switch (model.GetSymbol(identifier))
            {
                case null:
                    // A temporary that lowering synthesized: known by its name
                    Reads.Add(identifier.Identifier.ValueText);
                    return;
                case ILocalSymbol or IParameterSymbol or IFieldSymbol:
                    AddRead(AddLocation(identifier));
                    return;
                case IPropertySymbol property:
                    if (!IsIntrinsicProperty(property))
                    {
                        Reads.Memory = true;
                    }

                    return;
                default:
                    // A method group, a type, a namespace
                    return;
            }
        }

        private void AddMemberAccess(MemberAccessExpressionSyntax access)
        {
            if (access.IsKind(SyntaxKind.PointerMemberAccessExpression))
            {
                AddValue(access.Expression);
                Reads.Memory = true;
                return;
            }

            switch (model.GetSymbol(access) ?? model.GetSymbol(access.Name))
            {
                case IFieldSymbol:
                    AddRead(AddLocation(access));
                    return;
                case IPropertySymbol { IsStatic: true } property:
                    if (!IsIntrinsicProperty(property))
                    {
                        Reads.Memory = true;
                    }

                    return;
                case IPropertySymbol property:
                    AddValue(access.Expression);
                    if (StructAbiMembers.IsComputedProperty(property))
                    {
                        // The getter may read anything
                        Reads.Memory = true;
                    }

                    return;
                case IMethodSymbol { IsStatic: false }:
                    AddValue(access.Expression);
                    return;
                case null:
                    AddValue(access.Expression);
                    Reads.Add(access.Name.Identifier.ValueText);
                    return;
                default:
                    return;
            }
        }

        // ThreadIndex.X, BlockDim.Y ...: a value that no expression of the thread changes
        private static bool IsIntrinsicProperty(IPropertySymbol property) =>
            property.HasAttribute(KnownAttributes.CudaIntrinsic) ||
            property.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) == true;

        private void AddAssignment(AssignmentExpressionSyntax assignment)
        {
            if (assignment.Left is TupleExpressionSyntax tuple)
            {
                foreach (var argument in tuple.Arguments)
                {
                    AddWrite(AddLocation(argument.Expression));
                }

                AddValue(assignment.Right);
                return;
            }

            var root = AddLocation(assignment.Left);
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                AddRead(root);
            }

            AddValue(assignment.Right);
            AddWrite(root);
            if (model.GetSymbol(assignment.Left) is IPropertySymbol property &&
                StructAbiMembers.IsComputedProperty(property))
            {
                // The setter may write anything
                Writes.Memory = true;
            }
        }

        /// <summary>
        /// Adds the reads that find the location of <paramref name="expression"/> (indices, receivers) and returns the
        /// variable it belongs to, without reading or writing the location itself.
        /// </summary>
        public Root AddLocation(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return AddLocation(parenthesized.Expression);
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                    return new Root(model.GetDeclaredSymbol<ILocalSymbol>(designation) ??
                                    (object)designation.Identifier.ValueText);
                case IdentifierNameSyntax identifier:
                    return model.GetSymbol(identifier) switch
                    {
                        ILocalSymbol { IsRef: false } local => new Root(local),
                        IParameterSymbol { RefKind: RefKind.None } parameter => new Root(parameter),
                        null => new Root(identifier.Identifier.ValueText),
                        _ => new Root(null)
                    };
                case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                {
                    var member = model.GetSymbol(access) ?? model.GetSymbol(access.Name);
                    if (member is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true })
                    {
                        return new Root(null);
                    }

                    // A field of a struct is part of the struct; a member of anything else is memory
                    if (model.GetTypeSymbol(access.Expression) is { IsValueType: true } &&
                        access.Expression is not ThisExpressionSyntax)
                    {
                        return AddLocation(access.Expression);
                    }

                    AddValue(access.Expression);
                    return new Root(null);
                }
                case ElementAccessExpressionSyntax element:
                    AddValue(element.Expression);
                    AddArgumentValues(element.ArgumentList.Arguments);
                    return new Root(null);
                case PrefixUnaryExpressionSyntax indirection when indirection.IsKind(SyntaxKind.PointerIndirectionExpression):
                    AddValue(indirection.Operand);
                    return new Root(null);
                default:
                    AddValue(expression);
                    return new Root(null);
            }
        }

        private void AddInvocation(InvocationExpressionSyntax invocation)
        {
            if (model.GetSymbol(invocation) is not IMethodSymbol method)
            {
                AddArgumentValues(invocation.ArgumentList.Arguments);
                AddUnknown();
                return;
            }

            if (method.MethodKind is MethodKind.LocalFunction or MethodKind.DelegateInvoke or
                MethodKind.AnonymousFunction)
            {
                AddLocalCall(invocation, method);
                return;
            }

            // The receiver of an instance call is evaluated first (C# spec §12.6.6); a struct member reads and may
            // write the struct it is called on
            Root? receiver = null;
            if (!method.IsStatic && invocation.Expression is MemberAccessExpressionSyntax access)
            {
                if (method.ContainingType.IsValueType || method.ReducedFrom != null)
                {
                    receiver = AddLocation(access.Expression);
                    AddRead(receiver.Value);
                }
                else
                {
                    AddValue(access.Expression);
                }
            }

            AddCall(method, invocation.ArgumentList.Arguments, receiver);
        }

        // A local function, or a delegate held by a local that a lambda initializes and nothing reassigns: it may read
        // any local it captures, and writes what its body writes (a captured local counts as any local). Any other
        // delegate may do anything.
        private void AddLocalCall(InvocationExpressionSyntax invocation, IMethodSymbol method)
        {
            DeviceFunctionEffect effect;
            if (method.MethodKind == MethodKind.LocalFunction)
            {
                effect = DeviceFunctionEffects.Classify(method, model.Compilation);
            }
            else if (TryGetLambda(invocation.Expression) is { } lambda)
            {
                effect = DeviceFunctionEffects.ClassifyLambda(lambda, model);
            }
            else
            {
                AddArgumentValues(invocation.ArgumentList.Arguments);
                AddUnknown();
                return;
            }

            Reads.AllLocals = true;
            AddCall(method, invocation.ArgumentList.Arguments, receiver: null, effect);
            if (effect == DeviceFunctionEffect.Memory)
            {
                Writes.AllLocals = true;
            }
        }

        // The lambda that initializes the local named by expression, when no assignment changes the local
        private AnonymousFunctionExpressionSyntax? TryGetLambda(ExpressionSyntax expression)
        {
            if (model.GetSymbol(expression) is not ILocalSymbol { IsRef: false } local ||
                !local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
                declarator.Initializer?.Value is not AnonymousFunctionExpressionSyntax lambda ||
                declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } scope)
            {
                return null;
            }

            var declaringModel = model.Compilation.GetSemanticModel(declarator.SyntaxTree);
            var reassigned = scope.DescendantNodes()
                .AsStackEnumerable()
                .Any((Local: local, Model: declaringModel), static (node, s) =>
                    node is AssignmentExpressionSyntax assignment && s.Model.GetSymbol(assignment.Left).IsEqualTo(s.Local)
                );
            return reassigned ? null : lambda;
        }

        private void AddObjectCreation(BaseObjectCreationExpressionSyntax creation)
        {
            if (model.GetTypeSymbol(creation).IsSharedMemoryType())
            {
                Writes.Memory = true;
            }

            if (model.GetSymbol(creation) is IMethodSymbol constructor)
            {
                AddCall(constructor, creation.ArgumentList?.Arguments ?? default, receiver: null);
            }
            else if (creation.ArgumentList != null)
            {
                AddArgumentValues(creation.ArgumentList.Arguments);
            }

            if (creation.Initializer != null)
            {
                AddInitializer(creation.Initializer);
            }
        }

        // The members that an object initializer assigns belong to the new object; only their values are evaluated
        private void AddInitializer(InitializerExpressionSyntax initializer)
        {
            foreach (var expression in initializer.Expressions)
            {
                switch (expression)
                {
                    case AssignmentExpressionSyntax { Right: InitializerExpressionSyntax nested }:
                        AddInitializer(nested);
                        break;
                    case AssignmentExpressionSyntax assignment:
                        AddValue(assignment.Right);
                        break;
                    default:
                        AddValue(expression);
                        break;
                }
            }
        }

        private void AddCall(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, Root? receiver) =>
            AddCall(method, arguments, receiver, DeviceFunctionEffects.Classify(method, model.Compilation));

        private void AddCall(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, Root? receiver,
            DeviceFunctionEffect effect)
        {
            var references = ImmutableArray.CreateBuilder<Root>();
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                var refKind = argument.RefKindKeyword.Kind() switch
                {
                    SyntaxKind.RefKeyword => RefKind.Ref,
                    SyntaxKind.OutKeyword => RefKind.Out,
                    SyntaxKind.InKeyword => RefKind.In,
                    _ => FindParameter(method, arguments, i)?.RefKind ?? RefKind.None
                };
                if (refKind == RefKind.None)
                {
                    AddValue(argument.Expression);
                    continue;
                }

                var root = AddLocation(argument.Expression);
                if (refKind is RefKind.Ref or RefKind.Out)
                {
                    references.Add(root);
                }
            }

            if (!IsScalarMath(method))
            {
                Reads.Memory = true;
            }

            if (effect == DeviceFunctionEffect.None)
            {
                return;
            }

            foreach (var root in references)
            {
                AddWrite(root);
            }

            if (receiver is { } written)
            {
                AddWrite(written);
            }

            if (effect == DeviceFunctionEffect.Memory)
            {
                Writes.Memory = true;
            }
        }

        // A pure intrinsic whose parameters are all values without memory behind them (Math.Max, MathF.Sqrt ...)
        private static bool IsScalarMath(IMethodSymbol method) =>
            method.DeclaringSyntaxReferences.Length == 0 &&
            DeviceFunctionEffects.IsPureIntrinsic(method) &&
            method.Parameters
                .AsStackEnumerable()
                .All(static p => p.RefKind == RefKind.None && p.Type is { IsValueType: true } and not IPointerTypeSymbol &&
                                 !p.Type.IsArrayLikeType());

        private static IParameterSymbol? FindParameter(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments,
            int index)
        {
            if (arguments[index].NameColon is { } nameColon)
            {
                return method.Parameters
                    .AsStackEnumerable()
                    .FirstOrDefault(nameColon.Name.Identifier.ValueText, static (p, name) => p.Name == name);
            }

            var parameters = method.Parameters;
            if (index < parameters.Length)
            {
                return parameters[index];
            }

            return parameters.Length > 0 && parameters[parameters.Length - 1].IsParams
                ? parameters[parameters.Length - 1]
                : null;
        }
    }
}
