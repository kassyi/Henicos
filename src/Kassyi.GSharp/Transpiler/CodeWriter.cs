using System.Text;

namespace Kassyi.GSharp.Transpiler;

public class CodeWriter
{
    private readonly StringBuilder _sb;

    public CodeWriter(int capacity = 4096)
    {
        _sb = new StringBuilder(capacity);
    }

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

    public Scope Block(string header = "")
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

    public readonly ref struct Scope
    {
        private readonly CodeWriter _writer;

        public Scope(CodeWriter writer)
        {
            _writer = writer;
        }

        public void Dispose() => _writer.WriteLine("}");
    }

    public readonly ref struct ParensScope
    {
        private readonly CodeWriter _writer;

        public ParensScope(CodeWriter writer)
        {
            _writer = writer;
        }

        public void Dispose() => _writer.Write(")");
    }

    public readonly ref struct BracketsScope
    {
        private readonly CodeWriter _writer;

        public BracketsScope(CodeWriter writer)
        {
            _writer = writer;
        }

        public void Dispose() => _writer.Write("]");
    }
}
