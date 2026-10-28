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

    /// <summary>The variables of this set and of <paramref name="other"/>, in a set of its own.</summary>
    public readonly VariableSet Union(in VariableSet other)
    {
        var union = new VariableSet { Memory = Memory || other.Memory, AllLocals = AllLocals || other.AllLocals };
        union.AddAll(_variables);
        union.AddAll(other._variables);
        return union;
    }

    private void AddAll(ImmutableArray<object>.Builder? variables)
    {
        if (variables == null)
        {
            return;
        }

        foreach (var variable in variables)
        {
            Add(variable);
        }
    }

    /// <summary>Whether a variable of this set may be the same as a variable of <paramref name="other"/>.</summary>
    public readonly bool Overlaps(in VariableSet other)
    {
        if ((Memory && other.Memory) || (AllLocals && other.HasVariables) || (other.AllLocals && HasVariables))
        {
            return true;
        }

        return _variables != null &&
               _variables
                   .AsStackEnumerable()
                   .Any(other, static (variable, set) => set.Contains(variable));
    }

    /// <summary>Whether the set names <paramref name="variable"/> (a symbol or a name).</summary>
    public readonly bool Contains(object variable) =>
        _variables != null &&
        _variables
            .AsStackEnumerable()
            .Any(variable, static (existing, v) => existing is ISymbol symbol
                ? v is ISymbol other && symbol.IsEqualTo(other)
                : existing.Equals(v));
}

