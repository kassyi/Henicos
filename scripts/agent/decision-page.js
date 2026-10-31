#!/usr/bin/env node
'use strict';

// Builds a decision page: a single HTML file that lists the questions an agent needs the user to settle, each with
// options a/b/c (the recommended one first and pre-selected), and a bar that turns the user's picks into one line to
// paste back into the chat (`<page id>: all recommended` or `<page id>: Q1=a Q2=b ...`). Publish the file as an
// Artifact, or open it in a browser.
//
// Input is a JSON file:
//   {
//     "title": "...",                     // page and tab name, a few words
//     "pageId": "LN1031-7c2e",             // optional; generated when missing. Quote it in the chat
//     "lang": "ja",                        // optional, "ja" (default) or "en": the language of the page's own labels
//     "meta": ["2026-10-31", "base `f19cab3e6`"],   // optional short facts under the title
//     "intro": "...",                      // optional paragraph under the title
//     "sections": [
//       {
//         "heading": "...",
//         "text": "...",                   // optional paragraph
//         "table": { "headers": ["..."], "rows": [["..."]], "numeric": [3] },   // optional; numeric = column indexes
//         "questions": [
//           {
//             "id": "Q1",                  // the key in the pasted line, unique on the page
//             "title": "...",
//             "problem": "...",            // what goes wrong today (optional, but state it whenever there is one)
//             "note": "...",               // optional highlighted caveat (an exception to a rule, a risk)
//             "code": "...",               // optional code block (multi-line string) shown under the problem
//             "options": [                 // 2-4 options; the first is the recommendation
//               { "label": "...", "why": "...", "code": "..." }   // code: optional, e.g. the emitted shape
//             ]
//           }
//         ]
//       }
//     ]
//   }
// Text fields are plain text; `backticks` become inline code. Nothing else is interpreted, so HTML in the input is
// shown as text. Every question also gets an "Other" option with a text box; its text goes into the pasted line as
// `Q1=other:"..."`.
//
// Usage:
//   node scripts/agent/decision-page.js <input.json> [--out <file.html>]
//   node scripts/agent/decision-page.js --help
//
// Options:
//   --out <file.html>   Output path (default tmp/decision-pages/<pageId>.html under the repository root).

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const repoRoot = path.resolve(__dirname, '..', '..');

const labels = {
  ja: {
    recommended: '推奨',
    pageId: 'ページ ID',
    copy: '回答をコピー',
    copied: 'コピーしました',
    allRecommended: '全部推奨',
    other: 'その他',
    otherPlaceholder: '別の案や条件を書いてください（コードも可）',
    howTo: '推奨はあらかじめ選んであります。選び終わったら下の「回答をコピー」でチャットに貼ってください。',
  },
  en: {
    recommended: 'Recommended',
    pageId: 'Page ID',
    copy: 'Copy answer',
    copied: 'Copied',
    allRecommended: 'all recommended',
    other: 'Other',
    otherPlaceholder: 'Describe another option or condition (code is fine)',
    howTo: 'The recommended options are pre-selected. When you are done, paste the line from "Copy answer" into the chat.',
  },
};

function printHelp() {
  const text = fs.readFileSync(__filename, 'utf8');
  const lines = text.split(/\r?\n/);
  const start = lines.findIndex((l) => l.startsWith('// Builds'));
  const end = lines.findIndex((l, i) => i > start && !l.startsWith('//'));
  console.log(lines.slice(start, end).map((l) => l.replace(/^\/\/ ?/, '')).join('\n'));
}

