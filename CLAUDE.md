# SQLBI Whiteboard

A native Windows 11 whiteboard in C# and WPF. `README.md` describes the application and its
architecture.

**Read [docs/decisions.md](docs/decisions.md) before changing anything about packaging,
signing, or releases.** It records what was decided and why, and marks which decisions are
implemented and which are only agreed. [docs/release-management.md](docs/release-management.md)
is the operational counterpart.

## Conventions

- `TreatWarningsAsErrors` is on everywhere. A warning fails the build.
- Nullable reference types and implicit usings are enabled.
- Match the surrounding code: comments explain why, not what, and are sparse.

## Things that are easy to get wrong

- **Adding a first-party project?** Add its assembly to the signing step in
  `.azure/pipelines/build-whiteboard.yaml`. The installer harvests new files automatically,
  so an unsigned assembly would ship beside a signed executable unnoticed.
- **Brand assets are generated.** Only `src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg` is
  authored. Icons, installer artwork, and web assets come from `scripts/build-assets.ps1`;
  editing them by hand is lost on the next run. Colours must be changed in both the SVG and
  `tools/AssetGenerator/Program.cs`.
- **The installer builds four products**, not one: released and pre-release, each per-machine
  and per-user, selected by the `Channel` and `Scope` preprocessor variables. Changing
  product identity, install folder, or the file association affects all four.
- **The pre-release channel must stay a separate product** with its own `UpgradeCode`, and
  must not register `.wboard`. Decision 7 explains what breaks otherwise.
- **The channel is detected at run time**, never compiled in, so one publish serves both
  channels and a tested build can be promoted without rebuilding.
- **This repository is public.** Do not commit vault names, tenant or client identifiers,
  or anything else naming internal infrastructure.

## Building

```powershell
.\scripts\build.ps1              # application
.\scripts\build-installer.ps1    # four MSIs and the portable ZIP
.\scripts\build-assets.ps1       # regenerate brand assets from the SVG
dotnet run --project .\tests\SQLBI.Whiteboard.Core.SmokeTests\SQLBI.Whiteboard.Core.SmokeTests.csproj
```

`tools/AssetGenerator` is deliberately outside `Whiteboard.sln`, so solution builds ignore it.
