# Development and release

How the project is built, packaged, and shipped. The reasoning behind these choices is in
[decisions.md](decisions.md).

Sections marked **Not yet built** describe an agreed design that does not exist in the
repository yet.

## Day to day

`main` is protected. Work on short-lived branches and merge through a pull request — the
workflow is in [CONTRIBUTING.md](../CONTRIBUTING.md), and the reasoning is decision 11.

Everything builds with the .NET 8 SDK on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Core-only verification needs no WPF:

```powershell
dotnet run --project .\tests\SQLBI.Whiteboard.Core.SmokeTests\SQLBI.Whiteboard.Core.SmokeTests.csproj
```

`TreatWarningsAsErrors` is on for every project. A warning fails the build.

## Channels

Two download channels, and they are separate products so both can be installed at once.

| | Released | Pre-release |
| --- | --- | --- |
| Product name | SQLBI Whiteboard | SQLBI Whiteboard (Dev) |
| Install folder | `…\SQLBI\Whiteboard` | `…\SQLBI\Whiteboard Dev` |
| Settings | `%APPDATA%\SQLBI\Whiteboard` | `%APPDATA%\SQLBI\Whiteboard Dev` |
| Registers `.wboard` | yes | no |
| Marker file | none | `channel.txt` beside the executable |
| Published as | GitHub Release | GitHub prerelease |

The application reads `channel.txt` at startup (`AppChannel`). Its absence means the
released channel. Binaries are identical across channels.

## Building installers locally

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -Version 1.0.0
```

Produces, under `artifacts/installer`, four MSIs (released and pre-release, each per-machine
and per-user) and a portable ZIP. Signing is not performed locally.

The WiX toolset is pinned in `.config/dotnet-tools.json`; the script restores it and adds
the required extensions on every run, because extensions are per-machine state that a fresh
build agent does not have.

## Regenerating brand assets

Only needed when the artwork changes. The master is
`src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg`; everything else is generated and must
not be hand-edited.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-assets.ps1
```

Colours live in two places that must be changed together: the SVG, and
`tools/AssetGenerator/Program.cs`.

## The pipeline

`.azure/pipelines/build-whiteboard.yaml`, in the `SQLBI Whiteboard` Azure DevOps project.

Parameters:

| Parameter | Default | Purpose |
| --- | --- | --- |
| `verbosity` | `normal` | MSBuild verbosity |
| `sign` | on | Code sign binaries and MSIs |
| `publishToStorage` | off | Superseded by GitHub Releases (decision 10) |

Two variable groups supply its settings; both must be authorised for the pipeline before it
can run. The signing group's contents are described in decision 1 and held by the
maintainers.

Signing uses `AzureSignTool` against the certificate in Azure Key Vault.

> **When adding a new first-party project, add its assembly to the signing step.** The
> installer harvests new files automatically, so an unsigned assembly ships silently
> beside a signed executable. This has already happened once.

To run it: **Run pipeline**, set parameters, **Run**. Do not use **Re-run** to pick up a
fix — that replays the original commit.

## Releasing

**Not yet built.** The agreed shape:

1. Every push to `main` builds, signs, and publishes the pre-release channel to a GitHub
   prerelease automatically. Pull requests build unsigned and publish nothing.
2. A maintainer bumps the version in a pull request. Once merged, that build carries the
   release version.
3. Promotion is an approval on a Release stage of that run. It publishes the **already-built
   released-channel artifacts from the same run** — nothing is rebuilt (decision 9).
4. winget submission and the Store submission follow from the published release, in
   parallel; neither gates the download being available.

Until it exists, releasing means running the pipeline manually and uploading artifacts by
hand.

## Still to build

Roughly in dependency order:

1. Move the version out of the variable group and into the repository (decision 8).
2. Add CI triggers: pull requests unsigned, `main` signed. Until a pull-request check exists
   and has run once, branch protection can require a pull request but cannot require a
   passing check — GitHub only offers checks it has already seen.
3. Replace the `AzureFileCopy` step with `GitHubRelease@1`, and create a GitHub service
   connection with `contents: write`.
4. Split the pipeline into Build / Pre-release / Release stages with an approval gate.
5. Publish `stable.json` and `dev.json` manifests alongside the binaries, for the download
   page, a future in-app update check, and winget automation to share one source.
6. Add a GitHub Actions workflow for unsigned pull-request validation.
7. Add winget submission triggered by a published release.
8. MSIX packaging and Store submission (decision 13).

## Verification before a public release

None of this has been done for a real release yet.

- `signtool verify /pa /v` on each MSI.
- Install, upgrade, and uninstall for each scope; confirm uninstall removes the install
  folder, both shortcuts, and the file association.
- Double-click a `.wboard` file and confirm it opens in the released build.
- Install released and pre-release together; confirm both run, settings stay separate, and
  boards still open in the released copy.
- Run an installer on a clean machine and confirm SmartScreen does not warn.
