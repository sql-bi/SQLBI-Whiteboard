# Teaser shot script

The teaser on the landing page is film 2 of [tools/films](../../tools/films/README.md),
"One board". The shipped cut runs 50 seconds: one continuous camera move across a single
board on which seven screen captures sit as windows, ending on the whole board and the end
card. It names no date and no deadline, so it does not go out of date when the campaign
ends. The same master serves the home page, the Store trailer, the social renditions,
and three 12-second portrait shorts. The site serves it itself, not through an embed —
decision 19 in [../decisions.md](../decisions.md).

It replaced a 42-second cut of one continuous board session, recorded and edited by hand.
The staging files for that recording are still in this folder:

| File | Used in |
| --- | --- |
| `shot-script.md` | This script |
| `demo-board.wimport` | The drop that filled the board in the 42-second cut |
| `paste-me.dax` | The measure pasted in the 42-second cut |
| `assets/star-schema.png`, `assets/sales-trend.png` | Referenced by the recipe |
| `make-assets.ps1` | Regenerates the two PNGs if they change |
| `overlays/ctrl-v.png`, `overlays/f6.png` | Keystroke callouts for a hand edit |
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

The captures of the shipped cut were recorded at 30 fps, `ink-diagram.mp4` at
3840 × 2160 and the other six at 1920 × 1080, so the master is 1920 × 1080 at 30 fps.
The film never shows a capture larger than it was recorded. A re-recording at
2560 × 1440 can be placed on the board at the same size in board units without
changing anything else.

## The shot list

Each capture is a window on the board with its caption written below it. The camera is
the only thing that moves: it rests on a window with a slow push-in, moves to the next
with one ease-in-out curve and a slight pull back mid-move, and never cuts. The captures
play faster than recorded where the beat is shorter than the capture; the times and rates
are in `tools/films/src/film2/cuts.ts`.

| Time | Camera | Capture | Caption on the board |
| --- | --- | --- | --- |
| 0–6 s | Rests on D1 | `ink-diagram.mp4`: the pen draws three boxes and two connectors | SQLBI Whiteboard |
| 6–13 s | Pans right to D2 | `liveview-powerbi.mp4`: View, LiveView, the Power BI window lands, Freeze, highlighter marks | Capture a running application onto the board and draw on it. |
| 13–20 s | Pans down to D3 | `dax-f6.mp4`: a one-line measure is pasted, F6 formats it | Paste DAX or SQL. F6 formats it. |
| 20–27 s | Pans left to D4, past D7 | `shapes-connectors.mp4`: a rectangle, a diamond, a connector; the rectangle moves and the arrow follows | Shapes, sticky notes, and arrows that follow. |
| 27–34 s | Pans up to D5 | `frames-export.mp4`: Export to PowerPoint and the deck open in PowerPoint | Frames become slides. Export the deck. |
| 34–41 s | Pans down and left to D6 | `open-pptx-zip.mp4`: a .pptx dropped on the board becomes its slides | Open a PowerPoint deck or a Microsoft Whiteboard export. |
| 41–47 s | Pulls back until the whole board is in frame | `full-board.mp4` at the centre of the board: a double-click fits the board in its window as the camera pulls back | One board. A file on your PC. |
| 47–50 s | The board gives way to the end card | | App icon, SQLBI Whiteboard, Free and open source, whiteboard.sqlbi.com |

The site rendition loops, so its end card fades to paper over the last 500 ms and the
restart cuts to the white board of the first frame. No rendition has a voice. The site
files are silent; the social files and the Store trailer carry the music bed.

The shorts take the LiveView, DAX, and shapes stations: 10 seconds on the station with
the capture at its own speed, and a 2-second end card.

## Publish

`npm run render:film2-site` in `tools/films` renders the master
(`out/master/film2-16x9-master.mp4`) and runs these three commands, which write the two
renditions and the poster straight into `site/`; they deploy with the next site publish.

```bash
ffmpeg -y -i master.mp4 -vf "scale=1920:-2:flags=lanczos,fade=t=out:st=49.5:d=0.5:color=0xF7F6F2" -c:v libsvtav1 -crf 46 -preset 6 -movflags +faststart -an site/teaser-av1.mp4
```

```bash
ffmpeg -y -i master.mp4 -vf "scale=1920:-2:flags=lanczos,fade=t=out:st=49.5:d=0.5:color=0xF7F6F2" -c:v libx264 -crf 31 -preset veryslow -pix_fmt yuv420p -movflags +faststart -an site/teaser-h264.mp4
```

```bash
ffmpeg -y -ss 46.0 -i master.mp4 -frames:v 1 -vf "scale=1920:-2:flags=lanczos" -q:v 3 site/teaser-poster.jpg
```

The CRFs are higher than those of the 42-second cut (38 and 22) because the camera never
stops, so every frame differs from the one before. At 46 and 31 each file is about
3.5 MB, the size of the 42-second cut, and the text of the captures and the captions stays
readable, at rest and in the pans.

The poster is the whole board at 46 seconds. The `<video>` element in `site/index.html`
lists the AV1 source first and H.264 as the fallback; if a re-encode changes resolution,
frame rate, or codec profile, update its `codecs=` strings (read them with
`ffprobe -show_entries stream=profile,level`). The Store trailer is
`out/film2-store-1920x1080.mp4` from `npm run render:film2-store`, for
`installer/msix/STORE-LISTING.md`.
