#!/usr/bin/env node
// Checks the frontmatter of the agent rule files (.agents/rules/*.md), which Antigravity needs to load a rule: a file
// without it is never loaded automatically. Each file must start with a YAML block that sets `trigger` (always_on,
// glob or model_decision; manual is refused, since nothing would load it), a `description`, and for `glob` a quoted
// `globs` string of comma-separated patterns that start with `**/`. It also checks the limits of Antigravity: each
// file at most 24,000 bytes (longer ones are cut), and the always-on rules with AGENTS.md within a share of the
// 20,000-token rules budget, leaving room for the global rules (estimated at 4 bytes per token for ASCII and one token
// per other character). Every rule file must be imported by CLAUDE.md, which Claude Code reads.
// Usage: node scripts/human/check-rule-frontmatter.js [--root <dir>] [--self-test] [--help]
'use strict';

const fs = require('fs');
const path = require('path');

const Triggers = ['always_on', 'glob', 'model_decision'];
const Keys = ['trigger', 'globs', 'description'];
const MaxFileBytes = 24000;
// The 20,000-token budget is shared with the global rules of each machine (~/.gemini/GEMINI.md)
const MaxAlwaysOnTokens = 14000;

const helpText = [
    'Usage: node scripts/human/check-rule-frontmatter.js [--root <dir>] [--self-test]',
    '',
    '  --root       Repository root (default: the root of this script\'s checkout).',
    '  --self-test  Check the parser and the rules on fixed inputs.',
    '  --help       Show this help.',
    '',
    'Exits 1 when a rule file has no valid frontmatter, is too large, is missing from CLAUDE.md, or the always-on',
    'rules exceed their share of the rules budget.',
].join('\n');

/** The frontmatter of a Markdown text as { fields, error }; fields maps each key to its raw value. */
function parseFrontmatter(text) {
    const lines = text.replace(/^﻿/, '').split(/\r?\n/);
    if (lines[0] !== '---') {
        return { fields: null, error: 'does not start with a --- frontmatter block' };
    }
    const end = lines.indexOf('---', 1);
    if (end < 0) {
        return { fields: null, error: 'has no closing --- for its frontmatter' };
    }
    const fields = new Map();
    for (const line of lines.slice(1, end)) {
        const match = /^([A-Za-z_]+):\s*(.*)$/.exec(line);
        if (!match) {
            return { fields: null, error: `has a frontmatter line that is not "key: value": ${line}` };
        }
        if (fields.has(match[1])) {
            return { fields: null, error: `sets ${match[1]} twice` };
        }
        fields.set(match[1], match[2]);
    }
    return { fields, error: null };
}

/** The problems of one rule file's frontmatter (empty when it is valid). */
function checkFields(fields) {
    const problems = [];
    for (const key of fields.keys()) {
        if (!Keys.includes(key)) {
            problems.push(`unknown key ${key} (allowed: ${Keys.join(', ')})`);
        }
    }
    const trigger = fields.get('trigger');
    if (!Triggers.includes(trigger)) {
        const given = trigger === undefined ? 'missing' : trigger;
        problems.push(`trigger must be one of ${Triggers.join(', ')}, not ${given}`);
    }
    const description = fields.get('description');
    if (!description) {
        problems.push('has no description');
    } else if (/: | #|^["'&*!|>%@`\[{]/.test(description)) {
        // A plain YAML value ends at " #" (a comment) and may not hold ": " (a key) or start with an indicator
        problems.push('the description holds ": ", " #" or a leading YAML indicator; rephrase it');
    }
    const globs = fields.get('globs');
    if (trigger === 'glob') {
        // A plain YAML value starting with * is an alias, so the patterns are one double-quoted string
        const quoted = /^"([^"]+)"$/.exec(globs ?? '');
        if (!quoted) {
            problems.push('a glob rule needs globs: "<pattern>, <pattern>" in double quotes');
        } else {
            for (const pattern of quoted[1].split(',').map((p) => p.trim())) {
                if (!pattern.startsWith('**/')) {
                    problems.push(`glob ${pattern} must start with **/ (Antigravity matches absolute paths)`);
                }
            }
        }
    } else if (globs !== undefined) {
        problems.push('globs is only read with trigger: glob');
    }
    return problems;
}

/** The estimated tokens of a text: 4 bytes per token for ASCII, one token per other character. */
function estimateTokens(text) {
    let ascii = 0;
    let other = 0;
    for (const ch of text) {
        if (ch.charCodeAt(0) < 128) {
            ascii++;
        } else {
            other++;
        }
    }
    return Math.ceil(ascii / 4) + other;
}

