# Decisions

A log of the choices behind the build, packaging, and release setup, with the reasoning
that produced them. Each entry records whether it is **implemented** or **agreed, not yet
built** — do not assume an agreed decision exists in the repository.

Operational instructions live in [release-management.md](release-management.md).

---

## 1. Reuse the existing SQLBI code-signing certificate

**Implemented.**

Signing uses the SQLBI EV certificate already held in Azure Key Vault and used by Bravo and
DAX Studio, driven by `AzureSignTool` exactly as Bravo does.

Marginal cost is zero, and SmartScreen reputation attaches to the publisher certificate
rather than to a binary, so the first signed release inherits the standing the certificate
has already accumulated instead of building its own.

The certificate rotates by issuing a new one under a new name, so `SigningCertName` changes
at each rotation. Expiry and vault details are deliberately not recorded in this public
repository; they are held by the maintainers.

## 2. Whiteboard signs through its own service principal

**Implemented.**

The certificate is shared, but Whiteboard authenticates with a service principal created
for it alone, granted only certificate `Get` and key `Sign`.

Sharing Bravo's identity would mean a single compromised secret affected every product and
one audit trail covering all of them. A separate identity can be revoked on its own. A
separate certificate was not worth the cost, and releases sign as SQLBI Corp. either
way.

## 3. Signing runs in Azure Pipelines, not GitHub Actions

**Implemented** (pipeline exists; triggers are still manual, see decision 10).

The repository is public, which makes Actions free — but also makes **Actions logs public**.
Signing diagnostics, vault URLs, certificate subject, and service principal identifiers
would all be world-readable, including from failed runs. Keeping the certificate out of
GitHub entirely also removes a class of risk around fork pull requests.

Azure DevOps additionally matches how Bravo and DAX Studio ship, so the operational
knowledge is shared.

GitHub Actions is still the right home for work that never touches the certificate:
unsigned pull-request validation, and winget submission triggered by a published release.

## 4. Azure DevOps project is isolated from Bravo's

**Implemented.**

Whiteboard has its own Azure DevOps project so maintenance can be delegated separately, at
the cost of a duplicated signing variable group and its own service-principal secret. That
duplication was accepted deliberately.

Two variable groups: one holding the five signing values, one holding version and
product-specific values. Signing is kept in its own group so its Security can be restricted
independently.

## 5. WiX v5, not v6 or v7

**Implemented.**

WiX v7 refuses to run without accepting the Open Source Maintenance Fee EULA. v6 runs
without that gate but belongs to the same fee model. v5.0.2 predates the model entirely and
is pinned in `.config/dotnet-tools.json`.

This is a licensing decision for a commercial vendor, not a technical one — v6 and v7 are
fine tools. Revisit if SQLBI decides to pay the fee.

Even v5 is a large step from Bravo's v3 authoring: `heat` harvesting and its XSLT filter
collapse into one `<Files Include>` element, and `candle` plus `light` become one
`wix build`.

## 6. One WiX source produces every installer variant

**Implemented.**

Two preprocessor variables, `Channel` and `Scope`, select among four products from a single
`.wxs`. Using `Package/@Scope` and `HKMU` registry roots avoids the ICE suppressions Bravo's
authoring needs, so validation runs fully enabled.

## 7. The pre-release channel is a separate product

**Implemented.**

A dev build installs alongside a released one rather than replacing it: its own
`UpgradeCode` per scope, its own name and install folder, and its own settings.

Three consequences were chosen deliberately:

- **Dev does not register `.wboard` or `.wimport`.** If both channels claimed them the last
  install would win, and uninstalling dev would delete the association outright, breaking
  the released copy. Boards and import recipes always open in the released build; dev is
  launched explicitly.
- **Settings are separated** through a `channel.txt` placed beside the executable by the dev
  installer, and carried inside the dev portable ZIP. Without this the two copies silently
  overwrite each other's settings on every save — the settings parser ignores the `Version`
  field, so this fails quietly rather than loudly. The portable ZIP originally shipped
  without the marker in both channels, which reintroduced exactly this collision for anyone
  running a portable pre-release (issue 17).
- **The channel is detected at run time, not compiled in.** One set of binaries therefore
  serves both channels, all four installers come from a single publish, and a tested build
  can be promoted without being rebuilt (decision 9).

