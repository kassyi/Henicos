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

/** The first prompt of a chat is kept to this many characters (a title is made from it). */
const MaxFirstPromptLength = 500;
/** How much of the start of a transcript is searched for the first prompt. */
const TranscriptHeadBytes = 256 * 1024;

/** The text of the first record in the first TranscriptHeadBytes of a JSONL transcript that `pick` returns one for. */
function firstTextOf(file, pick) {
  const handle = fs.openSync(file, 'r');
  let text;
  try {
    const buffer = Buffer.alloc(Math.min(fs.fstatSync(handle).size, TranscriptHeadBytes));
    fs.readSync(handle, buffer, 0, buffer.length, 0);
    text = buffer.toString('utf8');
  } finally {
    fs.closeSync(handle);
  }
  for (const line of text.split('\n')) {
    let record;
    try {
      record = JSON.parse(line);
    } catch {
      continue; // a blank line, or the last one cut by the limit
    }
    const found = pick(record);
    if (typeof found === 'string' && found.trim() !== '') {
      return clip(found.trim(), MaxFirstPromptLength);
    }
  }
  return null;
}

/**
 * The tags the IDE and Claude Code put around a prompt (the file the person has open, the selection, system reminders,
 * command markers): not the person's words, so they never reach the stored prompts. A block is removed with its content.
 */
const PromptTagNames = [
  'ide_opened_file',
  'ide_selection',
  'ide_diagnostics',
  'system-reminder',
  'user-prompt-submit-hook',
  'command-name',
  'command-message',
  'command-args',
  'local-command-stdout',
  'local-command-stderr',
  'local-command-caveat',
];
const PromptTagBlock = new RegExp(String.raw`<(${PromptTagNames.join('|')})(?:\s[^>]*)?>[\s\S]*?<\/\1>`, 'gi');
// an opening tag whose closer never came (a cut prompt): the tag line goes, the rest stays
const PromptTagOpen = new RegExp(String.raw`<\/?(?:${PromptTagNames.join('|')})(?:\s[^>]*)?>`, 'gi');

/** The prompt of the person without the tags of PromptTagNames; blank lines left by them are collapsed. */
function stripPromptTags(text) {
  return String(text).replace(PromptTagBlock, '').replace(PromptTagOpen, '').replace(/\n{3,}/g, '\n\n').trim();
}

/** A user record that the person did not type: a command, its output or caveat, a background task notification. */
const NotPrompt = /^\s*(?:<(?:command-|local-command|task-notification)|Caveat:)/;

/** Claude Code: the text of a prompt the person typed (not a subagent's, not a command's output or caveat). */
function claudeUserText(record) {
  if (record?.type !== 'user' || record.isSidechain === true || record.isMeta === true) {
    return null;
  }
  const content = record.message?.content;
  const text = Array.isArray(content)
    ? content
        .filter((block) => block?.type === 'text' && typeof block.text === 'string')
        .map((block) => block.text)
        .join('\n\n')
    : content;
  return typeof text === 'string' && !NotPrompt.test(text) ? text : null;
}

/** Antigravity: the content of an input of the person, inside its <USER_REQUEST> wrapper when it has one. */
function antigravityUserText(record) {
  if (record?.type !== 'USER_INPUT' || typeof record.content !== 'string') {
    return null;
  }
  const request = /<USER_REQUEST>([\s\S]*?)<\/USER_REQUEST>/.exec(record.content);
  return request ? request[1].trim() : record.content;
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
  MaxFirstPromptLength,
  clip,
  stripPromptTags,
  extractReply,
  extractQuestion,
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
};
