# Development and release

How the project is built, packaged, and shipped. The reasoning behind these choices is in
[decisions.md](decisions.md).

Sections marked **Not yet built** describe an agreed design that does not exist in the
repository yet.

## What to run

Day-to-day shipping is a merge, plus one of the GitHub Actions below when the merge is not
enough (a retry, or a deploy that has no new commit). Pull request validation runs by
itself. Do not run it to “release” anything.

| You want to | Do this |
| --- | --- |
| Land a change | Open a pull request against `main`. The **Pull request** Action builds and tests; merge when it is green. |
| Ship a Whiteboard pre-release | Merge to `main` (anything except `docs/`, `site/`, `vscode/`, and top-level markdown). Azure Pipelines **SQLBI Whiteboard** signs and publishes the GitHub prerelease. To rebuild an existing commit: Azure DevOps → that pipeline → **Run pipeline**. Do not use **Re-run**. |
| Promote that build to a full release | Approve the **Release** stage of the same Azure run. Nothing is rebuilt. |
| Ship a VS Code extension update | Bump `version` in `vscode/sqlbi-whiteboard/package.json` in a pull request and merge. The **Publish VS Code extension** Action publishes `sqlbi.sqlbi-whiteboard` if that version is not already on the Marketplace. To retry: Actions → **Publish VS Code extension** → **Run workflow**. |
| Update whiteboard.sqlbi.com | Merge a change under `site/`. The **Publish site** Action deploys. To retry: Actions → **Publish site** → **Run workflow**. |
| Rebuild brand assets | Run `scripts/build-assets.ps1` locally. Only when the artwork changes. |

The three GitHub Actions live under **Actions** in this repository. Their names are
**Pull request**, **Publish VS Code extension**, and **Publish site**. The signed
Whiteboard pipeline is not a GitHub Action; it stays in Azure DevOps because of the
certificate (decision 3).

Version numbers are reviewed in pull requests, not typed into a pipeline UI.
Whiteboard's version is `VersionPrefix` in `Directory.Build.props`. The VS Code
extension's version is `version` in `vscode/sqlbi-whiteboard/package.json`. They move
independently.

---

## Day to day

`main` is protected. Work on short-lived branches and merge through a pull request — the
workflow is in [CONTRIBUTING.md](../CONTRIBUTING.md), and the reasoning is decision 11.

Everything builds with the .NET 10 SDK on Windows:

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
| Registers `.wboard` and `.wimport` | yes | no |
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

---

## How it works

### GitHub Actions

Three workflows in `.github/workflows/`. None of them see the EV certificate. Logs are
public, so they must not print tokens.

**Pull request** (`.github/workflows/pull-request.yml`) runs on every pull request against
`main`, and can be started by hand if a check needs repeating:

- **Build and test** — restores, builds the solution in Release, and runs the Core smoke
  tests. A warning fails it.
- **Build installers** — runs `scripts/build-installer.ps1` unsigned, so WiX authoring
  errors surface here rather than at release time. The output is discarded. It builds a
  diagonal pair of variants, framework-dependent, which is enough to walk every `<?if?>` in
  the WiX source.
- **VS Code extension** — compiles and tests `vscode/sqlbi-whiteboard` when that tree
  changed.

Fork pull requests get the same jobs and no secrets.

**Publish VS Code extension** (`.github/workflows/publish-vscode-extension.yml`) publishes
`vscode/sqlbi-whiteboard` as `sqlbi.sqlbi-whiteboard`. It runs when that extension's
`package.json` lands on `main`, and on **Run workflow**. It packages, then publishes only
when `version` is not already on the Marketplace, so a description-only edit does not
republish. It does not rewrite the version.

The repository secret `VSCE_PAT` is a Marketplace **Manage** personal access token with
organization **All accessible organizations**. Create it in the Azure DevOps organization
tied to the `sqlbi` publisher, then add it under the GitHub repo **Settings → Secrets and
variables → Actions**. Forks never receive it.

**Publish site** (`.github/workflows/publish-site.yml`) deploys `site/` to GitHub Pages
when that tree changes on `main`, and on **Run workflow**. It needs **Settings → Pages →
Source: GitHub Actions**. `site/CNAME` carries the custom domain.

### Azure Pipelines

`.azure/pipelines/build-whiteboard.yaml`, in the `SQLBI Whiteboard` Azure DevOps project,
is the only place that signs. A merge to `main` starts it unless the change is only
`docs/`, `site/`, `vscode/`, `.github/`, or top-level markdown. It can also be started with
**Run pipeline**.

Parameters:

| Parameter | Default | Purpose |
| --- | --- | --- |
| `verbosity` | `normal` | MSBuild verbosity |
| `sign` | on | Code sign binaries and MSIs |

Two variable groups must be authorised for the pipeline. Their contents stay in Azure
DevOps, not in this repository:

- **SQLBI-CodeSigning** — vault URL, tenant, client, secret, certificate name (decision 1).
- **SQLBI.Whiteboard** — product settings. The version is not among them.

Signing uses `AzureSignTool` against the certificate in Azure Key Vault.

> **When adding a new first-party assembly, add it to the signing step.** The installer
> harvests new files automatically, so an unsigned assembly ships silently beside a signed
> executable. This has already happened once. The VS Code extension is not an assembly and
> is not signed here.

Stages: **Build** produces every installer variant, **PreRelease** publishes the
pre-release channel to GitHub, **Release** is gated by approvals on the
`whiteboard-release` environment and uploads the released-channel installers **that the
same run already produced** (decision 9).

Creating a GitHub Release uses the `sql-bi write assets` service connection
(`contents: write`).

### Versioning

`VersionPrefix` in `Directory.Build.props` is the only place the **desktop** product
version is written. Everything in the installer derives from it:

- assemblies are stamped `major.minor.<days since 2000-01-01>.<seconds since midnight / 2>`,
  the algorithm MSBuild and the Bravo pipeline use, so every matrix job in a run stamps the
  same number;
- the informational version is the plain `major.minor.patch`;
- artifact and installer file names use it directly.

The VS Code extension does not read `VersionPrefix`.

### Releasing

The agreed shape, now in use for the pre-release path:

1. A maintainer bumps `VersionPrefix` in a pull request when the number should change.
2. The merge to `main` builds, signs, and publishes the pre-release channel to a GitHub
   prerelease.
3. Promotion is an approval on the Release stage of **that** run. It publishes the
   already-built released-channel artifacts — nothing is rebuilt.
4. winget submission and the Store submission follow from the published release, in
   parallel, once those pieces exist. Neither gates the download being available.

### Still to build

Roughly in dependency order:

1. Publish `stable.json` and `dev.json` manifests alongside the binaries, for the download
   page, a future in-app update check, and winget automation to share one source.
2. Add winget submission triggered by a published release.
3. MSIX packaging and Store submission (decision 13).

### Verification before a public release

The build, signing, and publishing chain is proven (see above). What has **not** been done
for a real release is everything that involves installing the result.

- `signtool verify /pa /v` on each MSI.
- Install, upgrade, and uninstall for each scope; confirm uninstall removes the install
  folder, both shortcuts, and the file association.
- Double-click a `.wboard` file and confirm it opens in the released build.
- Install released and pre-release together; confirm both run, settings stay separate, and
  boards still open in the released copy.
- Run an installer on a clean machine and confirm SmartScreen does not warn.
