# Microsoft Store listing — prepare, do not submit

The pipeline and `scripts/build-installer.ps1` produce
`SQLBI.Whiteboard.<version>.x64.msix`. Identity version is `VersionPrefix.0`
(so 0.9.1 becomes `0.9.1.0`). The Store re-signs the package. Do not upload
until the listing assets below exist.

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

## What only you can do in Partner Center

1. **Reserve the name** “SQLBI Whiteboard” (Windows desktop). Done — the
   identity above is the result. If it is ever re-reserved, update the defaults
   in `scripts/build-msix.ps1`.
2. **Age rating** questionnaire.
3. **Privacy policy URL** (required). Use
   `https://whiteboard.sqlbi.com/privacy.html`.
4. **Support contact** (email or https://www.sqlbi.com).
5. **Category** — Productivity is the closest fit.
6. **`runFullTrust` declaration** — required for this Win32 package. Partner
   Center will ask why; answer that it is a full-trust desktop whiteboard that
   uses Windows Graphics Capture and an Explorer thumbnail handler.
7. **Screenshots** — at least one, 1366×768 or 1920×1080, of the real UI
   (ink, a text container, a LiveView). The teaser in TODO.md can wait; the
   listing cannot ship without stills.
8. **Description** — draft below. Edit freely in Partner Center.

Do **not** start certification. Upload the MSIX only when you are ready to
submit. Certification must never gate the MSI / GitHub release.

## Once the listing is live

The Store ID gives the public addresses. Both 404 until the listing is
published, so nothing links to them yet:

- `https://apps.microsoft.com/detail/9NN5N0L2TMTF`
- `ms-windows-store://pdp/?ProductId=9NN5N0L2TMTF` (opens the Store app)

Then, and not before, the Store becomes a second route on
`whiteboard.sqlbi.com`. It stays second: Store availability is uneven on
managed corporate machines, which is why the MSI is the primary channel
(decision 10).

## Draft listing copy (0.9.1)

**Title:** SQLBI Whiteboard

**Short description:** A native Windows 11 whiteboard for pen, touch, and live application capture.

**Description:**

SQLBI Whiteboard is a pen-first canvas for live explanation. Draw on DAX and
SQL, pin a live window onto the board, and keep ink attached to the slide you
are talking about.

- Pressure-aware ink with palm rejection, tuned for Wacom and Surface pens
- An unbounded canvas with touch pan and pinch zoom
- Live capture of any application window or display
- Text containers with DAX and SQL highlighting and local formatting
- Portable `.wboard` files, with Explorer and VS Code previews

Requires Windows 10 version 2004 or later, 64-bit.

**Copyright:** © SQLBI Corp.

**Website:** https://whiteboard.sqlbi.com
