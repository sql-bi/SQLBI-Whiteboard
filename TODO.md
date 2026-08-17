# TODO

Outstanding work, roughly in the order it should be done. Items are written to be picked up
cold: each says what to change and why it matters.

Background reading, in this order:

- [CONTRIBUTING.md](CONTRIBUTING.md) — branch and pull request workflow. `main` is protected.
- [docs/release-management.md](docs/release-management.md) — how the project is built and shipped.
- [docs/decisions.md](docs/decisions.md) — why it is built that way. Each entry says whether it
  is implemented or only agreed.

The delivery chain is working end to end: a merge to `main` builds, signs, and publishes a
pre-release to GitHub Releases, and one approval promotes that same build to a release.
<https://whiteboard.sqlbi.com> resolves its download links at load time and needs no edit per
release.

## Before the first stable release

### Upgrade to .NET 10

Ship 1.0 on the current LTS rather than upgrading immediately after. Touches seven project
files:

| Project | Now |
| --- | --- |
| `src/SQLBI.Whiteboard` | `net8.0-windows10.0.26100.0` |
| `src/SQLBI.Whiteboard.Core`, `.Dax`, `.SqlServer` | `net8.0` |
| `tests/SQLBI.Whiteboard.Core.SmokeTests` | `net8.0` |
| `tools/AssetGenerator` | `net8.0-windows` |
| `prototypes/…CalligraphyPrototype` | `net8.0-windows` |

Also update the SDK version in `.azure/pipelines/build-whiteboard.yaml` (`UseDotNet@2`) and
`.github/workflows/pull-request.yml` (`actions/setup-dotnet`).

Watch for: `TreatWarningsAsErrors` is on everywhere, so new analyzer warnings fail the build;
the self-contained publish size changes, which shows up in the installers; and WPF behaviour
around ink and `RenderTargetBitmap` should be re-checked on real hardware rather than assumed.

### Decide the first version number

`VersionPrefix` in `Directory.Build.props` is the placeholder `0.1.0`. Changing that line is
what starts a release (decision 8).

### Verify the installers on a real machine

Nothing in this list has been exercised on an installed build. The checklist is at the end of
[docs/release-management.md](docs/release-management.md): signature verification, install,
upgrade and uninstall for both scopes, the `.wboard` association, released and pre-release
side by side, and a clean machine to confirm SmartScreen stays quiet.

### Promote the first release

Approve the Release stage on a run whose version you intend to ship. Releasing the same
version twice fails on the existing tag, which is the intended guard.

## Distribution

### Publish release manifests

`stable.json` and `dev.json` alongside the binaries: version, date, URLs, SHA-256. One source
that the download page, a future in-app update check, and winget automation can all read,
instead of each deriving the same facts differently.

### winget

`winget-releaser` opens a pull request against `microsoft/winget-pkgs` when a release is
published. Only possible because the repository is public and the assets are unauthenticated.

### Microsoft Store

The largest remaining piece, and independent of everything above.

1. **Build an MSIX.** None exists; only MSIs are produced. `Bravo.Installer.Msix` in the Bravo
   repository is the template. The application is already MSIX-clean: settings live in
   `%APPDATA%`, the only registry use is a read, and nothing writes to the install folder.
2. **Give the MSIX its own version derivation.** Store versions must end in `.0`, and the
   current four-part scheme never produces that — the last part is seconds since midnight
   divided by two.
3. **Do the first submission by hand.** Listing text, screenshots, age rating, and privacy
   details are one-time work no pipeline performs.
4. **Then automate it** with the Microsoft Store Developer CLI or the Partner Center
   submission API, as a stage that runs *in parallel* with the web release. Certification takes
   hours to days and must never gate a download that is otherwise ready.

Note the Store signs the package itself, so the SQLBI certificate is not involved. Store
availability is also uneven on managed corporate machines, which is why the MSI channel stays
the primary route rather than a fallback (decision 10).

## Deferred, deliberately

- **arm64.** Not built. Worth adding if Surface devices matter for a pen application.
- **Telemetry.** Bravo's installer custom action and opt-in checkboxes were not ported
  (decision 14). Port only if the data is actually wanted.
- **The brand mark.** The icon is the Fluent whiteboard glyph in SQLBI colours: in-family and
  deliberate, but not distinctive (decision 12). Replacing it touches no installer plumbing —
  change the SVG and rerun `scripts/build-assets.ps1`.
- **Landing page copy.** The tagline and feature list on `site/index.html` were drafted from
  the README and have not had an editorial pass. They are the first thing anyone reads about
  the product.