## 8. Version belongs in the repository

**Implemented.** `VersionPrefix` in `Directory.Build.props` is the single definition. The
pipeline and `scripts/build-installer.ps1` both read it with `dotnet msbuild -getProperty`,
so nothing restates it. `AppVersionMajor`, `AppVersionMinor` and `AppVersionPatch` are no
longer used and can be deleted from the variable group.

Moving it into the repository makes the version reviewable in a pull request, attaches it to
the commit that carries it, and makes "1.0.0 shipped from exactly this tree" answerable from
git alone. It is also a prerequisite for artifact promotion: an MSI bakes in its
`ProductVersion` and cannot be relabelled without a rebuild, so the version must be final at
build time.

## 9. Promote artifacts, not commits

**Implemented.** The pipeline has three stages: Build, PreRelease, and Release. PreRelease
publishes automatically; Release is gated by approvals on the `whiteboard-release`
environment and uploads the released-channel installers **that the same run already
produced**.

Rebuilding from a tagged commit ships bits that were never tested. Promotion instead
publishes the already-built, already-signed artifacts from the run that was verified.

Both channels' installers are produced by every build for this reason, so the released
installers already exist when promotion happens.

## 10. GitHub Releases hosts the downloads

**Implemented.** `GitHubRelease@1` publishes both channels; the `AzureFileCopy` step and the
`publishToStorage` parameter are gone, and no storage account is needed.

The repository is public, so release assets are downloadable without authentication — this
was the deciding factor, not cost. GitHub serves them from a CDN at no bandwidth cost, the
prerelease flag distinguishes the two channels natively, `/releases/latest/download/...` is
a permanent link the download page can hard-code, and winget reads the same source.

No storage account, service connection, or blob RBAC is therefore needed.

Creating a release requires a GitHub service connection with `contents: write`, separate
from the one used to read source. It is named `sql-bi write assets`.

## 11. `main` is protected and every change arrives by pull request

**Agreed; enable in the repository settings.** This supersedes an earlier decision to defer
protection until development had settled.

The original objection was that requiring pull requests would slow early development. It
does not: both maintainers work through coding agents, so creating a short-lived branch and
opening a pull request is a line of instruction rather than a change of habit. The cost was
overestimated, and `main` is about to start feeding a public download channel on every
merge.

Configuration, in two stages because the second has a prerequisite:

- **Done** — a pull request is required before merging, and the bypass list is empty.
  Protection that can be silently sidestepped tends to be.
- **Remaining** — require the pull request validation checks to pass. GitHub only offers
  checks it has already seen, so select them once the workflow has run.

Approvals are deliberately **not** required. A two-person core team should not be blocked by
one member's travel; review is welcome, waiting is not. Add required review only if
something slips through.

There is no `develop` branch and none is planned: `main` is the pre-release channel, and a
release is a tag plus a GitHub Release, so no branch needs to represent "released". Cut a
`release/x.y` branch only when a shipped version genuinely needs patching while `main` has
moved on.

The working agreement itself is in `CONTRIBUTING.md`, kept tool-agnostic so that it applies
to every contributor and to whichever coding agent each of them uses.

## 12. Brand assets are generated from one source

**Implemented.**

The icon uses SQLBI's brand gradient, the same pair Bravo uses, over the Fluent
`whiteboard_24_filled` glyph. Everything else — icon frames, document icon, installer
banner and dialog artwork, favicons, social card — is rendered from that composition by
`tools/AssetGenerator`.

This is deliberately a placeholder-grade identity: it looks in-family and considered, but
the glyph is a generic whiteboard mark rather than an identity of its own. Replacing it
later touches no installer plumbing.

## 13. Microsoft Store is a later, separate piece of work

**Implemented.**

Three constraints shape it:

- The Store needs an MSIX. `installer/msix` plus `scripts/build-msix.ps1` pack the
  released-channel publish folder. Bravo's `Bravo.Installer.Msix` is the shape that was
  followed (full-trust desktop package, file-type associations in the manifest).
- Store version numbers must end in `.0`. The MSIX Identity Version is `VersionPrefix.0`,
  not the four-part assembly stamp the MSI uses.
