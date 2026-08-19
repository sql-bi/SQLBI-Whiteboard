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
<https://whiteboard.sqlbi.com> resolves its download links at load time and needs no edit
per release. The current product version is `VersionPrefix` in `Directory.Build.props`
(0.9.1). Identity version for the Store package is `VersionPrefix.0` (`0.9.1.0`).

What 1.0 was waiting on is in the product: Preferences, `.wimport`, Explorer and VS Code
previews, the public documentation site, and Finger drawing (default when no pen is
detected). Two launch assets remain.

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

## 2. Microsoft Store

Packaging is in the repo. `scripts/build-installer.ps1` writes an unsigned
`SQLBI.Whiteboard.<version>.x64.msix` for the released channel. The package declares
`.wboard` / `.wimport` and the thumbnail handler. The Store signs the package itself, so
the SQLBI certificate is not involved.

Listing, screenshots, age rating, and the first Partner Center upload are still manual.
Follow [installer/msix/STORE-LISTING.md](installer/msix/STORE-LISTING.md). Publisher
display name and the default `CN=` are `SQLBI Corp` (no period). Copyright elsewhere is
`SQLBI Corp.` Do not automate submission until that first upload has succeeded.
Certification must never gate the GitHub MSI release.

Store availability is uneven on managed corporate machines, which is why the MSI channel
stays the primary route rather than a fallback (decision 10). The teaser can wait; the
listing cannot ship without stills (1366×768 or 1920×1080) of the real UI.
