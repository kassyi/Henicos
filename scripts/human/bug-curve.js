#!/usr/bin/env node
// Draws the number of open issues in the milestones M1-M3 for each day as a stacked area SVG,
// with the issues opened (up) and closed (down) as bars on the same axis. The bars sum a day, a week
// or a month, whichever keeps them readable for the length of the chart (--bucket fixes it).
// The milestone of an issue is its current one; GitHub keeps no cheap history of it.
// Needs an authenticated `gh` (GH_TOKEN in GitHub Actions), unless --issues gives the issues.
//
// Usage: node scripts/human/bug-curve.js --out <file.svg> [--repo <owner/name>]
//          [--bucket auto|day|week|month] [--issues <file.tsv>]
// --issues reads lines of "<created_at>\t<closed_at or empty>\t<milestone title>" instead of calling gh.
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const Usage = 'Usage: node scripts/human/bug-curve.js --out <file.svg> [--repo <owner/name>] [--bucket auto|day|week|month] [--issues <file.tsv>]';

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
const Colors = {
  light: '--surface:#fcfcff;--text1:#1a1c1e;--text2:#44474f;--grid:#e1e2e8;--axis:#74777f;--opened:#44474f;--closed:#00897b;',
  dark: '--surface:#191c1e;--text1:#e2e2e6;--text2:#c4c6d0;--grid:#2e3135;--axis:#8e9099;--opened:#e2e2e6;--closed:#80cbc4;',
};

const DayMs = 24 * 60 * 60 * 1000;
// Days start at 00:00 JST (UTC+9).
const OffsetMs = 9 * 60 * 60 * 1000;
const MonthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const Buckets = ['auto', 'day', 'week', 'month'];
// A bar narrower than this many pixels per bucket is unreadable, so auto moves to the next bucket.
const MinBucketPx = 6;
// Every tick interval from the finest; days is its rough length, used to keep ticks MinTickPx apart.
const TickIntervals = [
  ...[1, 2, 7, 14].map(n => ({ unit: 'day', n, days: n })),
  ...[1, 2, 3, 6].map(n => ({ unit: 'month', n, days: 30 * n })),
  ...[1, 2, 5, 10].map(n => ({ unit: 'year', n, days: 365 * n })),
];
// Ticks closer than this many pixels would overlap their labels.
const MinTickPx = 72;

const Width = 800;
const Height = 400;
const Plot = { left: 44, right: Width - 96, top: 104, bottom: Height - 32 };
// easeOutQuint for the areas, easeOutBack (a slight overshoot) for the bars.
const EaseOut = 'cubic-bezier(.22,1,.36,1)';
const EaseOutBack = 'cubic-bezier(.34,1.56,.64,1)';

// ---- Input ----

function parseArgs(argv) {
  const options = { out: '', repo: process.env.GITHUB_REPOSITORY || '{owner}/{repo}', bucket: 'auto', issues: '' };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--out': options.out = argv[++i] || ''; break;
      case '--repo': options.repo = argv[++i] || ''; break;
      case '--bucket': options.bucket = argv[++i] || ''; break;
      case '--issues': options.issues = argv[++i] || ''; break;
      case '--help':
        console.log(Usage);
        process.exit(0);
      default:
        throw new Error(`Unknown option: ${argv[i]}`);
    }
  }
  if (!options.out) {
    throw new Error('--out is required');
  }
  if (!Buckets.includes(options.bucket)) {
    throw new Error(`--bucket must be one of ${Buckets.join(', ')}`);
  }
  return options;
}

// Returns the issue lines ("<created_at>\t<closed_at>\t<milestone>") of every issue (not pull request) with a milestone.
function fetchIssueLines(repo) {
  const jq = '.[] | select(.pull_request == null and .milestone != null) | [.created_at, (.closed_at // ""), .milestone.title] | @tsv';
  return execFileSync(
    'gh',
    ['api', '--paginate', `repos/${repo}/issues?state=all&per_page=100`, '--jq', jq],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }
  );
}

// Returns [{ series, created, closed }] for the issues in M1-M3.
function parseIssues(lines) {
  const issues = [];
  for (const line of lines.split(/\r?\n/)) {
    const [created, closed, milestone] = line.split('\t');
    const series = Series.findIndex(s => milestone !== undefined && milestone.startsWith(s.key + ' '));
    if (series >= 0) {
      issues.push({ series, created: Date.parse(created), closed: closed ? Date.parse(closed) : Infinity });
    }
  }
  return issues;
}

// ---- Calendar (JST) ----

// The start (00:00 JST) of the day that contains ms.
function startOfDay(ms) {
  return Math.floor((ms + OffsetMs) / DayMs) * DayMs - OffsetMs;
}

