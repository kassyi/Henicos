#!/usr/bin/env node
// Draws the number of open issues in the milestones M1-M3 for each day as a stacked area SVG.
// The milestone of an issue is its current one; GitHub keeps no cheap history of it.
// Needs an authenticated `gh` (GH_TOKEN in GitHub Actions).
//
// Usage: node scripts/bug-curve.js --out <file.svg> [--repo <owner/name>]
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const Series = [
  { key: 'M1', name: 'M1 Wrong Results', light: '#2a78d6', dark: '#3987e5' },
  { key: 'M2', name: 'M2 Build Failures & Missing Diagnostics', light: '#eb6834', dark: '#d95926' },
  { key: 'M3', name: 'M3 Unconfirmed Suspicions', light: '#1baf7a', dark: '#199e70' },
];
const DayMs = 24 * 60 * 60 * 1000;

function parseArgs(argv) {
  const options = { out: '', repo: process.env.GITHUB_REPOSITORY || '{owner}/{repo}' };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--out': options.out = argv[++i] || ''; break;
      case '--repo': options.repo = argv[++i] || ''; break;
      case '--help':
        console.log('Usage: node scripts/bug-curve.js --out <file.svg> [--repo <owner/name>]');
        process.exit(0);
      default:
        throw new Error(`Unknown option: ${argv[i]}`);
    }
  }
  if (!options.out) {
    throw new Error('--out is required');
  }
  return options;
}

// Returns [{ series, created, closed }] for the issues (not pull requests) in M1-M3.
function fetchIssues(repo) {
  const jq = '.[] | select(.pull_request == null and .milestone != null) | [.created_at, (.closed_at // ""), .milestone.title] | @tsv';
  const output = execFileSync(
    'gh',
    ['api', '--paginate', `repos/${repo}/issues?state=all&per_page=100`, '--jq', jq],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }
  );
  const issues = [];
  for (const line of output.split('\n')) {
    const [created, closed, milestone] = line.split('\t');
    const series = Series.findIndex(s => milestone !== undefined && milestone.startsWith(s.key + ' '));
    if (series < 0) {
      continue;
    }
    issues.push({ series, created: Date.parse(created), closed: closed ? Date.parse(closed) : Infinity });
  }
  return issues;
}

// counts[d][s]: issues of series s open at the end of day d (UTC).
function countOpenPerDay(issues, today) {
  const first = Math.floor(Math.min(...issues.map(i => i.created)) / DayMs) * DayMs;
  const days = [];
  for (let day = first; day <= today; day += DayMs) {
    const end = day + DayMs;
    const counts = Series.map(() => 0);
    for (const issue of issues) {
      if (issue.created < end && issue.closed >= end) {
        counts[issue.series]++;
      }
    }
    days.push({ day, counts });
  }
  return days;
}

function niceStep(max) {
  const rough = Math.max(max, 1) / 4;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const step = [1, 2, 5, 10].map(m => m * magnitude).find(s => s >= rough);
  return step || magnitude * 10;
}

function formatDate(ms) {
  return new Date(ms).toISOString().slice(0, 10);
}

