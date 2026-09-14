import React, { useMemo } from 'react';
import { Box } from '@mui/material';

interface CsCodeHighlighterProps {
  code: string;
}

// VS Code Dark+ Color Palette Definition
const COLORS = {
  controlKeyword: '#c586c0', // Pink / Magenta for flow control (if, return, for, switch, etc.)
  keyword: '#569cd6',        // Blue for declarations & keywords (public, static, void, var, ref, out, etc.)
  type: '#4ec9b0',           // Turquoise Green for types & classes (uint, Span, Gpu, Vector2, etc.)
  method: '#dcdcaa',         // Soft Yellow for method / function calls
  string: '#ce9178',         // Orange / Brown for string & char literals
  number: '#b5cea8',         // Light Green for numbers (0, 1024, 1u, 1.5f)
  comment: '#6a9955',        // Green for comments
  identifier: '#9cdcfe',     // Light Blue for variables & fields
  operator: '#d4d4d4',       // Light Gray for operators
};

const CONTROL_KEYWORDS = new Set([
  'if', 'else', 'switch', 'case', 'default', 'for', 'foreach', 'while', 'do', 'return', 'goto', 'yield', 'break', 'continue'
]);

const GENERAL_KEYWORDS = new Set([
  'public', 'private', 'protected', 'internal', 'static', 'readonly', 'const', 'void', 'var', 'ref', 'out', 'in',
  'scoped', 'fixed', 'stackalloc', 'unsafe', 'new', 'is', 'as', 'with', 'typeof', 'nameof', 'operator',
  'checked', 'unchecked', 'where', 'struct', 'class', 'enum', 'interface', 'delegate', 'params', 'implicit', 'explicit', 'file'
]);

const KNOWN_TYPES = new Set([
  'uint', 'int', 'long', 'ulong', 'short', 'ushort', 'byte', 'sbyte', 'float', 'double', 'decimal', 'bool', 'char', 'string', 'object',
  'nuint', 'nint', 'Half', 'BFloat16', 'UInt128', 'Span', 'ReadOnlySpan', 'Vector128', 'Vector2', 'Vector3', 'Vector4',
  'Gpu', 'MemoryMarshal', 'Unsafe', 'BitOperations', 'Interlocked', 'Warp', 'Wmma', 'DynamicSharedMemory', 'State', 'Point', 'Context', 'Holder'
]);

function highlightCsCode(rawCode: string): React.ReactNode[] {
  // Normalize escaped newlines and break tags
  const normalized = (rawCode || '')
    .replace(/\\r\\n/g, '\n')
    .replace(/\\n/g, '\n')
    .replace(/<br\s*\/?>/gi, '\n');

  const lines = normalized.split(/\r?\n/);
  return lines.map((line, lineIdx) => {
    // Check for comment
    const commentIdx = line.indexOf('//');
    let codePart = line;
    let commentPart = '';
    if (commentIdx !== -1) {
      codePart = line.substring(0, commentIdx);
      commentPart = line.substring(commentIdx);
    }

    // Tokenize codePart using regex for string literals, numbers, words, and operators
    const tokenRegex = /(\$?"(?:[^"\\]|\\.)*"u8|\$?"(?:[^"\\]|\\.)*"|'[^\']+'|\b0x[0-9a-fA-F]+\b|\b\d+(?:\.\d+)?(?:f|u|U|L|UL)?\b|\b[a-zA-Z_]\w*\b|[^\s\w])/g;
    
    const tokens: React.ReactNode[] = [];
    let match: RegExpExecArray | null;
    let lastIndex = 0;

    while ((match = tokenRegex.exec(codePart)) !== null) {
      // Add preceding whitespace if any
      if (match.index > lastIndex) {
        tokens.push(codePart.substring(lastIndex, match.index));
      }
      const token = match[0];
      const key = `${lineIdx}-${match.index}`;

      // String / Char Literals
      if (token.startsWith('"') || token.startsWith('$"') || token.startsWith("'") || token.endsWith('u8')) {
        tokens.push(<span key={key} style={{ color: COLORS.string }}>{token}</span>);
      }
      // Number Literals
      else if (/^\d/.test(token) || /^0x/.test(token)) {
        tokens.push(<span key={key} style={{ color: COLORS.number }}>{token}</span>);
      }
      // Word Tokens (Keywords, Types, Methods, Identifiers)
      else if (/^[a-zA-Z_]/.test(token)) {
        if (CONTROL_KEYWORDS.has(token)) {
          tokens.push(<span key={key} style={{ color: COLORS.controlKeyword, fontWeight: 600 }}>{token}</span>);
        } else if (GENERAL_KEYWORDS.has(token)) {
          tokens.push(<span key={key} style={{ color: COLORS.keyword, fontWeight: 600 }}>{token}</span>);
        } else if (KNOWN_TYPES.has(token) || (token.length > 1 && token[0] === token[0].toUpperCase() && !/^\(/.test(token))) {
          // Check if followed by dot or is a Type
          const nextChars = codePart.substring(tokenRegex.lastIndex).trim();
          if (nextChars.startsWith('(') && !KNOWN_TYPES.has(token)) {
            // Function/Method call
            tokens.push(<span key={key} style={{ color: COLORS.method }}>{token}</span>);
          } else {
            // Type name (PascalCase)
            tokens.push(<span key={key} style={{ color: COLORS.type, fontWeight: 600 }}>{token}</span>);
          }
        } else {
          // Check if followed by ( => Method call
          const nextChars = codePart.substring(tokenRegex.lastIndex).trim();
          if (nextChars.startsWith('(')) {
            tokens.push(<span key={key} style={{ color: COLORS.method }}>{token}</span>);
          } else {
            tokens.push(<span key={key} style={{ color: COLORS.identifier }}>{token}</span>);
          }
        }
      }
      // Operators / Punctuation
      else {
        tokens.push(<span key={key} style={{ color: COLORS.operator }}>{token}</span>);
      }

      lastIndex = tokenRegex.lastIndex;
    }

    if (lastIndex < codePart.length) {
      tokens.push(codePart.substring(lastIndex));
    }

    return (
      <div key={lineIdx} style={{ minHeight: '1.3em', whiteSpace: 'pre' }}>
        {tokens.length === 0 && !commentPart ? ' ' : tokens}
        {commentPart && <span style={{ color: COLORS.comment, fontStyle: 'italic' }}>{commentPart}</span>}
      </div>
    );
  });
}

export const CsCodeHighlighter: React.FC<CsCodeHighlighterProps> = ({ code }) => {
  const highlightedCode = useMemo(() => highlightCsCode(code), [code]);

  return (
    <Box
      sx={{
        fontFamily: 'Consolas, Monaco, "Andale Mono", "Ubuntu Mono", monospace',
        fontSize: '0.85rem',
        lineHeight: 1.5,
        color: '#d4d4d4',
        bgcolor: '#1e1e1e', // VS Code Dark+ Background
        p: 2,
        borderRadius: 2,
        border: '1px solid #333333',
        boxShadow: 'inset 0 0 12px rgba(0, 0, 0, 0.5)',
        overflowX: 'auto',
        whiteSpace: 'pre',
      }}
    >
      {highlightedCode}
    </Box>
  );
};
