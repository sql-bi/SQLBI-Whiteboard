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

The repository is public, so Actions is free and **Actions logs are public**.
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

The reason is licensing, because SQLBI is a commercial vendor; v6 and v7 are technically
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
  field, so the collision produces no error. The portable ZIP originally shipped
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

The repository is public, so release assets are downloadable without authentication, and this
decided the choice ahead of cost. GitHub serves them from a CDN at no bandwidth cost, the
prerelease flag distinguishes the two channels natively, `/releases/latest/download/...` is
a permanent link the download page can hard-code, and winget reads the same source.

No storage account, service connection, or blob RBAC is therefore needed.

Creating a release requires a GitHub service connection with `contents: write`, separate
from the one used to read source. It is named `sql-bi write assets`.

## 11. `main` is protected and every change arrives by pull request

**Agreed; enable in the repository settings.** This supersedes an earlier decision to defer
protection until development had settled.

The original objection was that requiring pull requests would slow early development. It
does not, because both maintainers work through coding agents, so creating a short-lived branch and
opening a pull request is a line of instruction rather than a change of habit. The cost was
overestimated, and `main` is about to start feeding a public download channel on every
merge.

Configuration, in two stages because the second has a prerequisite:

- **Done** — a pull request is required before merging, and the bypass list is empty.
  The list is empty because protection with a bypass is often sidestepped without anyone
  noticing.
- **Remaining** — require the pull request validation checks to pass. GitHub only offers
  checks it has already seen, so select them once the workflow has run.

Approvals are deliberately **not** required, because a two-person core team should not be
blocked when one member travels. Review is welcome but optional. Add required review only if
something slips through.

There is no `develop` branch and none is planned, because `main` is the pre-release channel, and a
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
  was decided and now runs in under an hour unattended. That shortens how long the Store
  lags the download, and the reasoning still holds, because it is still someone else's
  queue, it can still stall or reject, and none of that should be able to hold up a
  release that is already built and signed. The Store stage runs after Release rather than beside it, so
  the GitHub release already exists by the time it starts, a failed or slow submission
  changes nothing that shipped, and a build that was never promoted is never submitted.

The first submission was manual, and was made on 20 August 2026 for 0.9.2, because listing,
screenshots, and age rating are one-time work no pipeline performs.
`installer/msix/STORE-LISTING.md` records every field that was entered, which is what the
automated submission reproduces. Automating the upload was deliberately held until that
first submission had succeeded, so an API failure could never be confused with an
incomplete listing.

Everything after it is the Store stage's job. It submits packages only — listing text,
screenshots, and **What's new** carry over from the last published submission untouched.
Changing them stays a deliberate act in Partner Center, recorded in `STORE-LISTING.md`.
Of the submissions it did not create, the stage deletes only one left `PendingCommit` by its
own failed upload, because any other pending submission may be a person's listing edit
that the pipeline must not discard.

The submission identity is not the signing one. The Partner Center account is associated
with a different Microsoft Entra tenant than the one the pipeline signs in, so the two
cannot be the same principal. Sharing one is not wanted anyway, because a single leaked
secret would then both sign as SQLBI and publish as SQLBI.

## 14. Bravo's telemetry was not ported

**Implemented as an omission.**

Bravo's installer carries a custom-action DLL and opt-in telemetry checkboxes wired through
a long sequence of remember-properties. None of it was carried over, and the installer is
considerably simpler for it. Port it only if the data is actually wanted.

## 15. Only the self-contained build is published

**Implemented.**

Each release carries three assets: the per-machine installer, the per-user installer, and
the portable ZIP, all self-contained. Publishing both flavours meant six assets with names
like `SQLBI.Whiteboard.0.1.0.x64-frameworkdependent-dev-userinstaller.msi`, where a visitor
has to decode four dimensions before downloading anything.

The self-contained installer is roughly 73 MB against 11 MB, and needs no .NET runtime
installed. The larger download was accepted because the visitor installs nothing else.
The framework-dependent build is still produced and kept as a pipeline artifact.

## 16. SQLBI Whiteboard is MIT-licensed open source

**Implemented.**

The repository is public and carries the MIT license, the same as Bravo, and the installer
presents the same terms. This was confirmed deliberately, because the license
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

Almost all of that time was CAB compression, and the job exists to validate the WiX
authoring, which compression does not exercise. The framework-dependent payload is roughly 15 MB against 252 MB, and
runs through the same authoring and the same `<Files Include>` harvesting.

The two variants are the diagonal pair, `stable/perMachine` and `dev/perUser`. The four
variants are four different paths through the `<?if?>` branches in
`installer/wix/SQLBI.Whiteboard.wxs`, so building only one would miss a typo in the dev
branch or a broken per-user directory. The diagonal pair still takes both sides of every
conditional.

What this stops covering is narrow: a failure specific to self-contained output, and the two
untested channel-scope combinations. Azure Pipelines builds all four variants self-contained
on every merge to `main`, so both are caught before anything is signed or published.

`scripts/build-installer.ps1` defaults to all four variants and to self-contained. The
reduction is set in the workflow and the script keeps those defaults, so nothing that
ships can inherit it by accident.

## 19. The teaser video is a file on the site, not an embed

