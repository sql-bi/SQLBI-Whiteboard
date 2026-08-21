# Teaser shot script

The teaser on the landing page: one continuous board session, recorded long and cut
down. The shipped cut runs 42 seconds. The same master serves the home page, the Store
trailer, and announcements. It is served from the site itself, not embedded — decision 19
in [../decisions.md](../decisions.md).

Everything the recording needs is in this folder:

| File | Used in |
| --- | --- |
| `shot-script.md` | This script |
| `demo-board.wimport` | Beat 3 — the drop that fills the board |
| `paste-me.dax` | Beat 2 — copy its contents to the clipboard before recording |
| `assets/star-schema.png`, `assets/sales-trend.png` | Referenced by the recipe |
| `make-assets.ps1` | Regenerates the two PNGs if they change |
| `overlays/ctrl-v.png`, `overlays/f6.png` | Keystroke callouts for the edit (beats 2's paste and F6) |
| `make-overlays.ps1` | Regenerates the overlays if they change |

## Recording setup

- **OBS Studio**, display capture, canvas and output **2560 × 1440, 60 fps**. 60 fps is
  non-negotiable for ink; 1440p keeps UI text crisp after Vimeo re-encodes.
- Windows display scaling **150 %** on the recording monitor, so the toolbar and tab strip
  read inside the 860-px-wide embed.
- **Released-channel build** — no "(Dev)" in the title bar. Current UI per TODO.md.
- Focus Assist / Do not disturb on. Clean desktop and taskbar (auto-hide, or record the
  window region only). Default white board, default toolbar position.
- Pen: the Cintiq Pro profile already validated in README § "Wacom Cintiq Pro validation".

## Staging, before every take

1. Open **Help → Preferences → Snippet format order** and move **DAX above Plain text**,
   so the paste in beat 2 lands already highlighted. (Restore afterwards if you prefer
   plain-first day to day.)
2. Copy the contents of `paste-me.dax` to the clipboard **as text** (open it in a text
   editor and Ctrl+A, Ctrl+C — do not copy the file in Explorer, paste prefers a file).
3. Open a File Explorer window showing this folder, positioned on a second monitor or
   off-canvas, ready to drag `demo-board.wimport`.
4. Start the LiveView target for beat 5: **Power BI Desktop** with a report page visible
   (any demo model — it only needs to look alive). Second monitor, or behind the app.
5. New empty board, Pen tool, default zoom.

## The beats

Timings are the *edited* target. Record each beat at natural speed; the cuts and
speed-ups happen in the edit. Total real time per take: 3–5 minutes.

| # | Edited | Action, exactly | Must be visible |
| --- | --- | --- | --- |
| 1 | 0–5 s | On the empty board, handwrite **"Contoso — sales review"** and underline it. Vary pressure; this beat stays real-time in the edit, it is the "feel" shot. | Wet ink following the pen with no lag; pressure width variation; the hover dot before contact. |
| 2 | 5–11 s | Click empty canvas below the title, **Ctrl+V**. The measure appears as a DAX container, highlighted, crammed on long lines. Press **F6**. It reformats in place. | Syntax colors on paste; the visible before/after of F6. |
| 3 | 11–16 s | Drag **`demo-board.wimport`** from Explorer onto the right half of the board. Five containers materialize: two images, DAX, SQL, notes. | The one-gesture board build. Pause a beat so the layout registers. |
| 4 | 16–21 s | With the pen, circle the December column on the **Sales by month** chart and draw a short arrow — strokes must stay entirely on that one image. Then left-drag the chart container to a clearer spot. | The ink travels with the container. This is the "aha" beat; move it far enough to be unmistakable. |
| 5 | 21–26 s | **View → LiveView**, pick the Power BI Desktop window in the system picker, place it. Ink one arrow onto the live feed, then **View → Freeze**. | A real application, live inside the board, annotated. The picker dialog gets jump-cut to ~1 s in the edit. |
| 6 | 26–30 s | Click empty canvas, then **double-click empty canvas**: the camera fits the whole board. Hold two seconds. | The zoom-to-fit reveal: title ink, formatted DAX, the dropped board, annotated chart, frozen LiveView — one composition. |

Notes that keep beats honest:

- Beat 4's strokes link only if they touch **exactly one** container — keep the circle and
  arrow fully on the chart image, not grazing a neighbor.
- The tab strip appears on camera in beat 5 (View tab) — that covers the tab-strip
  requirement without its own beat.
- Optional touch: one short two-finger pan somewhere between beats 3 and 4 shows touch
  navigation. Finger drawing gets its Store screenshot instead; 30 s cannot carry it.
- Optional end card: before the take, handwrite **whiteboard.sqlbi.com** small near the
  board's edge; beat 6's fit reveals it without an overlay.

## Takes

Rehearse the full sequence twice without recording — the handwriting beats are the only
part that cannot be fixed in post. Then record **three to five full takes**, letting
mistakes run (restart the beat on the same board rather than the take; the edit keeps the
best pass of each beat). Name recordings `take-01.mkv`, `take-02.mkv`, …

## Edit

- Jump cuts only. Every cut lands on the same white board with more content — no
  transitions needed.
- Beat 1 stays 1× always. Dialogs (capture picker) and any dead pen travel get cut or
  sped to 4×. Keep at least one second of stillness after beats 3 and 6.
- Keystroke callouts: overlay `overlays/ctrl-v.png` on the paste and `overlays/f6.png` on
  the format in beat 2 — transparent PNGs, drawn large; scale to taste (a keycap about
  1/10 of the frame height reads well), bottom-right corner, quick fade in and out.
- No voiceover. Music optional, added at upload; the clip must work silent.
- Export master: **2560 × 1440, 60 fps, H.264 high bitrate** (or ProRes/DNxHR if the
  editor offers it), audio track only if music was added.

## Publish

The site serves the clip itself (decision 19). From the 4K master, encode the two
renditions and the poster straight into `site/`; they deploy with the next site publish.

```bash
ffmpeg -y -i master.mp4 -vf "scale=2560:-2:flags=lanczos" -c:v libsvtav1 -crf 38 -preset 6 -movflags +faststart -an site/teaser-av1.mp4
```

```bash
ffmpeg -y -i master.mp4 -vf "scale=1920:-2:flags=lanczos" -c:v libx264 -crf 22 -preset slow -pix_fmt yuv420p -movflags +faststart -an site/teaser-h264.mp4
```

```bash
ffmpeg -y -ss 41.0 -i master.mp4 -frames:v 1 -vf "scale=1920:-2:flags=lanczos" -q:v 3 site/teaser-poster.jpg
```

The `<video>` element in `site/index.html` lists the AV1 source first and H.264 as the
fallback; if a re-encode changes resolution, frame rate, or codec profile, update its
`codecs=` strings (read them with `ffprobe -show_entries stream=profile,level`). Keep
the master outside the repository, and reuse it for the Store trailer per
`installer/msix/STORE-LISTING.md`.
