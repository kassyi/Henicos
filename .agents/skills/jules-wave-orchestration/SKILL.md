---
name: jules-wave-orchestration
description: Google Jules に Issue を渡す側（Wave の選定・仕様の作成・投入）と、Jules の PR をレビューしてマージできる状態に整える側の手順。Jules Wave を計画・投入するとき、Jules の PR をレビューするとき、その後片づけをするときに読む。Jules 自身が読む実行規則は jules-wave-spec。
---

# Jules Wave の投入とレビュー

Jules に仕事を渡し、戻ってきた PR を確かめる側の手順書です。Jules 自身が従う規則は [jules-wave-spec](../jules-wave-spec/SKILL.md) にあり、Issue には貼りません（Jules は `main` のファイルを読みます）。Jules の癖と仕様の書き方の注意は [references/know-how.md](references/know-how.md) を読んでください。

作業用のファイル（Wave の仕様、Issue 本文の退避、進行表）は ignore された `tmp/` に置きます: `tmp/issue/jules/wave<N>/`、`tmp/issue-priority.md`、`tmp/handoff-jules-waves.md`。

## A. Wave を投入する

1. **対象を選ぶ**: 1 Wave は 3〜4 件。互いに触るファイルが重ならないものにする。診断を足す Issue には、`node scripts/manage-diagnostics.js --next-id` で ID を先に割り当て、同じ Wave の中で重ならないようにする。Jules に任せないもの（know-how §4）は外す。
2. **コードと Issue の突き合わせ**: Issue の記述が今のコードと合っているかを `git grep` で確かめる（呼び出し元の数、ファイルの場所、同名クラスの有無など）。食い違いは仕様に「Correction」として書く。
3. **影響の実測**: 診断を追加・厳格化する Issue は、`tmp/` に `git clone --shared` で clone を作って試作し、ビルドと `just test` で本番コードとスナップショットへの影響を数える。終わったら clone を消す（B-8）。
4. **仕様を書く**（`tmp/issue/jules/wave<N>/`）:
   - `common.md`: `tmp/issue/jules/common.md`（短い雛形）をコピーし、Wave の番号だけ差し替える。
   - `<番号>.md`: PR 種別、Correction、事前に決めた事項、Phase 1（テスト）/ Phase 2（実装）の手順、触ってよいファイル、「Do not edit」（同じ Wave の別 PR が触るファイル）、承認してよい新しいスナップショット。
   - `orig_<番号>.md`: 投入前の Issue 本文の退避（`gh issue view <N> --json body --jq .body`）。編集しない。
   - `new_<番号>.md`: 投入する本文 = `orig` + `common` + `<番号>`。
   - **共通ルールを変えるときは `jules-wave-spec/SKILL.md` を直し、PR でマージしてから投入する。** マージ前の変更は Jules に届かない。
5. **承認を取る**: 仕様の要点を表にしてユーザーに見せ、承認を取る。号令があるまで投入しない。
6. **投入する**: `main` に最新の `jules-wave-spec/SKILL.md` があることを確かめてから、`gh issue edit <N> --body-file new_<N>.md --add-label jules`。Jules はラベルが付くと自分でブランチと PR を作る。本文とラベルが入ったことを確かめる。
7. **記録する**: `tmp/issue-priority.md`（Wave 表）、`tmp/handoff-jules-waves.md`（現況）。

## B. PR をレビューする

1. **概要**: `gh pr view <N> --json commits,files,body,closingIssuesReferences`。変更ファイルを仕様の許可リストと突き合わせる。
2. **差分**: `gh pr diff <N>` を読み、仕様の「事前に決めた事項」と禁止事項を照合する。置き換え先のヘルパーが元のコードと同じ意味かも確かめる。
3. **説明に無い削除を探す**: `gh pr diff <N> --name-only` と `git diff --stat` で、消えたファイル、大きく減ったファイル、消えた公開メンバーを洗い出し、仕様と PR の説明に書いてあるかを確かめる。書いていない削除は差し戻す。過去に Jules のコミット 4 つ（9bd8bd0b、88d1c039、14bf63d0、2dc23bdb）が、仕様に約束された機能・診断・テスト・ベンチマークを説明なしに消し、マージ後に `main` を上書きしていた（2026-10-24 に調査して復元）。
4. **検証用 clone**（`tmp/` に作る。scratchpad はパスが長すぎて nvcc が失敗する）:
   ```sh
   git clone --shared .. wave<N>-review && cd wave<N>-review
   git remote set-url origin https://github.com/kassyi/Henicos.git
   git fetch origin main pull/<A>/head:prA pull/<B>/head:prB
   git checkout -B review origin/main
   ```