/// <summary>
/// What evaluating an expression reads (<see cref="Reads"/>) and writes (<see cref="Writes"/>), as far as the order of
/// the operands of an enclosing expression can tell (<see cref="EvaluationOrder"/>).
/// </summary>
/// <remarks>
/// <para>
/// A local or by-value parameter is one variable; every other location (array and span elements, pointers, shared
/// memory, <c>ref</c> / <c>out</c> / <c>in</c> parameters, ref locals, static fields, fields of <c>this</c>) is
/// <see cref="VariableSet.Memory"/>. A field of a struct local is the local. A local or parameter that has another name
/// (its address is taken, a <c>ref</c> expression names it, or a lambda or local function captures it) is
/// <see cref="VariableSet.Memory"/> too, since it can be read and written without its name. A call reads <see cref="VariableSet.Memory"/>
/// (its callee may read anything) unless it is pure math over scalars, and writes what
/// <see cref="DeviceFunctionEffects"/> says: nothing, the variables of its <c>ref</c> / <c>out</c> arguments, or
/// <see cref="VariableSet.Memory"/>. Evaluating a <c>ref</c> / <c>out</c> / <c>in</c> argument only names its location
/// (<see cref="OfLocation"/>); a nested call reads the variable of its <c>ref</c> / <c>in</c> arguments and writes the
/// variable of its <c>ref</c> / <c>out</c> arguments as part of its own evaluation. A lambda is not evaluated.
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

    /// <summary>
    /// The variable that the location <paramref name="expression"/> belongs to, as a read of it alone (without the reads
    /// of its indices and receivers): what reading the target of a compound assignment reads.
    /// </summary>
    public static VariableSet RootOf(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new Walker(model);
        var root = walker.AddLocation(expression);
        var reads = default(VariableSet);
        if (root.Variable is { } variable)
        {
            reads.Add(variable);
        }
        else
        {
            reads.Memory = true;
        }

        return reads;
    }

    /// <summary>
    /// The effects of a call of <paramref name="target"/> whose arguments are already evaluated, with
    /// <paramref name="referenceArgument"/> (a local or parameter, or <see langword="null"/>) passed by <c>ref</c>: what
    /// evaluating an invocation adds for the call itself. An operator macro expansion uses it for the call of an inner
    /// operator's target with its context.
    /// </summary>
    public static ExpressionEffects OfCall(IMethodSymbol target, ISymbol? referenceArgument, SemanticModel model)
    {
        var walker = new Walker(model);
        walker.AddCallOf(target, referenceArgument);
        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>What evaluating this and <paramref name="other"/> reads and writes.</summary>
    public ExpressionEffects Union(in ExpressionEffects other) =>
        new(Reads.Union(other.Reads), Writes.Union(other.Writes));

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

            // A user-defined conversion of the value (implicit, or the operator of a cast) is a call
            if (model.GetSafeConversion(expression) is { IsUserDefined: true, MethodSymbol: { } conversion })
            {
                AddConversionCall(conversion);
            }

            if (expression is CastExpressionSyntax &&
                model.GetSymbol(expression) is IMethodSymbol { MethodKind: MethodKind.Conversion } castOperator)
            {
                AddConversionCall(castOperator);
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
                case WithExpressionSyntax with:
                    // The members that the initializer assigns belong to the copy
                    AddValue(with.Expression);
                    AddInitializer(with.Initializer);
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
            if (model.GetSymbol(operand) is IPropertySymbol property && StructAbiMembers.IsComputedProperty(property))
            {
                // The getter and the setter may read and write anything
                Reads.Memory = true;
                Writes.Memory = true;
            }
        }

        // A conversion operator reads what it may and writes what DeviceFunctionEffects says
        private void AddConversionCall(IMethodSymbol conversion)
        {
            Reads.Memory = true;
            if (DeviceFunctionEffects.Classify(conversion, model.Compilation) == DeviceFunctionEffect.Memory)
            {
                Writes.Memory = true;
            }
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
            if (IsFrameAccess(access))
            {
                AddRead(AddLocation(access));
                return;
            }

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

        // __gsharp_ctx-><local>: ContextHoister moves a local (or a lowering temporary) of a state machine into its frame,
        // and the member keeps the span of the local
        private static bool IsFrameAccess(MemberAccessExpressionSyntax access) =>
            access.Expression is IdentifierNameSyntax { Identifier.ValueText: GSharpInternalNames.StateMachine.Context };

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
                case MemberAccessExpressionSyntax frame when IsFrameAccess(frame):
                    // A local of the state machine that lives in its frame: the local itself
                    return AddLocation(frame.Name);
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation }:
                    return model.GetDeclaredSymbol<ILocalSymbol>(designation) is { } declared
                        ? RootOfVariable(declared)
                        : new Root(designation.Identifier.ValueText);
                case IdentifierNameSyntax identifier:
                    return model.GetSymbol(identifier) is { } symbol
                        ? RootOfSymbol(symbol)
                        : new Root(identifier.Identifier.ValueText);
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

        // A non-ref local or by-value parameter as itself (RootOfVariable), anything else as memory
        private Root RootOfSymbol(ISymbol symbol) => symbol switch
        {
            ILocalSymbol { IsRef: false } local => RootOfVariable(local),
            IParameterSymbol { RefKind: RefKind.None } parameter => RootOfVariable(parameter),
            _ => new Root(null)
        };

        // A local or by-value parameter, or memory when it has another name (AliasedVariables)
        private Root RootOfVariable(ISymbol variable) =>
            AliasedVariables.Contains(variable, model.Compilation) ? new Root(null) : new Root(variable);

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

            var declaringModel = model.Compilation.GetCachedSemanticModel(declarator.SyntaxTree);
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
                    _ => method.FindParameter(arguments, i)?.RefKind ?? RefKind.None
                };
                if (refKind == RefKind.None)
                {
                    AddValue(argument.Expression);
                    continue;
                }

                var root = AddLocation(argument.Expression);

                // The callee reads through a ref / in / ref readonly parameter, as part of this call
                if (refKind is not RefKind.Out)
                {
                    AddRead(root);
                }

                if (refKind is RefKind.Ref or RefKind.Out)
                {
                    references.Add(root);
                }
            }

            AddCallItself(method, references, receiver, effect);
        }

        // A call of <method> with <reference> (a variable, or null) passed by ref, after its arguments
        public void AddCallOf(IMethodSymbol method, ISymbol? reference)
        {
            var references = ImmutableArray.CreateBuilder<Root>();
            if (reference != null)
            {
                // The callee reads and writes through the ref parameter
                var root = RootOfSymbol(reference);
                AddRead(root);
                references.Add(root);
            }

            AddCallItself(method, references, receiver: null, DeviceFunctionEffects.Classify(method, model.Compilation));
        }

        // What the call itself reads and writes: memory unless it is pure math over scalars, and what
        // DeviceFunctionEffects says it writes (its ref / out arguments and receiver, or memory)
        private void AddCallItself(IMethodSymbol method, ImmutableArray<Root>.Builder references, Root? receiver,
            DeviceFunctionEffect effect)
        {
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
    }
}

/// <summary>
/// The locals and parameters of a member that have another name besides their own: their address is taken
/// (<c>&amp;x</c>), a <c>ref</c> expression names them (<c>ref int r = ref x;</c>, <c>return ref x;</c>), or a lambda or
/// local function captures them (the lambda of a Gpu macro such as <c>Gpu.ForUnroll</c> is expanded in place and
/// captures nothing). A write through the other name changes them, so <see cref="ExpressionEffects"/> treats
/// them as memory.
/// </summary>
/// <remarks>
/// <para>
/// Computed once per member declaration (the outermost one, which holds its lambdas and local functions) and cached for
/// the life of its syntax node.
/// </para>
/// <para>
/// [WHY] The cache holds the spans of the declarations, not their symbols: Roslyn reuses the syntax tree of an unchanged
/// file in the next compilation, where the symbols are new objects, so a cache of symbols would answer false for every
/// later compilation (and keep the old one alive). A span is the same in every compilation that shares the tree.
/// </para>
/// </remarks>
internal static class AliasedVariables
{
    private static readonly ConditionalWeakTable<SyntaxNode, HashSet<TextSpan>> s_aliased = new();

