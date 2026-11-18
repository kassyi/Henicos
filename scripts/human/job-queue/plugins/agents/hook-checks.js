'use strict';

// Hook I/O checks for the built-in agents plugin using a neutral fake rule plugin (zero cross-plugin deps).
//
// Usage: invoked by `node scripts/agent/check-queued-command.js --self-test`.

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const hookIo = require('./hook-io.js');

function withAgentsConfig(fn) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-agents-'));
  try {
    fs.writeFileSync(path.join(tmpDir, 'queue.js'), '// stub\n');
    const fakeDir = path.join(tmpDir, 'fake-buildtool');
    fs.mkdirSync(fakeDir, { recursive: true });
    fs.writeFileSync(
      path.join(fakeDir, 'hook-rules.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './fake-buildtool',",
        '  rules() {',
        '    return [{',
        "      matches: ({ name }) => name === 'buildtool',",
        "      reason: () => ({ tool: 'buildtool', verb: 'test', text: 'buildtool test' }),",
        '    }];',
        '  },',
        '  rewrite(_segment, list) {',
        "    return list[0] === 'buildtool' ? { gpu: false, options: [] } : null;",
        '  },',
        '};',
      ].join('\n'),
    );
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(
      configPath,
      JSON.stringify({
        version: 1,
        entry: 'queue.js',
        plugins: [{ name: 'agents' }, { name: './fake-buildtool' }],
      }),
    );
    return fn(fs.realpathSync.native(configPath), fs.realpathSync.native(tmpDir));
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checks(assert, { createEngine }) {
  withAgentsConfig((configPath, tmpDir) => {
    const { hookAnswer } = createEngine({ configPath, tolerant: false });

    const bashRewrite = hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'buildtool test', timeout: 600000, run_in_background: true },
      cwd: tmpDir,
    });
    assert.deepStrictEqual(
      Object.keys(bashRewrite.hookSpecificOutput).sort(),
      ['additionalContext', 'hookEventName', 'updatedInput'],
      'Claude Bash rewrite keys',
    );
    assert.strictEqual(
      bashRewrite.hookSpecificOutput.updatedInput.command,
      "node queue.js run --shell bash -- 'buildtool test'",
      'Claude Bash rewritten command',
    );
    assert.strictEqual(bashRewrite.hookSpecificOutput.updatedInput.timeout, 600000, 'preserves extra tool_input');
    assert.strictEqual(bashRewrite.hookSpecificOutput.updatedInput.run_in_background, true);

    const pwshRewrite = hookAnswer({
      tool_name: 'PowerShell',
      tool_input: { command: "buildtool test 'a'" },
      cwd: tmpDir,
    });
    assert.strictEqual(
      pwshRewrite.hookSpecificOutput.updatedInput.command,
      "node queue.js run --shell pwsh -- 'buildtool test ''a'''",
      'Claude PowerShell rewrite',
    );

    const bashDeny = hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'buildtool test; echo done' },
      cwd: tmpDir,
    });
    assert.strictEqual(bashDeny.hookSpecificOutput.permissionDecision, 'deny', 'Claude Bash denial');
    assert.strictEqual(bashDeny.hookSpecificOutput.updatedInput, undefined);

    const origCwd = process.cwd();
    let antigravity;
    try {
      process.chdir(tmpDir);
      antigravity = hookAnswer({
        toolCall: { name: 'run_command', args: { CommandLine: 'buildtool test' } },
      });
    } finally {
      process.chdir(origCwd);
    }
    assert.strictEqual(antigravity.decision, 'deny', 'Antigravity denies and never rewrites');
    assert.strictEqual(antigravity.updatedInput, undefined);
    assert.strictEqual(antigravity.hookSpecificOutput, undefined);
    assert.strictEqual(
      hookIo.rewrite({ agent: 'antigravity', canRewrite: true }, 'cmd', 'ctx'),
      null,
      'hookIo.rewrite returns null for antigravity',
    );

    assert.strictEqual(hookAnswer({}), null, 'unknown payload returns null');
    assert.strictEqual(hookAnswer({ tool_name: 'Bash', tool_input: { command: 'echo ok' }, cwd: tmpDir }), null);
    assert.strictEqual(
      hookAnswer({ toolCall: { name: 'run_command', args: { CommandLine: 'echo ok' } } }),
      null,
    );
  });
}

module.exports = { checks };
