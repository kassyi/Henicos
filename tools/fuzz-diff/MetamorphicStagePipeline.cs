using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Applies metamorphic rewrites one after another to a compilation unit, rebinding the semantic model after each
/// one, and keeps a rewrite only when it adds no compile error.
/// </summary>
internal sealed class MetamorphicStagePipeline
{
    private CSharpCompilation _compilation;
    private int _errorCount;

    private MetamorphicStagePipeline(CSharpCompilation compilation, CompilationUnitSyntax root, SemanticModel model)
    {
        _compilation = compilation;
        Root = root;
        Model = model;
        _errorCount = CountErrors(model);
    }

    /// <summary>Gets the compilation unit after the stages kept so far.</summary>
    internal CompilationUnitSyntax Root { get; private set; }

    /// <summary>Gets the semantic model of <see cref="Root"/>.</summary>
    internal SemanticModel Model { get; private set; }

    /// <summary>Starts a pipeline on <paramref name="source"/>.</summary>
    internal static MetamorphicStagePipeline Create(string source)
    {
        var compilation = MetamorphicTypeResolver.CreateCompilation(source, out var root, out var model);
        return new MetamorphicStagePipeline(compilation, root, model);
    }

    /// <summary>
    /// Runs <paramref name="stage"/> on <see cref="Root"/>. A result with more compile errors than <see cref="Root"/>
    /// is discarded, so one stage that breaks the code does not break the variant; the later stages see the code as
    /// it was before.
    /// </summary>
    /// <returns><see langword="true"/> when the stage changed the code and the change was kept.</returns>
    internal bool Apply(Func<CompilationUnitSyntax, SemanticModel, CompilationUnitSyntax> stage)
    {
        var nextRoot = stage(Root, Model);
        if (ReferenceEquals(nextRoot, Root))
        {
            return false;
        }

        var nextCompilation = MetamorphicTypeResolver.ReplaceSyntaxTree(
            _compilation,
            Root.SyntaxTree,
            nextRoot,
            out var reparsedRoot,
            out var nextModel);
        int nextErrorCount = CountErrors(nextModel);
        if (nextErrorCount > _errorCount)
        {
            return false;
        }

        _compilation = nextCompilation;
        Root = reparsedRoot;
        Model = nextModel;
        _errorCount = nextErrorCount;
        return true;
    }

    private static int CountErrors(SemanticModel model)
    {
        int count = 0;
        foreach (var diagnostic in model.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                count++;
            }
        }

        return count;
    }
}
