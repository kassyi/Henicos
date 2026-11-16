---
trigger: model_decision
description: Read before creating or recoloring a GitHub label - three tiers by style, one hue family per label, the dark-mode check.
---

# GitHub Label Colors

Labels are read mostly in GitHub's dark mode. Dark mode draws a label as its color at 18% opacity behind text in the
same color made lighter (Primer: the HSL lightness is raised by `0.6 - perceived lightness` when that is positive), so
two labels told apart only by lightness, or two pale ones, look alike there. The colors below are set in Material 3's
HCT space (hue, chroma, tone) and checked against that dark rendering.

## 1. Tiers

The tier is told by style, the label by hue family.

| Tier | Style | Labels |
| :- | :- | :- |
| Kind: what the issue or PR is | Vivid (HCT chroma 60–80, tone 45–68), any hue family but purple | bug, feature, refactor, test, documentation, performance, chore |
| Process: review and grouping | Purple only (HCT hue 290–295), lighter for less certain | agent-reviewed, tracking, human-reviewed |
| Note: how something was found | Pastel (HCT chroma 38–45, tone 85–88) | found-by:* |
| Added by a bot | Mid gray 969699 | dependencies, github_actions (Dependabot) |

Purple belongs to the process tier because it carries no verdict: green reads as "OK" and red or yellow as a warning.

## 2. Current colors

| Label | Family | Color | HCT (H / C / T) |
| :- | :- | :- | :- |
| bug | red (Material 3 error) | D3372F | 25 / 80 / 48 |
| performance | orange | CB6900 | 55 / 70 / 55 |
| chore | gold | D39D00 | 85 / 60 / 68 |
| test | green | 068A3A | 150 / 60 / 50 |
| documentation | cyan | 0091A1 | 210 / 50 / 55 |
| feature | blue | 0071D9 | 262 / 70 / 48 |
| refactor | magenta | C3379D | 345 / 75 / 48 |
| agent-reviewed | purple, light | D6CBFF | 295 / 45 / 84 |
| tracking | purple, middle | BCB2FF | 292 / 45 / 76 |
| human-reviewed | purple, deep | 8679FF | 290 / 75 / 58 |
| found-by:audit | pink | FFC6CC | 10 / 45 / 85 |
| found-by:matrix | cream | FFD889 | 85 / 45 / 88 |
| found-by:differential | mint | 95EDD8 | 180 / 38 / 88 |
| dependencies, github_actions | gray | 969699 | 260 / 4 / 62 |

## 3. Adding a label

1. Pick its tier from §1. A label that fits no tier is a question for the user before it is created.
2. Give it a hue family that no other label of the same tier uses. Gray stays for labels a bot adds; purple stays for
   the process tier.
3. Check it in dark mode against every label it can appear next to (the same issue or PR, or the same list): compute
   the dark text color with the Primer rule above and keep the OKLab distance to each of them at 0.08 or more. Labels
   that never meet (an issue-only label and a PR-only label) may be closer.
4. Create it with `gh label create <name> --color <hex> --description "<text>"`, after the user approved the name and
   color, and add it to §2 in the same change.
