# Microsoft Store listing

**0.9.2 was submitted by hand on 20 August 2026.** This page is what was
entered, so the next submission repeats it rather than reinventing it, and so
the eventual pipeline automation has something exact to reproduce.

The pipeline and `scripts/build-installer.ps1` produce
`SQLBI.Whiteboard.<version>.x64.msix`. Identity version is `VersionPrefix.0`
(so 0.9.2 becomes `0.9.2.0`). The Store re-signs the package.

## Package identity

Assigned by Partner Center when the name was reserved. The first three are the
only ones written anywhere: they are the defaults in `scripts/build-msix.ps1`
and the `PublisherDisplayName` in `AppxManifest.xml`, so an ordinary build is
submittable without extra arguments.

| Field | Value | Set where |
|---|---|---|
| `Package/Identity/Name` | `17351SQLBICorp.SQLBIWhiteboard` | `build-msix.ps1` default |
| `Package/Identity/Publisher` | `CN=922444FE-B5BD-491C-A501-DD2EC37191C8` | `build-msix.ps1` default |
| `Package/Properties/PublisherDisplayName` | `SQLBI Corp` | `AppxManifest.xml` |
| Package family name (PFN) | `17351SQLBICorp.SQLBIWhiteboard_x5fb4jp2zkb6m` | derived, never written |
| Package SID | `S-1-15-2-1890532371-2375246573-3128893498-3639117771-3030907503-1416850920-4129476770` | derived, never written |
| Store ID | `9NN5N0L2TMTF` | Partner Center only |

None of this is secret. Identity, PFN, and SID ship inside every copy of the
package and are readable with `Get-AppxPackage` on any machine that installs
it; the Store ID is the public listing URL. What must never enter the repo is
the Partner Center sign-in, the Azure AD client secret if submission is ever
automated, and the code-signing certificate.

The last three rows are **derived and self-checking**, which is why they are
worth recording even though nothing reads them:

- the PFN suffix is a base32 hash of the Publisher string, so it can only match
  if Publisher is character-exact;
- the SID is `SHA-256` over the lowercased PFN as UTF-16LE, taken as seven
  little-endian `uint32` sub-authorities under `S-1-15-2`.

Both were recomputed from the values above and match, so the block is
internally consistent. Re-derive them after any identity change rather than
trusting a copy-paste.

A Store rejection reports Name, Publisher, and PFN as three separate errors.
It is one problem: fix the first two and the rest follow.

Three fields that look like the same name and are not: `PublisherDisplayName`
is `SQLBI Corp` with no period, the copyright string elsewhere in the product
is `SQLBI Corp.` with one, and the manifest `Publisher` is the Store identity
`CN=<GUID>` rather than anything readable. Changing one to match another is a
rejection.

A fourth identifier belongs to the account rather than the package, and is the
one that gets confused with `Publisher`: the **Seller ID**, which is what the
Store Developer CLI wants for `--sellerId`. It is an `Int32` - a plain number,
never a GUID and never a `CN=` string. Passing the publisher identity there
fails inside the CLI with an unhandled `System.FormatException` from a numeric
parse that names neither the argument nor the expected shape.

Partner Center does not show it on **Account settings > Identifiers**; that
page carries the Windows publisher ID, which is the `CN=<GUID>` above. The
dependable way to read it is to let the CLI resolve it - run `msstore
reconfigure` without `--sellerId`, which retrieves it from the enrollment
accounts API, then `msstore info` to print what it stored.

The `17351` in the package identity name is **not** the Seller ID. Both are
numbers Microsoft assigned to this account and it is tempting to read one as
the other, but they do not match and neither can be derived from the other.

Give the pipeline the number explicitly even though the argument is optional.
When auto-retrieval fails the CLI falls through to an interactive prompt, which
on a build agent hangs the job instead of failing it.

Partner Center validates the identity in the package you upload and rejects a
mismatch. It does not rewrite your manifest — "associating" an app in Visual
Studio rewrites a *local* manifest, which is the source of the opposite belief.

Overriding `-PackageName` / `-Publisher` is only for a locally installable
build. A package can be signed only by a certificate whose subject equals
Publisher exactly, so sideload testing needs a self-signed certificate with a
matching subject; such a package is not submittable.

## What the repo already produces

