'use strict';

// What one hook call means for chat-status.js: the handlers of the Claude Code and Antigravity events. They read the
// transcript when a Stop asks for the question, and touch no chat file; chat-status/store.js applies the result.

const fs = require('fs');
const {
  MaxQuestionLength,
  MaxFirstPromptLength,
  clip,
  stripPromptTags,
  lastTextOf,
  firstTextOf,
  claudeUserText,
  antigravityUserText,
  claudeAssistantText,
  antigravityPlannerText,
  antigravityTranscripts,
  firstExisting,
  questionsOf,
  joinQuestions,
} = require('./questions.js');
const { prompted, withRecentPrompt, endTurn } = require('./open-questions.js');
const { conversationFacts } = require('./antigravity.js');

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
  return { state: 'waiting', kind, since: now, question: question ?? null, options: options ?? null, asks: null };
}

/** The first prompt of the chat, once: nothing when one is recorded, else what `read` finds (or nothing). */
function firstPrompt(previous, read) {
  if (typeof previous?.firstPrompt === 'string' && previous.firstPrompt !== '') {
    return {};
  }
  const text = read();
  const own = typeof text === 'string' ? stripPromptTags(text) : '';
  return own !== '' ? { firstPrompt: clip(own, MaxFirstPromptLength) } : {};
}

function working(now) {
  return { state: 'working', kind: null, since: now, question: null, options: null, asks: null };
}

function interpretClaude(payload, now) {
  const event = payload.hook_event_name;
  const base = { agent: 'claude', session: payload.session_id, cwd: payload.cwd, output: '', remove: false };
  switch (event) {
    case 'UserPromptSubmit':
      return {
        ...base,
        update: (previous) => ({
          ...working(now),
          ...prompted(now, payload.prompt),
          ...withRecentPrompt(previous, payload.prompt),
          ...firstPrompt(previous, () => payload.prompt),
        }),
      };
    case 'Stop':
      return {
        ...base,
        update: (previous) => {
          const transcript =
            payload.transcript_path && fs.existsSync(payload.transcript_path) ? payload.transcript_path : null;
          let text = typeof payload.last_assistant_message === 'string' ? payload.last_assistant_message : null;
          if (!text && transcript !== null) {
            text = lastTextOf(transcript, claudeAssistantText);
          }
          // a chat that started before the first prompt was recorded: from the start of its transcript
          const first = firstPrompt(previous, () => transcript && firstTextOf(transcript, claudeUserText));
          return { ...endTurn(previous, now, text, null), ...first };
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

/**
 * An Antigravity event, unless its conversation is a subagent's (its transcript opens with a system message): such a
 * chat is never shown, so its file is removed instead (a subagent fires the same hooks). The title Antigravity gave
 * the conversation is recorded as `agentTitle`.
 */
function interpretAntigravity(payload, now) {
  const action = antigravityAction(payload, now);
  if (typeof action.session !== 'string' || action.session === '') {
    return action;
  }
  const facts = conversationFacts(action.session, payload.transcriptPath);
  if (facts.subagent === true) {
    return { ...action, remove: true, update: () => null };
  }
  const titled = facts.title === null ? {} : { agentTitle: facts.title };
  return {
    ...action,
    update: (previous) => {
      const fields = action.update(previous);
      return fields === null ? null : { ...fields, ...titled };
    },
  };
}

function antigravityAction(payload, now) {
  const event = payload.hookEventName;
  const workspace = Array.isArray(payload.workspacePaths) ? payload.workspacePaths[0] : undefined;
  const base = { agent: 'antigravity', session: payload.conversationId, cwd: payload.cwd || workspace, remove: false };
  switch (event) {
    case 'Stop':
      return {
        ...base,
        output: '{}',
        update: (previous) => {
          if (payload.fullyIdle !== true) {
            return { state: 'background', kind: null, since: now, question: null, options: null, asks: null };
          }
          let file = null;
          if (typeof payload.transcriptPath === 'string' && payload.transcriptPath !== '') {
            file = firstExisting(antigravityTranscripts(payload.transcriptPath));
          }
          const text = file ? lastTextOf(file, antigravityPlannerText) : null;
          // the latest input: from the transcript, else the payload's lastUserInput
          const input = typeof payload.lastUserInput === 'string' ? payload.lastUserInput : null;
          const prompt = (file ? lastTextOf(file, antigravityUserText) : null) ?? input;
          const first = firstPrompt(previous, () => file && firstTextOf(file, antigravityUserText));
          const last = prompt === null ? {} : { lastPrompt: prompted(now, prompt).lastPrompt };
          return { ...endTurn(previous, now, text, prompt), ...last, ...withRecentPrompt(previous, prompt), ...first };
        },
      };
    case 'PreInvocation':
      // fires before every model call: only the first one after a Stop or a question changes the state, and the first
      // after a wait stands for the person's prompt (Antigravity has no event for it)
      return {
        ...base,
        output: '{}',
        update: (previous) =>
          previous?.state === 'working'
            ? null
            : { ...working(now), ...(previous?.state === 'waiting' ? prompted(now, null) : {}) },
      };
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
