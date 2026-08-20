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
(0.9.4). Identity version for the Store package is `VersionPrefix.0` (`0.9.4.0`).

What 1.0 was waiting on is in the product: Preferences, `.wimport`, Explorer and VS Code
previews, the public documentation site, and Finger drawing (default when no pen is
detected). The Store listing was submitted by hand on 20 August 2026 for 0.9.2.

One item remains. The release manifests, winget, and Store submission are done: the
manifests are live at <https://whiteboard.sqlbi.com/stable.json>, the first winget
submission is in review, and the pipeline's Store stage submits each promoted release.
All three are described in [docs/release-management.md](docs/release-management.md).

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

## Waiting on the first winget submission

Not work, but the reason winget is not finished yet.
[microsoft/winget-pkgs#421386](https://github.com/microsoft/winget-pkgs/pull/421386)
submits `SQLBI.Whiteboard` 0.9.2 and is in review. Nothing in this repository depends on
it: the workflow that keeps the package current afterwards is already in place, and the
token it needs is set.

Until that pull request merges, `.github/workflows/publish-winget.yml` fails on every
release, because `wingetcreate update` has no previous version to read. That failure is
visible and gates nothing.

## Waiting on the first automated Store submission

Also not work. The pipeline's Store stage has never run. The only submission so far is the
manual one for 0.9.2, and the stage was written against the documented behaviour of the
Microsoft Store Developer CLI rather than against a run of it. The first promoted release
after this lands is the one that proves it.

0.9.4 is that release. `VersionPrefix` was bumped for it, because the Release stage
refuses to reuse an existing tag and 0.9.3 could not be released a second time to carry
the test.

Watch two things on that run. The stage has to pick the MSIX out of `drop-x64-true`, since
both matrix jobs pack an identically named package and only one of them is self-contained.
And a submission already pending in Partner Center makes `msstore publish` fail by design —
the stage does not delete someone else's in-flight submission to make room for its own.

Nothing gates on it. The stage runs after the GitHub release exists, so a failure leaves
every download in place and is visible as a failed stage.