    /// <summary>Whether <paramref name="variable"/>, a local or parameter declared in source, has another name.</summary>
    public static bool Contains(ISymbol variable, Compilation compilation)
    {
        if (variable.DeclaringSyntaxReferences.Length == 0 ||
            variable.DeclaringSyntaxReferences[0] is not { } reference ||
            !compilation.ContainsSyntaxTree(reference.SyntaxTree) ||
            GetOutermostMember(reference.GetSyntax()) is not { } member)
        {
            return false;
        }

        return s_aliased.GetValue(member, m => Collect(m, compilation)).Contains(reference.Span);
    }

    // The outermost member that holds node. A top-level statement (GlobalStatementSyntax) counts as its own member;
    // device code never consists of top-level statements, so they are not followed further.
    private static SyntaxNode? GetOutermostMember(SyntaxNode node)
    {
        SyntaxNode? member = null;
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax)
            {
                member = current;
            }
        }

        return member;
    }

    private static HashSet<TextSpan> Collect(SyntaxNode member, Compilation compilation)
    {
        var model = compilation.GetCachedSemanticModel(member.SyntaxTree);
        var aliased = new HashSet<TextSpan>();
        foreach (var node in member.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case PrefixUnaryExpressionSyntax addressOf when addressOf.IsKind(SyntaxKind.AddressOfExpression):
                    AddRoot(addressOf.Operand, model, aliased);
                    break;
                case RefExpressionSyntax reference:
                    AddRoot(reference.Expression, model, aliased);
                    break;
                case InvocationExpressionSyntax invocation
                    when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { RefKind: not RefKind.None }:
                    // A call that returns by reference may return one of its ref arguments: Choose(ref x, ref y)
                    foreach (var argument in invocation.ArgumentList.Arguments)
                    {
                        if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword, SyntaxKind.InKeyword))
                        {
                            AddRoot(argument.Expression, model, aliased);
                        }
                    }

                    break;
                case AnonymousFunctionExpressionSyntax lambda when !IsMacroBody(lambda, model):
                    AddCaptured(lambda, model, aliased);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    AddCaptured(localFunction, model, aliased);
                    break;
            }
        }

        return aliased;
    }

    // The lambda of a Gpu macro (Gpu.ForUnroll, Gpu.GridStrideLoop ...), whose body the transpiler expands in place: it
    // is not a closure, so what it names is not captured
    private static bool IsMacroBody(AnonymousFunctionExpressionSyntax lambda, SemanticModel model) =>
        lambda.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation } &&
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol macro &&
        GpuMacroClassifier.Classify(macro).HasBody;

    // The locals and parameters declared outside function that its body names
    private static void AddCaptured(SyntaxNode function, SemanticModel model, HashSet<TextSpan> aliased) =>
        function.DescendantNodes()
            .AsStackEnumerable()
            .OfType<IdentifierNameSyntax>()
            .Select((Model: model, Function: function), static (identifier, s) =>
                s.Model.GetSymbolInfo(identifier).Symbol is { } symbol &&
                symbol is ILocalSymbol or IParameterSymbol &&
                !IsDeclaredIn(symbol, s.Function)
                    ? symbol
                    : null
            ).OfType<ISymbol>()
            .Do(aliased, static (symbol, set) => AddDeclaration(symbol, set));

    private static void AddDeclaration(ISymbol variable, HashSet<TextSpan> aliased)
    {
        if (variable.DeclaringSyntaxReferences.Length > 0)
        {
            aliased.Add(variable.DeclaringSyntaxReferences[0].Span);
        }
    }

    private static bool IsDeclaredIn(ISymbol symbol, SyntaxNode function) =>
        symbol.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(function, static (reference, f) => reference.SyntaxTree == f.SyntaxTree && f.Span.Contains(reference.Span));

    // The local or parameter that a location starts at: x, x.Field, x.Inline[i] (a struct holds its fields)
    private static void AddRoot(ExpressionSyntax expression, SemanticModel model, HashSet<TextSpan> aliased)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
                                                              model.GetTypeInfo(access.Expression).Type is
                                                                  { IsValueType: true }:
                    expression = access.Expression;
                    continue;
                case ElementAccessExpressionSyntax element when model.GetTypeInfo(element.Expression).Type is
                    { IsValueType: true }:
                    expression = element.Expression;
                    continue;
                case IdentifierNameSyntax identifier:
                    if (model.GetSymbolInfo(identifier).Symbol is { } variable &&
                        variable is ILocalSymbol or IParameterSymbol { RefKind: RefKind.None })
                    {
                        AddDeclaration(variable, aliased);
                    }

                    return;
                default:
                    return;
            }
        }
    }
}