// The calendar fields of ms in JST.
function jst(ms) {
  const date = new Date(ms + OffsetMs);
  return { year: date.getUTCFullYear(), month: date.getUTCMonth(), day: date.getUTCDate(), weekday: date.getUTCDay() };
}

// The start (00:00 JST) of the first day of the given JST month; month may run past 11.
function monthStart(year, month) {
  return Date.UTC(year, month, 1) - OffsetMs;
}

function formatDate(ms) {
  return new Date(ms + OffsetMs).toISOString().slice(0, 10);
}

// ---- Counting ----

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

// The start of the bucket that contains the day; weeks start on Monday.
function bucketStart(bucket, day) {
  const { year, month, weekday } = jst(day);
  switch (bucket) {
    case 'week': return day - ((weekday + 6) % 7) * DayMs;
    case 'month': return monthStart(year, month);
    default: return day;
  }
}

// The finest bucket whose bars are still MinBucketPx wide for the given width of a day.
function chooseBucket(dayPx) {
  if (dayPx >= MinBucketPx) {
    return 'day';
  }
  return dayPx * 7 >= MinBucketPx ? 'week' : 'month';
}

// Groups the days into buckets: [{ start, first, last, opened, closed }] with first / last as day indices.
function groupDays(days, bucket) {
  const groups = [];
  days.forEach((d, i) => {
    const start = bucketStart(bucket, d.day);
    const group = groups[groups.length - 1];
    if (group && group.start === start) {
      group.last = i;
      group.opened += d.opened;
      group.closed += d.closed;
    } else {
      groups.push({ start, first: i, last: i, opened: d.opened, closed: d.closed });
    }
  });
  return groups;
}

// ---- Ticks: [{ index, label }] with index as a day index ----

// Every n days; weekly ticks fall on Mondays, so that they line up with the week buckets.
// The year is shown on the first tick and where it changes.
function dayTicks(days, n) {
  const first = days[0].day;
  const offset = n % 7 === 0 ? (bucketStart('week', first) + 7 * DayMs - first) / DayMs % 7 : 0;
  const ticks = [];
  let previousYear = -1;
  for (let i = offset; i < days.length; i += n) {
    const { year } = jst(days[i].day);
    const date = formatDate(days[i].day);
    ticks.push({ index: i, label: year === previousYear ? date.slice(5) : date });
    previousYear = year;
  }
  return ticks;
}

// On the first day of every n-th month (unit 'month') or of every n-th January (unit 'year').
// A month tick shows the year on the first tick and on January.
function monthTicks(days, unit, n) {
  const first = days[0].day;
  const start = jst(first);
  const ticks = [];
  for (let m = start.day === 1 ? start.month : start.month + 1; ; m++) {
    const ms = monthStart(start.year, m);
    const index = Math.round((ms - first) / DayMs);
    if (index >= days.length) {
      return ticks;
    }
    const { year, month } = jst(ms);
    if (unit === 'year') {
      if (month === 0 && year % n === 0) {
        ticks.push({ index, label: `${year}` });
      }
    } else if (month % n === 0) {
      ticks.push({ index, label: month === 0 || ticks.length === 0 ? `${MonthNames[month]} ${year}` : MonthNames[month] });
    }
  }
}

// The finest tick interval whose ticks stay MinTickPx apart.
function chooseTicks(days, dayPx) {
  for (const { unit, n, days: length } of TickIntervals) {
    if (length * dayPx < MinTickPx) {
      continue;
    }
    const ticks = unit === 'day' ? dayTicks(days, n) : monthTicks(days, unit, n);
    if (ticks.length > 0) {
      return ticks;
    }
  }
  return [{ index: 0, label: formatDate(days[0].day) }];
}

// ---- Layout ----

function niceStep(span) {
  const rough = Math.max(span, 1) / 6;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const step = [1, 2, 5, 10].map(m => m * magnitude).find(s => s >= rough);
  return step || magnitude * 10;
}

// The buckets and the scales: x maps a day index, y a count. Half a day of padding on each side keeps the
// first and last bars inside the plot; the y axis runs below 0 for the closed bars.
function layout(days, bucketOption) {
  const slot = (Plot.right - Plot.left) / days.length;
  const bucket = bucketOption === 'auto' ? chooseBucket(slot) : bucketOption;
  const groups = groupDays(days, bucket);
  const totals = days.map(d => d.counts.reduce((a, b) => a + b, 0));
  const top = Math.max(...totals, ...groups.map(g => g.opened), 1);
  const bottom = Math.max(...groups.map(g => g.closed), 0);
  const step = niceStep(top + bottom);
  const yMax = Math.ceil(top / step) * step;
  const yMin = -Math.ceil(bottom / step) * step;
  return {
    slot, bucket, groups, totals, step, yMin, yMax,
    x: i => Plot.left + slot * (i + 0.5),
    y: v => Plot.bottom - ((v - yMin) / (yMax - yMin)) * (Plot.bottom - Plot.top),
  };
}