- Submission must never gate the web release. Certification took hours to days when this
  was decided and now runs in under an hour unattended, which changes how long the Store
  lags the download rather than the reasoning: it is still someone else's queue, it can
  still stall or reject, and none of that should be able to hold up a release that is
  already built and signed. The Store stage runs after Release rather than beside it, so
  the GitHub release already exists by the time it starts, a failed or slow submission
  changes nothing that shipped, and a build that was never promoted is never submitted.

The first submission was manual, and was made on 20 August 2026 for 0.9.2: listing,
screenshots, and age rating are one-time work no pipeline performs.
`installer/msix/STORE-LISTING.md` records every field that was entered, which is what the
automated submission reproduces. Automating the upload was deliberately held until that
first submission had succeeded, so an API failure could never be confused with an
incomplete listing.

Everything after it is the Store stage's job. It submits packages only — listing text,
screenshots, and **What's new** carry over from the last published submission untouched.
Changing them stays a deliberate act in Partner Center, recorded in `STORE-LISTING.md`.
The stage is equally careful with submissions it did not create: it deletes only one left
`PendingCommit` by its own failed upload, because any other pending submission may be a
person's listing edit and a pipeline must not discard it.

The submission identity is not the signing one. The Partner Center account is associated
with a different Microsoft Entra tenant than the one the pipeline signs in, so the two
cannot be the same principal even if sharing one were desirable — which it is not, since a
single leaked secret would then both sign as SQLBI and publish as SQLBI.

## 14. Bravo's telemetry was not ported

**Implemented as an omission.**

Bravo's installer carries a custom-action DLL and opt-in telemetry checkboxes wired through
a long sequence of remember-properties. None of it was carried over, and the installer is
considerably simpler for it. Port it only if the data is actually wanted.

## 15. Only the self-contained build is published

**Implemented.**

Each release carries three assets: the per-machine installer, the per-user installer, and
the portable ZIP, all self-contained. Publishing both flavours meant six assets with names
like `SQLBI.Whiteboard.0.1.0.x64-frameworkdependent-dev-userinstaller.msi`, which asks a
visitor to decode four dimensions before downloading anything.

The self-contained installer is roughly 73 MB against 11 MB, and needs no .NET runtime
installed. That trade favors the visitor. The framework-dependent build is still produced
and kept as a pipeline artifact.

## 16. SQLBI Whiteboard is MIT-licensed open source

**Implemented.**

The repository is public and carries the MIT license, the same as Bravo, and the installer
presents the same terms. This was confirmed deliberately rather than inherited: the license
text was copied from Bravo early on, and shipping it unexamined would have granted rights
nobody had decided to grant.

## 17. Published builds are ReadyToRun-compiled

**Implemented.**

`PublishReadyToRun` is set in `src/SQLBI.Whiteboard/SQLBI.Whiteboard.csproj`, so publishing
compiles IL ahead of time instead of leaving every method to the JIT on first call. Startup
latency is what a pen application is judged on, and it is paid on every launch rather than
once.

It costs about 22% on disk — the self-contained publish folder measured 207 MB without it
and 252 MB with it — which compresses down to a few MB in the installer. It applies only to
`dotnet publish`, and only when a runtime identifier is given; both publish paths pass one,
so a plain `dotnet build` is unaffected and local iteration does not slow down.

## 18. Pull request validation packages less than it ships

**Implemented.**

`Build installers` in `.github/workflows/pull-request.yml` packages the framework-dependent
build and only two of the four channel-scope variants. It took 6m 17s building everything;
the reduced job runs in well under two minutes.

Almost all of that time was CAB compression, and compression is not what the job validates —
the WiX authoring is. The framework-dependent payload is roughly 15 MB against 252 MB, and
runs through the same authoring and the same `<Files Include>` harvesting.

The two variants are the diagonal pair, `stable/perMachine` and `dev/perUser`. The four are
not repetition: they are four paths through the `<?if?>` branches in
`installer/wix/SQLBI.Whiteboard.wxs`, so building only one would miss a typo in the dev
branch or a broken per-user directory. The diagonal pair still takes both sides of every
conditional.

What this stops covering is narrow: a failure specific to self-contained output, and the two
untested channel-scope combinations. Azure Pipelines builds all four variants self-contained
on every merge to `main`, so both are caught before anything is signed or published.

`scripts/build-installer.ps1` defaults to all four variants and to self-contained. The
reduction is expressed in the workflow that wants it, not in the script, so nothing that
ships can inherit it by accident.

