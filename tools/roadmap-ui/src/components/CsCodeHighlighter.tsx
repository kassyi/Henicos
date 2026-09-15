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

interface TokenStyle {
  color: string;
  fontWeight?: number;
}

function classifyIdentifier(token: string, isCall: boolean): TokenStyle {
  if (CONTROL_KEYWORDS.has(token)) {
    return { color: COLORS.controlKeyword, fontWeight: 600 };
  }
  if (GENERAL_KEYWORDS.has(token)) {
    return { color: COLORS.keyword, fontWeight: 600 };
  }
  if (KNOWN_TYPES.has(token)) {
    return { color: COLORS.type, fontWeight: 600 };
  }
  if (isCall) {
    return { color: COLORS.method };
  }
  if (token[0] >= 'A' && token[0] <= 'Z') {
    return { color: COLORS.type, fontWeight: 600 };
  }
  return { color: COLORS.identifier };
}

function resolveTokenStyle(token: string, remainingCode: string): TokenStyle {
  // String / char literals
  if (token.startsWith('"') || token.startsWith('$"') || token.startsWith("'") || token.endsWith('u8')) {
    return { color: COLORS.string };
  }
  // Number literals (hex or decimal)
  if (/^(?:0x|\d)/.test(token)) {
    return { color: COLORS.number };
  }
  // Word / Identifiers
  if (/^[a-zA-Z_]/.test(token)) {
    const isCall = remainingCode.trimStart().startsWith('(');
    return classifyIdentifier(token, isCall);
  }
  // Operators / punctuation
  return { color: COLORS.operator };
}

function tokenizeCodePart(codePart: string, lineIdx: number, offset: number): React.ReactNode[] {
  const tokenRegex = /(\$?"(?:[^"\\]|\\.)*"u8|\$?"(?:[^"\\]|\\.)*"|'[^']+'|\b0x[0-9a-fA-F]+\b|\b\d+(?:\.\d+)?(?:f|u|U|L|UL)?\b|\b[a-zA-Z_]\w*\b|[^\s\w])/g;
  
  const tokens: React.ReactNode[] = [];
  let match: RegExpExecArray | null;
  let lastIndex = 0;

  while ((match = tokenRegex.exec(codePart)) !== null) {
    if (match.index > lastIndex) {
      tokens.push(codePart.substring(lastIndex, match.index));
    }
    const token = match[0];
    const key = `${lineIdx}-${offset + match.index}`;
    const style = resolveTokenStyle(token, codePart.slice(tokenRegex.lastIndex));

    tokens.push(
      <span key={key} style={style}>
        {token}
      </span>
    );

    lastIndex = tokenRegex.lastIndex;
  }

  if (lastIndex < codePart.length) {
    tokens.push(codePart.substring(lastIndex));
  }
  return tokens;
}

function highlightCsCode(rawCode: string): React.ReactNode[] {
  // Normalize escaped newlines and break tags
  const normalized = (rawCode || '')
    .replace(/\\r\\n/g, '\n')
    .replace(/\\n/g, '\n')
    .replace(/<br\s*\/?>/gi, '\n');

  const lines = normalized.split(/\r?\n/);
  let inBlockComment = false;

  return lines.map((line, lineIdx) => {
    const tokens: React.ReactNode[] = [];
    let currentIdx = 0;
    const len = line.length;

    while (currentIdx < len) {
      if (inBlockComment) {
        const endIdx = line.indexOf('*/', currentIdx);
        if (endIdx !== -1) {
          tokens.push(<span key={`${lineIdx}-${currentIdx}`} style={{ color: COLORS.comment, fontStyle: 'italic' }}>{line.substring(currentIdx, endIdx + 2)}</span>);
          currentIdx = endIdx + 2;
          inBlockComment = false;
        } else {
          tokens.push(<span key={`${lineIdx}-${currentIdx}`} style={{ color: COLORS.comment, fontStyle: 'italic' }}>{line.substring(currentIdx)}</span>);
          currentIdx = len;
        }
        continue;
      }

      // Look for /* or // inside string literals (naive check - simplified)
      // Actually, we should ideally not parse /* inside strings, but for now we look for earliest /* or //
      // To prevent string issues, we could do more, but assuming comment takes precedence for roadmap snippets
      const blockStart = line.indexOf('/*', currentIdx);
      const lineStart = line.indexOf('//', currentIdx);

      let nextCommentIdx = -1;
      let isBlock = false;

      if (blockStart !== -1 && lineStart !== -1) {
        if (blockStart < lineStart) {
          nextCommentIdx = blockStart;
          isBlock = true;
        } else {
          nextCommentIdx = lineStart;
          isBlock = false;
        }
      } else if (blockStart !== -1) {
        nextCommentIdx = blockStart;
        isBlock = true;
      } else if (lineStart !== -1) {
        nextCommentIdx = lineStart;
        isBlock = false;
      }

      // Basic string literal check to prevent matching comments inside strings
      // We look if there is an unclosed quote before nextCommentIdx. 
      // If so, we'll skip finding comment on this iteration.
      if (nextCommentIdx !== -1) {
        const codeBefore = line.substring(currentIdx, nextCommentIdx);
        const quoteCount = (codeBefore.match(/"/g) || []).length;
        if (quoteCount % 2 !== 0) {
           // It's inside a string, ignore this comment symbol for now,
           // just push code till after the string ends
           const stringEnd = line.indexOf('"', nextCommentIdx);
           if (stringEnd !== -1) {
              nextCommentIdx = -1;
           }
        }
      }

      const codeEndIdx = nextCommentIdx !== -1 ? nextCommentIdx : len;
      
      if (codeEndIdx > currentIdx) {
        const codePart = line.substring(currentIdx, codeEndIdx);
        tokens.push(...tokenizeCodePart(codePart, lineIdx, currentIdx));
      }

      currentIdx = codeEndIdx;

      if (currentIdx < len) {
        if (isBlock) {
          inBlockComment = true;
        } else {
          tokens.push(<span key={`${lineIdx}-${currentIdx}`} style={{ color: COLORS.comment, fontStyle: 'italic' }}>{line.substring(currentIdx)}</span>);
          currentIdx = len;
        }
      }
    }

    return (
      <div key={lineIdx} style={{ minHeight: '1.3em', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
        {tokens.length === 0 ? ' ' : tokens}
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
        overflowX: 'hidden',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
      }}
    >
      {highlightedCode}
    </Box>
  );
};
