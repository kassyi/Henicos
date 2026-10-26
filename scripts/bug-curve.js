#!/usr/bin/env node
// Draws the number of open issues in the milestones M1-M3 for each day as a stacked area SVG,
// with the issues opened (up) and closed (down) on each day as bars on the same axis.
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
// Days start at 00:00 JST (UTC+9).
const OffsetMs = 9 * 60 * 60 * 1000;
// easeOutQuint for the areas, easeOutBack (a slight overshoot) for the bars.
const EaseOut = 'cubic-bezier(.22,1,.36,1)';
const EaseOutBack = 'cubic-bezier(.34,1.56,.64,1)';

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

// The start (00:00 JST) of the day that contains ms.
function startOfDay(ms) {
  return Math.floor((ms + OffsetMs) / DayMs) * DayMs - OffsetMs;
}

// For each day (JST): counts[s] = issues of series s open at the end of the day,
// opened / closed = issues of M1-M3 created / closed during the day.
function countPerDay(issues, today) {
  const first = startOfDay(Math.min(...issues.map(i => i.created)));
  const days = [];
  for (let day = first; day <= today; day += DayMs) {
    const end = day + DayMs;
    const counts = Series.map(() => 0);
    let opened = 0;
    let closed = 0;
    for (const issue of issues) {
      if (issue.created < end && issue.closed >= end) {
        counts[issue.series]++;
      }
      if (issue.created >= day && issue.created < end) {
        opened++;
      }
      if (issue.closed >= day && issue.closed < end) {
        closed++;
      }
    }
    days.push({ day, counts, opened, closed });
  }
  return days;
}

function niceStep(span) {
  const rough = Math.max(span, 1) / 6;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const step = [1, 2, 5, 10].map(m => m * magnitude).find(s => s >= rough);
  return step || magnitude * 10;
}

function formatDate(ms) {
  return new Date(ms + OffsetMs).toISOString().slice(0, 10);
}

