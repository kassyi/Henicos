using Kassyi.GSharp.Compiler.Transpiler.Frames;

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
        Dictionary<DeclarationKey, ManagedContextField> UnhoistedLocals,
        Dictionary<DeclarationKey, string> RenamedLocals, ImmutableArray<FrameSlot> Slots)
        ProcessContextFields(
            IReadOnlyList<VariableDeclaratorSyntax>? contextFields,
            SemanticModel semanticModel,
            string kernelName,
            SyntaxNode loweredBody)
    {
        var regularFields = ImmutableArray.CreateBuilder<ManagedContextField>();
        var localInits = ImmutableArray.CreateBuilder<ManagedContextField>();
        var unhoistedLocals = new Dictionary<DeclarationKey, ManagedContextField>();
        var renamedLocals = new Dictionary<DeclarationKey, string>();
        var slots = ImmutableArray.CreateBuilder<FrameSlot>();

        if (contextFields == null)
        {
            return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedLocals, renamedLocals,
                ImmutableArray<FrameSlot>.Empty);
        }

        var sharedNames = FindSharedNames(contextFields);
        var bodyNames = FindDeclaredNames(loweredBody);
        foreach (var field in contextFields)
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
            {
                continue;
            }

            // A held ref local gets no field: the state machine only throws (LoweredStateMachine.HeldRefLocals).
            // Its type comes from the local, not HoistedFieldTypeResolver, which throws for some ref locals.
            if (RefLocalLiveness.IsRefType(declaration.Type))
            {
                var heldRefName = field.Identifier.ValueText;
                var heldRefType = semanticModel.GetDeclaredSymbol<ILocalSymbol>(field)?.Type
                                  ?? throw new InvalidOperationException(
                                      $"The held ref local '{heldRefName}' of '{kernelName}' has no symbol.");
                slots.Add(new FrameSlot(FrameSlotKind.HeldRef, heldRefType, heldRefName.ToCSharpIdentifier(), null, 0,
                    null, new ManagedSlotSpelling(null, false, null)));
                continue;
            }

            var key = LoweringIdentity.ForDeclaration(field, semanticModel);
            var contextField = CreateField(field, declaration, key, sharedNames, renamedLocals, semanticModel, kernelName);
            var writtenType = GetWrittenType(contextField.DeclaredType);
            var namesPointer = NamesPointer(contextField.DeclaredType);

            if (!ResumeLocals.IsRedeclared(field, contextField.ResolvedType, sharedMemoryInFrame: false))
            {
                regularFields.Add(contextField);
                slots.Add(new FrameSlot(FrameSlotKind.Field, contextField.ResolvedType, contextField.Name, null, 0,
                    null, new ManagedSlotSpelling(writtenType, namesPointer, null)));
                continue;
            }

            contextField = RenameIfTheBodyDeclares(contextField, key, bodyNames, renamedLocals);
            var resumeLocal = CreateResumeLocal(contextField, semanticModel);
            if (key.Symbol != null)
            {
                unhoistedLocals[key] = resumeLocal;
            }

            localInits.Add(resumeLocal);

            FrameSlotKind slotKind;
            ITypeSymbol? slotElementType = null;
            var slotLength = 0;
            string? slotInitializer = null;

            if (resumeLocal.Resume is { } resume)
            {
                switch (resume.Kind)
                {
                    case ResumeLocalKind.Initialized:
                        slotKind = FrameSlotKind.SharedMemory;
                        slotInitializer = resumeLocal.Declarator.Initializer is { } init
                            ? ManagedStateMachineEmitter.ToSource(init.WithoutTrivia())
                            : null;
                        break;
                    case ResumeLocalKind.StackAlloc:
                        slotKind = FrameSlotKind.RedeclaredStack;
                        slotElementType = resume.ElementType;
                        slotLength = resume.Length;
                        break;
                    case ResumeLocalKind.Bound:
                        slotKind = FrameSlotKind.Redeclared;
                        break;
                    case ResumeLocalKind.Unsupported:
                        slotKind = FrameSlotKind.Unsupported;
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected resume local kind: {resume.Kind}");
                }
            }
            else
            {
                throw new InvalidOperationException($"Missing resume description for '{resumeLocal.Name}'");
            }

            slots.Add(new FrameSlot(slotKind, contextField.ResolvedType, resumeLocal.Name, slotElementType, slotLength,
                null, new ManagedSlotSpelling(writtenType, namesPointer, slotInitializer)));
        }

        return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedLocals, renamedLocals,
            slots.ToImmutable());
    }

    // The type as written, for the managed writer; null for var, which it spells from the resolved type. Without
    // trivia: a comment above the original declaration would otherwise land inside the emitted one
    private static string? GetWrittenType(TypeSyntax declaredType) =>
        declaredType.IsVar ? null : ManagedStateMachineEmitter.ToSource(declaredType.WithoutTrivia());

    // Whether the written type holds a pointer anywhere (int*[] included), which makes the frame unsafe
    private static bool NamesPointer(TypeSyntax declaredType) =>
        declaredType.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static n => n is PointerTypeSyntax or FunctionPointerTypeSyntax);

    // The field of a hoisted local: named after the local, or after its C++ field when another hoisted local shares the
    // name (recorded in renamedLocals)
    private static ManagedContextField CreateField(VariableDeclaratorSyntax field, VariableDeclarationSyntax declaration,
        DeclarationKey key, HashSet<string> sharedNames, Dictionary<DeclarationKey, string> renamedLocals,
        SemanticModel semanticModel, string kernelName)
    {
        var name = PlanFieldName(field.Identifier.ValueText, key.Symbol, key.Region,
            sharedNames.Contains(field.Identifier.ValueText), kernelName);
        if (name.Renamed)
        {
            renamedLocals[key] = name.Name;
        }

        // The field is declared in the generated file, which has none of the user's using directives
        return new ManagedContextField(field,
            ManagedNameQualifier.Qualify(declaration.Type, semanticModel),
            HoistedFieldTypeResolver.Resolve(field, semanticModel, kernelName), name.Name);
    }

    /// <summary>
    /// Decides the field name for a hoisted local in a managed context.
    /// </summary>
    internal static ManagedFieldName PlanFieldName(string writtenName, ISymbol? symbol, int region, bool sharesName,
        string kernelName)
    {
        if (sharesName)
        {
            if (symbol == null)
            {
                throw new InvalidOperationException(
                    $"The hoisted local '{writtenName}' of '{kernelName}' shares its name with another one and " +
                    "does not resolve to a symbol.");
            }

            return new ManagedFieldName(GetDistinctName(symbol, region), Renamed: true);
        }

        return new ManagedFieldName(writtenName.ToCSharpIdentifier(), Renamed: false);
    }

    /// <summary>
    /// Decides whether a resume local needs a distinct name to avoid colliding with a local declared in the body.
    /// </summary>
    internal static ManagedFieldName PlanResumeLocalName(ManagedFieldName field, string writtenName, ISymbol? symbol,
        int region, bool declaredInBody)
    {
        if (symbol == null || field.Renamed || !declaredInBody)
        {
            return field;
        }

        return new ManagedFieldName(GetDistinctName(symbol, region), Renamed: true);
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

    // [WHY] A resume local is declared at the top of the state function, so a local of the same name declared in a
    // nested scope of the body (one that no barrier crosses, a sibling of the hoisted one) is CS0136. The check is by
    // name over the whole body, so a same-named local in an unrelated scope also renames the resume local; the new
    // name is the one a hoisted local that shares its name gets (GetDistinctName)
    private static ManagedContextField RenameIfTheBodyDeclares(ManagedContextField contextField, DeclarationKey key,
        HashSet<string> bodyNames, Dictionary<DeclarationKey, string> renamedLocals)
    {
        var planned = PlanResumeLocalName(
            new ManagedFieldName(contextField.Name, renamedLocals.ContainsKey(key)),
            contextField.Declarator.Identifier.ValueText,
            key.Symbol,
            key.Region,
            bodyNames.Contains(contextField.Declarator.Identifier.ValueText));

        if (planned.Renamed && !renamedLocals.ContainsKey(key))
        {
            renamedLocals[key] = planned.Name;
            return contextField with { Name = planned.Name };
        }

        return contextField;
    }

    // The names that the locals left in the body, the parameters of its lambdas and its local functions declare. A
    // catch variable and a query range variable cannot occur: a kernel rejects try / catch
    // (ExceptionHandlingInKernelProhibited) and query expressions (GSH0003)
    private static HashSet<string> FindDeclaredNames(SyntaxNode body)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in body.DescendantNodes())
        {
            if (GetDeclaredIdentifier(node) is { } identifier)
            {
                names.Add(identifier.ValueText);
            }
        }

        return names;
    }

    // The identifier a node declares in the body, or null for a node that declares none
    internal static SyntaxToken? GetDeclaredIdentifier(SyntaxNode node) =>
        node switch
        {
            VariableDeclaratorSyntax declarator => declarator.Identifier,
            SingleVariableDesignationSyntax designation => designation.Identifier,
            ForEachStatementSyntax forEach => forEach.Identifier,
            ParameterSyntax parameter => parameter.Identifier,
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier,
            _ => null
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

/// <summary>The name of a hoisted local's field or resume local in a managed context.</summary>
/// <param name="Name">The name to write.</param>
/// <param name="Renamed">Whether <paramref name="Name"/> is the distinct name rather than the local's own.</param>
internal readonly record struct ManagedFieldName(string Name, bool Renamed);
