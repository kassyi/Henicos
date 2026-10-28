namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// A hoisted context field with the type that <see cref="HoistedFieldTypeResolver"/> resolved for it and its name in
/// the managed state machine; for a local declared again on every resume, how it is declared (<see cref="ResumeLocals"/>).
/// </summary>
internal readonly record struct ManagedContextField(
    VariableDeclaratorSyntax Declarator,
    TypeSyntax DeclaredType,
    ITypeSymbol ResolvedType,
    string Name,
    ResumeLocal? Resume = null)
{
    /// <summary>The managed stackalloc storage that a resume local is bound to where it is declared.</summary>
    public string StorageName => GSharpInternalNames.Lowering.ResumeStoragePrefix + Name.TrimStart('@');
}

/// <summary>
/// Discovers, validates, and names the hoisted fields for a managed execution context class.
/// </summary>
internal static class ManagedContextFields
{
    private static readonly StandardCppDialect s_cppDialect = new();

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
            string kernelName)
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
        foreach (var field in contextFields)
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
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
