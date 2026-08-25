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
(1.0.3). Identity version for the Store package is `VersionPrefix.0` (`1.0.3.0`).

Declaring that number is decision 20 in [docs/decisions.md](docs/decisions.md). What 1.0
was waiting on shipped during 0.9.x: Preferences, `.wimport`, Explorer and VS Code
previews, the public documentation site, and Finger drawing (default when no pen is
detected).

No numbered work remains. The video teaser is recorded and served from the landing page
itself as `site/teaser-av1.mp4` / `site/teaser-h264.mp4` — the Vimeo-embed plan was
reversed, see decision 19 in [docs/decisions.md](docs/decisions.md); the production
script and staging assets are in `docs/teaser/`. The release manifests and the Store
submission are done and proven: the manifests are live at
<https://whiteboard.sqlbi.com/stable.json>, and the pipeline's Store stage has carried
two releases through certification unattended. The install-side verification list was
walked in full for 1.0.0, which was the last part of the chain that had only ever been
reasoned about. winget is the one piece still waiting, below. All of it is described in
[docs/release-management.md](docs/release-management.md).

## Waiting on the first winget submission

Not work, but the reason winget is not finished yet.
[microsoft/winget-pkgs#421386](https://github.com/microsoft/winget-pkgs/pull/421386)
submits `SQLBI.Whiteboard` 0.9.2 and is in review. Nothing in this repository depends on
it: the workflow that keeps the package current afterwards is already in place, and the
token it needs is set.

Until that pull request merges, `.github/workflows/publish-winget.yml` fails on every
release, because `wingetcreate update` has no previous version to read. That failure is
visible and gates nothing.
