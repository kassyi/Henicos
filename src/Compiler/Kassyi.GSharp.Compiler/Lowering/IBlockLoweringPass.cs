namespace Kassyi.GSharp.Compiler.Lowering;

using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>A lowering pass that transforms a block into another block.</summary>
internal interface IBlockLoweringPass
{
    /// <summary>Rewrites <paramref name="block"/>.</summary>
    BlockSyntax Rewrite(BlockSyntax block);
}