5. **テスト先行の確認**: `main` に各 PR のテストファイルだけを `git checkout prA -- <テストファイル>` で当て、`dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --filter "FullyQualifiedName~<クラス名>"` で新しいテストが失敗することを確かめる（Jules は 1 コミットにまとめるので、履歴では分からない）。
6. **統合して全件テスト**: `git reset --hard origin/main` で戻し、PR を 1 本ずつ `git merge --no-edit` する。衝突は解消の仕方を決めてから直す（`diagnostics.json` と診断の索引の末尾は両方残す）。`just test`（CPU と CUDA）を回し、`.received.*` が出ないこと、既存のスナップショットが変わらないことを確かめる。
7. **警告とコンパイル対象外のファイル**:
   - `dotnet build Henicos.slnx -c Debug --no-incremental` の警告を見る（増分ビルドでは警告が出ない）。診断を足した PR は、本番コード（Stdlib、Benchmarks）に新しい診断が出ないことも見る。
   - `TestCases/` 配下の `.cs` はテストプロジェクトに入らないので、壊れても `just test` は通る。一時的なテストで `RoslynTestUtility.GetTestCaseContext(path)` の `compilation.GetDiagnostics()` と `TestGenerator(path).GetDiagnosticsAsync()` のエラーが 0 件であることを確かめ、一時テストを消す。
8. **後始末**: シェルを clone の外へ `cd` し、`dotnet build-server shutdown` を実行してから clone を消す。clone を残したまま本体で `just test` を回すと、`BannedSymbolsValidationTests` が clone の `BannedSymbols.txt` まで拾って失敗する。

## C. マージできる状態に整える

ユーザーがボタンを押すだけの状態にする。マージそのものと、Jules のブランチへの push はしない。

1. PR タイトルを `gh pr edit <N> --title "<type>(<area>): <summary> (#<Issue 番号>)"` で直す。Issue 番号は、その PR が閉じる Issue のもの。
2. 本文の先頭行 `Closes #N` が正しい Issue 番号か確かめ、違えば `gh pr edit <N> --body-file` で直す。テストの出力が入っていることも確かめる。
3. **Squash and merge** で入れるようユーザーに伝える。リポジトリの設定で、スカッシュのタイトルは PR タイトル、本文は PR 本文になる（`squash_merge_commit_title=PR_TITLE`、`squash_merge_commit_message=PR_BODY`）。

## D. 指摘の扱い

- **小さい修正**: マージ後にこちらでブランチ（`chore/wave<N>-review-followups`）を切って直す。コミットはユーザーの指示で行う。
- **Jules に依頼する**: `gh pr comment <N>` で `@jules` 付きの会話コメントを英語で出す。`gh pr review --comment` のレビュー本文には反応しない。依頼しても直らないことがあるので、コミットの有無ではなく差分の中身で確かめる。
- **衝突する PR は 1 本ずつマージする。ユーザーに GitHub の画面で衝突を解消させない**（`diagnostics.json` の項目が混ざって `main` のビルドが壊れたことがある）。検証用 clone で解消して `just test` を通したものを、こちらで別ブランチとして用意する。
- **マージ直後に `main` をビルドする**: `git pull` のあと `dotnet build src/Compiler/Kassyi.GSharp.Compiler`。

## E. Wave のあと

- `tmp/issue-priority.md` を、冒頭の自律更新の指示に従って更新する（閉じた Issue は消し、Wave 表は直近の 1 Wave だけ残す）。
- 新しく起票した Issue は、行・モデル区分・推奨順を追記し、Issue に推奨モデルのコメントを英語で付ける。
- `tmp/handoff-jules-waves.md` の現況を更新し、完了した Wave の細かい記録は `tmp/archive/` へ移す。
