'use strict';

// Hook I/O adapter for the built-in agents plugin: reads Claude Code (Bash / PowerShell) and Antigravity
// (run_command) PreToolUse payloads and formats denial or updatedInput rewrite responses.
//
// Usage: loaded by the hook engine plugin loader when `agents` is enabled in job-queue.config.json.

/** Extracts command, shell dialect, working directory, and rewrite capability from a hook payload. */
function read(payload) {
  if (payload === null || typeof payload !== 'object' || Array.isArray(payload)) {
    return null;
  }
  if (payload.toolCall !== undefined) {
    if (payload.toolCall === null || typeof payload.toolCall !== 'object') {
      return null;
    }
    const args = payload.toolCall.args;
    return {
      agent: 'antigravity',
      command: String((args && args.CommandLine) || ''),
      kind: 'pwsh',
      cwd: process.cwd(),
      canRewrite: false,
      raw: payload,
    };
  }
  if (payload.tool_input !== undefined || payload.tool_name !== undefined) {
    const toolInput =
      payload.tool_input !== null && typeof payload.tool_input === 'object' && !Array.isArray(payload.tool_input)
        ? payload.tool_input
        : {};
    return {
      agent: 'claude',
      command: String(toolInput.command || ''),
      kind: payload.tool_name === 'PowerShell' ? 'pwsh' : 'bash',
      cwd: String(payload.cwd || process.cwd()),
      canRewrite: true,
      toolInput,
      raw: payload,
    };
  }
  return null;
}

/** Formats a denial response in the agent's hook protocol. */
function deny(readResult, reasonText) {
  if (readResult.agent === 'antigravity') {
    return { decision: 'deny', reason: reasonText };
  }
  return {
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'deny',
      permissionDecisionReason: reasonText,
    },
  };
}

/** Formats a command rewrite response when the agent supports updatedInput (never for Antigravity). */
function rewrite(readResult, command, additionalContextText) {
  if (!readResult.canRewrite || readResult.agent !== 'claude') {
    return null;
  }
  return {
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      updatedInput: { ...readResult.toolInput, command },
      additionalContext: additionalContextText,
    },
  };
}

module.exports = {
  name: 'agents',
  read,
  deny,
  rewrite,
};
