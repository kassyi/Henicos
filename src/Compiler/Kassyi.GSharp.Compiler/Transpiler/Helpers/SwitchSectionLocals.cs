namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>Finds the locals that a switch section declares and another section of the same switch uses (#795).</summary>
/// <remarks>
/// [WHY] In C# the switch block is one scope: a local declared in one section can be assigned and read in a later
/// section, and the jump to that section skips the declaration (definite assignment makes sure that the section assigns
/// it before reading it; a const needs no assignment). The C++ braces each section, so such a local is declared in an
/// extra block around the switch, <c>{ &lt;declarations&gt; switch (...) { ... } }</c>, and its declaration in the section
/// becomes an assignment. No jump crosses the declaration there, so a type with a C++ default constructor (a struct with
/// an array field) and an array alias work too. A const is declared there with its initializer and type
/// (<c>const &lt;type&gt; &lt;name&gt; = &lt;value&gt;;</c>) and writes nothing in its section; so is a const that such a const
/// reads. A local that cannot be declared before its initialization (a span, a ref local, a stack array) is GSH0003.
/// Roslyn binds the switch block the same way: one scope (its SwitchBinder) declares the locals of every section.
/// </remarks>
internal static class SwitchSectionLocals
{
    /// <summary>
    /// Whether the block around the switch of <paramref name="declarator"/> declares it: a local declared directly in
    /// a switch section that another section names, or a const that such a const reads.
    /// </summary>
    public static bool IsDeclaredBeforeSwitch(VariableDeclaratorSyntax declarator, SemanticModel semanticModel) =>
        declarator.Parent is VariableDeclarationSyntax
        {
            Parent: LocalDeclarationStatementSyntax { Parent: SwitchSectionSyntax { Parent: SwitchStatementSyntax } section }
                statement
        } &&
        IsDeclaredBeforeSwitch(declarator, statement, section, semanticModel);

    /// <summary>
    /// Reports the shared locals of <paramref name="switchStatement"/> that cannot be declared before it, and returns
    /// whether it needs the block that declares the others (<see cref="WriteDeclarations"/>).
    /// </summary>
    public static bool ReportAndFindSharedLocals(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        var declaresAny = false;
        foreach (var (section, declaration, declarator) in new SectionDeclarators(switchStatement))
        {
            if (IsDeclaredBeforeSwitch(declarator, declaration, section, model))
            {
                declaresAny = true;
                continue;
            }

            // A const can always be declared before the switch, so only a variable is left here
            if (!declaration.IsConst && IsNamedInOtherSection(declarator, section, model))
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, declarator,
                    GpuCodeConstraints.SwitchSectionLocalNotDeclarable);
            }
        }

        return declaresAny;
    }

    /// <summary>
    /// Declares the locals that the sections of <paramref name="switchStatement"/> share, in the order of the source so
    /// that a const follows the consts it reads: <c>&lt;type&gt; &lt;name&gt;;</c> or
    /// <c>const &lt;type&gt; &lt;name&gt; = &lt;value&gt;;</c>.
    /// </summary>
    public static void WriteDeclarations(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        foreach (var (section, declaration, declarator) in new SectionDeclarators(switchStatement))
        {
            if (!IsDeclaredBeforeSwitch(declarator, declaration, section, model))
            {
                continue;
            }

            if (declaration.IsConst)
            {
                context.Emitter.Writer.Append("const ");
            }

            context.Emitter.Dialect.AppendType(ref context, model.GetTypeSymbol(declaration.Declaration.Type));
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
            if (declaration.IsConst)
            {
                DeclarationTranspilerHelper.WriteDeclaratorInitializer(declarator, ref context);
            }

            context.Emitter.Writer.AppendLine(";");
        }
    }

    // The one condition for a local that the block around the switch declares; ReportAndFindSharedLocals,
    // WriteDeclarations and the public overload all read it, so they agree on the same locals
    private static bool IsDeclaredBeforeSwitch(VariableDeclaratorSyntax declarator,
        LocalDeclarationStatementSyntax declaration, SwitchSectionSyntax section, SemanticModel model)
    {
        if (!declaration.IsConst)
        {
            return IsNamedInOtherSection(declarator, section, model) &&
                   CanDeclareBeforeSwitch(declaration, declarator, model);
        }

        if (IsNamedInOtherSection(declarator, section, model))
        {
            return true;
        }

        // A const declared before the switch is written ahead of this section, so a const of this section that it
        // reads goes there too (a const of another section that reads it names it from another section, above)
        var index = section.Statements.IndexOf(declaration);
        var local = LocalIdentity.Of(declarator, model);
        for (var i = index; i < section.Statements.Count; i++)
        {
            if (section.Statements[i] is not LocalDeclarationStatementSyntax { IsConst: true } later)
            {
                continue;
            }

            foreach (var reader in later.Declaration.Variables)
            {
                if (reader != declarator &&
                    reader.Initializer is { } initializer &&
                    initializer
                        .DescendantNodes()
                        .AsStackEnumerable()
                        .Any(local, static (node, l) => l.IsReferencedBy(node)) &&
                    IsDeclaredBeforeSwitch(reader, later, section, model))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A ref local, a span (a ref struct) and a stack array (a C++ array, which cannot be assigned) need their
    // initializer at the declaration; shared memory, WMMA types and iterator locals have forms of their own
    private static bool CanDeclareBeforeSwitch(LocalDeclarationStatementSyntax declaration,
        VariableDeclaratorSyntax declarator, SemanticModel model) =>
        declaration.Declaration.Type is not RefTypeSyntax &&
        model.GetDeclaredSymbol<ISymbol>(declarator) is ILocalSymbol { IsRef: false } local &&
        local.Type is not { TypeKind: TypeKind.Error } &&
        local.Type is IArrayTypeSymbol or IPointerTypeSymbol or { TypeKind: TypeKind.Enum or TypeKind.Struct } &&
        !local.Type.IsRefLikeType &&
        !local.Type.IsSharedMemoryType() &&
        !local.Type.IsWmmaFragmentType() &&
        !local.Type.IsWmmaMarkerType() &&
        !StackArrayLocals.IsStackArray(local, out _);

    // C# forbids two locals of one name whose scopes overlap (CS0128, CS0136), so a matching name in another section
    // is a reference to this local unless it binds to something else (a member name, a lambda parameter)
    private static bool IsNamedInOtherSection(VariableDeclaratorSyntax declarator, SwitchSectionSyntax section,
        SemanticModel semanticModel) =>
        section.Parent is SwitchStatementSyntax switchStatement &&
        switchStatement.Sections
            .AsStackEnumerable()
            .Where(section, static (other, declaring) => other != declaring)
            .Any(LocalIdentity.Of(declarator, semanticModel), static (other, l) => other.DescendantNodes()
                .AsStackEnumerable()
                .Any(l, static (node, l) => l.IsReferencedBy(node))
            );

    // Each declarator of a local declaration directly in a section of the switch, with its section and statement:
    // the one walk that ReportAndFindSharedLocals and WriteDeclarations share
    private struct SectionDeclarators(SwitchStatementSyntax switchStatement)
    {
        private int _section;
        private int _statement = -1;
        private int _declarator = -1;

        public (SwitchSectionSyntax Section, LocalDeclarationStatementSyntax Declaration, VariableDeclaratorSyntax Declarator)
            Current { get; private set; }

        public readonly SectionDeclarators GetEnumerator() => this;

        public bool MoveNext()
        {
            var sections = switchStatement.Sections;
            while (_section < sections.Count)
            {
                var section = sections[_section];
                var statements = section.Statements;
                if (_statement >= 0 &&
                    statements[_statement] is LocalDeclarationStatementSyntax declaration &&
                    ++_declarator < declaration.Declaration.Variables.Count)
                {
                    Current = (section, declaration, declaration.Declaration.Variables[_declarator]);
                    return true;
                }

                _declarator = -1;
                if (++_statement < statements.Count)
                {
                    continue;
                }

                _statement = -1;
                _section++;
            }

            return false;
        }
    }

    private readonly struct LocalIdentity(string name, ISymbol? symbol, int region, SemanticModel semanticModel)
    {
        public static LocalIdentity Of(VariableDeclaratorSyntax declarator, SemanticModel semanticModel) =>
            new(
                declarator.Identifier.ValueText,
                semanticModel.GetDeclaredSymbol<ISymbol>(declarator),
                LoweringIdentity.GetRegion(declarator),
                semanticModel
            );

        public bool IsReferencedBy(SyntaxNode node) =>
            node is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == name &&
            LoweringIdentity.GetRegion(identifier) == region &&
            (symbol == null || semanticModel.GetSymbol(identifier).IsEqualTo(symbol));
    }
}
