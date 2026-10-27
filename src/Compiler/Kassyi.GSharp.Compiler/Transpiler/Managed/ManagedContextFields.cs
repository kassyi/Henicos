namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// A hoisted context field with the type that <see cref="HoistedFieldTypeResolver"/> resolved for it and its name in
/// the managed state machine.
/// </summary>
internal readonly record struct ManagedContextField(
    VariableDeclaratorSyntax Declarator,
    TypeSyntax DeclaredType,
    ITypeSymbol ResolvedType,
    string Name);

/// <summary>
/// Discovers, validates, and names the hoisted fields for a managed execution context class.
/// </summary>
internal static class ManagedContextFields
{
    private static readonly StandardCppDialect s_cppDialect = new();

    /// <summary>
    /// Categorizes hoisted fields into regular context fields and unhoisted ref-struct fields initialized on the stack.
    /// </summary>
    /// <remarks>
    /// Declarations are keyed by <see cref="DeclarationKey"/> (symbol and region), so the copies of one declaration
    /// that an expansion stamped with different regions get fields of their own.
    /// </remarks>
    public static (ImmutableArray<ManagedContextField> LocalInits, ImmutableArray<ManagedContextField> RegularFields,
        HashSet<DeclarationKey> UnhoistedKeys, Dictionary<DeclarationKey, string> RenamedLocals)
        ProcessContextFields(
            IReadOnlyList<VariableDeclaratorSyntax>? contextFields,
            SemanticModel semanticModel,
            string kernelName)
    {
        var regularFields = ImmutableArray.CreateBuilder<ManagedContextField>();
        var localInits = ImmutableArray.CreateBuilder<ManagedContextField>();
        var unhoistedKeys = new HashSet<DeclarationKey>();
        var renamedLocals = new Dictionary<DeclarationKey, string>();

        if (contextFields == null)
        {
            return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedKeys, renamedLocals);
        }

        var sharedNames = FindSharedNames(contextFields);
        foreach (var field in contextFields)
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
            {
                continue;
            }

            var key = LoweringIdentity.ForDeclaration(field, semanticModel);
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
            var contextField = new ManagedContextField(field,
                ManagedNameQualifier.Qualify(declaration.Type, semanticModel),
                HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName), name);

            if (contextField.ResolvedType.IsRefLikeType)
            {
                if (key.Symbol != null)
                {
                    unhoistedKeys.Add(key);
                }

                // A ref struct local is declared again on every resume, so its initializer is emitted as is
                localInits.Add(contextField with
                {
                    Declarator = ManagedNameQualifier.Qualify(
                        SharedMemorySiteKeyAnnotator.AddSharedMemorySiteKeys(field, semanticModel),
                        semanticModel)
                });
            }
            else
            {
                regularFields.Add(contextField);
            }
        }

        return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedKeys, renamedLocals);
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
