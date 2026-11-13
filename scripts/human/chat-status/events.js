'use strict';

// What one hook call means for chat-status.js: the handlers of the Claude Code and Antigravity events. They read the
// transcript when a Stop asks for the question, and touch no chat file; chat-status/store.js applies the result.

const fs = require('fs');
const {
  MaxQuestionLength,
  clip,
  extractQuestion,
  lastTextOf,
  claudeAssistantText,
  antigravityPlannerText,
  antigravityTranscripts,
  firstExisting,
  questionsOf,
  joinQuestions,
} = require('./questions.js');

/**
 * What one hook call means, with no file access but the transcript read: `{ agent, session, cwd, update, remove,
 * output }`, where `update(previous)` returns the fields to record or null to leave the file alone. Returns null for
 * a payload that is neither agent's.
 */
function interpret(payload, now) {
  if (typeof payload?.hook_event_name === 'string') {
    return interpretClaude(payload, now);
  }
  if (typeof payload?.hookEventName === 'string') {
    return interpretAntigravity(payload, now);
  }
  return null;
}

function waiting(kind, now, question, options) {
  return { state: 'waiting', kind, since: now, question: question ?? null, options: options ?? null };
}

function working(now) {
  return { state: 'working', kind: null, since: now, question: null, options: null };
}

function interpretClaude(payload, now) {
  const event = payload.hook_event_name;
  const base = { agent: 'claude', session: payload.session_id, cwd: payload.cwd, output: '', remove: false };
  switch (event) {
    case 'UserPromptSubmit':
      return { ...base, update: () => working(now) };
    case 'Stop':
      return {
        ...base,
        update: () => {
          let text = typeof payload.last_assistant_message === 'string' ? payload.last_assistant_message : null;
          if (!text && payload.transcript_path && fs.existsSync(payload.transcript_path)) {
            text = lastTextOf(payload.transcript_path, claudeAssistantText);
          }
          return waiting('reply', now, extractQuestion(text));
        },
      };
    case 'PreToolUse':
      if (payload.tool_name !== 'AskUserQuestion') {
        return { ...base, update: () => null };
      }
      return {
        ...base,
        update: () => {
          const questions = questionsOf(payload.tool_input);
          return waiting('question', now, joinQuestions(questions), questions);
        },
      };
    case 'PostToolUse':
      return {
        ...base,
        update: (previous) =>
          previous?.state === 'waiting' && (previous.kind === 'question' || previous.kind === 'permission')
            ? working(now)
            : null,
      };
    case 'Notification':
      return {
        ...base,
        update: (previous) => {
          const message = clip(typeof payload.message === 'string' ? payload.message : '', MaxQuestionLength);
          if (payload.notification_type === 'permission_prompt') {
            return waiting('permission', now, message);
          }
          // idle_prompt and the like: a chat that already waits keeps its question and since
          if (previous?.state === 'waiting') {
            return null;
          }
          return waiting('input', now, message);
        },
      };
    case 'SessionEnd':
      return { ...base, remove: true, update: () => null };
    default:
      return { ...base, update: () => null };
  }
}

function interpretAntigravity(payload, now) {
  const event = payload.hookEventName;
  const workspace = Array.isArray(payload.workspacePaths) ? payload.workspacePaths[0] : undefined;
  const base = { agent: 'antigravity', session: payload.conversationId, cwd: payload.cwd || workspace, remove: false };
  switch (event) {
    case 'Stop':
      return {
        ...base,
        output: '{}',
        update: () => {
          if (payload.fullyIdle !== true) {
            return { state: 'background', kind: null, since: now, question: null, options: null };
          }
          let text = null;
          if (typeof payload.transcriptPath === 'string' && payload.transcriptPath !== '') {
            const file = firstExisting(antigravityTranscripts(payload.transcriptPath));
            text = file ? lastTextOf(file, antigravityPlannerText) : null;
          }
          return waiting('reply', now, extractQuestion(text));
        },
      };
    case 'PreInvocation':
      // fires before every model call: only the first one after a Stop or a question changes the state
      return { ...base, output: '{}', update: (previous) => (previous?.state === 'working' ? null : working(now)) };
    case 'PreToolUse':
      if (payload.toolCall?.name !== 'ask_question') {
        return { ...base, output: '', update: () => null };
      }
      return {
        ...base,
        output: '',
        update: () => {
          const questions = questionsOf(payload.toolCall.args);
          return waiting('question', now, joinQuestions(questions), questions);
        },
      };
    default:
      return { ...base, output: '{}', update: () => null };
  }
}

module.exports = { interpret };
