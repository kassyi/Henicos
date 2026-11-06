using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The input of one detector run: one file parsed for one set of preprocessor symbols.</summary>
/// <param name="Path">The file, relative to the repository root.</param>
/// <param name="Root">The root of the syntax tree.</param>
/// <param name="Text">The text of the file.</param>
internal sealed record DetectorContext(string Path, SyntaxNode Root, SourceText Text);
