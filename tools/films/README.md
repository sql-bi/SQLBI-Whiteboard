# Films

The campaign films, made with [Remotion](https://www.remotion.dev). The project sits
outside `Whiteboard.sln`, like `tools/AssetGenerator`, so solution builds ignore it.

Film 1, "Export before 16 October", is 30 seconds long. It tells people who use the
Microsoft Whiteboard app with a personal account to export their boards before the app
retires on 16 October 2026, and shows SQLBI Whiteboard opening the exported ZIP.

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
