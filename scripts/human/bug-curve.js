#!/usr/bin/env node
// Draws the number of open issues in the milestones M1-M3 for each day as a stacked area SVG,
// with the issues opened (up) and closed (down) on each day as bars on the same axis.
// The milestone of an issue is its current one; GitHub keeps no cheap history of it.
// Needs an authenticated `gh` (GH_TOKEN in GitHub Actions).
//
// Usage: node scripts/human/bug-curve.js --out <file.svg> [--repo <owner/name>]
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

// fill: the area gradient (opacity from top to bottom); line: the top edge and the legend swatch.
const Series = [
  {
    key: 'M1', name: 'M1 Wrong Results', opacity: [0.55, 0.15], lineWidth: 2, dash: '',
    light: { fill: '#e46962', line: '#b3261e' }, dark: { fill: '#f28b82', line: '#ffb4ab' },
  },
  {
    key: 'M2', name: 'M2 Build Failures & Missing Diagnostics', opacity: [0.45, 0.10], lineWidth: 2, dash: '',
    light: { fill: '#f2b90f', line: '#a45f00' }, dark: { fill: '#fdd663', line: '#ffb951' },
  },
  {
    key: 'M3', name: 'M3 Unconfirmed Suspicions', opacity: [0.25, 0.05], lineWidth: 1.5, dash: '4 2',
    light: { fill: '#74777f', line: '#74777f' }, dark: { fill: '#c4c6d0', line: '#c4c6d0' },
  },
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
        console.log('Usage: node scripts/human/bug-curve.js --out <file.svg> [--repo <owner/name>]');
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
  parts.push(`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" font-family="Roboto,system-ui,-apple-system,'Segoe UI',sans-serif">`);
  parts.push(`<title>Open bugs by milestone (M1-M3), ${formatDate(last.day)}</title>`);
  const seriesVars = mode => Series.map((s, i) => `--s${i}f:${s[mode].fill};--s${i}l:${s[mode].line};`).join('');
  parts.push('<style>');
  parts.push(`svg{--surface:#fcfcff;--text1:#1a1c1e;--text2:#44474f;--grid:#e1e2e8;--axis:#74777f;--opened:#44474f;--closed:#00897b;${seriesVars('light')}}`);
  parts.push(`@media (prefers-color-scheme:dark){svg{--surface:#191c1e;--text1:#e2e2e6;--text2:#c4c6d0;--grid:#2e3135;--axis:#8e9099;--opened:#e2e2e6;--closed:#80cbc4;${seriesVars('dark')}}}`);
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
  parts.push('<defs>');
  parts.push(`<clipPath id="reveal"><rect class="wipe" x="${plot.left}" y="0" width="${plot.right - plot.left}" height="${height}"/></clipPath>`);
  Series.forEach((s, i) => {
    parts.push(`<linearGradient id="g${i}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" style="stop-color:var(--s${i}f);stop-opacity:${s.opacity[0]}"/><stop offset="1" style="stop-color:var(--s${i}f);stop-opacity:${s.opacity[1]}"/></linearGradient>`);
  });
  parts.push('</defs>');
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
  legend(56, Series.map((s, i) => ({ fill: `var(--s${i}l)`, label: `${s.name}  ${last.counts[i]}` })));
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

  // Stacked areas with a gradient fill, revealed from left to right; a line in the series color marks each top edge.
  parts.push('<g clip-path="url(#reveal)">');
  const lower = days.map(() => 0);
  const labels = [];
  Series.forEach((s, i) => {
    const upper = days.map((d, k) => lower[k] + d.counts[i]);
    const edge = upper.map((v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`);
    const base = lower.map((v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`).reverse();
    const dash = s.dash ? ` stroke-dasharray="${s.dash}"` : '';
    parts.push(`<polygon points="${edge.concat(base).join(' ')}" fill="url(#g${i})"/>`);
    parts.push(`<polyline points="${edge.join(' ')}" fill="none" stroke="var(--s${i}l)" stroke-width="${s.lineWidth}" stroke-linejoin="round"${dash}/>`);

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
      parts.push(`<rect class="bar up" ${style} x="${left}" y="${y(d.opened).toFixed(1)}" width="${barWidth.toFixed(1)}" height="${(y(0) - y(d.opened)).toFixed(1)}" rx="2" fill="var(--opened)" stroke="var(--surface)" stroke-width="1.5"/>`);
    }
    if (d.closed > 0) {
      parts.push(`<rect class="bar down" ${style} x="${left}" y="${y(0).toFixed(1)}" width="${barWidth.toFixed(1)}" height="${(y(-d.closed) - y(0)).toFixed(1)}" rx="2" fill="var(--closed)" fill-opacity="0.55" stroke="var(--surface)" stroke-width="1"/>`);
    }
  });
  parts.push(`<line x1="${plot.left}" x2="${plot.right}" y1="${y(0)}" y2="${y(0)}" stroke="var(--axis)" stroke-width="1.5"/>`);
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
