namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>Finds the locals that a switch section declares and another section of the same switch uses (#795).</summary>
/// <remarks>
/// [WHY] In C# the switch block is one scope: a local declared in one section can be assigned and read in a later
/// section, and the jump to that section skips the initializer (definite assignment makes sure that the section assigns
/// it before reading it). The C++ braces each section, so such a local is declared before the first case label of the
/// switch, without an initializer, and its declaration in the section becomes an assignment, as for a declaration that
/// a <c>goto</c> jumps past (<see cref="GotoJumpAnalysis"/>). A type that cannot be declared first and assigned later
/// (an array, a span, a ref local) is reported. A const local of a section is not declared at all: every reference to
/// it is written as its value, so a section that the jump enters reads it too.
/// </remarks>
internal static class SwitchSectionLocals
{
    /// <summary>
    /// Whether <paramref name="declarator"/> declares a non-const local directly in a switch section that another
    /// section of the same switch names.
    /// </summary>
    public static bool IsSharedAcrossSections(VariableDeclaratorSyntax declarator, SemanticModel semanticModel) =>
        declarator.Parent is VariableDeclarationSyntax
        {
            Parent: LocalDeclarationStatementSyntax { Parent: SwitchSectionSyntax section } statement
        } &&
        !statement.IsConst &&
        section.Parent is SwitchStatementSyntax switchStatement &&
        IsNamedInOtherSection(declarator, section, switchStatement, semanticModel);

    /// <summary>
    /// Whether <paramref name="local"/> is a const local declared directly in a switch section: its references are
    /// written as its value and its declaration writes nothing.
    /// </summary>
    public static bool IsInlinedConstant(ILocalSymbol local) =>
        local is { IsConst: true, HasConstantValue: true } &&
        local.TryGetDeclaringSyntax(out VariableDeclaratorSyntax? declarator) &&
        declarator.Parent?.Parent is LocalDeclarationStatementSyntax { Parent: SwitchSectionSyntax };

    /// <summary>
    /// Declares the shared locals of <paramref name="switchStatement"/>'s sections before its first case label:
    /// <c>&lt;type&gt; &lt;name&gt;;</c> per local. A local whose type cannot be assigned after its declaration is reported.
    /// </summary>
    public static void WriteSharedDeclarations(SwitchStatementSyntax switchStatement, ref TranspileContext context)
    {
        foreach (var section in switchStatement.Sections)
        {
            foreach (var statement in section.Statements)
            {
                if (statement is not LocalDeclarationStatementSyntax { IsConst: false } declaration)
                {
                    continue;
                }

                var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(declaration.Declaration.Type);
                foreach (var declarator in declaration.Declaration.Variables)
                {
                    if (!IsNamedInOtherSection(declarator, section, switchStatement, context.Query.SemanticModel))
                    {
                        continue;
                    }

                    if (!Rules.DeclarationRules.VariableDeclarationTranspilerRule.IsAssignableAfterDeclaration(typeSymbol) ||
                        declaration.Declaration.Type is RefTypeSyntax)
                    {
                        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, declarator,
                            GpuCodeConstraints.SwitchSectionLocalNotSplittable);
                        continue;
                    }

                    // <type> <name>;
                    context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                    context.Emitter.Writer.Append(' ');
                    CppNameResolver.AppendDeclaredName(declarator, declarator.Identifier, ref context);
                    context.Emitter.Writer.AppendLine(";");
                }
            }
        }
    }

    // C# forbids two locals of one name whose scopes overlap (CS0128, CS0136), so a matching name in another section
    // is a reference to this local unless it binds to something else (a member name, a lambda parameter)
    private static bool IsNamedInOtherSection(VariableDeclaratorSyntax declarator, SwitchSectionSyntax section,
        SwitchStatementSyntax switchStatement, SemanticModel semanticModel)
    {
        var local = new LocalIdentity(
            declarator.Identifier.ValueText,
            semanticModel.GetDeclaredSymbol<ISymbol>(declarator),
            LoweringIdentity.GetRegion(declarator),
            semanticModel
        );
        return switchStatement.Sections
            .AsStackEnumerable()
            .Where(section, static (other, declaring) => other != declaring)
            .Any(local, static (other, l) => other.DescendantNodes()
                .AsStackEnumerable()
                .Any(l, static (node, l) => l.IsReferencedBy(node))
            );
    }

    private readonly struct LocalIdentity(string name, ISymbol? symbol, int region, SemanticModel semanticModel)
    {
        public bool IsReferencedBy(SyntaxNode node) =>
            node is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == name &&
            LoweringIdentity.GetRegion(identifier) == region &&
            (symbol == null || semanticModel.GetSymbol(identifier).IsEqualTo(symbol));
    }
}
