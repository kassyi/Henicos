'use strict';

// Question extraction for chat-status.js: the last question of an assistant text, the last text of a JSONL transcript
// (read from its end, at most TranscriptTailBytes), and the questions and options of AskUserQuestion / ask_question.

const fs = require('fs');
const path = require('path');

const MaxQuestionLength = 2000;
const MaxOptionLength = 500;
const TranscriptTailBytes = 1024 * 1024;

/** Cuts a text to `limit` characters, marking the cut with an ellipsis. */
function clip(text, limit) {
  if (typeof text !== 'string') {
    return null;
  }
  const chars = Array.from(text);
  return chars.length <= limit ? text : `${chars.slice(0, limit - 1).join('')}…`;
}

// An ASCII '?' ends a sentence only before a space, a line end or closing punctuation (not in `a?b` or a URL query);
// a full-width '？' always does.
const QuestionMark = /？|\?(?=$|[\s)\]"'*_」』）])/g;
// What ends the sentence before the question: a line break, a full-width stop, or an ASCII stop before a space.
const SentenceEnd = /[\n。！？]|[.!?](?=\s)/g;
const ListMarker = /^(?:[-*+]|\d+[.)]|#+|>)\s+/;

/** The last sentence that ends in `?` or `？`, or else the last paragraph, cut to MaxQuestionLength. */
function extractQuestion(text) {
  if (typeof text !== 'string') {
    return null;
  }
  const plain = text.replace(/```[\s\S]*?(?:```|$)/g, '\n').replace(/\r\n?/g, '\n');
  let mark = -1;
  for (const match of plain.matchAll(QuestionMark)) {
    mark = match.index;
  }
  if (mark >= 0) {
    let start = 0;
    for (const match of plain.slice(0, mark).matchAll(SentenceEnd)) {
      start = match.index + 1;
    }
    let end = mark + 1;
    while (end < plain.length && '*_」』）)'.includes(plain[end])) {
      end++;
    }
    const sentence = plain.slice(start, end).trim().replace(ListMarker, '').trim();
    if (sentence !== '') {
      return clip(sentence, MaxQuestionLength);
    }
  }
  const paragraphs = plain
    .split(/\n[ \t]*\n/)
    .map((part) => part.trim())
    .filter((part) => part !== '');
  return paragraphs.length === 0 ? null : clip(paragraphs[paragraphs.length - 1], MaxQuestionLength);
}

/** Reads the last `limit` bytes of a file as complete lines (the first, partial line is dropped). */
function readTailLines(file, limit) {
  const handle = fs.openSync(file, 'r');
  try {
    const size = fs.fstatSync(handle).size;
    const length = Math.min(size, limit);
    const buffer = Buffer.alloc(length);
    fs.readSync(handle, buffer, 0, length, size - length);
    const lines = buffer.toString('utf8').split('\n');
    if (length < size) {
      lines.shift();
    }
    return lines;
  } finally {
    fs.closeSync(handle);
  }
}

/** The text of the last record of a JSONL transcript that `pick` returns a non-empty text for. */
function lastTextOf(file, pick) {
  const lines = readTailLines(file, TranscriptTailBytes);
  for (let i = lines.length - 1; i >= 0; i--) {
    const line = lines[i].trim();
    if (line === '') {
      continue;
    }
    let record;
    try {
      record = JSON.parse(line);
    } catch {
      continue; // a line still being written
    }
    const text = pick(record);
    if (typeof text === 'string' && text.trim() !== '') {
      return text;
    }
  }
  return null;
}

/** Claude Code: the text blocks of the last assistant message of the main chain (not a subagent's). */
function claudeAssistantText(record) {
  if (record?.type !== 'assistant' || record.isSidechain === true || !Array.isArray(record.message?.content)) {
    return null;
  }
  const texts = record.message.content
    .filter((block) => block?.type === 'text' && typeof block.text === 'string')
    .map((block) => block.text);
  return texts.length === 0 ? null : texts.join('\n\n');
}

/** Antigravity: the content of a planner response of the model. */
function antigravityPlannerText(record) {
  return record?.type === 'PLANNER_RESPONSE' && record.source === 'MODEL' ? record.content : null;
}

/** Antigravity writes transcript_full.jsonl next to transcriptPath (transcript.jsonl); either may be missing. */
function antigravityTranscripts(transcriptPath) {
  const directory = path.dirname(transcriptPath);
  return [path.join(directory, 'transcript_full.jsonl'), transcriptPath];
}

function firstExisting(files) {
  return files.find((file) => fs.existsSync(file)) ?? null;
}

/** The questions of AskUserQuestion (Claude Code) or ask_question (Antigravity), with their options. */
function questionsOf(input) {
  const list = Array.isArray(input?.questions) ? input.questions : input?.question ? [input] : [];
  return list
    .filter((item) => typeof item?.question === 'string')
    .map((item) => ({
      question: clip(item.question, MaxQuestionLength),
      multiSelect: item.multiSelect === true || item.is_multi_select === true || item.isMultiSelect === true,
      choices: (Array.isArray(item.options) ? item.options : []).map((option) =>
        typeof option === 'string'
          ? { label: clip(option, MaxOptionLength) }
          : {
              label: clip(String(option?.label ?? ''), MaxOptionLength),
              ...(typeof option?.description === 'string'
                ? { description: clip(option.description, MaxOptionLength) }
                : {}),
            }
      ),
    }));
}

function joinQuestions(questions) {
  return questions.length === 0 ? null : clip(questions.map((q) => q.question).join('\n'), MaxQuestionLength);
}

module.exports = {
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
};
