namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The name that a <c>SharedMemory&lt;T&gt;</c> declaration is written with: the name of the local it declares, or the
/// name the statement prelude gave to a creation that is not a local's initializer
/// (<see cref="SharedMemoryCreationLift"/>).
/// </summary>
internal readonly struct SharedMemoryName
{
    private readonly VariableDeclaratorSyntax? _declarator;
    private readonly string? _lifted;

    private SharedMemoryName(VariableDeclaratorSyntax? declarator, string? lifted)
    {
        _declarator = declarator;
        _lifted = lifted;
    }

    /// <summary>The name of the local that <paramref name="declarator"/> declares.</summary>
    public static SharedMemoryName Of(VariableDeclaratorSyntax declarator) => new(declarator, null);

    /// <summary>The generated <paramref name="name"/> of a creation outside a local declaration.</summary>
    public static SharedMemoryName Lifted(string name) => new(null, name);

    /// <summary>
    /// The declarator the report of a failure points at; <see langword="null"/> for a generated name.
    /// </summary>
    public VariableDeclaratorSyntax? Declarator => _declarator;

    public void Append(ref TranspileContext context)
    {
        if (_declarator != null)
        {
            CppNameResolver.AppendDeclaredName(_declarator, _declarator.Identifier, ref context);
        }
        else
        {
            context.Emitter.Writer.Append(_lifted ?? string.Empty);
        }
    }
}
