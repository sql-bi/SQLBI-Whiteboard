# Development and release

How the project is built, packaged, and shipped. The reasoning behind these choices is in
[decisions.md](decisions.md). Everything described here is built and in use.

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
| Build a Store MSIX | `scripts/build-installer.ps1` already writes `SQLBI.Whiteboard.<version>.x64.msix`. To pack only: `scripts/build-msix.ps1`. Identity version is `VersionPrefix.0`. |
| Submit to the Store | Nothing. Approving the **Release** stage also runs the **Store** stage, which submits that run's MSIX. To skip it for one run, clear **Submit the released MSIX to the Microsoft Store** in **Run pipeline**. Listing text is still by hand: `installer/msix/STORE-LISTING.md`. |

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

Colors live in two places that must be changed together: the SVG, and
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

Three variable groups must be authorized for the pipeline. Their contents stay in Azure
DevOps, not in this repository:

- **SQLBI-CodeSigning** — vault URL, tenant, client, secret, certificate name (decision 1).
- **SQLBI.Whiteboard** — product settings. The version is not among them.
- **SQLBI-StoreSubmission** — Partner Center tenant, client, secret, and seller id. Declared
  on the Store stage rather than at pipeline level, so the build and signing stages cannot
  read it.

> **The Store tenant is not the signing tenant.** The Partner Center account is associated
> with a different Microsoft Entra tenant than the one this pipeline signs in, so the two
> credentials are different principals in different directories. That is also why every
> variable in the Store group carries a `Store` prefix: two groups defining a bare
> `TenantId` would collide, and Azure DevOps resolves a collision between groups silently.

Signing uses `AzureSignTool` against the certificate in Azure Key Vault.

> **When adding a new first-party assembly, add it to the signing step.** The installer
> harvests new files automatically, so an unsigned assembly ships silently beside a signed
> executable. This has already happened once. The VS Code extension is not an assembly and
> is not signed here.

Stages: **Build** produces every installer variant plus an unsigned released-channel
MSIX, **PreRelease** publishes the pre-release channel to GitHub, **Release** is gated
by approvals on the `whiteboard-release` environment and uploads the released-channel
installers **that the same run already produced** (decision 9), and **Store** submits that
run's MSIX to Partner Center.

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
4. The Store submission and the winget submission both follow from the published release,
   the first as a pipeline stage and the second as a GitHub Action. Neither gates the
   download being available.

### Release manifests

`stable.json` and `dev.json` describe the newest release on each channel: version, tag,
publication date, and for each installer its name, URL, size, and SHA-256.

They are served from the site, at <https://whiteboard.sqlbi.com/stable.json> and
`/dev.json`, and generated into each Pages deployment by
`scripts/build-release-manifests.ps1` rather than committed. Publishing a release
redeploys the page, which is what keeps them current.

Two things decided that shape:

- The manifests are built from the GitHub releases API, which reports a SHA-256 digest for
  every asset. Nothing has to be downloaded or re-hashed, and the manifests describe
  releases published before the script existed rather than only later ones.
- They are served from the site rather than attached to a release because
  `github.com` release-asset URLs send no `Access-Control-Allow-Origin` header. A browser
  cannot read a manifest published as a release asset; `api.github.com` does allow it,
  which is why the download page's fallback still works.

A channel with no published release produces no file. The contract is that the file
existing means a release exists, so a consumer that gets a 404 knows to fall back rather
than having to interpret an empty manifest.

The download page reads `stable.json` first and falls back to the API, so it makes no API
call at all on an ordinary visit.

The in-app update check reads the same files, one a day at most, and picks the file by
channel: a released copy asks `stable.json`, a pre-release copy asks `dev.json`. That
second half is the reason these are published beside the site rather than attached to a
release - `releases/latest/download/` resolves only to the newest full release, so a
manifest published that way cannot describe the pre-release channel at all. If the site
cannot be reached the check falls back to reading the newest release tag from GitHub,
which is a floor rather than an answer but never reports a stale "up to date".

