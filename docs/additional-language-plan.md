# Additional languages: highlighting and manual selection

Status: steps 1 and 2 of the delivery sequence are implemented. Step 1 built the
capability split, its persistence tests, the highlighting spike recorded below, and the
F6 voting prompt. Step 2 added the highlighting adapter and the definitions for C, C++,
C#, Java and Visual Basic .NET, with a WPF test host for them. JavaScript, TypeScript,
Python, R, Rust and PHP are selectable but not yet colored. The eleven
formatting/detection voting issues below were created on 8 September 2026.

## Outcome and scope

Add Python, C, C++, Java, C#, JavaScript, TypeScript, Visual Basic .NET, R, Rust,
and PHP to both text-container language selectors. Selecting a language colors the
existing text in edit mode, on the board, and in exports. It does not rewrite the text.
The chosen language survives saving and reopening the board.

These languages initially provide highlighting only. F6 shows a language-specific
message inviting the user to vote for formatting and automatic detection on GitHub.
This plan interprets the request's "SQLBI Formatter" as Whiteboard's existing F6
command, both while editing and on a selected text container.

Visual Basic means VB.NET. VBA and VB6 are separate future scopes. JavaScript and
TypeScript initially cover ordinary source snippets; JSX and TSX are excluded. PHP
initially covers PHP code with or without opening tags, not HTML syntax highlighting
in mixed templates. Coloring is lexical: keywords, comments, literals, operators,
punctuation, and recognizable language constructs, without project-aware symbol
resolution, compilation, diagnostics, or completion.

No new content detection, extension inference, or `.wimport` fence aliases are added
in this phase. Existing import and paste behavior remains available; the user chooses
one of the new languages after creating a text container. An explicitly saved language
in a `.wboard` is restored. Existing Plain text, DAX, SQL Server, and KQL behavior stays
unchanged.

## Language inventory and voting destinations

Use stable language IDs, independent of menu captions. Each issue collects votes for
both formatting and detection for that language.

