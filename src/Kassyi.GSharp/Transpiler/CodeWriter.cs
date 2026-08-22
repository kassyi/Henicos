using System;
using System.Text;

namespace Kassyi.GSharp.Transpiler
{
    public class CodeWriter
    {
        private readonly StringBuilder _sb;
        private int _indentLevel = 0;
        private const string IndentString = "    ";

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

        public CodeWriter WriteIndent()
        {
            for (int i = 0; i < _indentLevel; i++)
            {
                _sb.Append(IndentString);
            }
            return this;
        }

        public IDisposable Block(string header = "")
        {
            if (!string.IsNullOrEmpty(header))
            {
                Write(header).Write(" ");
            }
            WriteLine("{");
            _indentLevel++;
            return new Scope(this);
        }
        
        public IDisposable Block()
        {
            WriteLine("{");
            _indentLevel++;
            return new Scope(this);
        }

        public IDisposable Parens()
        {
            Write("(");
            return new ParensScope(this);
        }

        public IDisposable Brackets()
        {
            Write("[");
            return new BracketsScope(this);
        }

        public override string ToString() => _sb.ToString();

        private class Scope : IDisposable
        {
            private readonly CodeWriter _writer;

            public Scope(CodeWriter writer)
            {
                _writer = writer;
            }

            public void Dispose()
            {
                _writer._indentLevel--;
                _writer.WriteIndent().WriteLine("}");
            }
        }

        private class ParensScope : IDisposable
        {
            private readonly CodeWriter _writer;

            public ParensScope(CodeWriter writer)
            {
                _writer = writer;
            }

            public void Dispose()
            {
                _writer.Write(")");
            }
        }

        private class BracketsScope : IDisposable
        {
            private readonly CodeWriter _writer;

            public BracketsScope(CodeWriter writer)
            {
                _writer = writer;
            }

            public void Dispose()
            {
                _writer.Write("]");
            }
        }
    }
}
