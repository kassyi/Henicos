namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Discovers, validates, and names the hoisted fields for a managed execution context class.
/// </summary>
internal static class ManagedContextFields
{
    private static readonly StandardCppDialect s_cppDialect = new();

    /// <summary>
    /// <c>throw new global::System.NotSupportedException("...");</c>: the body of a state machine whose method holds
    /// the ref local <paramref name="refLocal"/> in its frame (<see cref="LoweredStateMachine.HeldRefLocals"/>).
    /// </summary>
    /// <remarks>
    /// [WHY] A class cannot hold a ref field (CS9059), and generated code that does not compile hides every analyzer
    /// diagnostic of the build; the frame class leaves the ref local out and the body only throws, behind the error
    /// that each held ref local is (GSH0039 or GSH0003, #1640).
    /// </remarks>
    public static string CreateHeldRefLocalThrow(string methodName, VariableDeclaratorSyntax refLocal) =>
        $"throw new global::System.NotSupportedException(\"{methodName} cannot run on the CPU: the ref local " +
        $"'{refLocal.Identifier.ValueText}' cannot be kept across a barrier or a call that may suspend " +
        $"({GSharpDiagnostics.CrossBarrierRefStructProhibited.Id}).\");";

    /// <summary>
    /// Categorizes hoisted fields into regular context fields and the ref struct and stackalloc pointer locals that are
    /// declared again at the top of the state function on every resume (<see cref="ResumeLocals"/>).
    /// </summary>
    /// <remarks>
    /// Declarations are keyed by <see cref="DeclarationKey"/> (symbol and region), so the copies of one declaration
    /// that an expansion stamped with different regions get fields of their own.
    /// </remarks>
    public static (ImmutableArray<ManagedContextField> LocalInits, ImmutableArray<ManagedContextField> RegularFields,
        Dictionary<DeclarationKey, ManagedContextField> UnhoistedLocals, Dictionary<DeclarationKey, string> RenamedLocals)
        ProcessContextFields(
            IReadOnlyList<VariableDeclaratorSyntax>? contextFields,
            SemanticModel semanticModel,
            string kernelName,
            SyntaxNode? loweredBody = null)
    {
        var regularFields = ImmutableArray.CreateBuilder<ManagedContextField>();
        var localInits = ImmutableArray.CreateBuilder<ManagedContextField>();
        var unhoistedLocals = new Dictionary<DeclarationKey, ManagedContextField>();
        var renamedLocals = new Dictionary<DeclarationKey, string>();

        if (contextFields == null)
        {
            return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedLocals, renamedLocals);
        }

        var sharedNames = FindSharedNames(contextFields);
        var bodyNames = loweredBody == null ? [] : FindDeclaredNames(loweredBody);
        foreach (var field in contextFields)
        {
            // A held ref local gets no field: the state machine only throws (LoweredStateMachine.HeldRefLocals)
            if (field.Parent is not VariableDeclarationSyntax declaration ||
                RefLocalLiveness.IsRefType(declaration.Type))
            {
                continue;
            }

            var key = LoweringIdentity.ForDeclaration(field, semanticModel);
            var contextField = CreateField(field, declaration, key, sharedNames, renamedLocals, semanticModel, kernelName);
            if (!ResumeLocals.IsRedeclared(field, contextField.ResolvedType, sharedMemoryInFrame: false))
            {
                regularFields.Add(contextField);
                continue;
            }

            // [WHY] A resume local is declared at the top of the state function, so a local of the same name declared
            // in a nested scope of the body (one that no barrier crosses, a sibling of the hoisted one) is CS0136
            if (key.Symbol != null && !renamedLocals.ContainsKey(key) &&
                bodyNames.Contains(field.Identifier.ValueText))
            {
                var distinctName = GetDistinctName(key.Symbol, key.Region);
                renamedLocals[key] = distinctName;
                contextField = contextField with { Name = distinctName };
            }

            var resumeLocal = CreateResumeLocal(contextField, semanticModel);
            if (key.Symbol != null)
            {
                unhoistedLocals[key] = resumeLocal;
            }

            localInits.Add(resumeLocal);
        }

        return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedLocals, renamedLocals);
    }

    // The field of a hoisted local: named after the local, or after its C++ field when another hoisted local shares the
    // name (recorded in renamedLocals)
    private static ManagedContextField CreateField(VariableDeclaratorSyntax field, VariableDeclarationSyntax declaration,
        DeclarationKey key, HashSet<string> sharedNames, Dictionary<DeclarationKey, string> renamedLocals,
        SemanticModel semanticModel, string kernelName)
    {
        var name = field.Identifier.ValueText.ToCSharpIdentifier();
        if (sharedNames.Contains(field.Identifier.ValueText))
        {
            if (key.Symbol == null)
            {
                throw new InvalidOperationException(
                    $"The hoisted local '{field.Identifier.ValueText}' of '{kernelName}' shares its name with another one and does not resolve to a symbol.");
            }

            name = GetDistinctName(key.Symbol, key.Region);
            renamedLocals[key] = name;
        }

        // The field is declared in the generated file, which has none of the user's using directives
        return new ManagedContextField(field,
            ManagedNameQualifier.Qualify(declaration.Type, semanticModel),
            HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName), name);
    }

    // A ref struct or stackalloc pointer local is declared again on every resume. A SharedMemory<T> keeps its
    // initializer there, which reads only constants and its site key; a dynamic one only finds its region there, and its
    // declaration places it (ContextLocalRewriter keeps that assignment)
    private static ManagedContextField CreateResumeLocal(ManagedContextField field, SemanticModel semanticModel) =>
        field with
        {
            Declarator = ManagedNameQualifier.Qualify(
                SharedMemorySiteKeyAnnotator.AddSharedMemorySiteKeys(field.Declarator, semanticModel, findOnly: true),
                semanticModel),
            Resume = ResumeLocals.Describe(field.Declarator, field.ResolvedType, semanticModel)
        };

    // The names that the locals left in the body, the parameters of its lambdas and its local functions declare
    private static HashSet<string> FindDeclaredNames(SyntaxNode body)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator:
                    names.Add(declarator.Identifier.ValueText);
                    break;
                case SingleVariableDesignationSyntax designation:
                    names.Add(designation.Identifier.ValueText);
                    break;
                case ForEachStatementSyntax forEach:
                    names.Add(forEach.Identifier.ValueText);
                    break;
                case ParameterSyntax parameter:
                    names.Add(parameter.Identifier.ValueText);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    names.Add(localFunction.Identifier.ValueText);
                    break;
            }
        }

        return names;
    }

    // Names that more than one hoisted local declares; such locals live in sibling scopes of the user's method, or are
    // copies of one declaration that an expansion stamped with different regions
    private static HashSet<string> FindSharedNames(IReadOnlyList<VariableDeclaratorSyntax> contextFields)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shared = new HashSet<string>(StringComparer.Ordinal);
        contextFields
            .AsStackEnumerable()
            .Select(static field => field.Identifier.ValueText)
            .AddIf(shared, seen, static (name, names) => !names.Add(name));
        return shared;
    }

    // [WHY] Every field of a context class needs its own name. A local that shares its name takes the name of its field
    // in the C++ context struct, which carries the declaration ordinal and the region; user identifiers cannot contain
    // '__' (GSH0055), so that name collides with nothing the user writes. A function-scope name does not depend on the
    // dialect.
    private static string GetDistinctName(ISymbol declared, int region) =>
        CppNameResolver.Resolve(declared, declared.Name, s_cppDialect, region).ToString();
}
