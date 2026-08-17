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
separate certificate was not worth the cost, and releases sign as SQLBI Corporation either
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

- **Dev does not register `.wboard`.** If both channels claimed it the last install would
  win, and uninstalling dev would delete the association outright, breaking the released
  copy. Boards always open in the released build; dev is launched explicitly.
- **Settings are separated** through a `channel.txt` the dev installer places beside the
  executable. Without this the two copies silently overwrite each other's settings on every
  save — the settings parser ignores the `Version` field, so this fails quietly rather than
  loudly.
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

**Agreed, not yet built.**

Three constraints shape it:

- The Store needs an MSIX, and only MSIs are built today. Bravo's `Bravo.Installer.Msix`
  is the template.
- Store version numbers must end in `.0`, which the current four-part scheme never
  produces. The MSIX needs its own version derivation.
- Certification takes hours to days, so submission must run in parallel with the web
  release and must never gate it.

The first submission should be manual: listing, screenshots, and age rating are one-time
work no pipeline performs.

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

The self-contained build is roughly 60 MB against 8 MB, and needs no .NET runtime installed.
That trade favours the visitor. The framework-dependent build is still produced and kept as
a pipeline artifact.

## 16. SQLBI Whiteboard is MIT-licensed open source

**Implemented.**

The repository is public and carries the MIT licence, the same as Bravo, and the installer
presents the same terms. This was confirmed deliberately rather than inherited: the licence
text was copied from Bravo early on, and shipping it unexamined would have granted rights
nobody had decided to grant.

---

## Open questions

- Whether the first public release is `0.1.0` or `1.0.0`. `VersionPrefix` in
  `Directory.Build.props` is still the placeholder `0.1.0`.
- When the Store listing happens, and who owns the one-time listing work.
- arm64 is not built; add it if Surface devices matter for a pen application.
- The brand mark is placeholder-grade (decision 12).
