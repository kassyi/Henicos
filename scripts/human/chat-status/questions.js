'use strict';

// Question extraction for chat-status.js: the paragraph of an assistant text that asks (or its last one), the last
// text of a JSONL transcript (read from its end, at most TranscriptTailBytes), and the questions and options of
// AskUserQuestion / ask_question.

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

/** Cuts a text to its last `limit` characters, marking the cut with an ellipsis at the start. */
function clipStart(text, limit) {
  const chars = Array.from(text);
  return chars.length <= limit ? text : `…${chars.slice(chars.length - limit + 1).join('')}`;
}

// An ASCII '?' ends a sentence only before a space, a line end or closing punctuation (not in `a?b` or a URL query);
// a full-width '？' always does.
const QuestionMark = /？|\?(?=$|[\s)\]"'*_」』）])/;
// A list marker, a heading or a quote at the start of a line.
const LineMarker = /^\s*(?:[-*+]|\d+[.)]|#+|>)\s+/;

/** A paragraph without the Markdown that only takes width: list markers, headings, `**`, link targets. */
function stripMarkdown(paragraph) {
  return paragraph
    .split('\n')
    .map((line) => line.replace(LineMarker, '').trim())
    .join('\n')
    .replace(/\*\*/g, '')
    .replace(/\[([^\]\n]*)\]\([^)\s]*\)/g, '$1')
    .trim();
}

/** Whether a paragraph asks: a question mark outside its code spans. */
function asksIn(paragraph) {
  return QuestionMark.test(paragraph.replace(/`[^`\n]*`/g, ''));
}

/**
 * The part of a reply the person needs: agents put their question at the end, after the summary, so the last
 * paragraph with a question mark (outside code spans and fenced blocks), else the last paragraph; without the
 * Markdown that only takes width, cut from its start to MaxQuestionLength so that the question stays. `asks` says
 * whether a question mark was found.
 */
function extractReply(text) {
  if (typeof text !== 'string') {
    return null;
  }
  const paragraphs = text
    .replace(/\r\n?/g, '\n')
    .replace(/```[\s\S]*?(?:```|$)/g, '\n\n')
    .split(/\n[ \t]*\n/)
    .map(stripMarkdown)
    .filter((part) => part !== '');
  if (paragraphs.length === 0) {
    return null;
  }
  const asking = paragraphs.findLast(asksIn);
  const chosen = asking ?? paragraphs[paragraphs.length - 1];
  return { question: clipStart(chosen, MaxQuestionLength), asks: asking !== undefined };
}

/** The text of extractReply alone. */
function extractQuestion(text) {
  return extractReply(text)?.question ?? null;
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
  extractReply,
  extractQuestion,
  lastTextOf,
  claudeAssistantText,
  antigravityPlannerText,
  antigravityTranscripts,
  firstExisting,
  questionsOf,
  joinQuestions,
};