Versions in the manifest are compared on `major.minor.patch` only; the `-dev.<build>`
suffix is deliberately ignored, so a pre-release copy is told about the next version
rather than about every rebuild of its own.

### winget

`.github/workflows/publish-winget.yml` submits a released version to
microsoft/winget-pkgs when a release is published, and can be re-run by hand for a release
whose first attempt failed. Pre-releases are skipped: the Dev channel is a separate product
so it can sit beside a released copy (decision 7), and a package manager that installed it
on `winget install SQLBI.Whiteboard` would defeat that.

`wingetcreate` downloads each installer, computes its own SHA-256, and reads the
`ProductCode` out of the MSI, so those values are derived from the bytes being submitted
rather than copied. `installer/winget/` holds the seed manifests for the first submission
and stays as the reviewable record of what was sent.

It runs beside the release rather than inside it, for the reason the Store submission does
(decision 13): a submission is a pull request against someone else's repository, reviewed
by people, and it can sit for days. Nothing about the download being available depends on
it.

The first submission was made by hand on 20 August 2026, which reserved the
`SQLBI.Whiteboard` identifier. Everything after it is the workflow's job.

`WINGET_TOKEN` is a repository secret holding a GitHub personal access token, and the
workflow cannot succeed without it. Two constraints on that token are easy to get wrong:

- It must be a **classic** token. `wingetcreate` does not support fine-grained tokens, and
  fine-grained is what GitHub offers first now.
- The scope is `public_repo`, not full `repo` - winget-pkgs and the fork are both public.
  Adding `delete_repo` lets `wingetcreate` clean up the fork it created when a submission
  fails, rather than leaving one behind each time.

`wingetcreate` forks winget-pkgs and opens the pull request as whoever owns the token, so
it is an identity rather than only a permission, and `GITHUB_TOKEN` cannot stand in.

Locally, `wingetcreate` needs no token at all: run it without `--token` and it uses a
browser OAuth flow, which is what its own documentation recommends, because a token on the
command line ends up in shell history.

### Microsoft Store

The **Store** stage of the Azure pipeline submits the released MSIX to Partner Center. It
depends on **Release**, so it runs only for a build that was actually promoted, and only
after the GitHub release exists — by the time a submission can fail, every download is
already published. Certification takes hours to days and gates nothing (decision 13).

`UseMSStoreCLI@0` installs the [Microsoft Store Developer CLI][msstore]; `msstore
reconfigure` authenticates with the `SQLBI-StoreSubmission` group, and `msstore publish`
uploads the package against Store product `9NN5N0L2TMTF`. Credentials are passed through
the environment rather than the command line, because the agent echoes a native command
line and log masking is a safety net rather than a guarantee.

[msstore]: https://learn.microsoft.com/windows/apps/publish/msstore-dev-cli/overview

Three things about it are deliberate:

- **It takes the MSIX from the `drop-x64-true` artifact by name.** Both matrix jobs pack a
  released-channel MSIX and `build-msix.ps1` names them identically —
  `SQLBI.Whiteboard.<version>.x64.msix` carries no flavour — so a recursive search of the
  workspace could just as easily submit the framework-dependent package, which needs a .NET
  runtime the Store cannot assume. Naming the artifact makes that unreachable, and the stage
  fails rather than guessing if the count is not exactly one.
- **It submits packages only.** Listing text, screenshots, and **What's new** carry over
  from the last published submission untouched. Changing them is `msstore submission
  updateMetadata` and stays a deliberate act in Partner Center, recorded in
  `installer/msix/STORE-LISTING.md`.
- **It does not clear a pending submission.** If one is already in flight `msstore publish`
  fails, which is correct: that submission may be a person's listing edit, and a pipeline
  must not discard it. Resolve it in Partner Center and re-run the stage.

The first submission was manual, on 20 August 2026 for 0.9.2, because the listing,
screenshots, and age rating are one-time work no API performs — and holding the automation
until it had succeeded meant an API failure could never be confused with an incomplete
listing. The same reasoning as winget, arrived at independently.

Clearing **Submit the released MSIX to the Microsoft Store** in **Run pipeline** promotes a
release without touching the Store.

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