- Unsigned MSIX with `.wboard` / `.wimport` open verbs and the thumbnail COM
  server declared in the package manifest.
- Store tile images in `installer/msix/Assets/` (regenerate with
  `scripts/build-assets.ps1`).

## What was done by hand in Partner Center

Done once for 0.9.2. Only the version-specific parts — packages, screenshots
when the UI changes, and **What's new** from the second submission onwards —
repeat.

1. **Reserve the name** “SQLBI Whiteboard” (Windows desktop). Done — the
   identity above is the result. If it is ever re-reserved, update the defaults
   in `scripts/build-msix.ps1`.
2. **Age rating** questionnaire.
3. **Privacy policy URL** (required). Use
   `https://whiteboard.sqlbi.com/privacy.html`.
4. **Support contact** (email or https://www.sqlbi.com) and the product
   website `https://whiteboard.sqlbi.com`. Both live on the Properties page,
   not the listing page.
5. **Category** — Productivity is the closest fit.
6. **`runFullTrust` declaration** — required for this Win32 package. Partner
   Center will ask why; answer that it is a full-trust desktop whiteboard that
   uses Windows Graphics Capture and an Explorer thumbnail handler.
7. **Screenshots** — at least one, 1366×768 or 1920×1080, of the real UI.
   Nothing in this repository can stand in: the site artwork is the wrong
   shape, and the launch card carries both the product title and a competitor's
   name. What to shoot is under the listing fields below, and reshooting is
   needed whenever the UI moves on. The teaser in TODO.md is independent of
   these stills.
8. **The listing text** — every field is written out below. Paste it, then edit
   freely in Partner Center.

Certification must never gate the MSI / GitHub release, which is why the
submission is a separate stage from the one that publishes the download
(decision 13). That stage now exists: the pipeline submits the MSIX after a
release is promoted, so nothing above repeats per version except a screenshot
when the UI moves on.

What it submits is packages only. Listing text, screenshots, and **What's new**
carry over from the last published submission untouched, so this page stays the
record of them and a change here means a change made by hand in Partner Center.

## Once the listing is live

The Store ID gives the public addresses. Both 404 until certification
completes and the listing is published, so nothing links to them yet:

- `https://apps.microsoft.com/detail/9NN5N0L2TMTF`
- `ms-windows-store://pdp/?ProductId=9NN5N0L2TMTF` (opens the Store app)

Then, and not before, the Store becomes a second route on
`whiteboard.sqlbi.com`. It stays second: Store availability is uneven on
managed corporate machines, which is why the MSI is the primary channel
(decision 10).

## The listing page, field by field (0.9.2)

Partner Center's **Store listing** page in the order the form presents it. Only
the description and one desktop screenshot are required to save it; everything
else below is optional, and what we skip is marked as skipped rather than left
for a future reader to wonder about.

The listing never mentions Microsoft Whiteboard or its retirement. That framing
is ours to make on `whiteboard.sqlbi.com`; inside the Store a comparative claim
about another vendor's product buys nothing and risks certification.

### Product name

`SQLBI Whiteboard`, reserved and pre-selected. Nothing to do.

### Description

```
SQLBI Whiteboard is a pen-first canvas for live explanation. Draw on DAX and SQL code, pin a live application window onto the board, and keep your ink attached to whatever you are talking about.

It is a native Windows application, not a service. There is no account to create, nothing to sign in to, and your boards are ordinary files on your own disk.

WHAT IT DOES

Pen-first ink — pressure-aware strokes with basic palm rejection, a rear eraser that removes whole strokes, and a barrel-button laser pointer.

Unbounded canvas — pan with one finger, pinch to zoom, or use the mouse wheel. The board never runs out of room in the middle of an explanation.

Live application capture — put any window or display on the board with LiveView and draw over it while it keeps running. Freeze the frame when you want it to hold still.

Code containers — paste DAX or T-SQL and get syntax highlighting, a language-aware title, and F6 to format the code in place. Plain text works the same way.

Images and text — import PNG, JPEG, BMP, and GIF, paste from the clipboard, or drag files in from File Explorer. Ink that touches a container travels with it when you move or resize it.

Portable board files — boards save as .wboard files with an embedded preview, so File Explorer and Visual Studio Code both show what is inside.

Markdown recipes — a .wimport file builds a whole board of images and code from headings, so a board can be generated by your own tools.

Full-screen presenting — F11 fills the display and hides everything but the canvas.

Free and open source under the MIT License. Requires Windows 10 version 2004 or later, 64-bit.
```

The field takes plain text and keeps line breaks. The limit is 10,000
characters, so there is room to grow this without restructuring it.

### What's new in this version

Empty. The form asks for it to be blank on a first submission, and a changelog
entry here would only duplicate `site/changelog.html`.

### Product features

One line per box, **Add more** for each. Thirteen of the twenty allowed:

```
Pressure-aware pen ink with basic palm rejection
Unbounded canvas with touch pan and pinch zoom
Whole-stroke erasing with the pen's rear eraser
Laser pointer for presenting
Live capture of any application window or display
DAX and T-SQL containers with syntax highlighting
F6 formats DAX and SQL in place
Image import, clipboard paste, and Explorer drag-and-drop
Ink stays attached to the container it touches
Portable .wboard files with Explorer and VS Code previews
Markdown .wimport recipes that build a board
Full-screen presenting on any monitor
Works offline: no account, no tenant, no cloud
```

### Screenshots (Desktop)

The four that were submitted are kept in `installer/msix/listing/`, at
1918×1078 — a window capture without its borders, comfortably above the
1366×768 minimum:

| File | Shows |
|---|---|
| `Screenshot-01.png` | Ink and highlighter over a matrix, with the DAX measure beside it in a text container |
| `Screenshot-02.png` | A LiveView of the Fabric portal, with the View strip open |
| `Screenshot-03.png` | A T-SQL container with highlighting, annotated |
| `Screenshot-04.png` | Preferences, on Input |

Reshoot when the UI moves on, and keep the replacements here: a listing whose
stills predate the toolbar people see is worse than no stills. Everything on
screen is Contoso sample data or SQLBI's own demo workspace, which is the rule
rather than luck — a Store screenshot is public, so it can never carry a
customer name.

### Store logos, hero art, and trailers

All optional. Upload the two in `installer/msix/listing/`; skip the rest, and
let the Store fall back to `Assets/StoreLogo.png` and
`Assets/Square150x150Logo.png` from the package for anything else.

| Slot | Decision |
|---|---|
| 9:16 poster art, 720×1080 | Upload `listing/PosterArt-720x1080.png`. It is the main listing logo on Windows 10 and 11 |
| 1:1 box art, 1080×1080 | Upload `listing/BoxArt-1080x1080.png`. The Store substitutes it into layouts the poster does not fit |
| Store display images, 300×300 / 150×150 / 71×71 | Skip; the package logos cover them |
| 16:9 super hero art, 1920×1080 | Skip. Only needed to head the listing with a trailer, and it must not contain the product title |
| Trailers, Xbox art | Skip. Windows desktop only |

Both are generated by `tools/AssetGenerator` from the same glyph as the tiles
and regenerate with `scripts/build-assets.ps1`, because hand-made brand artwork
is lost on the next run (CONTRIBUTING.md).

Everything the listing uses — this artwork and the screenshots — lives in
`installer/msix/listing/` and never in `installer/msix/Assets/`, which
`build-msix.ps1` copies wholesale into the package. A file put in `Assets/`
ships inside every MSIX, which for a megabyte of screenshots is pure freight.

### Supplemental fields

Short title and voice title stay empty — both are Xbox surfaces, and the real
name is already short. Short description:

```
A native Windows whiteboard for pen, touch, and live application capture. Draw over DAX and SQL, pin a running window onto an unbounded canvas, and save boards as portable files on your own disk. No account, no cloud.
```

217 characters, inside the 270 the form recommends.

### Additional information

| Field | Value |
|---|---|
| Keywords | `whiteboard`, `digital ink`, `pen tablet`, `DAX`, `T-SQL`, `screen annotation`, `teaching` |
| Copyright and trademark info | `© 2026 SQLBI Corp. Released under the MIT License.` |
| Additional license terms | Empty. MIT is stated in the description; amending the standard terms only adds review |
| Developed by | `SQLBI Corp` |

Seven keywords, ten words, both inside the limits. Press Enter after each. Do
not take the AI-recommended keywords without reading them: they tend to suggest
competing product names, which are rejected.
