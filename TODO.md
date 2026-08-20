# TODO

Outstanding work. Items are written to be picked up cold: each says what to change
and why it matters.

Background reading, in this order:

- [CONTRIBUTING.md](CONTRIBUTING.md) — branch and pull request workflow. `main` is protected.
- [docs/release-management.md](docs/release-management.md) — how the project is built and shipped.
- [docs/decisions.md](docs/decisions.md) — why it is built that way. Each entry says whether it
  is implemented or only agreed.

## Where the project stands

The delivery chain works end to end: a merge to `main` builds, signs, and publishes a
pre-release to GitHub Releases, and one approval promotes that same build to a release.
<https://whiteboard.sqlbi.com> reads its download links from the release manifest
deployed beside it and needs no edit per release. The current product version is `VersionPrefix` in `Directory.Build.props`
(0.9.3). Identity version for the Store package is `VersionPrefix.0` (`0.9.3.0`).

What 1.0 was waiting on is in the product: Preferences, `.wimport`, Explorer and VS Code
previews, the public documentation site, and Finger drawing (default when no pen is
detected). The Store listing was submitted by hand on 20 August 2026 for 0.9.2.

Three items remain. The release manifests and the winget workflow are built and
described in [docs/release-management.md](docs/release-management.md); what is left of
winget is account setup nothing in a repository can do for itself.

## 1. Video teaser

A short recording that shows what the application does. Ink following a pen, containers
carrying their strokes, and LiveView all need motion to read. The landing page, the Store
listing, and any announcement all need the same clip.

The home page already has a Vimeo embed (`site/index.html`) with tracking stripped
(`dnt=1`, no `player.js`). The id `763673561` is a placeholder. When the real clip is
published, replace that id only. Do not add YouTube or a self-hosted file.

Record against the current UI: Preferences, Finger drawing, the tab strip, LiveView
freeze/disconnect/reconnect, and a `.wimport` drop. The campaign hero on the home page
stays through 14 September 2026; the teaser is independent of that art.

## 2. Automatic Store submission

The one-time work is done: 0.9.2 was submitted by hand on 20 August 2026, with the listing,
screenshots, age rating, and package upload entered in Partner Center. Every field that was
entered is recorded in [installer/msix/STORE-LISTING.md](installer/msix/STORE-LISTING.md),
so an automated submission has an exact target rather than a guess. That page is also where
a listing change belongs, so the record does not drift from what the Store shows.

What remains is publishing the next version without visiting Partner Center, through the
Microsoft Store submission API. It needs an Azure AD application authorized for the Partner
Center account; its tenant, client id, and secret are infrastructure, so they belong in the
pipeline's variable group and never in this repository (CONTRIBUTING.md).

Two constraints shape where it can sit in the pipeline:

- Certification takes hours to days, so submission runs beside the GitHub release and never
  gates it (decision 13). A failed or slow submission must leave the MSI download unaffected.
- Only the released channel is submittable, and only from a run that already produced the
  MSIX it uploads (decision 9). Do not rebuild for the Store.

Packaging itself needs no work. `scripts/build-installer.ps1` writes an unsigned
`SQLBI.Whiteboard.<version>.x64.msix` for the released channel, the package declares
`.wboard` / `.wimport` and the thumbnail handler, and the Store re-signs it, so the SQLBI
certificate is not involved.

Watch the identity fields when touching any of this: `PublisherDisplayName` is `SQLBI Corp`
(no period) and copyright elsewhere is `SQLBI Corp.`, while the manifest `Publisher` is the
Store identity `CN=<GUID>` rather than a readable name. The three are unrelated fields.

Store availability is uneven on managed corporate machines, which is why the MSI channel
stays the primary route rather than a fallback (decision 10). Nothing links to the Store
until certification completes.

## 3. winget account setup

`scripts/build-release-manifests.ps1` and `.github/workflows/publish-winget.yml` are
built and need no further work. Two things have to be done once, by hand, before the
workflow can succeed:

1. Submit `installer/winget/` to microsoft/winget-pkgs once. `wingetcreate update` reads
   the previous version's manifests from that repository, so it cannot make the first
   submission. `PackageIdentifier` is `SQLBI.Whiteboard`, which the first accepted pull
   request reserves.
2. Add a `WINGET_TOKEN` repository secret: a classic PAT with `public_repo`, owned by the
   account that will carry the fork of microsoft/winget-pkgs. `wingetcreate` opens the pull
   request as that account, so the token is an identity rather than only a permission.
   `GITHUB_TOKEN` cannot stand in: it has no rights outside this repository.

Until both are done the workflow fails on every release, which is visible and harmless -
it runs beside the release and gates nothing.
