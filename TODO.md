# TODO

Outstanding work, in the order it should be started. Items are written to be picked up cold:
each says what to change and why it matters.

Background reading, in this order:

- [CONTRIBUTING.md](CONTRIBUTING.md) — branch and pull request workflow. `main` is protected.
- [docs/release-management.md](docs/release-management.md) — how the project is built and shipped.
- [docs/decisions.md](docs/decisions.md) — why it is built that way. Each entry says whether it
  is implemented or only agreed.

## Where the project stands

0.2.1 is released. The delivery chain works end to end: a merge to `main` builds, signs, and
publishes a pre-release to GitHub Releases, and one approval promotes that same build to a
release. <https://whiteboard.sqlbi.com> resolves its download links at load time and needs no
edit per release. The .NET 10 upgrade has been checked on real pen hardware, and the installers
have been exercised on a real machine.

Shipping is therefore no longer the problem. The 0.2.x releases exist to get builds into hands
and to keep the pipeline exercised; **1.0 is the launch** — the version the site, the teaser,
and the Store listing are written for. Everything below is what 1.0 needs.

## The road to 1.0

Ordered by dependency first and effort second, so an item can be picked up as soon as the ones
above it are done. The feature work comes before the material that describes it, because a
teaser and a documentation page written against a moving UI have to be redone.

### 1. Configuration dialog — done

`Tools > Preferences...` is a searchable catalog: startup monitor, start full screen, laser
trail timing, and the toolbar options. New settings are catalog entries rather than a new
layout. The menu is still always visible; hide-on-demand is a later UI pass, not a missing
piece of this item.

### 2. Drag and drop import — done

A drop on the window imports at the release point in camera space. Images become image
containers; `.txt`, `.dax`, and `.sql` become text containers in the matching language.
The classifier is the hook for the import format below — `.md` is still unsupported.

### 3. An import file format — done

`.wimport` is Markdown that builds containers: `##` is one container, `---` starts a new row,
images and DAX/SQL are inferred from the body or a local link. Drop adds to the current board;
Open / double-click (released channel) starts a new board that saves as `.wboard`. There is no
export. Languages are a table so later fences (Python, C#, …) do not need a new matcher.

### 4. A rendered preview inside the `.wboard` archive — done

Saving writes `preview.png` into the ZIP. Older boards and empty boards have none.

### 5. Preview of `.wboard` files in Explorer — done

The released installer registers a Native AOT in-process thumbnail handler that reads
`preview.png` only. It does not load WPF. Boards without a preview keep the document icon.
The pre-release channel does not register `.wboard`, so it does not register the handler
either.

### 6. Preview of `.wboard` files in VS Code — done

`sqlbi.sqlbi-whiteboard` on the Marketplace opens a `.wboard` file as the embedded
`preview.png`. The source is `vscode/sqlbi-whiteboard/`. Boards without a preview show a
short message. Bump that folder's `package.json` version to ship an update; GitHub Actions
publishes it. The desktop installer does not carry the extension.

### 7. Video teaser

A short recording that shows what the application does. Nothing about the product is obvious
from a screenshot: ink following a pen, containers carrying their strokes, and LiveView all need
motion to read. It is also the asset the landing page, the Store listing, and any announcement
all need, so it sits on the critical path for all three.

Here rather than earlier because it should show the 1.0 UI, including the configuration dialog
and drag and drop.

### 8. Documentation on whiteboard.sqlbi.com — done

The landing page stays the download. Guide, shortcuts, the `.wimport` contract, and
contribute/publish are separate pages under `site/`, linked from the nav. The site is
hand-authored HTML, not generated from the README. `docs/wimport.md` remains the in-repo
contract; `site/wimport.html` is the same grammar for the public site.

### 9. Microsoft Store

Packaging is in the repo. `scripts/build-installer.ps1` writes an unsigned
`SQLBI.Whiteboard.<version>.x64.msix` for the released channel. Identity version is
`VersionPrefix.0` (0.6.0 → `0.6.0.0`). The package declares `.wboard` / `.wimport` and the
thumbnail handler in the Appx manifest. Listing, screenshots, age rating, and the first
Partner Center upload are still manual — see `installer/msix/STORE-LISTING.md`. Do not
automate submission until that first upload has succeeded. Certification must never gate
the GitHub MSI release.

Note the Store signs the package itself, so the SQLBI certificate is not involved. Store
availability is also uneven on managed corporate machines, which is why the MSI channel stays
the primary route rather than a fallback (decision 10).

### 10. Finger mode

Shipped as a preference (`FingerMode`: Off, On, When no pen is detected), default Off.
When effective, one finger uses the current tool and two fingers still pan and pinch-zoom;
a second finger cancels an in-progress finger stroke. Eraser and Pan appear on the toolbar
because there is no inverted tip. "When no pen is detected" looks at `TabletDeviceType.Stylus`,
which is a digitizer, not a pen in the room.

## Distribution plumbing

Independent of 1.0 and of each other. Neither blocks the launch, and both are cheaper to do
before it than during it.

### Publish release manifests

`stable.json` and `dev.json` alongside the binaries: version, date, URLs, SHA-256. One source
that the download page, a future in-app update check, and winget automation can all read,
instead of each deriving the same facts differently.

### winget

`winget-releaser` opens a pull request against `microsoft/winget-pkgs` when a release is
published. Only possible because the repository is public and the assets are unauthenticated.

## Deferred, deliberately

- **arm64.** Not built. Worth adding if Surface devices matter for a pen application.
- **The brand mark.** The icon is the Fluent whiteboard glyph in SQLBI colours: in-family and
  deliberate, but not distinctive (decision 12). Replacing it touches no installer plumbing —
  change the SVG and rerun `scripts/build-assets.ps1`.
- **Landing page copy.** The tagline and feature list on `site/index.html` were drafted from
  the README and have not had an editorial pass. They are the first thing anyone reads about
  the product, and the documentation work above supersedes them if it lands first.