**Implemented.**

The original plan was a Vimeo embed with tracking stripped (`dnt=1`, no player script),
deliberately excluding YouTube and self-hosted files. It was reversed when the clip
existed, for three reasons:

- Vimeo is blocked or unreliable in several regions; an embed shows those visitors a dead
  frame. A file served with the page plays wherever the page loads.
- The embed was the only third party on the privacy page. Without it, the page can say
  "static HTML, no third parties" with no exception.
- The numbers do not support the bandwidth argument for an embed. The clip is 42 seconds
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
no bandwidth meter to watch anyway. If a launch ever approaches that, the options are
a lighter loop rendition or a caching proxy in front of the domain, and an embed stays
ruled out.

---

## 20. The version is 1.0.0

**Implemented.**

The open question was what had to be true before the version stopped being 0.x. The
answer was a set of product features: Preferences, `.wimport`, the Explorer and VS Code
previews, the documentation site, and Finger drawing. All of them shipped during 0.9.x,
and no numbered work was left behind them.

Delivery does not change with it. A merge still publishes a pre-release, promotion is
still an approval on that same run, and the Store still takes the MSIX from it, so 1.0.0
reaches people by the path 0.9.5 already proved. Because the pipeline did not change, the
number could be treated as a statement about the product.

The Store carries `1.0.0.0` as its identity version. winget starts at 1.2.2, the version its
first submission settled on.

---

## 21. SVG is kept as markup and drawn by SharpVectors

**Implemented.**

WPF has no SVG decoder, so supporting SVG at all meant taking a rendering library —
including the option of rasterizing on arrival, which needs one just the same. The choice
was therefore which library to take.

`SharpVectors.Wpf` produces a `DrawingGroup`, so an SVG container is redrawn at whatever
size it is displayed at rather than stretched from pixels. This is why SVG is accepted on
a canvas whose zoom is unbounded. It is BSD-3, managed-only, and ships no native
binary, which keeps it out of the signing step and out of the per-architecture question
the installer would otherwise have to answer. Direct2D was considered, since
`Vortice` is already referenced and would have cost nothing, and rejected because it
implements a restricted SVG subset with no `<text>` element, which is most of what a DAX
SVG measure emits.

Assets are stored as the bytes that arrived, and `BoardArchive` has always treated them as
opaque, so the format version did not move. A board holding an SVG opened in 1.0.3 draws
the missing-image placeholder for it rather than failing to open.

The renderer is given `ExternalResourcesAccessModes.Ignore`. Its default is to fetch what
the markup names, which would let a pasted or dropped file turn opening a board into an
outbound request.

Three rendering defects are worked around before the drawing is produced rather than in
the drawing itself. The stored asset is untouched in every case; only what is handed to
the renderer changes.

- SharpVectors puts an `<image>`'s own `clip-path` on the same drawing group as the scale
  and offset it builds for the image's size, so the clip is transformed along with the
  bitmap (issue 98). `SvgMarkup.Rewrite` moves the clip, and the image's transform with
  it, onto a `<g>` around the image, which the renderer handles as the author meant.
- Text with `letter-spacing` is drawn one glyph at a time, and each glyph is given the
  text's own anchor, so with `text-anchor="middle"` every glyph is centred on the pen and
  the pen advances half a glyph; with `end` it does not advance at all. A centred label
  piles up in half its width. `SvgMarkup.Rewrite` removes the spacing from text that is
  not anchored at its start, so the whole string is measured and placed at once. The
  label is placed where the author asked, a little tighter than they asked. A tracked
  heading anchored at its start is left as written, since that path draws correctly.
- An embedded bitmap is fitted into its `<image>` by the WPF `Width` and `Height` of the
  decoded picture, which are device-independent units and scale with the DPI the file
  declares. A 72-DPI PNG comes out a third larger than its pixels, a 216-DPI logo less
  than half its size, while the browsers measure pixels. `SvgImageCodec` gives the
  renderer an image visitor that decodes base64 data URIs itself and hands back the same
  pixels at 96 DPI, so a unit is a pixel. Anything else is left to the renderer's reader,
  still under `ExternalResourcesAccessModes.Ignore`.

---

## 22. Pen ink is collected from the pen, not from the InkCanvas

**Implemented.**

`InkCanvas` collects a stroke between a stylus down and the matching up. On a pen whose
barrel switch shares the report with the tip switch — the development Cintiq, and the
reason this was found — pressing or releasing that button fabricates a stylus up followed
by a stylus down, and between a release and the next press the driver reports the tip as
open while it is still pressing. WPF therefore reports the pen in the air for as long as
the button is held, and the contact is torn in two on every click.

Every attempt to repair that inside the InkCanvas moved the fault rather than removing it:
strokes joined across stretches the pen never drew, ink was lost between a release and the
next press, and a modifier held down could not be released. Meanwhile the pen's own packet
stream never breaks — position and pressure arrive continuously, in contact or not, which
a trace of a real session established (`PenTrace`, enabled by pointing
`SQLBI_WHITEBOARD_PENTRACE` at a file).

