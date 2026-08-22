namespace Kassyi.GSharp.Transpiler.Ast;

public class AstContext(AstBuilder builder)
{
    public AstBuilder Builder { get; } = builder;
    public Dictionary<string, TypeReference> LocalVariables { get; } = new();

    private int _inlineCounter;

    public string GetNextInlinePrefix()
    {
        _inlineCounter++;
        return $"inline_{_inlineCounter}_";
    }
}