/** The problems of the rule files of a checkout, and the always-on token estimate. */
function checkRoot(root) {
    const rulesDir = path.join(root, '.agents', 'rules');
    const problems = [];
    const claudePath = path.join(root, 'CLAUDE.md');
    const claude = fs.existsSync(claudePath) ? fs.readFileSync(claudePath, 'utf8') : '';
    let alwaysOn = estimateTokens(fs.readFileSync(path.join(root, 'AGENTS.md'), 'utf8'));
    const files = fs.readdirSync(rulesDir).filter((f) => f.endsWith('.md')).sort();
    for (const file of files) {
        const full = path.join(rulesDir, file);
        const text = fs.readFileSync(full, 'utf8');
        const where = `.agents/rules/${file}`;
        const bytes = Buffer.byteLength(text, 'utf8');
        if (bytes > MaxFileBytes) {
            problems.push(`${where}: ${bytes} bytes, over the ${MaxFileBytes}-byte limit of a rule file`);
        }
        if (!claude.includes(`@.agents/rules/${file}`)) {
            problems.push(`${where}: not imported by CLAUDE.md (@.agents/rules/${file})`);
        }
        const { fields, error } = parseFrontmatter(text);
        if (error) {
            problems.push(`${where}: ${error}`);
            continue;
        }
        checkFields(fields).forEach((p) => problems.push(`${where}: ${p}`));
        if (fields.get('trigger') === 'always_on') {
            alwaysOn += estimateTokens(text);
        }
    }
    if (alwaysOn > MaxAlwaysOnTokens) {
        problems.push(
            `the always-on rules and AGENTS.md come to about ${alwaysOn} tokens, over ${MaxAlwaysOnTokens}: ` +
                'make a rule glob or model_decision, or shorten one',
        );
    }
    return { problems, alwaysOn, files: files.length };
}

function selfTest() {
    const assert = require('assert');
    const ok = (text) => parseFrontmatter(text).fields;
    // Frontmatter of one rule: '---', the given lines, '---'
    const rule = (...lines) => ok(['---', ...lines, '---', ''].join('\n'));
    assert.strictEqual(parseFrontmatter('# Title\n').error, 'does not start with a --- frontmatter block');
    assert.match(parseFrontmatter('---\ntrigger: glob\n').error, /no closing/);
    assert.deepStrictEqual(checkFields(ok('---\r\ntrigger: always_on\r\ndescription: d\r\n---\r\n')), []);
    assert.deepStrictEqual(checkFields(rule('trigger: glob', 'globs: "**/*.cs, **/src/**"', 'description: d')), []);
    assert.match(checkFields(ok('---\ntrigger: manual\ndescription: d\n---\n'))[0], /trigger must be/);
    assert.match(checkFields(ok('---\ntrigger: glob\nglobs: **/*.cs\ndescription: d\n---\n'))[0], /double quotes/);
    assert.match(checkFields(ok('---\ntrigger: glob\nglobs: "src/**"\ndescription: d\n---\n'))[0], /must start with/);
    assert.match(checkFields(ok('---\ntrigger: always_on\n---\n'))[0], /no description/);
    assert.match(checkFields(ok('---\ntrigger: always_on\ndescription: d\nglobs: "**/x"\n---\n'))[0], /only read/);
    assert.match(checkFields(rule('trigger: always_on', 'description: d', 'alwaysApply: true'))[0], /unknown key/);
    assert.match(checkFields(rule('trigger: always_on', 'description: Add # intent at the end'))[0], /rephrase/);
    assert.match(checkFields(ok('---\ntrigger: always_on\ndescription: Rules: none\n---\n'))[0], /rephrase/);
    assert.deepStrictEqual(checkFields(ok('---\ntrigger: always_on\ndescription: C# rules - none\n---\n')), []);
    assert.strictEqual(estimateTokens('abcdefgh'), 2);
    assert.strictEqual(estimateTokens('日本'), 2);
    process.stdout.write('check-rule-frontmatter self-test passed\n');
}

function main(argv) {
    let root = path.resolve(__dirname, '..', '..');
    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === '--help') {
            process.stdout.write(`${helpText}\n`);
            return 0;
        }
        if (argv[i] === '--self-test') {
            selfTest();
            return 0;
        }
        if (argv[i] === '--root' && argv[i + 1]) {
            root = path.resolve(argv[++i]);
            continue;
        }
        process.stderr.write(`unknown argument: ${argv[i]}\n${helpText}\n`);
        return 2;
    }
    const { problems, alwaysOn, files } = checkRoot(root);
    problems.forEach((p) => process.stderr.write(`${p}\n`));
    process.stdout.write(
        `${files} rule files, always-on rules with AGENTS.md about ${alwaysOn} tokens (limit ${MaxAlwaysOnTokens}); ` +
            `${problems.length} problem(s)\n`,
    );
    return problems.length === 0 ? 0 : 1;
}

process.exitCode = main(process.argv.slice(2));
