'use strict';

// The checks of the open questions for the self-test of chat-status.js (self-test-summary.js adds them): the heuristic
// kept until a new prompt, and the verdicts of Haiku (through a fake CLI) across turns: a question left unanswered, a
// report, a request without a question mark, a failed answer, a complaint of the person turned into a question, a
// paraphrase, and the deterministic guard groundedOpen.

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const chat = require('../chat-status.js');
const summary = require('./summary.js');
const openQuestions = require('./open-questions.js');

function openQuestionChecks(helpers) {
  const { freshDirectory, readChat, claude, fakeClaude } = helpers;
  const checks = [];
  const check = (name, body) => checks.push({ name, body });
  check('open questions, heuristic: kept until a new prompt, then only the latest reply counts', (root) => {
    const directory = freshDirectory(root, 'heuristic');
    const texts = () => readChat(directory, 'claude', 'c1').openQuestions.map((item) => item.text);
    chat.applyPayload(claude('UserPromptSubmit', { prompt: 'Fix it' }), directory, 10);
    chat.applyPayload(claude('Stop', { last_assistant_message: 'Fixed.\n\nMerge #1 now?' }), directory, 20);
    let record = readChat(directory, 'claude', 'c1');
    assert.deepStrictEqual([record.askVerdict, record.openBefore, record.turnPrompt], ['heuristic', [], 'Fix it']);
    assert.deepStrictEqual(texts(), ['Merge #1 now?']);
    // another Stop with no prompt between (a background task ended): the question stays, a new one goes first
    chat.applyPayload(claude('Stop', { last_assistant_message: 'Also: rebase #2?' }), directory, 30);
    assert.deepStrictEqual(texts(), ['Also: rebase #2?', 'Merge #1 now?']);
    record = readChat(directory, 'claude', 'c1');
    assert.deepStrictEqual([record.turnPrompt, record.openBefore.length], [null, 1]);
    chat.applyPayload(claude('Stop', { last_assistant_message: 'One more?' }), directory, 40);
    chat.applyPayload(claude('Stop', { last_assistant_message: 'And this?' }), directory, 50);
    assert.deepStrictEqual(texts(), ['And this?', 'One more?', 'Also: rebase #2?'], 'at most three, newest first');
    // a new prompt: only the latest reply counts
    chat.applyPayload(claude('UserPromptSubmit', { prompt: 'Something else' }), directory, 60);
    chat.applyPayload(claude('Stop', { last_assistant_message: 'Done with that.' }), directory, 70);
    assert.deepStrictEqual(texts(), []);
    assert.strictEqual(readChat(directory, 'claude', 'c1').turnPrompt, 'Something else');
  });

  /** Ends a turn: the prompt (or none) and the reply through the hook's events, then Haiku through the fake CLI. */
  const turn = (root, directory, now, prompt, reply, answer) => {
    if (prompt !== null) {
      chat.applyPayload(claude('UserPromptSubmit', { prompt }), directory, now - 5);
    }
    chat.applyPayload(claude('Stop', { last_assistant_message: reply }), directory, now);
    const file = path.join(directory, chat.chatFileName('claude', 'c1'));
    const log = path.join(root, `turn-${now}.log`);
    const env = { ...process.env, FAKE_CLAUDE_LOG: log, FAKE_CLAUDE_ANSWER: answer };
    const outcome = summary.runSummarizer(file, now, fakeClaude(root), { env });
    return {
      outcome,
      record: readChat(directory, 'claude', 'c1'),
      input: JSON.parse(fs.readFileSync(log, 'utf8')).input,
    };
  };
  const openTexts = (record) => record.openQuestions.map((item) => item.text);

  check('open questions, Haiku: a question the person did not answer stays through a report', (root) => {
    const directory = freshDirectory(root, 'unanswered');
    const first = turn(root, directory, 100, 'Fix it', 'Fixed. Merge #1 now?', '{"open": ["Merge #1 now?"]}');
    assert.deepStrictEqual([first.outcome, openTexts(first.record)], ['written', ['Merge #1 now?']]);
    const second = turn(
      root,
      directory,
      200,
      'Also look at the docs',
      'The docs are fine.',
      '{"open": ["Merge #1 now?"]}'
    );
    assert.match(second.input, /<open_before>\n- Merge #1 now\?\n<\/open_before>/, 'Haiku gets the open question');
    assert.match(second.input, /<turn_prompt>\nAlso look at the docs\n<\/turn_prompt>/, 'and the prompt of the turn');
    assert.match(second.input, /<latest>\nThe docs are fine\.\n<\/latest>/);
    assert.deepStrictEqual(openTexts(second.record), ['Merge #1 now?']);
    assert.deepStrictEqual(second.record.openQuestions[0].askedAt, 100, 'asked in the first turn');
    assert.deepStrictEqual([second.record.askVerdict, second.record.summaryOf], ['haiku', 200]);
    assert.strictEqual(summary.asksPerson(second.record), true, 'the chat still waits for the answer');
  });

  check('open questions, Haiku: a question answered in the next prompt is gone', (root) => {
    const directory = freshDirectory(root, 'answered');
    turn(root, directory, 100, 'Fix it', 'Fixed. Merge #1 now?', '{"open": ["Merge #1 now?"]}');
    const second = turn(root, directory, 200, 'Yes, merge it', 'Merged #1.', '{"title": "Merge #1", "open": []}');
    assert.deepStrictEqual([openTexts(second.record), second.record.title], [[], 'Merge #1']);
    assert.strictEqual(summary.asksPerson(second.record), false, 'a report: no row');
  });

  check('open questions, Haiku: a report followed by a question, and a request without a question mark', (root) => {
    const directory = freshDirectory(root, 'report-and-question');
    const reply = 'All three lanes passed; CI is green.\n\nWhich pull request goes first?';
    const both = turn(root, directory, 100, 'Run the lanes', reply, '{"open": ["Which pull request goes first?"]}');
    assert.deepStrictEqual(openTexts(both.record), ['Which pull request goes first?']);
    const request = 'Both are ready. Please decide the merge order.';
    const plain = turn(root, directory, 200, 'Thanks', request, '{"open": ["Which one merges first?"]}');
    assert.strictEqual(plain.record.asks, false, 'the heuristic sees no question mark');
    assert.strictEqual(summary.asksPerson(plain.record), true, 'Haiku does');
  });

  check('open questions, Haiku: no answer, prose around it or a failure keeps the heuristic', (root) => {
    const directory = freshDirectory(root, 'fallback');
    const prose = turn(root, directory, 100, 'Fix it', 'Merge #1 now?', 'Sure! {"open": []}');
    assert.deepStrictEqual([prose.outcome, prose.record.askVerdict], ['unreadable', 'heuristic']);
    assert.deepStrictEqual(openTexts(prose.record), ['Merge #1 now?'], 'not dropped because Haiku failed');
    assert.match(fs.readFileSync(path.join(directory, 'errors.log'), 'utf8'), /not the JSON asked for: Sure!/);
    const file = path.join(directory, chat.chatFileName('claude', 'c1'));
    chat.applyPayload(claude('Stop', { last_assistant_message: 'Still waiting for you.' }), directory, 200);
    const env = { ...process.env, FAKE_CLAUDE_EXIT: '3' };
    assert.strictEqual(summary.runSummarizer(file, 200, fakeClaude(root), { env }), 'failed');
    assert.deepStrictEqual(openTexts(readChat(directory, 'claude', 'c1')), ['Merge #1 now?']);
    assert.match(
      fs.readFileSync(path.join(directory, 'errors.log'), 'utf8'),
      /no summary of .*exit code 3 fake failure/
    );
  });

  check('open questions, Haiku: a complaint of the person turned into a question is dropped, also carried', (root) => {
    const directory = freshDirectory(root, 'roles');
    const turned = '{"title": "Queue panel", "open": ["Where is the screenshot of the resume?"]}';
    // the person's complaint, then a reply that states what was done and asks nothing
    const first = turn(
      root,
      directory,
      100,
      'There is no screenshot of the resume',
      'Right. I asked the helper to add one.\n\nI will rerun the checks and push.',
      turned
    );
    assert.strictEqual(first.outcome, 'written');
    assert.deepStrictEqual(openTexts(first.record), [], 'the complaint is not a question of the agent');
    // a later prompt and a statement reply: Haiku restates it again, still not the agent's
    const second = turn(root, directory, 200, 'Any news?', 'The checks passed and the push is done.', turned);
    assert.deepStrictEqual(openTexts(second.record), []);
    assert.strictEqual(second.record.askVerdict, 'haiku');
  });

  check('open questions, Haiku: a question the latest text asks stays, paraphrased or carried', (root) => {
    const directory = freshDirectory(root, 'grounded');
    const asked = turn(
      root,
      directory,
      100,
      'Fix it',
      'Fixed.\n\nShall I merge the pull request now?',
      '{"open": ["Shall I merge the pull request now?"]}'
    );
    assert.deepStrictEqual(openTexts(asked.record), ['Shall I merge the pull request now?']);
    // the person said something else; Haiku keeps the question the agent asked before
    const carried = turn(
      root,
      directory,
      200,
      'Also check the docs',
      'The docs are fine.',
      '{"open": ["Shall I merge the pull request now?"]}'
    );
    assert.deepStrictEqual(openTexts(carried.record), ['Shall I merge the pull request now?']);
    // a request without a question mark, written as a question by Haiku, with a prompt that shares few words
    const request = turn(
      root,
      directory,
      300,
      'ok',
      'Done.\n\nPlease review the pull request and tell me the verdict.',
      '{"open": ["Can you review the pull request and give a verdict?"]}'
    );
    assert.deepStrictEqual(openTexts(request.record), ['Can you review the pull request and give a verdict?']);
  });

  check('open questions: groundedOpen drops what the prompt explains better than the latest text', () => {
    const before = [{ text: 'Merge #1 now?', askedAt: 1 }];
    const result = openQuestions.groundedOpen(
      ['Merge #1 now?', 'Where is the screenshot of the resume?', 'Is the queue empty?'],
      before,
      'Is the queue empty yet? I will wait.',
      ['Any news?', 'There is no screenshot of the resume']
    );
    assert.deepStrictEqual(result, { kept: ['Merge #1 now?', 'Is the queue empty?'], dropped: 1 });
  });

  return checks;
}

module.exports = { openQuestionChecks };