## 19. The teaser video is a file on the site, not an embed

**Implemented.**

The original plan was a Vimeo embed with tracking stripped (`dnt=1`, no player script),
deliberately excluding YouTube and self-hosted files. It was reversed when the clip
existed, for three reasons:

- Vimeo is blocked or unreliable in several regions; an embed shows those visitors a dead
  frame. A file served with the page plays wherever the page loads.
- The embed was the only third party on the privacy page. Removing it makes "static HTML,
  no third parties" true without a footnote.
- The bandwidth argument for an embed did not survive the numbers. The clip is 42 seconds
  of mostly still screen content: 3.3 MB as 1440p60 AV1 and 2.7 MB as 1080p60 H.264,
  around 0.6 Mbit/s — fluent on connections far below any embed's minimum. GitHub Pages
  serves through a CDN with range-request support, and both files carry `faststart`, so
  playback begins after a few hundred kilobytes.

What is given up is adaptive streaming and player analytics. At these bitrates the ladder
has nothing to adapt between, and the site collects no analytics anyway.

The two renditions and the poster live in `site/` (`teaser-av1.mp4`, `teaser-h264.mp4`,
`teaser-poster.jpg`) and deploy with every site publish. The `<video>` element lists AV1
first and H.264 as the universal fallback, and plays automatically — muted, looping,
with controls kept visible — because the ink only reads in motion. A visitor whose
system asks for reduced motion gets the poster and a play button instead: a small
script removes the autoplay. A YouTube `nocookie` embed was considered for the loop and
rejected — its privacy-enhanced mode only defers cookies until playback, which an
autoplaying loop triggers on page load for everyone.

The 4K Camtasia master is kept outside the repository; re-encoding is two ffmpeg
commands recorded in `docs/teaser/shot-script.md`. Autoplay means every home-page visit
downloads a rendition (about 3 MB), so GitHub Pages' ~100 GB/month soft bandwidth limit
maps to roughly 30,000 visits a month — far beyond this site's traffic, and GitHub has
no bandwidth meter to watch anyway. If a launch ever approaches that, the escape hatch
is a lighter loop rendition or a caching proxy in front of the domain, not an embed.

---

## 20. The version is 1.0.0

**Implemented.**

The open question was never the number, it was what had to be true before it stopped
being 0.x. That turned out to be product rather than pipeline: Preferences, `.wimport`,
the Explorer and VS Code previews, the documentation site, and Finger drawing. All of
them shipped during 0.9.x, and no numbered work was left behind them.

Nothing about delivery changes with it. A merge still publishes a pre-release, promotion
is still an approval on that same run, and the Store still takes the MSIX from it — so
1.0.0 reaches people by the path 0.9.5 already proved, which is the reason the number
could be treated as a statement about the product rather than an event in the pipeline.

The Store carries `1.0.0.0` as its identity version, and winget continues from whatever
version its first submission settles on.

---

## 21. SVG is kept as markup and drawn by SharpVectors

**Implemented.**

WPF has no SVG decoder, so supporting SVG at all meant taking a rendering library —
including the option of rasterizing on arrival, which needs one just the same. That made
the choice about which library, not whether to have one.

`SharpVectors.Wpf` produces a `DrawingGroup`, so an SVG container is redrawn at whatever
size it is displayed at rather than stretched from pixels. That is the point of accepting
SVG on a canvas whose zoom is unbounded. It is BSD-3, managed-only, and ships no native
binary, which keeps it out of the signing step and out of the per-architecture question
the installer would otherwise have to answer. Direct2D was the tempting alternative, since
`Vortice` is already referenced and would have cost nothing: it implements a restricted
SVG subset with no `<text>` element, which is most of what a DAX SVG measure emits.

Assets are stored as the bytes that arrived, and `BoardArchive` has always treated them as
opaque, so the format version did not move. A board holding an SVG opened in 1.0.3 draws
the missing-image placeholder for it rather than failing to open.

The renderer is given `ExternalResourcesAccessModes.Ignore`. Its default is to fetch what
the markup names, which would let a pasted or dropped file turn opening a board into an
outbound request.

---

## Open questions

- arm64 is not built; add it if Surface devices matter for a pen application.
- The brand mark is placeholder-grade (decision 12).
