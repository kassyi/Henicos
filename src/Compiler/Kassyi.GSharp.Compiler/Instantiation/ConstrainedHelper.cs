namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// A small generic method that the copy adds beside itself to call an instance member of an interface the way C# calls
/// it through a type parameter:
/// <c>R Call&lt;TS&gt;(ref TS receiver, ...) where TS : IFoo =&gt; receiver.M(...)</c>. The constrained call
/// runs the implementation that the generic method runs, an explicit one or a default one that the closed copy
/// cannot name, with no copy of the receiver and no boxing.
/// </summary>
/// <param name="ClosedType">The type argument the receiver has in the copy.</param>
/// <param name="Member">The interface method that the helper calls, as the instance reads it.</param>
internal sealed record ConstrainedHelper(ITypeSymbol ClosedType, IMethodSymbol Member);
