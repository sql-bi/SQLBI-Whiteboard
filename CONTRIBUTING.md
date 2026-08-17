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
   ```

   `TreatWarningsAsErrors` is on for every project, so a warning fails the build.
4. Open the pull request against `main`:

   ```powershell
   gh pr create --fill
   ```

5. Merge once checks pass. Approval from the other maintainer is welcome but not required —
   neither of us should be blocked by the other's travel. Delete the branch after merging.

Do not push to `main` directly. Once the release pipeline is wired up, every merge to `main`
publishes a pre-release build, so `main` is a published artefact rather than a scratch area.

### Commit messages

Write a subject line in the imperative describing the change, then a body explaining why it
was needed and what it affects. Explain the reasoning, not the diff — the diff is already in
the commit.

## Things that are easy to get wrong

**Adding a first-party project?** Add its assembly to the signing step in
`.azure/pipelines/build-whiteboard.yaml`. The installer harvests new files automatically, so
an unsigned assembly would ship beside a signed executable unnoticed. This has happened once
already.

**Brand assets are generated.** Only `src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg` is
authored. Icons, installer artwork, and web assets come from `scripts/build-assets.ps1`, and
hand edits are lost on the next run. Colours must change in both the SVG and
`tools/AssetGenerator/Program.cs`.

**The installer builds four products, not one.** Released and pre-release, each per-machine
and per-user, selected by the `Channel` and `Scope` preprocessor variables. Changing product
identity, install folder, or the file association affects all four.

**The pre-release channel must stay a separate product,** with its own `UpgradeCode`, and
must not register `.wboard`. Decision 7 explains what breaks otherwise.

**The channel is detected at run time,** never compiled in, so one publish serves both
channels and a tested build can be promoted without rebuilding.

**This repository is public.** Never commit vault names, tenant or client identifiers, or
anything else naming internal infrastructure. Signing coordinates belong in the team's
internal notes, not here.

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