function escapeXml(text) {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function renderSvg(days) {
  const width = 800;
  const height = 340;
  const plot = { left: 44, right: width - 96, top: 92, bottom: height - 32 };
  const totals = days.map(d => d.counts.reduce((a, b) => a + b, 0));
  const step = niceStep(Math.max(...totals));
  const yMax = Math.ceil(Math.max(...totals, 1) / step) * step;
  const x = i => plot.left + (days.length < 2 ? 0 : (i / (days.length - 1)) * (plot.right - plot.left));
  const y = v => plot.bottom - (v / yMax) * (plot.bottom - plot.top);
  const last = days[days.length - 1];

  const parts = [];
  parts.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" font-family="-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif">`);
  parts.push(`<title>Open bugs by milestone (M1-M3), ${formatDate(last.day)}</title>`);
  const lightVars = Series.map((s, i) => `--s${i}:${s.light};`).join('');
  const darkVars = Series.map((s, i) => `--s${i}:${s.dark};`).join('');
  parts.push('<style>');
  parts.push(`svg{--surface:#fcfcfb;--text1:#0b0b0b;--text2:#52514e;--grid:#e4e3df;${lightVars}}`);
  parts.push(`@media (prefers-color-scheme:dark){svg{--surface:#1a1a19;--text1:#f0efec;--text2:#c3c2b7;--grid:#3a3936;${darkVars}}}`);
  parts.push('.t1{fill:var(--text1)}.t2{fill:var(--text2)}');
  parts.push('</style>');
  parts.push(`<rect width="${width}" height="${height}" rx="8" fill="var(--surface)"/>`);
  parts.push(`<text x="16" y="28" font-size="16" font-weight="600" class="t1">Open bugs by milestone</text>`);
  parts.push(`<text x="${width - 16}" y="28" font-size="12" text-anchor="end" class="t2">updated ${formatDate(last.day)} · total ${totals[totals.length - 1]}</text>`);

  // Legend with today's counts, in stacking order from the bottom.
  let legendX = 16;
  Series.forEach((s, i) => {
    const label = `${s.name}  ${last.counts[i]}`;
    parts.push(`<rect x="${legendX}" y="46" width="12" height="12" rx="2" fill="var(--s${i})"/>`);
    parts.push(`<text x="${legendX + 18}" y="56" font-size="12" class="t2">${escapeXml(label)}</text>`);
    legendX += 18 + label.length * 6.6 + 20;
  });

  for (let v = 0; v <= yMax; v += step) {
    parts.push(`<line x1="${plot.left}" x2="${plot.right}" y1="${y(v)}" y2="${y(v)}" stroke="var(--grid)" stroke-width="1"/>`);
    parts.push(`<text x="${plot.left - 8}" y="${y(v) + 4}" font-size="11" text-anchor="end" class="t2">${v}</text>`);
  }

  const tickCount = Math.min(6, days.length);
  for (let t = 0; t < tickCount; t++) {
    const i = tickCount < 2 ? 0 : Math.round((t / (tickCount - 1)) * (days.length - 1));
    const anchor = t === 0 ? 'start' : t === tickCount - 1 ? 'end' : 'middle';
    parts.push(`<text x="${x(i)}" y="${plot.bottom + 18}" font-size="11" text-anchor="${anchor}" class="t2">${formatDate(days[i].day).slice(5)}</text>`);
  }

  // Stacked areas; the surface-colored stroke on each top edge separates the bands.
  const lower = days.map(() => 0);
  Series.forEach((s, i) => {
    const upper = days.map((d, k) => lower[k] + d.counts[i]);
    const top = upper.map((v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`);
    const bottom = lower.map((v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`).reverse();
    parts.push(`<polygon points="${top.concat(bottom).join(' ')}" fill="var(--s${i})"/>`);
    parts.push(`<polyline points="${top.join(' ')}" fill="none" stroke="var(--surface)" stroke-width="2"/>`);

    // Direct label at the right end when the band is tall enough to point at.
    const k = days.length - 1;
    const bandHeight = y(lower[k]) - y(upper[k]);
    if (last.counts[i] > 0 && bandHeight >= 12) {
      const labelY = (y(lower[k]) + y(upper[k])) / 2 + 4;
      parts.push(`<text x="${plot.right + 8}" y="${labelY.toFixed(1)}" font-size="12" class="t1">${s.key} ${last.counts[i]}</text>`);
    }
    upper.forEach((v, k2) => { lower[k2] = v; });
  });

  parts.push('</svg>');
  return parts.join('\n') + '\n';
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const issues = fetchIssues(options.repo);
  if (issues.length === 0) {
    throw new Error('No issue in M1-M3 was found');
  }
  const today = Math.floor(Date.now() / DayMs) * DayMs;
  const svg = renderSvg(countOpenPerDay(issues, today));
  fs.mkdirSync(path.dirname(path.resolve(options.out)), { recursive: true });
  fs.writeFileSync(options.out, svg);
  console.log(`Wrote ${options.out} (${issues.length} issues)`);
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
