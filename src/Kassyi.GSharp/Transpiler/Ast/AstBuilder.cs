namespace Kassyi.GSharp.Transpiler.Ast;

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
            new BranchHandler(),
            new LoadAddressHandler(),
            new IndirectLoadHandler(),
            new IndirectStoreHandler(),
            new FieldAccessHandler(),
            new SwitchHandler(),
            new PopHandler()
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

    /// <summary>
    /// Pipeline Step 2: Builds the AST model from the extracted metadata.
    /// </summary>
    public static AstContext Build(Pipeline.GpuKernelMetadata metadata)
    {
        var builder = new AstBuilder();
        var ctx = new AstContext(builder)
        {
            Metadata = metadata
        };
        ctx.RootBlock = builder.ParseMethod(metadata.MethodDefinition, ctx);
        return ctx;
    }

    public BlockNode ParseMethod(MethodDefinition method, AstContext? context = null)
    {
        var statements = new List<IAstNode>();
        context ??= new AstContext(this);

        if (!method.HasBody)
        {
            return new BlockNode(statements);
        }

        foreach (var v in method.Body.Variables)
        {
            context.LocalVariables.Add($"loc_{v.Index}", v.VariableType);
        }

        var instructions = method.Body.Instructions;
        var stack = new Stack<IAstNode>();
        var targetDepths = new Dictionary<int, int>();
        var branchTargets = GetBranchTargets(instructions);

        foreach (var ins in instructions)
        {
            HandleBranchTargetLabel(ins, branchTargets, targetDepths, stack, statements);

            switch (ins.OpCode.Code)
            {
                case Code.Nop:
                    continue;
                case Code.Ret:
                    HandleReturn(method, stack, statements);
                    continue;
            }

            HandleFlowControl(ins, targetDepths, stack, statements);
            DispatchInstruction(ins, method, context, stack, statements);

            if (ins.OpCode.FlowControl is FlowControl.Branch or FlowControl.Throw)
            {
                stack.Clear(); // Unconditional branch, stack is logically empty until next label
            }
        }

        return new BlockNode(statements);
    }

    private static HashSet<int> GetBranchTargets(Mono.Collections.Generic.Collection<Instruction> instructions)
    {
        var branchTargets = new HashSet<int>();
        foreach (var ins in instructions)
        {
            switch (ins.Operand)
            {
                case Instruction target:
                    branchTargets.Add(target.Offset);
                    break;
                case Instruction[] targets:
                    foreach (var t in targets)
                    {
                        branchTargets.Add(t.Offset);
                    }

                    break;
            }
        }

        return branchTargets;
    }

    private static void HandleBranchTargetLabel(Instruction ins, HashSet<int> branchTargets,
        Dictionary<int, int> targetDepths, Stack<IAstNode> stack, List<IAstNode> statements)
    {
        if (!branchTargets.Contains(ins.Offset))
        {
            return;
        }

        if (!targetDepths.TryGetValue(ins.Offset, out var depth))
        {
            depth = stack.Count;
            targetDepths[ins.Offset] = depth;
        }

        if (stack.Count > 0)
        {
            FlushStack(stack, statements);
        }
        else
        {
            for (var i = 0; i < depth; i++)
            {
                stack.Push(new VariableNode($"__stack_{i}"));
            }
        }

        statements.Add(new LabelNode($"IL_{ins.Offset:x4}"));
    }

    private static void HandleReturn(MethodDefinition method, Stack<IAstNode> stack, List<IAstNode> statements)
    {
        if (stack.Count > 0)
        {
            FlushStack(stack, statements);
        }

        statements.Add(method.ReturnType.FullName != "System.Void"
            ? new ReturnNode(stack.Pop())
            : new ReturnNode(null));
    }

    private static void HandleFlowControl(Instruction ins, Dictionary<int, int> targetDepths, Stack<IAstNode> stack,
        List<IAstNode> statements)
    {
        if (ins.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Throw))
        {
            return;
        }

        FlushStack(stack, statements);

        if (ins.Operand is Instruction target)
        {
            if (!targetDepths.ContainsKey(target.Offset))
            {
                targetDepths[target.Offset] = stack.Count;
            }
        }
        else if (ins.Operand is Instruction[] targets)
        {
            foreach (var t in targets)
            {
                if (!targetDepths.ContainsKey(t.Offset))
                {
                    targetDepths[t.Offset] = stack.Count;
                }
            }
        }
    }

    private void DispatchInstruction(Instruction ins, MethodDefinition method, AstContext context,
        Stack<IAstNode> stack, List<IAstNode> statements)
    {
        if (s_handlers.TryGetValue(ins.OpCode.Code, out var handler))
        {
            if (handler is MethodCallHandler callHandler)
            {
                callHandler.Handle(ins, stack, statements, method, context);
            }
            else
            {
                handler.Handle(ins, stack, statements, method);
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported IL OpCode: {ins.OpCode}");
        }
    }

    private static void FlushStack(Stack<IAstNode> stack, List<IAstNode> statements)
    {
        if (stack.Count == 0)
        {
            return;
        }

        var elements = stack.ToArray();
        stack.Clear();
        for (var i = elements.Length - 1; i >= 0; i--)
        {
            var tempVar = new VariableNode($"__stack_{i}");
            if (elements[elements.Length - 1 - i] is not VariableNode vn || vn.Name != tempVar.Name)
            {
                statements.Add(new AssignNode(tempVar.Name, elements[elements.Length - 1 - i]));
            }

            stack.Push(tempVar);
        }
    }
}


