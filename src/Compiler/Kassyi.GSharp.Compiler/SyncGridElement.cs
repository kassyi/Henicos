namespace Kassyi.GSharp.Compiler;

/// <summary>A statement of a flattened <c>Gpu.SyncGrid(body)</c> and the body it was written in.</summary>
/// <param name="Node">
/// The statement, or the expression of an expression-bodied lambda (<c>Gpu.SyncGrid(() =&gt; x += 1)</c>).
/// </param>
/// <param name="BodyIndex">The index of the body in <see cref="SyncGridStatements.BodyParents"/>; 0 is the outermost.</param>
/// <param name="HasBodyReturn">
/// Whether the statement holds a <c>return</c> of its body (<see cref="SyncGridBody.ContainsBodyReturn"/>).
/// </param>
internal readonly record struct SyncGridElement(CSharpSyntaxNode Node, int BodyIndex, bool HasBodyReturn);