So the window reads that stream directly. `MainWindow.AppendPenInk` owns the contact — it
begins at the first pressured packet and ends after a run of weightless ones — and applies
the straight-line constraint and the calligraphy dynamics to each point as it arrives. The
wet stroke is drawn by `BoardSurface.PendingStroke` rather than by WPF's dynamic renderer.
The straight-line constraint is one boolean read per point, independent of whether WPF
thinks the pen is down. Shift and the barrel button must be held at the start of the
physical stroke, so an accidental press during writing does not straighten the ink,
including a pen button mapped to Shift. `PenStraightLineConstraint` remembers each
modifier separately at contact (or the first pressured packet when WPF misses the down).
A release disables that modifier for the rest of the stroke; fabricated up/down events
cannot rearm it, nor can a different modifier pressed mid-stroke take over. Only
`EndPenInk` resets the choice for a new stroke. Mouse ink uses the same rule, starting
at the left-button down.

`TouchInkCanvas` keeps the InkCanvas for finger ink, where nothing tears the contact, and
hosts the laser sampler and the hover tracker. It collects no pen ink; strokes the
InkCanvas still opens for a pen are discarded on arrival.

The cost is that pen wet ink is drawn on the UI thread rather than WPF's dedicated
dynamic-rendering thread. Reverting is not planned, because the machinery this replaced —
a stylus plug-in for the constraint, recovery of ink from in-air packets, splitting a
collected stroke back into contacts, and a second stroke lifecycle inside the renderer —
was several hundred lines and never converged.

---

## 23. A mouse gets the tools, not the gestures

**Implemented** in 1.2.0. The proposal it came from, with the alternatives that were
weighed and rejected, is [mouse-mode.md](mouse-mode.md).

The application was built so that no input device imitates another: the pen inks, touch
navigates, and the mouse moves things. This was deliberate, because it keeps the pen path
direct, and it stays the rule for the pen.