// ---- SVG parts: each returns the lines it adds ----

function escapeXml(text) {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function renderStyle() {
  const seriesVars = mode => Series.map((s, i) => `--s${i}f:${s[mode].fill};--s${i}l:${s[mode].line};`).join('');
  return [
    '<style>',
    `svg{${Colors.light}${seriesVars('light')}}`,
    `@media (prefers-color-scheme:dark){svg{${Colors.dark}${seriesVars('dark')}}}`,
    '.t1{fill:var(--text1)}.t2{fill:var(--text2)}',
    `.wipe{transform-origin:${Plot.left}px 0;animation:wipe 1.4s ${EaseOut} both}`,
    `.bar{transform-box:fill-box;animation:grow .7s ${EaseOutBack} both}`,
    '.up{transform-origin:50% 100%}.down{transform-origin:50% 0}',
    `.fade{animation:fade .6s ${EaseOut} 1s both}`,
    '@keyframes wipe{from{transform:scaleX(0)}to{transform:scaleX(1)}}',
    '@keyframes grow{from{transform:scaleY(0)}to{transform:scaleY(1)}}',
    '@keyframes fade{from{opacity:0}to{opacity:1}}',
    '@media (prefers-reduced-motion:reduce){.wipe,.bar,.fade{animation:none}}',
    '</style>',
  ];
}

// The clip path that reveals the areas from left to right, and the area gradients.
function renderDefs() {
  return [
    '<defs>',
    `<clipPath id="reveal"><rect class="wipe" x="${Plot.left}" y="0" width="${Plot.right - Plot.left}" height="${Height}"/></clipPath>`,
    ...Series.map((s, i) => `<linearGradient id="g${i}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" style="stop-color:var(--s${i}f);stop-opacity:${s.opacity[0]}"/><stop offset="1" style="stop-color:var(--s${i}f);stop-opacity:${s.opacity[1]}"/></linearGradient>`),
    '</defs>',
  ];
}

// One legend row of colored swatches and labels.
function renderLegendRow(row, items) {
  const lines = [];
  let legendX = 16;
  for (const { fill, label } of items) {
    lines.push(`<rect x="${legendX}" y="${row - 10}" width="12" height="12" rx="2" fill="${fill}"/>`);
    lines.push(`<text x="${legendX + 18}" y="${row}" font-size="12" class="t2">${escapeXml(label)}</text>`);
    legendX += 18 + label.length * 6.6 + 20;
  }
  return lines;
}

// The title, the update date and the legend with the counts of today and of the current bucket.
function renderHeader(days, { bucket, groups, totals }) {
  const last = days[days.length - 1];
  const current = groups[groups.length - 1];
  return [
    `<rect width="${Width}" height="${Height}" rx="8" fill="var(--surface)"/>`,
    `<text x="16" y="28" font-size="16" font-weight="600" class="t1">Open bugs by milestone</text>`,
    `<text x="${Width - 16}" y="28" font-size="12" text-anchor="end" class="t2">updated ${formatDate(last.day)} (JST) · total ${totals[totals.length - 1]}</text>`,
    ...renderLegendRow(56, Series.map((s, i) => ({ fill: `var(--s${i}l)`, label: `${s.name}  ${last.counts[i]}` }))),
    ...renderLegendRow(78, [
      { fill: 'var(--opened)', label: `Opened per ${bucket} (up)  ${current.opened}` },
      { fill: 'var(--closed)', label: `Closed per ${bucket} (down)  ${current.closed}` },
    ]),
  ];
}

// Horizontal grid lines with their counts (absolute values below 0), then the date ticks.
function renderAxes(days, { slot, step, yMin, yMax, x, y }) {
  const lines = [];
  for (let v = yMin; v <= yMax; v += step) {
    lines.push(`<line x1="${Plot.left}" x2="${Plot.right}" y1="${y(v)}" y2="${y(v)}" stroke="var(--grid)" stroke-width="1"/>`);
    lines.push(`<text x="${Plot.left - 8}" y="${y(v) + 4}" font-size="11" text-anchor="end" class="t2">${Math.abs(v)}</text>`);
  }
  for (const { index, label } of chooseTicks(days, slot)) {
    lines.push(`<line x1="${x(index)}" x2="${x(index)}" y1="${Plot.bottom}" y2="${Plot.bottom + 4}" stroke="var(--axis)" stroke-width="1"/>`);
    lines.push(`<text x="${x(index)}" y="${Plot.bottom + 18}" font-size="11" text-anchor="middle" class="t2">${label}</text>`);
  }
  return lines;
}

// Stacked areas with a gradient fill, revealed from left to right; a line in the series color marks each top edge.
// Returns the areas and, separately, the direct labels at the right end, which are drawn last.
function renderAreas(days, { x, y }) {
  const lines = ['<g clip-path="url(#reveal)">'];
  const labels = [];
  const last = days.length - 1;
  const point = (v, k) => `${x(k).toFixed(1)},${y(v).toFixed(1)}`;
  let lower = days.map(() => 0);
  Series.forEach((s, i) => {
    const upper = days.map((d, k) => lower[k] + d.counts[i]);
    const edge = upper.map(point);
    const base = lower.map(point).reverse();
    const dash = s.dash ? ` stroke-dasharray="${s.dash}"` : '';
    lines.push(`<polygon points="${edge.concat(base).join(' ')}" fill="url(#g${i})"/>`);
    lines.push(`<polyline points="${edge.join(' ')}" fill="none" stroke="var(--s${i}l)" stroke-width="${s.lineWidth}" stroke-linejoin="round"${dash}/>`);

    // A direct label only when the band is tall enough to point at.
    const count = days[last].counts[i];
    const bandHeight = y(lower[last]) - y(upper[last]);
    if (count > 0 && bandHeight >= 12) {
      const labelY = (y(lower[last]) + y(upper[last])) / 2 + 4;
      labels.push(`<text x="${Plot.right + 8}" y="${labelY.toFixed(1)}" font-size="12" class="t1 fade">${s.key} ${count}</text>`);
    }
    lower = upper;
  });
  lines.push('</g>');
  return { lines, labels };
}

// Bars in front of the areas, one per bucket and centered on the days it covers, each with a surface-colored
// ring, growing one after another; then the zero line. A partial bucket at either end is narrower.
function renderBars({ slot, groups, x, y }) {
  const lines = [];
  const delay = i => Math.round(200 + (i / Math.max(groups.length - 1, 1)) * 800);
  groups.forEach((g, i) => {
    const barWidth = Math.max(2, Math.min(16, (g.last - g.first + 1) * slot * 0.6));
    const left = ((x(g.first) + x(g.last)) / 2 - barWidth / 2).toFixed(1);
    const style = `style="animation-delay:${delay(i)}ms"`;
    if (g.opened > 0) {
      lines.push(`<rect class="bar up" ${style} x="${left}" y="${y(g.opened).toFixed(1)}" width="${barWidth.toFixed(1)}" height="${(y(0) - y(g.opened)).toFixed(1)}" rx="2" fill="var(--opened)" stroke="var(--surface)" stroke-width="1.5"/>`);
    }
    if (g.closed > 0) {
      lines.push(`<rect class="bar down" ${style} x="${left}" y="${y(0).toFixed(1)}" width="${barWidth.toFixed(1)}" height="${(y(-g.closed) - y(0)).toFixed(1)}" rx="2" fill="var(--closed)" fill-opacity="0.55" stroke="var(--surface)" stroke-width="1"/>`);
    }
  });
  lines.push(`<line x1="${Plot.left}" x2="${Plot.right}" y1="${y(0)}" y2="${y(0)}" stroke="var(--axis)" stroke-width="1.5"/>`);
  return lines;
}

function renderSvg(days, bucketOption) {
  const scale = layout(days, bucketOption);
  const areas = renderAreas(days, scale);
  return [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${Width}" height="${Height}" viewBox="0 0 ${Width} ${Height}" font-family="Roboto,system-ui,-apple-system,'Segoe UI',sans-serif">`,
    `<title>Open bugs by milestone (M1-M3), ${formatDate(days[days.length - 1].day)}</title>`,
    ...renderStyle(),
    ...renderDefs(),
    ...renderHeader(days, scale),
    ...renderAxes(days, scale),
    ...areas.lines,
    ...renderBars(scale),
    ...areas.labels,
    '</svg>',
  ].join('\n') + '\n';
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const lines = options.issues ? fs.readFileSync(options.issues, 'utf8') : fetchIssueLines(options.repo);
  const issues = parseIssues(lines);
  if (issues.length === 0) {
    throw new Error('No issue in M1-M3 was found');
  }
  const svg = renderSvg(countPerDay(issues, startOfDay(Date.now())), options.bucket);
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
