# Contributing

This file is the working agreement for everyone changing this repository, whether they are
typing the code themselves or directing a coding agent. It is deliberately tool-agnostic.

- [docs/decisions.md](docs/decisions.md) — what was decided about packaging, signing, and
  releases, and why. Read it before changing any of them.
- [docs/release-management.md](docs/release-management.md) — how the project is built and
  shipped.

## Workflow

`main` is protected. Every change arrives through a pull request.

1. Branch from an up-to-date `main`. Use a short descriptive name, prefixed by intent:
   `feature/text-container-alignment`, `fix/liveview-reconnect`, `docs/release-notes`.
2. Keep the branch short-lived and the change one logical unit. A branch that lives for
   days accumulates conflicts and stops being reviewable.
3. Before opening the pull request, confirm locally that the build is clean and the smoke
   tests pass:

   ```powershell
   dotnet build Whiteboard.sln -c Release
   dotnet run --project .\tests\SQLBI.Whiteboard.Core.SmokeTests\SQLBI.Whiteboard.Core.SmokeTests.csproj
   dotnet run --project .\tests\SQLBI.Whiteboard.SmokeTests\SQLBI.Whiteboard.SmokeTests.csproj
   ```

   The first harness is framework-neutral. The second needs WPF, and covers what only
   the application can answer, such as syntax highlighting.

   `TreatWarningsAsErrors` is on for every project, so a warning fails the build.
4. Open the pull request against `main`:

   ```powershell
   gh pr create --title "Add the calligraphy pressure curve" --body "..."
   ```

   `gh pr create --fill` takes the title and body from your commits instead, which is fine
   when the branch holds one well-written commit and misleading when it does not.
5. Merge once checks pass. Approval from the other maintainer is welcome but not required —
   neither of us should be blocked by the other's travel. The branch is deleted
   automatically on merge.

Do not push to `main` directly. Once the release pipeline is wired up, every merge to `main`
publishes a pre-release build, so `main` is a published artifact rather than a scratch area.

### The pull request is the permanent record

`main` accepts squash merges only, and takes the commit subject from the pull request title
and the commit body from its description. The individual commits on your branch are
discarded at merge, so the pull request — not the branch history — is what remains.

Write the title in the imperative, describing the change. Use the description to explain why
the change was needed and what it affects. Explain the reasoning, not the diff; the diff is
already there.

This also means one pull request becomes one commit on `main`. Since every merge will
publish a pre-release build, that keeps a build traceable to a single revertable change —
another reason to keep a branch to one logical unit.

Commits on the branch itself are working notes. Keep them tidy enough to review, but they
need not be publication quality.

## Things that are easy to get wrong

**Adding a first-party project?** Add its assembly to the signing step in
`.azure/pipelines/build-whiteboard.yaml`. The installer harvests new files automatically, so
an unsigned assembly would ship beside a signed executable unnoticed. This has happened once
already.

