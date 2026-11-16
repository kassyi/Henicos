'use strict';

// The questions a chat has asked the person and that are still open, for chat-status.js. A turn ends at a Stop: the
// hook records at once what the heuristic says (the questions still open stay until the person sends a new prompt;
// after one, only the latest reply counts, by its question mark), together with what Claude Haiku needs to judge the
// turn later (the questions open before it and the prompt of the turn); the summarizer then replaces the heuristic's
// list with Haiku's (chat-status/summary.js). A failed or missing Haiku never drops a question.

const { extractReply, clip, stripPromptTags } = require('./questions.js');

/** The open questions kept, newest first. */
const MaxOpenQuestions = 3;
/** The end of the person's latest prompt that is kept for Haiku. */
const MaxPromptLength = 2000;
/** The latest prompts kept (newest first) for Haiku to name the chat by, each cut to this many characters. */
const MaxRecentPrompts = 3;
const MaxRecentPromptLength = 300;
/** A question of Haiku's counts as the latest text's when this share of its character pairs occurs in the text. */
const GroundedShare = 0.5;
/** A question of Haiku's is one asked before when this share of the pairs of each text occurs in the other. */
const CarriedShare = 0.8;

function clipStart(text, limit) {
  const chars = Array.from(text);
  return chars.length <= limit ? text : `…${chars.slice(chars.length - limit + 1).join('')}`;
}

/** The fields of a prompt the person sent: when, and its end (Antigravity has no text here: null keeps the old one). */
function prompted(now, text) {
  const trimmed = typeof text === 'string' ? stripPromptTags(text).trim() : '';
  const prompt = trimmed !== '' ? clipStart(trimmed, MaxPromptLength) : null;
  return prompt === null ? { promptedAt: now } : { promptedAt: now, lastPrompt: prompt };
}

/**
 * The latest prompts of the chat, newest first, as the field `recentPrompts` to record: `text` goes in front (a prompt
 * already in front stays once). Haiku names the chat from them, so that a long chat is titled by its current work and
 * not by its first prompt. Nothing to record for an empty text.
 */
function withRecentPrompt(previous, text) {
  const trimmed = typeof text === 'string' ? stripPromptTags(text).trim() : '';
  if (trimmed === '') {
    return {};
  }
  const prompt = clip(trimmed, MaxRecentPromptLength);
  const before = Array.isArray(previous?.recentPrompts) ? previous.recentPrompts.filter((item) => item !== '') : [];
  return { recentPrompts: [prompt, ...before.filter((item) => item !== prompt)].slice(0, MaxRecentPrompts) };
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

/** The character pairs of a text without spaces and punctuation, lower case: the unit the guard compares in. */
function pairsOf(text) {
  const chars = Array.from(String(text).toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ''));
  const pairs = new Set();
  for (let i = 0; i + 1 < chars.length; i++) {
    pairs.add(chars[i] + chars[i + 1]);
  }
  return pairs;
}

/** The share of the pairs of `text` that occur in `other`; 0 for a text with no pair. */
function shareIn(text, other) {
  const own = pairsOf(text);
  if (own.size === 0) {
    return 0;
  }
  const rest = pairsOf(other);
  let found = 0;
  for (const pair of own) {
    found += rest.has(pair) ? 1 : 0;
  }
  return found / own.size;
}

/**
 * The questions Haiku named that the agent really asks: only the agent's questions are open, so a question that is
 * not one of those open before (the same text, `before`) and that a prompt of the person (`personTexts`: the prompt of
 * the turn and the recent ones) explains better than the latest text of the agent is the person's own words
 * turned into a question, and is dropped. A question the latest
 * text supports (at least half of its character pairs occur there) stays; so does one that neither text explains, since
 * a paraphrase is not told from a mistake then. `dropped` counts what was removed.
 */
function groundedOpen(texts, before, latest, personTexts) {
  const kept = texts.filter((text) => {
    if (before.some((item) => shareIn(text, item.text) >= CarriedShare && shareIn(item.text, text) >= CarriedShare)) {
      return true;
    }
    const fromLatest = shareIn(text, latest);
    if (fromLatest >= GroundedShare) {
      return true;
    }
    return !personTexts.some((own) => typeof own === 'string' && shareIn(text, own) > fromLatest);
  });
  return { kept, dropped: texts.length - kept.length };
}

module.exports = {
  MaxOpenQuestions,
  MaxPromptLength,
  MaxRecentPrompts,
  prompted,
  withRecentPrompt,
  openOf,
  endTurn,
  judgedOpen,
  groundedOpen,
};
