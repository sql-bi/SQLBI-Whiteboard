# Films

The campaign films, made with [Remotion](https://www.remotion.dev). The project sits
outside `Whiteboard.sln`, like `tools/AssetGenerator`, so solution builds ignore it.

Film 1, "Export before 16 October", is 30 seconds long. It tells people who use the
Microsoft Whiteboard app with a personal account to export their boards before the app
retires on 16 October 2026, and shows SQLBI Whiteboard opening the exported ZIP.

Film 2, "One board", is 50 seconds long. One continuous camera move crosses a single
board on which seven captures sit as windows, each with its caption written below it,
and ends on the whole board and the end card. It is the teaser on the site and the Store
trailer, and three 12-second portrait shorts are cut from it. It names no date, so it
stays true after the campaign.

## License

Remotion is used under its free license, which applies to companies with up to three
employees. SQLBI qualifies because it has fewer than four. The terms are at
<https://www.remotion.dev/docs/license>; a company that grows past that size needs a
company license.

## Requirements

- Windows, because the type is Segoe UI Variable Display and must come from the fonts
  installed on the PC. `npm run stills` writes `out/stills/font-check.png`, which says
  whether the renderer found it.
- Node 24 and npm.
- ffmpeg on the `PATH`, or its path in the `FFMPEG` environment variable.

The first render downloads Remotion's Chrome Headless Shell. To render with an installed
Chrome instead, set `FILMS_CHROME`:

```powershell
$env:FILMS_CHROME = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
```

## Install, preview, and render

```powershell
cd tools\films
npm install
npm run studio        # preview in the browser, with a timeline
npm run stills        # review stills of every shape, into out\stills
npm run render:all    # every rendition and the poster, into out
```

One script renders each rendition: `render:16x9`, `render:1x1`, `render:4x5`,
`render:9x16`, and `render:poster`. Each shape is rendered at twice its size into
`out\master` (3840 × 2160 for 16:9) and scaled down from there. `npm run typecheck`
checks the TypeScript.

| File | Size | Where it goes | Safe area |
| --- | --- | --- | --- |
| `film1-16x9.mp4` | 1920 × 1080 | YouTube, LinkedIn, X, the Store | 96 px on every side |
| `film1-1x1.mp4` | 1080 × 1080 | LinkedIn feed, X, Instagram feed | 90 px |
| `film1-4x5.mp4` | 1080 × 1350 | Instagram feed, LinkedIn | 90 px |
| `film1-9x16.mp4` | 1080 × 1920 | Instagram Reels, TikTok, YouTube Shorts | y 250 to 1550, x 60 to 930 |
| `film1-poster.jpg` | 1920 × 1080 | Poster frame, from the saved-board beat | |

The videos are H.264 High, yuv420p, 30 fps, AAC at 128 kbps, with the index at the start
of the file, and exactly 30 seconds long. Text, callouts, and the end card stay inside the
safe area, because the platforms draw their own controls over the rest. All four shapes
come from one composition, whose layout in `src/layout.ts` follows the aspect ratio.

Nothing under `out` is committed.

### Film 2

```powershell
npm run stills:film2        # a still every 5 seconds of each composition, into out\stills
npm run render:film2-all    # every rendition, into out, and the site files, into ..\..\site
```

One script renders each rendition: `render:film2-16x9`, `render:film2-1x1`,
`render:film2-4x5`, `render:film2-short-liveview`, `render:film2-short-dax`,
`render:film2-short-shapes`, `render:film2-store`, and `render:film2-site`. Each
composition is rendered once at its own size into `out\master`, not at twice its size,
because six of the captures are 1080p and a larger master would only enlarge them.

| File | Size | Where it goes | Safe area |
| --- | --- | --- | --- |
| `film2-16x9.mp4` | 1920 × 1080 | YouTube, LinkedIn, X | 96 px on every side |
| `film2-1x1.mp4` | 1080 × 1080 | LinkedIn feed, X, Instagram feed | 90 px |
| `film2-4x5.mp4` | 1080 × 1350 | Instagram feed, LinkedIn | 90 px |
| `film2-short-liveview.mp4` | 1080 × 1920 | Reels, TikTok, Shorts: the LiveView station | y 250 to 1550, x 60 to 930 |
| `film2-short-dax.mp4` | 1080 × 1920 | Reels, TikTok, Shorts: the DAX and F6 station | y 250 to 1550, x 60 to 930 |
| `film2-short-shapes.mp4` | 1080 × 1920 | Reels, TikTok, Shorts: the shapes station | y 250 to 1550, x 60 to 930 |
| `film2-store-1920x1080.mp4` | 1920 × 1080 | The Microsoft Store trailer | 96 px |
| `site/teaser-av1.mp4`, `site/teaser-h264.mp4` | 1920 × 1080 | The landing page, silent and looping | |
| `site/teaser-poster.jpg` | 1920 × 1080 | The landing page poster, the whole board at 46 s | |

The files in `out` are H.264 High, yuv420p, 30 fps, with the music bed in AAC at 128 kbps
and the index at the start of the file. The film is exactly 50 seconds long and each short
exactly 12, ending on a 2-second end card. The site files carry no audio, and their end
card fades to paper over the last half second so the loop restarts on the white board;
the commands are in [docs/teaser/shot-script.md](../../docs/teaser/shot-script.md).

`src/film2/board.ts` lays out the board for each shape, and `src/film2/cuts.ts` holds
the camera moves and the moment each capture plays, for the film and for each short.

## Captures

Record with OBS at 2560 × 1440 and 60 fps, with Windows display scaling at 150 % and the
released build, as in [docs/teaser/shot-script.md](../../docs/teaser/shot-script.md).
Save each capture in `captures\` under the name below. The folder is not committed; the
maintainer keeps the masters. Until a file is there, the film shows a navy placeholder
with its name and what it will show.

| # | File | Length | What it shows |
| --- | --- | --- | --- |
| C1 | `microsoft-whiteboard-board.mp4` | 8 s | A finished board open in the Microsoft Whiteboard app for Windows |
| C2 | `microsoft-whiteboard-export.mp4` | 12 s | Settings gear, Export, Zip (HTML+JSON), the save dialog, and the saved file |
| C3 | `sqlbi-open-zip.mp4` | 10 s | SQLBI Whiteboard: File, Open on the ZIP, and the board appears |
| C4 | `sqlbi-saved-board.mp4` | 8 s | The imported board; Save; the title bar shows Workshop.wboard |

`src/film1/captures.ts` sets, for each capture, where the film starts reading it, how fast
it plays it, and where the numbered callouts sit. The callout positions are estimates, so
check them against the real captures in `npm run studio` and correct them there.

Film 2 uses seven more. They were recorded at 30 fps, `ink-diagram.mp4` at 3840 × 2160 and
the others at 1920 × 1080. Each sits on the board at 1920 × 1080 and the camera never
zooms past that, so no capture is shown larger than it was recorded.

| # | File | Length | What it shows |
| --- | --- | --- | --- |
| D1 | `ink-diagram.mp4` | 8.6 s | The pen draws three boxes and two connectors, with pressure visible |
| D2 | `liveview-powerbi.mp4` | 9.6 s | View, LiveView, the Power BI window on the board, Freeze, highlighter marks |
| D3 | `dax-f6.mp4` | 9.2 s | A long one-line measure is pasted; F6 formats it |
| D4 | `shapes-connectors.mp4` | 10.1 s | Insert: a rectangle, a diamond, and a connector; the rectangle moves and the arrow follows |
| D5 | `frames-export.mp4` | 11.0 s | A board of slides, Export to PowerPoint, and the deck open in PowerPoint |
| D6 | `open-pptx-zip.mp4` | 8.5 s | A .pptx dropped on the board becomes its slides |
| D7 | `full-board.mp4` | 6.4 s | A double-click on empty canvas fits the whole board in the window |

The lengths are those of the video tracks. `src/film2/captures.ts` records what happens
when in each capture, read from frame sheets.

## Voice and music

The voice is `audio\film1-vo.mp3`. [audio/film1-vo.txt](audio/film1-vo.txt) holds the
script and the phrase each beat covers. Until the file exists the film carries a silent
audio track of the same length.

`scripts/elevenlabs.mjs` generates both audio files with the ElevenLabs API. It reads the
key from the `ELEVENLABS_API_KEY` environment variable and writes it nowhere. A key made in
the ElevenLabs account page needs the Text to Speech, Voices (read), Models (read), User
(read), and Music permissions. Set the variable in the user profile once, then run from a
new terminal:

```powershell
node scripts\elevenlabs.mjs status                       # plan, credits, and your cloned voices with their ids
node scripts\elevenlabs.mjs voice --voice <voice_id>     # the script in film1-vo.txt → audio\film1-vo.mp3
node scripts\elevenlabs.mjs music --seconds 30           # an instrumental bed → audio\film1-bed.mp3
node scripts\elevenlabs.mjs timings                      # where each phrase starts in the voice track
node scripts\elevenlabs.mjs timings --write              # and move the beats in beats.json there
```

`voice` uses `eleven_multilingual_v2`; pass `--model eleven_v3` for the newer model.
`music` takes `--prompt` to replace the default calm instrumental brief. `timings` finds
the pauses with ffmpeg's silencedetect, so a read with a pause inside a phrase, or none
between two phrases, gives the wrong count; then set `src/film1/beats.json` by hand.

The music bed, when there is one, is `audio\film1-bed.mp3`. It plays 22 dB below the voice
and fades out under the end card. Without it the film has no music. The MP3 files are not
committed.

Film 2 has no voice. Its bed is `audio\film2-bed.mp3`, generated 60 seconds long because
the generator fades out over its last 10 seconds, so the first 50 hold their level until
the end card:

```powershell
node scripts\elevenlabs.mjs music --film film2 --seconds 60 --prompt "Calm, minimal instrumental. Soft felt piano and a warm, slowly evolving pad, unhurried and steady, gently optimistic, with an even level throughout. No drums, no percussion, no vocals. Suitable under a quiet product film with no narration. Ends with a gentle fade."
```

The render mixes it with ffmpeg at a level that averages −20 dB: from its start under
the film, fading out over the end card, and from 14 seconds in under each short. Without
it the files in `out` carry a silent track.
