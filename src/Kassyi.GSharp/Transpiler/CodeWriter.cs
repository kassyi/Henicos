namespace Kassyi.GSharp.Transpiler;

#pragma warning disable IDE0051 // Remove unused private members
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

public class CodeWriter(int capacity = 4096)
{
    private readonly StringBuilder _sb = new(capacity);

    public CodeWriter Write(string text)
    {
        _sb.Append(text);
        return this;
    }

    public CodeWriter Write(char c)
    {
        _sb.Append(c);
        return this;
    }

    public CodeWriter Write(object obj)
    {
        _sb.Append(obj);
        return this;
    }

    public CodeWriter WriteLine(string text = "")
    {
        _sb.AppendLine(text);
        return this;
    }

    public Scope Block(string header)
    {
        if (!string.IsNullOrEmpty(header))
        {
            Write(header).Write(" ");
        }
        WriteLine("{");
        return new Scope(this);
    }

    public Scope Block()
    {
        WriteLine("{");
        return new Scope(this);
    }

    public ParensScope Parens()
    {
        Write("(");
        return new ParensScope(this);
    }

    public BracketsScope Brackets()
    {
        Write("[");
        return new BracketsScope(this);
    }

    public override string ToString() => _sb.ToString();

    public readonly ref struct Scope(CodeWriter writer)
    {
        public void Dispose() => writer.WriteLine("}");
    }

    public readonly ref struct ParensScope(CodeWriter writer)
    {
        public void Dispose() => writer.Write(")");
    }

    public readonly ref struct BracketsScope(CodeWriter writer)
    {
        public void Dispose() => writer.Write("]");
    }
}