| Caption | ID | Highlighting cases to cover | Voting issue |
| --- | --- | --- | --- |
| Python | `python` | Decorators, comments, numeric literals, triple-quoted/raw strings, f-strings | [#108](https://github.com/sql-bi/SQLBI-Whiteboard/issues/108) |
| C | `c` | Comments, character/string literals, escapes, preprocessor directives, numeric suffixes | [#109](https://github.com/sql-bi/SQLBI-Whiteboard/issues/109) |
| C++ | `cpp` | C constructs plus templates, modern keywords, raw strings and their delimiters | [#110](https://github.com/sql-bi/SQLBI-Whiteboard/issues/110) |
| Java | `java` | Annotations, comments, strings, text blocks, modern keywords | [#111](https://github.com/sql-bi/SQLBI-Whiteboard/issues/111) |
| C# | `csharp` | Attributes, directives, verbatim/interpolated/raw strings, contextual keywords | [#112](https://github.com/sql-bi/SQLBI-Whiteboard/issues/112) |
| JavaScript | `javascript` | Comments, strings, template interpolation, regex literals versus division | [#113](https://github.com/sql-bi/SQLBI-Whiteboard/issues/113) |
| TypeScript | `typescript` | JavaScript constructs plus type keywords, annotations and generic syntax | [#114](https://github.com/sql-bi/SQLBI-Whiteboard/issues/114) |
| Visual Basic .NET | `vbnet` | Case-insensitive keywords, apostrophe/REM comments, escaped/interpolated strings, continuations, XML literals | [#115](https://github.com/sql-bi/SQLBI-Whiteboard/issues/115) |
| R | `r` | Comments, backtick names, strings/raw strings, formulas, pipes and custom infix operators | [#116](https://github.com/sql-bi/SQLBI-Whiteboard/issues/116) |
| Rust | `rust` | Nested comments, raw strings, lifetimes versus character literals, macro invocations | [#117](https://github.com/sql-bi/SQLBI-Whiteboard/issues/117) |
| PHP | `php` | Variables, comments, strings, heredoc/nowdoc and optional PHP tags | [#118](https://github.com/sql-bi/SQLBI-Whiteboard/issues/118) |

Do not promise exact IDE coloring for ambiguous contextual constructs. Recognize string
and comment boundaries correctly and favor conservative coloring for ambiguous names.

## 1. Separate selection from detection

The current `TextLanguageIds.All` serves both language inventory and default detection
order. Simply appending these eleven IDs would put them in Snippet format order and
settings migration. Split those responsibilities before adding services.

- In `src/SQLBI.Whiteboard.Core/Model/BoardObjects.cs`, register all fifteen IDs for
  persistence/selection, and introduce a distinct detection-capable list containing
  only DAX, SQL Server, KQL, and Plain text in their current default order.
- Make `Normalize` recognize the eleven new IDs. Keep the existing unknown-language
  fallback for genuinely unknown IDs.
- Make `NormalizeOrder` validate against the detection-capable list, ignoring
  manual-only IDs rather than converting them into Plain text. Preserve the existing
  treatment of unknown values unless a separate migration is needed.
- Update defaults and normalization in `Core/Settings/AppSettings.cs`; preserve chosen
  detection order, Plain-text-first behavior, and historical default migration.
- Keep `PreferencesWindow.xaml.cs` limited to detection-capable languages. Do not show
  unavailable features as reorderable detection options.
- `TextLanguageRegistry.All` exposes all fifteen services to `LanguageChipCombo` and
  `TextEditorLanguageCombo`. Retain the existing four entries in their current order;
  append the eleven entries in the inventory order above. Ensure the menu scrolls.
- Add an explicit detection capability to language metadata, separate from
  `CanFormat`. New services have detection disabled, `CanFormat = false`, and
  `TryAccept` always returns false. Their `TryFormat` returns false and unchanged text.
  Filter detection before calling `TryAccept`, so no manual-only analyzer runs on paste.

Decision 28's automatic insertion rule continues to apply to detection-capable
languages only. Record that qualification in `docs/decisions.md` when implemented.

## 2. Implement one reusable highlighting adapter

Use the existing AvalonEdit dependency as the first implementation candidate: inspect
the pinned package's highlighting definitions and highlighter API, then run a small
spike converting line highlighting into Whiteboard `StyledTextSpan` values. Do not
assume the bundled grammars cover all eleven languages or their modern syntax.

The spike must show identical spans in edit and display paths, multiline state across
lines, safe handling of incomplete code, and reuse by export. Use full-document
analysis rather than viewport-only coloring. Evaluate C# raw strings, JavaScript
template/regex syntax, and Rust nested comments early, because they expose grammar
limitations. Approve the engine approach against these cases before authoring all
eleven definitions. If XSHD cannot represent a construct reliably, use a small stateful
scanner for that language behind the same adapter rather than a growing single regex.

Implement the adapter and definitions inside `src/SQLBI.Whiteboard`; avoid introducing
eleven parser projects or external runtimes for highlighting. Reuse compatible bundled
definitions where they pass the corpus, and ship missing definitions as embedded
resources under a dedicated `Highlighting/` folder. Record source/version/license for
any imported definitions and include required notices. No runtime downloads.

The adapter must:

- Return ordered, nonoverlapping, in-bounds spans using UTF-16 offsets, matching the
  existing `TextClassificationColorizer` and export consumers. Resolve nested grammar
  styles into flat runs before returning them.
- Map token categories to a shared readable palette consistent with the existing
  languages, and use Consolas. Retain current wrapping, scaling and line-number policy.
- Preserve every source character, including whitespace and line endings. Highlight
  incomplete snippets without rejecting them; recover after unterminated constructs
  according to the language's lexical rules.
- Use generic titles such as "Python Code"; semantic definition-name discovery is
  outside this phase. Follow existing title-override behavior for imported containers.
- Reuse `TextLanguageAnalysis` for `TextContainerVisual`, the editor colorizer and
  `Export/EditableSlide.cs`, rather than adding an editor-only coloring path. Verify
  PDF/vector and preview rendering through their existing consumers too.
- Keep mutable document/highlighter state local to an analysis operation. Freeze WPF
  brushes before sharing across threads and bound caches. Enable background edit
  analysis where useful, respecting cancellation/stale-result handling already present.
- Check long lines and incomplete strings for pathological runtime. A failed analyzer
  must fall back to uncolored text without losing source or crashing display/export.

### Spike result: adopt the engine, author the definitions

A throwaway spike ran the pinned AvalonEdit 6.3.1.120 over fixtures for the constructs
named above, converting `DocumentHighlighter` line output into flat spans by painting each
line's sections in order and grouping the result. The engine is approved; the bundled
grammars are not.

What the adapter shape delivers, measured rather than assumed:

- Spans came back ordered, nonoverlapping and in bounds on every fixture, including
  unterminated strings and comments.
- Offsets are document UTF-16 offsets and stayed correct across a surrogate pair, which is
  what `TextClassificationColorizer` and the export consumers need.
- Span state carries across lines, so a block comment, a heredoc and a triple-quoted string
  color to their real end. CRLF and LF both work; no span ever covered a line delimiter.
- Empty input produces no spans. An unterminated construct colors to the end and the next
  construct recovers, which is the language's own lexical rule.
- 200 concurrent analyses sharing one definition returned identical spans, for a bundled
  definition and a hand-written one, so a definition can be shared and the document and
  highlighter kept local to the operation.
- Runtime is linear in the ordinary cases — 380 KB over 20,000 lines with an unterminated
  block comment took 79 ms — but one 429 KB line of 80,000 tokens took 837 ms. The adapter
  needs a size guard rather than trust.

What the bundled definitions do not cover. Seven of the eleven languages have one (Python,
C++, Java, C#, JavaScript, VB, PHP); C has to borrow C++, and R, Rust and TypeScript have
none at all. The seven that exist predate the syntax people write now: C# raw strings and
Java text blocks are read as an empty string followed by loose code, JavaScript template
literals are not recognized at all, Python decorators and Java annotations are uncolored,
and `1_000` colors as `1`. They are not a shortcut worth taking.

What our own XSHD can and cannot express, tested by writing three definitions:

- A span whose rule set contains itself gives true nested comments: `/* outer /* inner */
  still comment */` came back as one comment run, 2,000 levels deep took 59 ms, and an
  unterminated nested comment swallows the rest as Rust says it should.
- Ordering rules buys the ambiguous cases: a lifetime rule with a negative lookahead before
  the character rule separates `&'static` from `'a'`. Nested rule sets buy string escapes,
  and a span that re-enters the code rule set buys interpolation — `` `sum ${a + b} end` ``
  and a template nested inside its own hole both came out right.
- XSHD cannot count. Bounded rules cover C# raw strings at three, four and five quotes and
  Rust `r#"` and `r##"`, which is most real code, but a longer delimiter is beyond it.
- A rule regex cannot see far enough left to settle regex versus division: `/ab+c/gi` and
  `foo(/lit/, 1 / 2)` are right, while `let x = a / b, re2 = /x\/y/` misses the second
  literal, because the lookbehind window starts where the previous rule stopped.

So: reuse the AvalonEdit engine and the flattening adapter, ship our own definitions under
`Highlighting/` rather than the bundled ones, and keep a small stateful scanner behind the
same adapter for the constructs XSHD cannot reach.

Writing the first five definitions narrowed that last list. C# needs no scanner: three
rules, longest delimiter first, cover raw strings at three, four and five quotes, and a
snippet with six is not a snippet anyone writes. The same bounded trick serves C++ raw
string delimiters and Rust hashes. What is left for a scanner is JavaScript, where regex
versus division is not a matter of counting, and the same rule regex is right or wrong
depending only on where the previous rule stopped.

Two lexical hazards showed up that the spike had not: an apostrophe is a digit separator
in C and C++, so the character literal has to be a rule that runs after the number rather
than a span that runs before it; and an angle bracket opens a Visual Basic XML literal or
a comparison, so the literal is recognized only where a value belongs. Both are recorded
in the definitions themselves.

## 3. Add the F6 voting prompt

Associate an optional immutable `FormattingRequestUri` with each service (or its
descriptor), using the exact issue links above. Plain text has no voting URI. Keep
`CanFormat` false: showing a request dialog is not formatting support.

In `MainWindow.xaml.cs`, route both `FormatTextEdit` and `FormatSelectedText` through
one shared unsupported-format check before their current `!language.CanFormat` early
returns. A manual-only language opens the dialog; a supported formatter follows its
existing path. Invalid DAX/SQL/KQL input must not trigger a voting prompt.

Proposed copy, substituting the selected language:

> **Python formatting is not available yet**
>
> Vote for Python formatting and automatic detection on GitHub. Add a thumbs-up
> reaction to the issue's opening post to help us prioritize this language.

Show a clickable **Vote on GitHub** link and a **Close** button. The link opens the
language's fixed HTTPS issue URL in the default browser only after the user activates
it, following the application's existing shell-link pattern. It does not submit a vote;
GitHub handles sign-in and reactions. Never append the snippet or local paths to the URL.

The dialog should be owned by the main window, keyboard accessible, dismissible with
Escape, and avoid stacking on repeated F6. Closing it returns focus to the editor or
board without changing source, language, caret, selection, dimensions, dirty state, or
undo history. Handle browser-launch failures with a selectable/copyable issue URL.
Show no network request or dialog merely from selecting a language. Plain text and
F6 with no selected text remain quiet. Reuse this check for any additional format
entry points found during implementation.

## 4. Tests and release verification

Extend the framework-neutral smoke tests for language-ID persistence and detection-order
normalization. The existing harness targets `net10.0` and cannot directly host AvalonEdit
WPF highlighting; add focused Windows-targeted highlighting tests if the adapter remains
in WPF. If a new first-party test assembly is published, follow the signing requirements
in CONTRIBUTING.md; do not package test-only artifacts with the application.

Automated acceptance coverage:

- All eleven IDs round-trip through `.wboard`; older boards and unknown IDs keep their
  existing behavior. Document that older app versions may display the new IDs as plain
  text; this feature does not require changing the archive schema.
- Default and custom detection orders still contain only the original four languages.
  Manual-only IDs injected into settings are ignored. Existing paste fixtures produce
  the same result; none of the eleven new services accepts source automatically.
- Every language has fixtures for ordinary tokens, multiline constructs from the table,
  incomplete input, CRLF/LF, Unicode before tokens, and empty input. Assert meaningful
  token categories and valid spans, not just successful execution.
- Check string/comment containment, absence of text mutation, stale analysis after a
  language switch, and bounded behavior on representative large snippets.
- Each manual-only service resolves to its exact issue URI; Plain/DAX/SQL/KQL have no
  unsupported-feature prompt. Verify both format entry points use the shared behavior.

Manual acceptance matrix, for each language:

1. Paste text, choose the language in display mode, then edit and switch language again.
   Confirm the selector works with all fifteen entries and editing remains responsive.
2. Check multiline coloring, resizing, wrapping and zoom; save and reopen the board.
3. Export representative content to editable PowerPoint and vector PDF, and check the
   saved preview for matching text and highlighting.
4. Press F6 while editing and again in display mode. Confirm the correct language in
   the prompt and the exact GitHub destination; check keyboard dismissal, unchanged
   caret/selection/source/history, and browser failure fallback.
5. Confirm DAX, SQL and KQL still format, and Plain text remains quiet.

Run `dotnet build Whiteboard.sln -c Release`, the existing Core smoke-test executable,
and the focused Windows tests. Review new resource inclusion in a published build.
Update README, relevant guide/shortcut pages and release notes when the behavior ships;
do not describe the plan as an already available feature. Follow the repository's
branch, PR and version/release-note workflow.

## Delivery sequence and prioritization

1. Shared capability split, persistence tests, adapter spike and voting dialog.
2. C and C# definitions to establish the first end-to-end examples; C++, Java and VB.NET.
3. JavaScript and TypeScript together; Python and R; Rust and PHP.
4. Complete the eleven-language regression/export matrix and ship the complete set.

Use reviewable implementation PRs, keeping incomplete services out of the public
registry until their highlighting and voting links are ready. All eleven languages
are in the committed scope; voting does not gate their highlighting work.

For subsequent formatting/detection work, compare the number of thumbs-up reactions on
the opening post of these eleven issues when planning the next feature. Higher-voted
languages receive priority, with implementation dependencies and shared engines noted
when they affect delivery order. Comments can distinguish demand for formatting versus
detection; reaction totals deliberately measure the combined per-language request.
No automatic vote polling, telemetry, scheduling, or delivery dates are part of this phase.
When a capability ships, update its issue and service metadata; if only formatting
ships, remove the F6 prompt while retaining the issue for remaining detection work.
