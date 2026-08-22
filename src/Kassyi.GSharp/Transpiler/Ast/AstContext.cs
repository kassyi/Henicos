using Kassyi.GSharp.Transpiler.Pipeline;

namespace Kassyi.GSharp.Transpiler.Ast;

public class AstContext(AstBuilder builder)
{
    public AstBuilder Builder { get; } = builder;
    public Dictionary<string, TypeReference> LocalVariables { get; } = new();
    public BlockNode? RootBlock { get; set; }
    public GpuKernelMetadata? Metadata { get; set; }

    private int _inlineCounter;

    public string GetNextInlinePrefix()
    {
        _inlineCounter++;
        return $"inline_{_inlineCounter}_";
    }
}
