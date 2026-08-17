# Application icon

`SQLBI.Whiteboard.svg` is the master artwork: the Fluent whiteboard glyph in white on a
rounded tile filled with the SQLBI brand gradient, `#F42727` to `#B71D1D`, the same pair
Bravo uses.

`SQLBI.Whiteboard.ico` and `SQLBI.Whiteboard.png` here, and `banner.png` and
`background.png` under `installer/wix/assets`, are all generated from that composition by
`tools/AssetGenerator`. Do not edit them by hand; change the SVG and the matching values in
`tools/AssetGenerator/Program.cs`, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-assets.ps1
```

`SQLBI.Whiteboard.svg` is derived from Microsoft Fluent UI System Icons'
`whiteboard_24_filled` glyph. The toolbar uses `inking_tool`, `highlight`,
`calligraphy_pen`, `resize_image`, `record` (24 regular), and `chevron_down`
(16 regular) from the same set,
obtained from
`@fluentui/svg-icons` version 1.1.308:

https://unpkg.com/@fluentui/svg-icons@1.1.308/icons/whiteboard_24_filled.svg

The upstream project is Copyright (c) 2020 Microsoft Corporation and is
distributed under the MIT License. The license text is included in
`FluentSystemIcons-LICENSE.txt`.
