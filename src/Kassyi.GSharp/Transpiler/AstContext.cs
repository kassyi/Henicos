using System.Collections.Generic;
using Kassyi.GSharp.Transpiler.Ast;

namespace Kassyi.GSharp.Transpiler;

public class AstContext
{
    public AstBuilder Builder { get; }
    public HashSet<string> LocalVariables { get; } = new();
    
    private int _inlineCounter = 0;
    
    public AstContext(AstBuilder builder)
    {
        Builder = builder;
    }
    
    public string GetNextInlinePrefix()
    {
        _inlineCounter++;
        return $"inline_{_inlineCounter}_";
    }
}
