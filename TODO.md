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

### 2. Drag and drop import

Dropping a file onto the canvas should import it as a container placed where the pointer was
released, rather than at a default position. Images already arrive through the clipboard, so
this is mostly hit-testing the drop point into camera space and reusing the existing import
path.

Small and self-contained, and it becomes the natural entry point for the format below.

### 3. An import file format

A plain-text format — Markdown is the obvious candidate — that builds a new whiteboard from
images and text. This makes boards authorable outside the application and generatable by a
script or an agent, instead of only by drawing.

Two decisions to settle before writing code: which subset of Markdown maps to containers, and
how positions are expressed when the source has none. Once the format exists, drag and drop
carries it too.

### 4. A rendered preview inside the `.wboard` archive

Both previews below need a picture of the board without opening the application. Rendering one
on demand means a full WPF load inside a shell extension, which is exactly what a thumbnail
provider must not do. Writing a rendered bitmap into the archive on save makes both previews
cheap.

This is a file-format change, so it lands before its two consumers rather than being retrofitted
around them. Boards saved by earlier versions carry no preview and have to degrade to the
document icon.

### 5. Preview of `.wboard` files in Explorer

The association exists, but a `.wboard` file shows only the document icon. A thumbnail provider
reading the embedded preview makes a folder of boards browsable. Note this adds the project's
first shell extension, which the installer has to register and the uninstaller has to remove
cleanly.

### 6. Preview of `.wboard` files in VS Code

The same preview in an editor: an extension that opens a `.wboard` file as an image rather than
as an archive. Useful wherever boards live in a repository next to the material they document.
Cheaper than the Explorer provider once the archive carries the bitmap, and shipped through the
marketplace rather than through the installer.

### 7. Video teaser

A short recording that shows what the application does. Nothing about the product is obvious
from a screenshot: ink following a pen, containers carrying their strokes, and LiveView all need
motion to read. It is also the asset the landing page, the Store listing, and any announcement
all need, so it sits on the critical path for all three.

Here rather than earlier because it should show the 1.0 UI, including the configuration dialog
and drag and drop.

### 8. Documentation on whiteboard.sqlbi.com

The site is a download page today. It needs real documentation: the tool set, containers,
LiveView, the DAX and SQL Server text containers, the import format, and the keyboard and pen
shortcuts. The README already carries most of this content, so the open question is whether the
site renders from those files or keeps its own copy — decide that before writing the pages
twice.

### 9. Microsoft Store

The largest remaining piece and the one with the longest lead time. It depends on none of the
feature work above, so the packaging can start in parallel at any point; only the listing has to
wait for the teaser and for screenshots of the finished UI.

1. **Build an MSIX.** None exists; only MSIs are produced. `Bravo.Installer.Msix` in the Bravo
   repository is the template. The application is already MSIX-clean: settings live in
   `%APPDATA%`, the only registry use is a read, and nothing writes to the install folder. The
   Explorer thumbnail provider is the one thing that changes this — an MSIX registers a shell
   extension through its manifest rather than through installer custom actions, so items 5 and 9
   have to agree on how the extension is declared.
2. **Give the MSIX its own version derivation.** Store versions must end in `.0`, and the
   current four-part scheme never produces that — the last part is seconds since midnight
   divided by two.
3. **Do the first submission by hand.** Listing text, screenshots, age rating, and privacy
   details are one-time work no pipeline performs.
4. **Then automate it** with the Microsoft Store Developer CLI or the Partner Center submission
   API, as a stage that runs *in parallel* with the web release. Certification takes hours to
   days and must never gate a download that is otherwise ready.

Note the Store signs the package itself, so the SQLBI certificate is not involved. Store
availability is also uneven on managed corporate machines, which is why the MSI channel stays
the primary route rather than a fallback (decision 10).

### 10. Finger mode

Touch input as an alternative to the pen, for machines without one. Last because it is the only
item here that may reasonably ship after 1.0: the interaction model assumes a pen and a separate
pointing device, and finger drawing has to answer what pan and zoom do instead. That is a design
question rather than an implementation one, and answering it badly under launch pressure is
worse than shipping 1.0 as pen-first and adding touch in 1.1.

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
