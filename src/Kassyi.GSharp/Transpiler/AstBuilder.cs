using System.Collections.Frozen;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Kassyi.GSharp.Transpiler.Ast;
using Kassyi.GSharp.Transpiler.Handlers;

namespace Kassyi.GSharp.Transpiler;

public class AstBuilder
{
    private static readonly FrozenDictionary<Code, IOpCodeHandler> s_handlers;

    static AstBuilder()
    {
        var dict = new Dictionary<Code, IOpCodeHandler>();
        var handlers = new IOpCodeHandler[]
        {
            new LoadArgumentHandler(),
            new LoadConstantHandler(),
            new BinaryOpHandler(),
            new StoreArgumentHandler(),
            new LocalVariableHandler(),
            new ArrayHandler(),
            new MethodCallHandler(),
            new BranchHandler()
        };

        foreach (var handler in handlers)
        {
            foreach (var code in handler.SupportedOpCodes)
            {
                dict[code] = handler;
            }
        }
        s_handlers = dict.ToFrozenDictionary();
    }

    public BlockNode ParseMethod(MethodDefinition method)
    {
        var statements = new List<IAstNode>();

        if (!method.HasBody)
        {
            return new BlockNode(statements);
        }

        var instructions = method.Body.Instructions;
        var stack = new Stack<IAstNode>();

        // Pre-pass: find all branch targets
        var branchTargets = new HashSet<int>();
        foreach (var ins in instructions)
        {
            if (ins.Operand is Instruction target)
            {
                branchTargets.Add(target.Offset);
            }
            else if (ins.Operand is Instruction[] targets)
            {
                foreach (var t in targets)
                {
                    branchTargets.Add(t.Offset);
                }
            }
        }

        for (var i = 0; i < instructions.Count; i++)
        {
            var ins = instructions[i];

            if (branchTargets.Contains(ins.Offset))
            {
                statements.Add(new LabelNode($"IL_{ins.Offset:x4}"));
            }

            if (ins.OpCode.Code == Code.Nop || ins.OpCode.Code == Code.Ret)
            {
                continue;
            }

            if (s_handlers.TryGetValue(ins.OpCode.Code, out var handler))
            {
                handler.Handle(ins, stack, statements, method);
            }
            else
            {
                throw new NotSupportedException($"Unsupported IL OpCode: {ins.OpCode}");
            }
        }

        return new BlockNode(statements);
    }
}