function escapeXml(text) {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function renderSvg(days) {
  const width = 800;
  const height = 400;
  const plot = { left: 44, right: width - 96, top: 104, bottom: height - 32 };
  const totals = days.map(d => d.counts.reduce((a, b) => a + b, 0));
  const top = Math.max(...totals, ...days.map(d => d.opened), 1);
  const bottom = Math.max(...days.map(d => d.closed), 0);
  const step = niceStep(top + bottom);
  const yMax = Math.ceil(top / step) * step;
  const yMin = -Math.ceil(bottom / step) * step;
  // Half a day of padding on each side so that the first and last bars stay inside the plot.
  const slot = (plot.right - plot.left) / days.length;
  const barWidth = Math.max(2, Math.min(16, slot * 0.5));
  const x = i => plot.left + slot * (i + 0.5);
  const y = v => plot.bottom - ((v - yMin) / (yMax - yMin)) * (plot.bottom - plot.top);
  const last = days[days.length - 1];
  const delay = i => Math.round(200 + (i / Math.max(days.length - 1, 1)) * 800);

  const parts = [];
  parts.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" font-family="-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif">`);
  parts.push(`<title>Open bugs by milestone (M1-M3), ${formatDate(last.day)}</title>`);
  const lightVars = Series.map((s, i) => `--s${i}:${s.light};`).join('');
  const darkVars = Series.map((s, i) => `--s${i}:${s.dark};`).join('');
  parts.push('<style>');
  parts.push(`svg{--surface:#fcfcfb;--text1:#0b0b0b;--text2:#52514e;--grid:#e4e3df;--opened:#3d3c39;--closed:#9a9892;${lightVars}}`);
  parts.push(`@media (prefers-color-scheme:dark){svg{--surface:#1a1a19;--text1:#f0efec;--text2:#c3c2b7;--grid:#3a3936;--opened:#e6e5e0;--closed:#7d7b75;${darkVars}}}`);
  parts.push('.t1{fill:var(--text1)}.t2{fill:var(--text2)}');
  parts.push(`.wipe{transform-origin:${plot.left}px 0;animation:wipe 1.4s ${EaseOut} both}`);
  parts.push(`.bar{transform-box:fill-box;animation:grow .7s ${EaseOutBack} both}`);
  parts.push('.up{transform-origin:50% 100%}.down{transform-origin:50% 0}');
  parts.push(`.fade{animation:fade .6s ${EaseOut} 1s both}`);
  parts.push('@keyframes wipe{from{transform:scaleX(0)}to{transform:scaleX(1)}}');
  parts.push('@keyframes grow{from{transform:scaleY(0)}to{transform:scaleY(1)}}');
  parts.push('@keyframes fade{from{opacity:0}to{opacity:1}}');
  parts.push('@media (prefers-reduced-motion:reduce){.wipe,.bar,.fade{animation:none}}');
  parts.push('</style>');
  parts.push(`<defs><clipPath id="reveal"><rect class="wipe" x="${plot.left}" y="0" width="${plot.right - plot.left}" height="${height}"/></clipPath></defs>`);
  parts.push(`<rect width="${width}" height="${height}" rx="8" fill="var(--surface)"/>`);
  parts.push(`<text x="16" y="28" font-size="16" font-weight="600" class="t1">Open bugs by milestone</text>`);
  parts.push(`<text x="${width - 16}" y="28" font-size="12" text-anchor="end" class="t2">updated ${formatDate(last.day)} (JST) · total ${totals[totals.length - 1]}</text>`);

  // Legend with today's counts: the stacked series (from the bottom), then the bars.
  const legend = (row, items) => {
    let legendX = 16;
    for (const { fill, label } of items) {
      parts.push(`<rect x="${legendX}" y="${row - 10}" width="12" height="12" rx="2" fill="${fill}"/>`);
      parts.push(`<text x="${legendX + 18}" y="${row}" font-size="12" class="t2">${escapeXml(label)}</text>`);
      legendX += 18 + label.length * 6.6 + 20;
    }
  };
  legend(56, Series.map((s, i) => ({ fill: `var(--s${i})`, label: `${s.name}  ${last.counts[i]}` })));
  legend(78, [
    { fill: 'var(--opened)', label: `Opened per day (up)  ${last.opened}` },
    { fill: 'var(--closed)', label: `Closed per day (down)  ${last.closed}` },
  ]);

  for (let v = yMin; v <= yMax; v += step) {
    parts.push(`<line x1="${plot.left}" x2="${plot.right}" y1="${y(v)}" y2="${y(v)}" stroke="var(--grid)" stroke-width="1"/>`);
    parts.push(`<text x="${plot.left - 8}" y="${y(v) + 4}" font-size="11" text-anchor="end" class="t2">${Math.abs(v)}</text>`);
  }

  const tickCount = Math.min(6, days.length);
  for (let t = 0; t < tickCount; t++) {
    const i = tickCount < 2 ? 0 : Math.round((t / (tickCount - 1)) * (days.length - 1));
    parts.push(`<text x="${x(i)}" y="${plot.bottom + 18}" font-size="11" text-anchor="middle" class="t2">${formatDate(days[i].day).slice(5)}</text>`);
  }

  // Stacked areas, revealed from left to right; the surface-colored stroke on each top edge separates the bands.
  parts.push('<g clip-path="url(#reveal)">');
  const lower = days.map(() => 0);
  const labels = [];
  Series.forEach((s, i) => {
    const upper = days.map((d, k) => lower[k] + d.counts[i]);
    const edge = upper.map((v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`);
    const base = lower.map((v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`).reverse();
    parts.push(`<polygon points="${edge.concat(base).join(' ')}" fill="var(--s${i})"/>`);
    parts.push(`<polyline points="${edge.join(' ')}" fill="none" stroke="var(--surface)" stroke-width="2"/>`);

    // Direct label at the right end when the band is tall enough to point at.
    const k = days.length - 1;
    const bandHeight = y(lower[k]) - y(upper[k]);
    if (last.counts[i] > 0 && bandHeight >= 12) {
      const labelY = (y(lower[k]) + y(upper[k])) / 2 + 4;
      labels.push(`<text x="${plot.right + 8}" y="${labelY.toFixed(1)}" font-size="12" class="t1 fade">${s.key} ${last.counts[i]}</text>`);
    }
    upper.forEach((v, k2) => { lower[k2] = v; });
  });
  parts.push('</g>');

  // Bars in front of the areas, each with a surface-colored ring, growing one after another.
  days.forEach((d, i) => {
    const left = (x(i) - barWidth / 2).toFixed(1);
    const style = `style="animation-delay:${delay(i)}ms"`;
    if (d.opened > 0) {
      parts.push(`<rect class="bar up" ${style} x="${left}" y="${y(d.opened).toFixed(1)}" width="${barWidth.toFixed(1)}" height="${(y(0) - y(d.opened)).toFixed(1)}" rx="2" fill="var(--opened)" stroke="var(--surface)" stroke-width="2"/>`);
    }
    if (d.closed > 0) {
      parts.push(`<rect class="bar down" ${style} x="${left}" y="${y(0).toFixed(1)}" width="${barWidth.toFixed(1)}" height="${(y(-d.closed) - y(0)).toFixed(1)}" rx="2" fill="var(--closed)" stroke="var(--surface)" stroke-width="2"/>`);
    }
  });
  parts.push(`<line x1="${plot.left}" x2="${plot.right}" y1="${y(0)}" y2="${y(0)}" stroke="var(--text2)" stroke-width="1"/>`);
  parts.push(...labels);

  parts.push('</svg>');
  return parts.join('\n') + '\n';
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const issues = fetchIssues(options.repo);
  if (issues.length === 0) {
    throw new Error('No issue in M1-M3 was found');
  }
  const today = startOfDay(Date.now());
  const svg = renderSvg(countPerDay(issues, today));
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
