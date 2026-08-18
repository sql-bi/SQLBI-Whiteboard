# SQLBI Whiteboard for VS Code

Opens a `.wboard` file as the `preview.png` written on save, instead of as a ZIP archive. Boards without a preview show a short message.

This extension does not render the board and does not edit the file. Open the board in SQLBI Whiteboard to change it.

## Install

Install [SQLBI Whiteboard](https://marketplace.visualstudio.com/items?itemName=sqlbi.sqlbi-whiteboard) from the Marketplace (`sqlbi.sqlbi-whiteboard`).

To run from this folder during development:

```powershell
cd vscode/sqlbi-whiteboard
npm install
npm run compile
```

Then **Run > Start Debugging**, or package a VSIX:

```powershell
npx --yes @vscode/vsce package
code --install-extension sqlbi-whiteboard-1.0.0.vsix
```

Shipping a new Marketplace version is a `package.json` version bump merged to `main`. See [docs/release-management.md](../../docs/release-management.md).

## Use

Open any `.wboard` file. Wheel zooms, drag pans, double-click resets.

## Format

A `.wboard` is a ZIP. The preview entry is `preview.png` at the archive root, matching `BoardArchive.PreviewEntryName` in the desktop application.