**Bumping the version means writing the release notes.** A pull request that changes
`VersionPrefix` in `Directory.Build.props` must add a matching `## <version> - <date>`
section to `CHANGELOG.md`, or the **Release notes** check fails. Write what a person gets
by upgrading, not what the diff did — that file becomes the GitHub release body and the
[What's new](https://whiteboard.sqlbi.com/changelog.html) page, so it is the only version
history most people will ever read. Pre-release Dev builds need no entry.

**Brand assets are generated.** Only `src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg` is
authored. Icons, installer artwork, and web assets come from `scripts/build-assets.ps1`, and
hand edits are lost on the next run. Colors must change in both the SVG and
`tools/AssetGenerator/Program.cs`.

**The installer builds four products, not one.** Released and pre-release, each per-machine
and per-user, selected by the `Channel` and `Scope` preprocessor variables. Changing product
identity, install folder, or the file association affects all four.

**The pre-release channel must stay a separate product,** with its own `UpgradeCode`, and
must not register `.wboard` or `.wimport`. Decision 7 explains what breaks otherwise.

**The channel is detected at run time,** never compiled in, so one publish serves both
channels and a tested build can be promoted without rebuilding.

**This repository is public.** Never commit vault names, tenant or client identifiers, or
anything else naming internal infrastructure. Signing coordinates belong in the team's
internal notes, not here.

## Writing for people

Four places carry text read by people who did not write the code: the Preferences rows in
`src/SQLBI.Whiteboard/SettingsCatalog.cs`, `README.md`, the site under `site/*.html`, and
`CHANGELOG.md`. Internal documentation under `docs/` is where reasoning, alternatives, and
mechanism belong, at whatever length they need. The four are short, and the same rules
apply to all of them.

### The rules

- **Say what the person gets.** What happens when they turn it on, press the key, or pick
  the option. Not what the code does, how it does it, or why it was built.
- **Match the neighbours.** Before writing, read the two or three entries beside yours.
  Their sentence count is the length and their voice is the voice. If yours is noticeably
  longer, cut it. This has been asked for four times.
- **State each fact once, where the person meets it.** A setting is explained in its own
  Description. Everywhere else it is a phrase in a list, or one sentence inside the
  paragraph that already describes the thing it changes. Do not repeat the Description in
  the README, on the site, and in the notes.
- **Leave out what the screen already says.** A switch shows its own default, and every
  setting applies at once unless the text says otherwise. In a Description, the README, and
  the site, never write "off by default", "changes apply immediately", "the checkbox", or
  "this setting". Say when a change takes effect only when it is not immediate: "the next
  time the application starts". The notes are the exception for the default, because their
  reader has no switch in front of them: "It is off by default" is one clause at the end.
- **No reasoning, no alternatives, no mechanism, no history.** Not why the feature exists,
  not what else was considered, not which events it listens to or what it waits for, not
  what the previous version did. Those go under `docs/` or in the pull request.
- **Do not hedge and do not sell.** "Fills the monitor", not "tries to fill the monitor
  when possible" and not "conveniently fills the monitor".
- **Name things as the person sees them.** Menu paths and setting titles as they appear on
  screen, in bold in Markdown and in `<span class="ui">` on the site. Keys as printed on
  the keyboard, in `<kbd>` on the site. Pen, touch, mouse, and keyboard, never "pointer
  device", "input event", or "activity".

### The shape of each place

- **Preferences.** The **Title** is what the row is called: two to five words naming the
  thing or what it does, like "Start full screen", "Always show the Eraser", or "Pause when
  Whiteboard loses focus". A switch is never "Enable …", "Allow …", or "Whether to …",
  because the switch already says that. The **Summary** is the one line beside the control:
  under about sixty characters, no full stop, saying what On does or what the choice is
  about. The **Description** opens behind the chevron: two or three sentences saying what
  On does, what Off does when it is not the plain opposite, and what interacts with it, such
  as a key, another setting, or a mode. **Keywords** are what a person would type into the
  search box, including words the title does not use.
- **`README.md`** describes the application to someone reading the repository. A setting
  is a phrase in the Preferences bullet of the feature list, a phrase in the
  **Help > Preferences** row of the command table, and, when a command does the same thing,
  one sentence in that command's row. It never gets a paragraph of its own.
- **The site (`site/*.html`)** speaks to the person using the application: what they do
  and what they get. A feature is one paragraph of two to four sentences, in the voice and
  at the length of the paragraphs around it. A setting is a phrase in the Preferences
  paragraph of the guide and, at most, one sentence in the paragraph about the thing it
  changes.
- **`CHANGELOG.md`**, which becomes the GitHub release body and the What's new page, is
  plain and technically accurate: one `###` per thing a person notices, two to four lines
  each, saying what changed and what it is for. A bug entry names the symptom and the fix,
  not the diagnosis. Link to `docs/` for anything longer. A pull request that does not bump
  the version adds nothing here.

### What it looks like when it slips

The 1.3.0 notes as first written are what a paragraph looks like when it runs long, and
their shortened form is what was wanted.

It slips the same way each time in the notes: one entry per piece of work that was built,
rather than per thing a person notices. The 1.5.0 notes went in with five entries where the
change deserved three. The title bar marker belongs in the entry about closing a board,
where a person meets it, and being offered recovered work when reopening a board is the
same story to a reader as being offered it at startup. So count the entries in the two
versions below yours before writing: that is the shape to match, not a ceiling to approach.

It slips in a Description by saying everything the code knows. The setting that goes full
screen after ten idle seconds went in with four sentences: the title restated, the cases
in which the countdown waits, the default, and that changes apply immediately. Its
neighbours have two or three. What was wanted:

> Fill the current monitor and hide the title and tabs after 10 seconds without pen,
> touch, mouse, or keyboard input, while Whiteboard is the active window. F11 and Escape
> leave full screen as usual, and the 10 seconds start again.

The same feature went into the README as a paragraph of its own after the command table,
where it belonged as one sentence in the F11 row, and it went into the site nowhere at all.

## Code style

Match the surrounding code. Nullable reference types and implicit usings are enabled.
Comments explain why, not what, and are sparse — the codebase reads as one voice rather than
as a series of contributions.

## Building

```powershell
.\scripts\build.ps1              # application
.\scripts\build-installer.ps1    # four MSIs and the portable ZIP
.\scripts\build-assets.ps1       # regenerate brand assets from the SVG
```

`tools/AssetGenerator` sits outside `Whiteboard.sln` on purpose, so solution builds ignore it.
