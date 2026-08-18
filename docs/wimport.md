# `.wimport` format for authors and agents

Read this file before writing a `.wimport`. It is the contract SQLBI Whiteboard implements.
The public edition is [whiteboard.sqlbi.com/wimport.html](https://whiteboard.sqlbi.com/wimport.html). Keep the two in step.

A `.wimport` file is a Markdown **recipe**. Whiteboard imports it as image and text
containers. It is not a board file. There is no export back to `.wimport`. After import the
user rearranges, draws, and saves a `.wboard`.

The file must be valid CommonMark so it previews in VS Code and GitHub without a custom
renderer. Associate the extension if the editor does not treat it as Markdown:

```json
"files.associations": { "*.wimport": "markdown" }
```

A folder of recipes can ship that association in `.vscode/settings.json`, and recommend
`sqlbi.sqlbi-whiteboard` in `.vscode/extensions.json` so `.wboard` files open as the
embedded preview. This repository and `docs/samples` both do that.

## Produce this

- Extension: `.wimport` (not `.md`, not `.imp`).
- Encoding: UTF-8.
- Paths: local and relative to the `.wimport` file. No `http://` or `https://`.
- One idea per `##` heading. One container per heading.

## Heading grammar

| Line | Meaning |
| --- | --- |
| `# Title` | Optional board title. At most one. Not a container. Ignored if it appears after the first `##`. |
| `## Title` | Starts a container. The heading text is the container title. |
| `###` and deeper | Stay inside the current container body. They do not start a new object. |
| `---`, `***`, or `___` on its own line | Thematic break. Starts a **new row** of containers. Not a container. |
| Any other line before the first `##` | Documentation for the preview. Not imported. |

An empty `##` heading is titled `Text`.

## How the body becomes a container

Recognizers run in this order. The first match wins. Extra material after the match is
ignored.

1. **Markdown image** — `![optional alt](relative/path.png)`
   - Image container.
   - Title is the `##` heading. If the heading is empty, the alt text is used, then the file
     name.
   - Allowed extensions: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`.
2. **Fenced code** whose info-string is a registered language
   - Text container. Contents of the fence. Language from the tag.
3. **Markdown link** to an image extension — `[label](relative/path.png)`
   - Same as an image.
4. **Markdown link** to a registered language extension — `[label](relative/path.dax)`
   - Text container. File contents. Language from the extension.
5. **Any remaining text**
   - Text container. The Markdown source is stored as **plain text**. Whiteboard does not
     render Markdown.
6. **Empty body**
   - The heading is skipped.

Do not put an image and a measure under the same `##`. Use two headings.

A fenced block whose tag is unknown (for example `python` today) is **not** a language
container. The whole body, fence included, becomes plain text.

### Languages shipped today

| Language | Fence info-string | File extensions |
| --- | --- | --- |
| DAX | `dax` | `.dax` |
| SQL Server | `sql`, `tsql` | `.sql` |

Further languages (Python, C#, TypeScript, …) will be extra rows in this table. Do not invent
fence tags Whiteboard does not list here: they import as plain text.

Linked text files larger than 1 000 000 bytes are skipped and reported as missing.

## Layout

Whiteboard measures each container, then packs them **left to right**. A thematic break
starts a new row even if the current row is not full. Without a break, a row wraps when the
next item would exceed about 2400 world units.

On drop, the group’s **top-left** is the pointer. On Open or File → Import, the group’s
top-left is the top-left of the visible view.

Do not put coordinates in the file. There is no `pos:`, no YAML front matter, and no HTML
comment layout.

## How the file is opened

| Action | Result |
| --- | --- |
| Drop onto an open board | Containers are **added**. Pointer is the group top-left. Undo is one step. |
| File → Import… | Containers are **added** at the visible top-left. |
| File → Open, or double-click | **New untitled board**, then import. Save / Save As writes `.wboard` only. The `.wimport` path is used only to resolve relative links. |

Missing or unreadable linked files are skipped. Whiteboard then shows a dialog listing the
resolved paths, which can be copied.

## Template

```markdown
# Optional board title

Optional notes for the Markdown preview. Not imported.

## Container title for an image
![short alt](./images/diagram.png)

## Container title for notes
- Bullet one
- Bullet two

---

## Container title for embedded DAX
```dax
Total Sales := SUM(Sales[Amount])
```

## Container title for linked SQL
[Top customers](./sql/top-customers.sql)
```

A complete sample is `docs/samples/contoso-workshop.wimport`.

## Checklist before you finish

- File name ends in `.wimport`.
- Every container is a `##` heading with exactly one payload.
- Images and linked code exist next to the file, using relative paths.
- Image files are png/jpeg/bmp/gif. Code files you link are `.dax` or `.sql`, or the code is
  embedded in a `dax` / `sql` / `tsql` fence.
- Row breaks are a thematic break on its own line, not a fake heading.
- You did not use `http://`, YAML, or explicit positions.
- Opening the file in a Markdown preview still reads as a normal document.

## Do not

- Write a `.md` and expect Whiteboard to import it.
- Ask Whiteboard to save or export `.wimport`.
- Embed bitmap bytes. Images are always a link to a file.
- Mix two payloads in one `##` section.
- Use HTML, YAML front matter, or custom XML.
- Reference network URLs.
- Generate ink, LiveViews, or a `.wboard` ZIP. Those are not this format.
