'use strict';

// The checks of what chat-status.js reads from Antigravity's own files, for its self-test (self-test.js runs them): a
// subagent's conversation is never recorded or listed, the title of annotations/<conversation>.pbtxt, the first
// prompt inside <USER_REQUEST>, and the payload's lastUserInput. Every file is a fake tree under the self-test folder.

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const chat = require('../chat-status.js');
const antigravityFiles = require('./antigravity.js');

function antigravityChecks(helpers) {
  const { freshDirectory, readChat, writeJsonl, antigravity } = helpers;
  const checks = [];
  const check = (name, body) => checks.push({ name, body });

  /** A fake Antigravity folder with one conversation: its transcript (when given) and annotation (when given). */
  const conversation = (root, name, id, records, annotation) => {
    const agy = freshDirectory(root, name);
    const logs = path.join(agy, 'brain', id, '.system_generated', 'logs');
    if (records !== null) {
      writeJsonl(path.join(logs, 'transcript_full.jsonl'), records);
    }
    if (annotation !== null) {
      fs.mkdirSync(path.join(agy, 'annotations'), { recursive: true });
      fs.writeFileSync(path.join(agy, 'annotations', `${id}.pbtxt`), annotation);
    }
    return { agy, transcriptPath: path.join(logs, 'transcript.jsonl') };
  };
  const system = { type: 'SYSTEM_MESSAGE', source: 'SYSTEM', content: 'You are a reviewer.' };
  const person = {
    type: 'USER_INPUT',
    source: 'USER_EXPLICIT',
    content: '<USER_REQUEST>\nSurvey the hooks\n</USER_REQUEST>',
  };
  const answer = { type: 'PLANNER_RESPONSE', source: 'MODEL', content: 'Verdict: CLEAN. Shall I go on?' };

  check('antigravity: a subagent (its transcript opens with a system message) is never recorded or listed', (root) => {
    const directory = freshDirectory(root, 'agy-subagent');
    const { agy, transcriptPath } = conversation(root, 'agy-sub-tree', 'g1', [system, answer], null);
    const file = path.join(directory, chat.chatFileName('antigravity', 'g1'));
    assert.strictEqual(chat.applyPayload(antigravity('PreInvocation', { transcriptPath }), directory, 10), '{}');
    assert.ok(!fs.existsSync(file), 'no file in the first place');
    // a file from before the hook could tell: removed at the next event, and never listed meanwhile
    fs.writeFileSync(
      file,
      JSON.stringify({ agent: 'antigravity', session: 'g1', state: 'waiting', since: 1, updatedAt: 20 })
    );
    assert.deepStrictEqual(chat.listChats({ directory, now: 30, antigravityRoot: agy }), []);
    assert.ok(chat.listChats({ directory, now: 30, antigravityRoot: freshDirectory(root, 'agy-empty') }).length === 1);
    chat.applyPayload(antigravity('Stop', { fullyIdle: true, transcriptPath }), directory, 40);
    assert.ok(!fs.existsSync(file), 'removed');
  });

  check(
    'antigravity: without a transcript, an annotation without a title marks a subagent; none tells nothing',
    (root) => {
      const directory = freshDirectory(root, 'agy-annotation');
      const untitled = conversation(root, 'agy-untitled', 'g1', null, 'last_user_view_time:{seconds:1  nanos:2}\n');
      chat.applyPayload(antigravity('PreInvocation', { transcriptPath: untitled.transcriptPath }), directory, 10);
      assert.strictEqual(readChat(directory, 'antigravity', 'g1'), null);
      const unknown = conversation(root, 'agy-unknown', 'g1', null, null);
      chat.applyPayload(antigravity('PreInvocation', { transcriptPath: unknown.transcriptPath }), directory, 20);
      assert.strictEqual(readChat(directory, 'antigravity', 'g1').state, 'working', 'recorded while nothing tells');
    }
  );

  check('antigravity: the title of the annotation, the request inside <USER_REQUEST>, lastUserInput', (root) => {
    const directory = freshDirectory(root, 'agy-title');
    // protobuf text format: raw UTF-8, an escaped quote and octal bytes (\346\227\245 is U+65E5)
    const annotation = 'title:"Hook \\"survey\\" \\346\\227\\245 調査"  last_user_view_time:{seconds:1}\n';
    const { agy, transcriptPath } = conversation(root, 'agy-title-tree', 'g1', [person, answer], annotation);
    chat.applyPayload(
      antigravity('Stop', { fullyIdle: true, transcriptPath, lastUserInput: 'ignored' }),
      directory,
      10
    );
    const record = readChat(directory, 'antigravity', 'g1');
    const title = 'Hook "survey" 日 調査';
    assert.deepStrictEqual(
      [record.agentTitle, record.firstPrompt, record.lastPrompt],
      [title, 'Survey the hooks', 'Survey the hooks']
    );
    assert.strictEqual(chat.listChats({ directory, now: 20, antigravityRoot: agy })[0]?.agentTitle, title);
    // no transcript: the latest input of the payload
    const bare = conversation(root, 'agy-bare', 'g2', null, null);
    const payload = {
      conversationId: 'g2',
      fullyIdle: true,
      transcriptPath: bare.transcriptPath,
      lastUserInput: 'Go on',
    };
    chat.applyPayload(antigravity('Stop', payload), directory, 30);
    assert.strictEqual(readChat(directory, 'antigravity', 'g2').lastPrompt, 'Go on');
  });

  check('antigravity: protobuf text escapes and the folder of a transcript path', () => {
    assert.strictEqual(antigravityFiles.unescapeText('a\\"b\\\\c\\n\\303\\251'), 'a"b\\c\né');
    const transcript = path.join('x', 'agy', 'brain', 'g1', '.system_generated', 'logs', 'transcript.jsonl');
    assert.strictEqual(antigravityFiles.rootOf(transcript), path.resolve('x', 'agy'));
    assert.strictEqual(antigravityFiles.rootOf(null), antigravityFiles.DefaultRoot);
  });

  return checks;
}

module.exports = { antigravityChecks };