The cost was that people who downloaded a whiteboard onto a laptop with no pen and no
touchscreen found the toolbar did nothing. 1.1 conceded the point at startup and
collected votes in
[discussion 78](https://github.com/sql-bi/SQLBI-Whiteboard/discussions/78). So there is now
a **Mouse drawing** setting, on by default when Windows reports neither a stylus nor a
touchscreen, under which the left button does what the selected tool does.

The rule for it, and for any later change to it, is **a mouse gets the tools, not the
gestures.** Everything on the toolbar becomes reachable with a mouse. Nothing that exists
because of what a hand and a pen can do — pressure, hover, the reverse end, the barrel
button, palm rejection, two fingers — is simulated with modifiers and timers. Where a
gesture has no honest mouse equivalent the mouse does without it, and the documentation
says so. The rule keeps mouse support from adding work to every future input feature.

Four consequences are worth recording, because each was a choice with a live alternative:

- **`Ctrl` is the old mouse.** Letting the left button draw takes away the most useful
  thing about mouse input, moving an image without leaving the Pen, so it is kept on a
  modifier. Giving it to the right button instead was
  rejected: right-drag pan is the only pan that needs no keyboard.
- **Framing moves behind `Ctrl` too, but only where it has to.** Double-click is tested
  before the tool branch, so two quick dabs with the Pen would otherwise reframe the board.
  With Select or Pan active, and whenever Mouse drawing is off, a plain double-click still
  frames.
- **Pressure is the constant the straight-line constraint already uses.** Deriving it from
  speed was rejected as a default: the width would vary for a reason the hand cannot feel,
  and a wobble nobody asked for reads as a bug. Calligraphy is unaffected, because its width
  comes from speed rather than from pressure, and the highlighter already ignores pressure.
- **The tool becomes sticky.** Nothing is handed back after a mouse gesture that was not a
  `Ctrl` borrow — including a right-button pan, which would otherwise take someone who chose
  the Eraser and quietly leave them holding a pen.

This was a few hundred lines rather than a subsystem because of decision 22. Because pen
ink is collected from raw points rather than from the InkCanvas, `AppendInkPoint` takes a
screen point and a pressure and does not depend on the device; the mouse calls it,
and gets the straight-line constraint and the calligraphy dynamics without a second
implementation. The erase, pan and container paths already existed on the mouse handlers —
`PointerAction.Erase` was written and unreachable.

The pen path did not change, which was the condition for building mouse drawing.
Every mouse handler already returned early on a non-null `StylusDevice`, so pen-promoted
mouse events never enter the mouse path. Mouse drawing can therefore be left on beside a
pen, which is why **On** is offered and not only the automatic default.

---

## 24. The mouse mode offer has no default button

**Implemented** in 1.2.1.

Decision 23 left Mouse drawing discoverable only in Preferences, which is the one place
someone who does not know the feature exists will not look. **Picking a tool from the toolbar
with the mouse** is a clear signal that they might want it, and the application already
sees it. A pen user reaches for the palette with the pen, so a mouse there means the next
stroke will not draw, and the offer appears at that point.

Three properties of it were chosen rather than inherited, and each is the kind a later
change would quietly undo:

- **Neither button is `IsDefault`, and Enter is swallowed.** The dialog asks how the
  application should behave, and the two answers differ, because one changes what the left
  button means. A default button would let
  Enter answer it for someone who was typing, and whichever button we picked would be the
  wrong one half the time. `Window_PreviewKeyDown` therefore marks every Enter handled
  before it can reach a button, and invokes one only when the person has deliberately moved
  focus to it. Escape closes, because dismissing is always safe. Nothing is focused when
  the dialog opens, so the first Tab reaches the checkbox rather than a primed button.
- **Once a session, and the checkbox is unchecked.** Prompting on every toolbar click would
  be intolerable, and prompting once and never again would lose the person who was not
  ready to decide. A single Cancel answers this session; the checkbox answers every one
  after it, and `Help → Preferences → Input` is the way back from that.
- **The offer is queued, not shown from the click handler.** It is dispatched at background
  priority so the click first does what it came to do. The tool is selected, and the dialog
  then explains why it may not behave as expected — rather than intercepting the click and
  leaving the person unsure whether their tool was chosen at all.

**Enable mouse mode** sets `MouseMode.On` rather than `WhenNoDigitizer`, because the offer
can only have appeared on a machine where the automatic default already decided not to.

---

## 25. Preferences says one line and keeps the rest behind a chevron

**Implemented** in 1.2.2. Every setting carries two texts rather than one: a `Summary` of a
single line that is always on the row, and a `Description` — the defaults, the reasoning,
the consequences — that appears only when the row's chevron is pressed. A setting whose
summary is the whole story leaves `Description` empty and gets no chevron at all.

The dialog had grown to where the longest description ran to six wrapped lines, and a
category was so much prose that finding a setting meant reading all of it. The reasoning
is kept, because it lets someone decide about a setting instead of guessing, so it is
shown only on request rather than deleted.

Two parts of this are choices a later change could quietly undo:

- **Not a tooltip.** A tooltip is the usual way to hide text, and it was not used because
  this application is used with a pen and a finger, and neither hovers, so on a Cintiq the
  reasoning could not be read. The chevron is a real button because pressing is the one
  gesture every input this application supports can perform.
- **Search marks what it matched.** Every hit is highlighted where it lies — in the title,
  in the summary, in the prose once that is open. A hit lying only in the prose has nothing
  on the row to mark, so the chevron is marked instead, which shows that the match is
  in the prose without opening a row under the hands of someone still typing. Expanding those rows
  automatically was built first and then removed, because the list jumps on every
  keystroke. A row matched only by a keyword marks nothing, because the word is not in
  the prose either, and a marked chevron would point to text that is not there.

The chevron sits under the title and in front of the summary. The right-hand edge of the
row was tried first and rejected, because editors range from a switch to a wide combo, so the
chevron landed somewhere different on every row; it took the width the summary needed to
stay on one line; and on **Snippet format order** it came to rest in the same column as
that editor's own reordering chevrons, where it read as one of them.

**Always show the Eraser is drawn rather than switched.** It is one boolean and a switch was
the ordinary answer, but the question it asks is where a button appears, so it is offered as
two pictures of the toolbar — identical but for the Eraser, and redrawn to match whichever
arrangement **Layout** has chosen, since that setting decides whether the Eraser joins the
bar or takes a row beneath it. The Eraser is in the accent color because the difference
between the two pictures is the whole question, and the Off picture reserves its space
rather than closing up, so that turning it on adds the Eraser instead of moving everything
else.

---

## 26. Export cuts the board where it is empty

**Implemented for PowerPoint and PDF.** The plan, with the alternatives, is
[export.md](export.md); the calls made while building it are in
[export-decisions.md](export-decisions.md).

A `.wboard` is one unbounded plane with no notion of a page, so exporting it to a deck is
first a question of where the slides are. The answer taken is the recursive whitespace
cut: a container and the strokes linked to it are one unit, every other stroke is a unit
of its own, and a region is cut at the widest empty band between the units' projections
on either axis, recursively, until it fits a slide or no band is wider than a threshold.
Nothing can be cut through, a stroke that spans two containers keeps them together, and
the cut tree gives a reading order. Bottom-up clustering produces nearly the same areas and
still needs the cut for anything larger than a slide; a grid splits objects; manual frames
on the board are the long-term answer for someone preparing a deck and are a later phase,
because they change the file format.

"Fits a slide" is defined by the smallest text the person will accept: a text container's
body is 18 world units, a 16:9 slide is 960 points wide, so 12-point text caps an area at
1440 world units. The same cap applies to areas with no text. An area that cannot be cut is
scaled down and the dialog says by how much, rather than being tiled across slides.

The slide is a picture rendered by the same `BoardSurface` that draws the screen, at twice
full HD, so calligraphy, the highlighter, and SVG come out as they are seen. The text
containers go in the speaker notes so DAX and SQL can still be copied. **Editable** is the
other slide content: images as pictures, text containers as text boxes carrying the
syntax colors the screen shows, and all the ink as one transparent picture on top, placed
through the same camera the picture would have used. It is less exact than the picture, and
it is the default for a new setup because a deck is exported to
be reworked more often than to be shown as is; the picture is one choice away. Slides are
in reading order by default, because it is the order a reader would guess.
Ink as freeform shapes was left out: it would be a second stroke renderer to keep in step
with the first.

`DocumentFormat.OpenXml` writes the deck and `PdfSharp` the PDF: Microsoft's own SDK and
a long-lived MIT library, both managed-only, so they pass the tests decision 21 set for a
dependency. The writers live in a new assembly, `SQLBI.Whiteboard.Export`, with no WPF
dependency, which the signing step lists.

A PDF page is a picture by default. Unlike a slide, a PDF page can be any size, so the
whole board can go on one page the shape of the board,
rendered at up to six thousand pixels on the longer edge, for a reader who zooms. Pages get
a bookmark each and a footer with the board name, date, and page number. **Vector** is the
other page content: ink as paths that sweep the nib WPF uses along each stroke, text as
text in embedded subsets of Segoe UI and Consolas, images as images, so the page stays
sharp at any zoom and the code can be selected. It is the same element list the editable
deck uses, with the ink as strokes in their own z-order rather than one overlay. Print
through Microsoft Print to PDF was weighed as the zero-dependency route and kept for
later, because the driver, not the application, asks for the file name.

---

## 27. A frame is a slide drawn on the board, and it is not a container

**Implemented.** Phase E5 of [export.md](export.md); the smaller calls are under E5 in
[export-decisions.md](export-decisions.md).

The automatic cut (decision 26) serves a board that was drawn without a deck in mind.
Someone preparing a board for a deck wants to say where the slides are, and the way to
say it on a whiteboard is to draw a rectangle. **View → Frame** adds one the size of the
screen; it can be moved, resized, renamed, and reordered like anything else selectable,
and Export takes it as it is: whatever sits inside a frame is that slide, frames come
first, and the rest of the board is cut automatically.

A frame is deliberately not a container. The one rule the board has about containers is
that a stroke touching exactly one of them is linked to it, and a frame around a picture
would break that rule for every stroke on the picture. So a frame links nothing, is
selected only by its edge or its title tab, and moving it moves nothing inside it. Export
reads it as a label over the content.

Frames are the first object to change the file format since 1.0, and the change is kept
as small as it can be: a board is written as version 6 only when it holds a frame, so a
board without one still opens in every release since format 5.

---

## 28. Plain text is the last snippet format, and a language must earn a snippet

**Implemented.**

Snippet format order decides which language a paste becomes: the first that accepts the
text wins, and plain text accepts everything. With plain text first, the shipped default
since 1.0, every paste was a note until someone found the setting; a language added by an
update (KQL in 1.3.0) joined the end of the list, behind plain text, and so never applied.
The goal is that a new language works on the day it ships, with no visit to Preferences.

Two things were changed together, because the first is safe only with the second:

- **Plain text comes last** in the default order: DAX, SQL, KQL, Plain text. A saved order
  equal to a default the application ever shipped was never chosen by anyone, so an
  upgrade replaces it (the settings version moved to 16 to do this once). A chosen order
  is kept, and a language it does not know joins it in front of plain text wherever plain
  text sits, unless plain text is first, which is the one order that means "keep my pastes
  plain"; there the new language goes last. The order itself carries the intent, so the
  checkbox that was considered, "new languages go before plain text", was not added, because it
  would say the same thing twice and allow the contradictory state of plain text first with
  languages jumping ahead of it.
- **A language claims a snippet only when it carries a signal** of its own. Every parser
  accepts a bare name or a number: `Sales` is a DAX table expression and a KQL query. With
  plain text last that would have turned a one-word note into code. So DAX needs a
  function, operator, keyword, column reference, or variable; T-SQL a keyword or function;
  KQL a pipe, query operator, keyword, command, or function. The parser still decides
  once the signal is found.

Both rules are about the languages that can read a snippet. Choosing a language by hand
and recognizing one automatically are now two separate lists: a text container can be set
to any of fifteen languages, while only DAX, T-SQL, KQL, and plain text take part in the
snippet format order. A language that only colors never claims a paste, never joins a
saved order, and is ignored if one is written into settings by hand — reading it as plain
text instead would move plain text up an order it was never part of. The archive format is
unchanged by this: a board saved with one of the new languages opens in an older release
as plain text, with its source intact.

---

## 29. A text container's width in columns is the line width of its snippet

**Implemented.**

The DAX formatter wraps to a maximum line length; the SQL and KQL formatters break by
structure and have no width at all. A "line width" setting shared by the three languages
was therefore not added, because only one of them would honour it. What every snippet already
has is a width, and a text container wraps its lines at that width on screen, so the
container's width in columns is the line width the reader sees. Two things were changed
to make it the line width the formatter uses too:

- **F6 formats DAX to the columns the container shows.** Columns are invariant under the
  display-mode scaling, so this works whether or not the container has been enlarged for
  the room. A new container is 65 columns wide, which is also the formatter's default, so
  a pasted and formatted snippet has no visual wraps; before, the default width showed
  about 58 columns and formatted DAX wrapped on screen at once.
- **Shift while dragging the handle changes the width in columns** and reflows the text,
  keeping the font size; the handle shows the count while dragging. A plain drag keeps
  scaling the container like a picture. Replacing scaling with reflow was considered and
  rejected, because making a snippet bigger for the room is the resize a presenter needs, it is
  what every other container does with the same gesture, and reflowing on a drag would
  change line breaks while someone is only making space. Shift already means "constrain"
  for strokes, so it reads as the narrower version of the gesture. The right edge of a text
  container is the same width handle without the modifier: the cursor says so on hover,
  which is what makes it discoverable, and the corner stays the scale handle.

Structural switches for SQL and KQL, one column per line or one pipe per line, are the
levers those languages have, and can come as yes-or-no settings when someone asks.

---

## 30. Autosave is a safety net, and never the save

**Implemented** in 1.5.0, answering
[issue 121](https://github.com/sql-bi/SQLBI-Whiteboard/issues/121).

The board is copied into a session slot every thirty seconds and again on the way out, and
the next start brings it back. **Nothing on this path ever writes to the `.wboard` the
person named.** A file changes when they ask for it to change, and an application that
saved on their behalf would eventually overwrite something they meant to keep. Everything
below follows from that.

- **A board with a name is asked about; a board without one is not.** An untitled board has
  no file its changes could overwrite, so nobody is interrupted on the way out to protect
  it. It goes into the session and comes back. A named board
  has somewhere its changes belong, and only the person can say whether they belong there.

- **The exit question has four answers and a default, where decision 24 refused one.**
  Save, **Keep for next time**, Discard changes, Cancel. Keep is `IsDefault` because it is
  the one answer that decides nothing: the file is untouched, the board returns at the next
  start, and Enter landing on it can never be the wrong outcome. The mouse mode offer
  had no such answer, because both of its answers changed something, so neither could be
  the default. Whether a dialog has a default depends on its answers.

- **Cancel stays because of LiveView.** Keep and Cancel look interchangeable — both end
  with the board in front of you — but a restored LiveView container comes back as its last
  frame and has to be reconnected by hand, because Windows cannot save the capture
  permission. Cancel is the only answer that keeps a feed live, which matters most in the
  situation where the window gets closed by accident: mid-recording, in front of an
  audience. The camera position is carried in the session for the same reason, to narrow
  what is left of the gap.

- **Modified means two things, because one signal cannot cover it.** The command history
  carries a save point — the command on top of the undo stack when the file was written —
  so a board undone back to what was saved stops counting as changed, and one undone *past*
  it starts counting again. A counter cannot express that, since undo moves it the same way
  drawing does. The dozen places that change the document without going through a command
  set a flag instead. The LiveView paths deliberately set neither: a frame arriving on its
  own is not somebody changing the board, and would otherwise put an unasked question in
  front of anyone who left the application running beside a feed.

- **One slot per running copy, not one file.** Each copy takes a slot named by a GUID and
  holds a lock file open for as long as it runs. Two windows therefore never write to the
  same files, so running two of them is safe without the application having to forbid it.
  Forbidding it was considered and rejected, because two boards side by side is a real
  thing to want. A slot whose lock can be taken belonged to a copy that has
  gone; its sidecar says whether it went on purpose. One start restores one slot, the most
  recent; the others keep their slots and are offered again rather than being discarded,
  and anything abandoned for thirty days is pruned.

- **A clean exit restores silently; a crash asks.** Coming back to where you were is
  ordinary and needs no ceremony, and **Reopen the last board** in Preferences turns it off
  for anyone who wants a blank board every time. Recovering after a crash is not ordinary,
  so it is offered rather than done — and it is offered whatever that setting says, because
  the setting is about how the application starts, not about whether work lost to a crash
  should be retrievable. Declining discards that slot, and the question says so. Keeping the
  slot would put the same question in front of the same person at every start for thirty
  days, and people learn to dismiss a question they see that often.

- **An unmodified session keeps the file name and not the board.** The file is the better
  copy — it may have been edited elsewhere since — so a slot that matched its file reopens
  the file. Only a board that never matched one carries its own copy.

- **Opening a board asks the slots whether anything newer is waiting for it**
  ([issue 122](https://github.com/sql-bi/SQLBI-Whiteboard/issues/122)). Without this,
  opening the board they lost, when somebody is most likely to want their recovered work,
  offered nothing, because the recovery offer only appears at startup and only for the
  newest slot. The sidecar already records the path, so it is a
  lookup. Three calls inside it:
  - **Every route into a board goes through one place.** The Open dialog, a drop, and a
    double-click in Explorer all arrive at `OpenPathAsync`, so the question is asked there
    rather than three times over.
  - **The answer is Yes, No, or Cancel, and No discards.** Opening the saved file by name
    answers the question about those changes, and leaving the slot would bring the same
    prompt back on the next open of the same file. Cancel opens neither and
    keeps the slot, which is what an accidental Yes-or-No needs to be recoverable from.
  - **Only a slot left by a crash is offered.** One that exited cleanly is either restored at
    startup or dropped there, and offering it here as well would ask twice about one board.

---

## 31. Design objects are board objects, and a shape is picked up by its outline

**Implemented** in 1.6.0, shipping issues
[129](https://github.com/sql-bi/SQLBI-Whiteboard/issues/129) to
[133](https://github.com/sql-bi/SQLBI-Whiteboard/issues/133). The plan and the twelve
calls behind it are [design-objects.md](design-objects.md).

Shapes, text labels, and connectors are records in `BoardObjects.cs` beside pictures, text
containers, LiveViews, and frames, drawn by the same surface in the same z-order. They are
not a second canvas. They save in the board, undo, export, and appear in the Explorer
thumbnail and the VS Code preview because the one surface already does all of
that. The alternative — a design layer of its own — would have needed every one of those
paths written twice.

- **A board is written as version 7 only when it holds a design object.** A board holding a
  shape, a label, or a connector is written as 7; a board with only frames is still 6, and
  one with neither is still 5. That is decision 27's rule applied again, so a board keeps
  opening in every release that can read it, and a board without a shape opens in the
  same releases as before.

- **Shapes and labels are containers; connectors are not.** Ink that touches only a shape
  links to it and travels with it, which is what makes a shape worth drawing round a
  sketch. A connector carries its own two ends and would link nothing sensibly, so it stays
  outside that rule, as a frame does.

- **A shape is selected by its outline, never by its interior.** The same eight-pixel band a
  frame's edge uses. A shape drawn around something is drawn around it on purpose, so a tap
  in the middle has to reach what is inside; picking the shape up by its fill would bury
  every stroke and picture under it. A label has no such inside and is selected anywhere on
  its rotated rectangle.

- **A connector binds to eight points and follows.** The four corners and four side
  midpoints of a shape or a container, taken when an end is released within 16 screen
  pixels; Ctrl at release binds to the nearest point anywhere on the border instead. What is
  stored is the fraction along the bounds, not the point, so an endpoint follows a move, a
  resize, and a load without the file saying the same thing twice. Deleting a shape frees
  its arrows rather than taking them with it.

- **The Insert tab is in the strip, and the toolbar button is a preference.** The compact
  floating toolbar sits under a presenter picture-in-picture during a recording, and keeping
  its width is worth more than the shortcut. So the tab strip holds the shapes, the
  connectors, and Text, and **Insert and Lasso on the toolbar** — off — is how somebody who
  wants them under their hand asks for a wider toolbar.

- **An area takes what it partly covers, and can extend.** Partly inside is the default
  because a sweep over a diagram is how a selection is usually made; fully inside is for
  picking one thing out of several. **Extend to touching** then grows the set by one round
  or by rounds until nothing is added, which is how a whole connected diagram comes from one
  stroke in it.

- **The grid is an application preference, never a board's and never an export's.** It says
  how one person likes to work — a board opened somewhere else should not arrive carrying
  their graph paper — and it exists to make the zoom level visible, which is a thing about
  the screen. So `BoardRasterizer` and `BoardPreviewRenderer` turn it off as they already
  turn frames off.

Left out of the first cut on purpose: text inside a shape, rotating a shape, elbow
connectors, arrowheads at both ends, per-board grid settings, and grouping as a saved
object. Decision 32 says which of those first use brought back.

---

## 32. A mode belongs with its tool, and a shape carries its own text

**Implemented** in 1.6.0, after the maintainer used the first Dev build of it. The full
list, the four palette designs, and the order the work ran in are in
[design-objects.md](design-objects.md), under "Toward a solid 1.6.0".

Most of the changes below move features of the first cut to other places. All of them
came from drawing one diagram with it, not from a review of the code.

- **One shape, then Select.** **After inserting an object** now defaults to Return to
  Select, and the new object arrives selected. Decision 3 had the tool stay, which suits a
  row of shapes. In the common case, the tap that would have moved the shape just drawn
  started another one instead. The preference is still there for whoever wants
  the old behaviour. The Insert row also stopped being a sticky tab, so it closes on the
  first press on the canvas rather than lying over the board while the shape is drawn.

- **Lasso moved from the Edit row onto the Select button.** Nobody looked for it in a command
  strip two rows away, so a mode that changes what a tool does is now on that tool. Holding Select for 600 ms — the Windows long press — switches Rectangle and Lasso, and
  a tap on Select while Select is already in hand does the same, so a mouse reaches it
  without waiting. The button's glyph says which is armed. The alternative was a chevron on
  Select, which is what **Insert and Lasso on the toolbar** already offers; it stays a
  preference, because decision 2 says the compact toolbar does not grow by default.

- **A connector binds anywhere on a shape.** Aiming at one of eight points within 16 pixels
  was too hard. The whole of a target now takes the end, the eight dots appear while the
  pointer is over it, the one that would be taken grows, and the preview snaps to it. Ctrl
  at release still means the nearest point anywhere on the border, as decision 31 says.
  **Anchors → Auto** then lets a bound end move to the side facing the other end, so an
  arrow between two shapes stays sensible as they are rearranged; Fixed stays the default
  for an end dropped on a point on purpose. The deck carries the same tie, so a shape
  dragged in PowerPoint keeps its straight arrows.

- **A shape carries its own text, and turns freely.** Both were out of scope, and a diagram
  needs both first: a box with a word in it, pointing the way the arrow
  goes. A shape takes the seven text properties a label already had, laid out inside its
  outline, opened with F2, the bar's Text button, or simply by typing, as PowerPoint does.
  An angle is any angle, set by a handle above the object with Shift snapping to 15°; the
  45° buttons stay and now land on the nearest multiple, so an object set by hand comes back
  onto the grid rather than drifting off it for good.

- **The selection carries its own commands.** A **…** at the end of the property bar holds
  Delete, Copy, Duplicate (Ctrl+D), and four depth commands rather than two, and the bar now
  appears for a picture, a LiveView, or a frame, which had no property row and so had no bar
  at all. Bring forward and Send backward join the View row as W and K. Lock, Alt text,
  and Comment were left out, because they serve a slide in PowerPoint and not a board.

- **The Insert row pins into a palette.** Design A of four. B was the toolbar Insert button,
  already built and already ruled out as a default by decision 2; C was a bubble beside the
  last object, which follows the work and covers it; D was a tear-off gesture nobody would
  find. A pin at the end of the row is visible, reversible, and leaves the toolbar alone,
  and the palette reuses the same button list, so a shape button exists in one place.
  Where it is left is kept as a fraction of the window, so another size puts it back.

- **Ctrl+A selects everything an area could take; Ctrl+Shift+A the ink alone.** The second
  shortcut was nearly Ctrl+S, which reads as "strokes" and is Save in every Windows
  application, and in this one since 1.0. A Save that instead selected ink would cost
  somebody their work, so the ink went behind Shift and Ctrl+S was left where it is.

Still left out: text inside a connector, elbow connectors, arrowheads at both ends, Lock,
handles on a label or a picture, per-board grid settings, and grouping as a saved object.

---

## 33. A mode says which tools exist, and never what a board contains

**Implemented** in 1.6.0. The plan is [design-objects.md](design-objects.md), under
"Modes: Teaching, Design, Custom".

1.6.0 added an Insert tab, a palette, handles on a selected shape, a bar over every
selection with a menu on it, and two more depth commands. Someone who opens this
application to annotate a demo needs none of them, and every one of them is in the way. A
**mode** — **Teaching**, **Design**, or **Custom**, in **Preferences → Mode** or on the
**Design** toggle on the View row — says which of them exist.

- **A mode is about the tools, never about the file.** A board made in Design opens in
  Teaching with every shape, label, and connector still drawn, still a container ink links
  to, still moved, resized from the corner, deleted with its ink, and exported natively; a
  connector still follows its shapes. Only the tools to make or restyle them are absent.
  The alternative — a Teaching board that simply cannot hold these objects — would have made
  the mode a property of the file, and a file somebody could not open where they made it.

- **Teaching keeps the lasso.** It was in the first cut of this and came out after the
  first morning with it: selecting a few of the strokes you have just drawn, to move them
  or wipe them, is annotating rather than designing, and the rectangle alone cannot take
  three strokes out of a diagram without taking the rest. It costs Teaching nothing on the
  screen — the lasso is the Select button behaving differently after a hold — so Teaching
  is the **Extended selection** group and nothing else, which also leaves the two
  Selection preferences in its Preferences, where they now have something to configure.

- **Four groups, not twenty switches and not one.** **Design tools**, **Property bar**,
  **Extended selection**, **Depth and duplicate**. Each is a group because the things in it
  arrive and leave together: the rotation handle, the quarter turns, and a shape's own text
  are all the same answer to "can this shape be designed with". A switch per control would
  be a second Preferences dialog nobody could hold in their head, and one switch would have
  made trying a change impossible.

- **Design is the default, and the default does not follow the channel.** An upgrade
  that quietly took the 1.6.0 tools away from somebody who had them would be reported as a
  bug. Tying the default to the Dev or the released channel was considered and dropped,
  because the channel identifies the build and says nothing about who uses it, the same
  copy is promoted from one to the other without being rebuilt (decision 9), and a mode
  that changed when a build was promoted would surprise the person using it.

- **Custom exists to try a Teaching change one group at a time.** Teaching is a claim about
  what a person who only annotates needs, and the way to test a claim like that is to put
  one group back and use the board for a week. Choosing Teaching or Design leaves the four
  switches exactly as they were, so Custom comes back to the arrangement it was left in, and
  the View row's toggle returns to Custom rather than to Teaching when that is where it came
  from — the same memory the Grid button keeps of its last style.

- **A control a mode leaves out is gone, not greyed.** The Insert tab leaves the strip, the
  two depth buttons leave the View row, the toolbar's Insert button and the Select chevron
  are collapsed whatever their own preference says, and the Preferences rows that only
  configure an absent group leave the list — so Teaching's Preferences are 1.5.2's plus Mode
  and Board. A key or a command belonging to a group that is off does nothing at all: no
  dialog, no beep. Greying them would have kept every one of them on the screen, which is
  the thing Teaching exists to stop.

- **The settings behind a group are left untouched.** Turning the design tools off hides the
  Insert palette without clearing the setting that says it is shown, and takes the lasso
  away without forgetting that Lasso was armed. The area gesture meanwhile behaves as though
  the two Selection preferences were at their defaults, so an area with that group off takes
  what it covers and never grows, whatever a file written in Design still holds. The four
  rows in Preferences follow the same rule from the other side: under Teaching or Design
  they show the feature set the mode resolves to, greyed, and read nothing from the
  switches, so what a greyed row says is what the board will do.

---

## Open questions

- arm64 is not built; add it if Surface devices matter for a pen application.
- The brand mark is placeholder-grade (decision 12).
