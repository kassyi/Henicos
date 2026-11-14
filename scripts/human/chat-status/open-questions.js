'use strict';

// The questions a chat has asked the person and that are still open, for chat-status.js. A turn ends at a Stop: the
// hook records at once what the heuristic says (the questions still open stay until the person sends a new prompt;
// after one, only the latest reply counts, by its question mark), together with what Claude Haiku needs to judge the
// turn later (the questions open before it and the prompt of the turn); the summarizer then replaces the heuristic's
// list with Haiku's (chat-status/summary.js). A failed or missing Haiku never drops a question.

const { extractReply } = require('./questions.js');

/** The open questions kept, newest first. */
const MaxOpenQuestions = 3;
/** The end of the person's latest prompt that is kept for Haiku. */
const MaxPromptLength = 2000;

function clipStart(text, limit) {
  const chars = Array.from(text);
  return chars.length <= limit ? text : `…${chars.slice(chars.length - limit + 1).join('')}`;
}

/** The fields of a prompt the person sent: when, and its end (Antigravity has no text here: null keeps the old one). */
function prompted(now, text) {
  const prompt = typeof text === 'string' && text.trim() !== '' ? clipStart(text.trim(), MaxPromptLength) : null;
  return prompt === null ? { promptedAt: now } : { promptedAt: now, lastPrompt: prompt };
}

/** The open questions of a record, as stored (absent from older files). */
function openOf(record) {
  return Array.isArray(record?.openQuestions)
    ? record.openQuestions.filter((item) => typeof item?.text === 'string' && item.text !== '')
    : [];
}

/**
 * The fields of a turn that ended with the reply `text`: the waiting state with the paragraph that asks (question,
 * asks), the heuristic's open questions, and for Haiku the questions open before the turn (`openBefore`) and the
 * prompt the person sent in it (`turnPrompt`, null when none was sent since the last Stop).
 */
function endTurn(previous, now, text, lastPrompt) {
  const reply = extractReply(text);
  const before = openOf(previous);
  const newPrompt = (previous?.promptedAt ?? 0) > (previous?.stoppedAt ?? 0);
  const asked = reply?.asks ? [{ text: reply.question, askedAt: now }] : [];
  const kept = (newPrompt ? [] : before).filter((item) => item.text !== reply?.question);
  return {
    state: 'waiting',
    kind: 'reply',
    since: now,
    question: reply?.question ?? null,
    options: null,
    asks: reply?.asks ?? false,
    askVerdict: 'heuristic',
    openQuestions: [...asked, ...kept].slice(0, MaxOpenQuestions),
    openBefore: before,
    turnPrompt: newPrompt ? (prompted(now, lastPrompt ?? previous?.lastPrompt).lastPrompt ?? null) : null,
    stoppedAt: now,
  };
}

/** Haiku's open questions as records: the time a question was first asked is kept when it was open before. */
function judgedOpen(texts, before, now) {
  return texts.slice(0, MaxOpenQuestions).map((text) => ({
    text,
    askedAt: before.find((item) => item.text === text)?.askedAt ?? now,
  }));
}

module.exports = { MaxOpenQuestions, MaxPromptLength, prompted, openOf, endTurn, judgedOpen };