function fail(message) {
  console.error(`decision-page: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const args = { input: null, out: null };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--help' || a === '-h') {
      printHelp();
      process.exit(0);
    } else if (a === '--out') {
      args.out = argv[++i];
      if (!args.out) fail('--out needs a path');
    } else if (a.startsWith('--')) {
      fail(`unknown option ${a}`);
    } else if (args.input === null) {
      args.input = a;
    } else {
      fail(`unexpected argument ${a}`);
    }
  }
  if (args.input === null) fail('no input file (see --help)');
  return args;
}

function escapeHtml(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// Plain text with `code` spans.
function inline(s) {
  return String(s)
    .split('`')
    .map((part, i) => (i % 2 === 1 ? `<code>${escapeHtml(part)}</code>` : escapeHtml(part)))
    .join('');
}

function validate(page) {
  if (typeof page.title !== 'string' || page.title === '') fail('"title" is required');
  if (!Array.isArray(page.sections) || page.sections.length === 0) fail('"sections" must be a non-empty array');
  if (page.lang !== undefined && !labels[page.lang]) fail(`unknown "lang" ${page.lang}`);
  const ids = new Set();
  page.sections.forEach((section, si) => {
    if (typeof section.heading !== 'string') fail(`section ${si + 1} has no "heading"`);
    (section.questions || []).forEach((q) => {
      if (typeof q.id !== 'string' || !/^[A-Za-z0-9_.-]+$/.test(q.id)) fail(`question id "${q.id}" must be letters, digits, _ . -`);
      if (ids.has(q.id)) fail(`question id "${q.id}" is used twice`);
      ids.add(q.id);
      if (!Array.isArray(q.options) || q.options.length < 2 || q.options.length > 4) {
        fail(`question ${q.id} needs 2-4 options`);
      }
      q.options.forEach((o, oi) => {
        if (typeof o.label !== 'string' || o.label === '') fail(`question ${q.id} option ${oi + 1} has no "label"`);
        if (o.code !== undefined && typeof o.code !== 'string') fail(`question ${q.id} option ${oi + 1}: "code" must be a string`);
      });
      if (q.code !== undefined && typeof q.code !== 'string') fail(`question ${q.id}: "code" must be a string`);
    });
  });
  if (ids.size === 0) fail('the page has no questions');
}

function renderTable(table) {
  const numeric = new Set(table.numeric || []);
  const head = table.headers.map((h) => `<th>${inline(h)}</th>`).join('');
  const rows = table.rows
    .map((row) => `<tr>${row.map((c, i) => `<td${numeric.has(i) ? ' class="num"' : ''}>${inline(c)}</td>`).join('')}</tr>`)
    .join('\n');
  return `<div class="tablewrap"><table><thead><tr>${head}</tr></thead><tbody>\n${rows}\n</tbody></table></div>`;
}

function codeBlock(code) {
  return code ? `<pre><code>${escapeHtml(code.replace(/\s+$/, ''))}</code></pre>` : '';
}

function renderQuestion(q, text) {
  const options = q.options
    .map((o, i) => {
      const letter = String.fromCharCode(97 + i);
      const rec = i === 0 ? `<span class="rec">${text.recommended}</span>` : '';
      const why = o.why ? `<span class="why">${inline(o.why)}</span>` : '';
      const code = o.code ? `<span class="optcode">${codeBlock(o.code)}</span>` : '';
      return `<label class="opt"><input type="radio" name="${q.id}" value="${letter}"${i === 0 ? ' checked' : ''}>` +
        `<b>${letter}. ${inline(o.label)}${rec}</b>${why}${code}</label>`;
    })
    .join('\n');
  const other = `<label class="opt"><input type="radio" name="${q.id}" value="other"><b>${escapeHtml(text.other)}</b>` +
    `<textarea class="othertext" id="other-${q.id}" rows="3" placeholder="${escapeHtml(text.otherPlaceholder)}"></textarea></label>`;
  const problem = q.problem ? `<p class="problem">${inline(q.problem)}</p>` : '';
  const note = q.note ? `<p class="note">${inline(q.note)}</p>` : '';
  return `<fieldset data-q="${q.id}"><legend>${escapeHtml(q.id)}. ${inline(q.title || '')}</legend>\n` +
    `${problem}${note}${codeBlock(q.code)}\n${options}\n${other}\n</fieldset>`;
}

function renderSection(section, text) {
  const parts = [`<h2>${inline(section.heading)}</h2>`];
  if (section.text) parts.push(`<p>${inline(section.text)}</p>`);
  if (section.table) parts.push(renderTable(section.table));
  (section.questions || []).forEach((q) => parts.push(renderQuestion(q, text)));
  return `<section>\n${parts.join('\n')}\n</section>`;
}

const style = `
/* Layout: one reading column; each section holds its questions as fieldsets with radio options (recommended first) */
:root {
  --bg: #f6f7f8; --surface: #ffffff; --fg: #1d2329; --muted: #5d6873; --line: #d8dde2;
  --accent: #1f6f5c; --accent-soft: #e3f1ec; --warn: #9a5b00; --warn-soft: #fbf0dd;
  --sans: "IBM Plex Sans JP", "Hiragino Sans", "Yu Gothic UI", system-ui, sans-serif;
  --mono: "IBM Plex Mono", ui-monospace, Consolas, monospace;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #14181b; --surface: #1c2125; --fg: #e4e8eb; --muted: #9aa5ae; --line: #323a40;
  --accent: #5cc3a6; --accent-soft: #1d332c; --warn: #e3a54a; --warn-soft: #36291a; color-scheme: dark } }
:root[data-theme="dark"] {
  --bg: #14181b; --surface: #1c2125; --fg: #e4e8eb; --muted: #9aa5ae; --line: #323a40;
  --accent: #5cc3a6; --accent-soft: #1d332c; --warn: #e3a54a; --warn-soft: #36291a; color-scheme: dark }
body { background: var(--bg); color: var(--fg); font-family: var(--sans); font-size: 15px; line-height: 1.7;
  padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); padding-block: 32px 96px; }
main { max-width: 760px; margin: 0 auto; display: flex; flex-direction: column; gap: 28px; }
header { display: flex; flex-direction: column; gap: 6px; }
header p { margin: 0; }
h1 { font-size: 1.6rem; margin: 0; text-wrap: balance; }
h2 { font-size: 1.15rem; margin: 0 0 4px; text-wrap: balance; }
code { font-family: var(--mono); font-size: .9em; }
.meta { color: var(--muted); font-size: .85rem; display: flex; flex-wrap: wrap; gap: 6px 16px; }
.pageid { font-family: var(--mono); background: var(--accent-soft); color: var(--accent); padding: 0 6px; border-radius: 4px; }
table { border-collapse: collapse; width: 100%; font-size: .88rem; }
.tablewrap { overflow-x: auto; }
th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid var(--line); vertical-align: top; }
th { color: var(--muted); font-weight: 500; font-size: .78rem; letter-spacing: .04em; }
td.num { font-variant-numeric: tabular-nums; white-space: nowrap; }
section { display: flex; flex-direction: column; gap: 14px; }
section > p { margin: 0; color: var(--muted); }
fieldset { border: 1px solid var(--line); background: var(--surface); border-radius: 8px; padding: 14px 16px; margin: 0; display: flex; flex-direction: column; gap: 10px; min-width: 0; }
legend { font-weight: 700; padding: 0 6px; }
.problem { margin: 0; font-size: .92rem; }
label.opt { display: grid; grid-template-columns: auto 1fr; gap: 2px 10px; padding: 8px 10px; border-radius: 6px; border: 1px solid transparent; cursor: pointer; }
label.opt:has(input:checked) { border-color: var(--accent); background: var(--accent-soft); }
label.opt input { margin-top: 6px; accent-color: var(--accent); }
.opt b { font-weight: 500; }
.opt .why { grid-column: 2; color: var(--muted); font-size: .85rem; }
.opt .optcode { grid-column: 2; min-width: 0; }
pre { margin: 0; background: var(--bg); border: 1px solid var(--line); border-radius: 6px; padding: 8px 10px; overflow-x: auto; font-size: .82rem; line-height: 1.5; }
pre code { font-size: inherit; }
.othertext { grid-column: 2; min-width: 0; font-family: var(--mono); font-size: .85rem; background: var(--bg); color: var(--fg); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; resize: vertical; }
.rec { font-size: .72rem; letter-spacing: .05em; color: var(--accent); border: 1px solid var(--accent); border-radius: 3px; padding: 0 5px; margin-left: 6px; white-space: nowrap; }
.note { background: var(--warn-soft); color: var(--fg); border-left: 3px solid var(--warn); padding: 8px 12px; border-radius: 4px; font-size: .88rem; margin: 0; }
.bar { position: fixed; left: 0; right: 0; bottom: 0; background: var(--surface); border-top: 1px solid var(--line);
  padding: 10px max(16px, env(safe-area-inset-right, 0px)) calc(10px + env(safe-area-inset-bottom, 0px)) max(16px, env(safe-area-inset-left, 0px)); }
.bar-inner { max-width: 760px; margin: 0 auto; display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
#answer { flex: 1 1 260px; min-width: 0; font-family: var(--mono); font-size: .82rem; background: var(--bg); color: var(--fg); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; overflow-wrap: anywhere; }
button { font: inherit; font-weight: 500; background: var(--accent); color: var(--surface); border: 0; border-radius: 6px; padding: 8px 16px; cursor: pointer; }
button:focus-visible, input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
#copied { color: var(--accent); font-size: .85rem; }
`;

function render(page) {
  const text = labels[page.lang || 'ja'];
  const meta = [`<span>${text.pageId} <span class="pageid">${escapeHtml(page.pageId)}</span></span>`]
    .concat((page.meta || []).map((m) => `<span>${inline(m)}</span>`))
    .join('');
  const intro = `<p>${page.intro ? inline(page.intro) + ' ' : ''}${escapeHtml(text.howTo)}</p>`;
  const script = `
const sets = [...document.querySelectorAll('fieldset[data-q]')];
const out = document.getElementById('answer');
function pick(f) {
  const v = (f.querySelector('input:checked') || {}).value || '?';
  return v === 'other' ? 'other:' + JSON.stringify(f.querySelector('.othertext').value.trim()) : v;
}
function render() {
  const picks = sets.map(f => [f.dataset.q, pick(f)]);
  const allRec = picks.every(([, v]) => v === 'a');
  out.textContent = ${JSON.stringify(page.pageId + ': ')} + (allRec ? ${JSON.stringify(text.allRecommended)} : picks.map(([q, v]) => q + '=' + v).join(' '));
}
document.addEventListener('change', render);
document.addEventListener('input', e => {
  if (!e.target.classList.contains('othertext')) return;
  const radio = e.target.closest('label').querySelector('input[type=radio]');
  if (e.target.value.trim() !== '') radio.checked = true;
  render();
});
render();
document.getElementById('copy').addEventListener('click', () => {
  const done = () => { const c = document.getElementById('copied'); c.hidden = false; setTimeout(() => { c.hidden = true; }, 1800); };
  const select = () => { const r = document.createRange(); r.selectNodeContents(out); const s = getSelection(); s.removeAllRanges(); s.addRange(r); };
  try { navigator.clipboard.writeText(out.textContent).then(done, select); } catch (e) { select(); }
});
`;
  return `<title>${escapeHtml(page.title)}</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans+JP:wght@400;500;700&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>${style}</style>
<main>
<header>
<h1>${inline(page.title)}</h1>
<div class="meta">${meta}</div>
${intro}
</header>
${page.sections.map((s) => renderSection(s, text)).join('\n')}
</main>
<div class="bar"><div class="bar-inner">
<div id="answer" aria-live="polite"></div>
<button id="copy" type="button">${escapeHtml(text.copy)}</button><span id="copied" hidden>${escapeHtml(text.copied)}</span>
</div></div>
<script>${script}</script>
`;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  let page;
  try {
    page = JSON.parse(fs.readFileSync(args.input, 'utf8'));
  } catch (e) {
    fail(`cannot read ${args.input}: ${e.message}`);
  }
  validate(page);
  if (!page.pageId) page.pageId = `D${crypto.randomBytes(4).toString('hex')}`;
  const out = args.out || path.join(repoRoot, 'tmp', 'decision-pages', `${page.pageId}.html`);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, render(page), 'utf8');
  console.log(`${page.pageId}\t${out}`);
}

main();
