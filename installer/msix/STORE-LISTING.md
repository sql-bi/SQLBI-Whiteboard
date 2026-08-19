# Microsoft Store listing — prepare, do not submit

The pipeline and `scripts/build-installer.ps1` produce
`SQLBI.Whiteboard.<version>.x64.msix`. Identity version is `VersionPrefix.0`
(so 0.9.1 becomes `0.9.1.0`). The Store re-signs the package. Do not upload
until the listing assets below exist.

## What the repo already produces

- Unsigned MSIX with `.wboard` / `.wimport` open verbs and the thumbnail COM
  server declared in the package manifest.
- Store tile images in `installer/msix/Assets/` (regenerate with
  `scripts/build-assets.ps1`).

## What only you can do in Partner Center

1. **Reserve the name** “SQLBI Whiteboard” (Windows desktop).
2. **Read the Store identity** (Package/Identity Name and Publisher `CN=…`) and,
   if they differ from `SQLBI.Whiteboard` / `CN=SQLBI Corp`, pack again:

   ```powershell
   ./scripts/build-msix.ps1 -Publisher "CN=…from Partner Center…" -PackageName "…from Partner Center…"
   ```

3. **Associate** the app so Partner Center accepts that identity.
4. **Age rating** questionnaire.
5. **Privacy policy URL** (required). Use
   `https://whiteboard.sqlbi.com/privacy.html`.
6. **Support contact** (email or https://www.sqlbi.com).
7. **Category** — Productivity is the closest fit.
8. **`runFullTrust` declaration** — required for this Win32 package. Partner
   Center will ask why; answer that it is a full-trust desktop whiteboard that
   uses Windows Graphics Capture and an Explorer thumbnail handler.
9. **Screenshots** — at least one, 1366×768 or 1920×1080, of the real UI
   (ink, a text container, a LiveView). The teaser in TODO.md can wait; the
   listing cannot ship without stills.
10. **Description** — draft below. Edit freely in Partner Center.

Do **not** start certification. Upload the MSIX only when you are ready to
submit. Certification must never gate the MSI / GitHub release.

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
