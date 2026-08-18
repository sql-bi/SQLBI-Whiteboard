# SQLBI Whiteboard for VS Code

Opens a `.wboard` file as the `preview.png` written on save, instead of as a ZIP archive. Boards without a preview show a short message.

This extension does not render the board and does not edit the file. Open the board in SQLBI Whiteboard to change it.

## Install

Not on the Marketplace yet. From the repository:

```powershell
cd vscode/sqlbi-whiteboard
npm install
npm run compile
```

Then **Run > Start Debugging** in VS Code with this folder as the workspace, or install a packaged VSIX:

```powershell
npx --yes @vscode/vsce package
code --install-extension sqlbi-whiteboard-1.0.0.vsix
```

## Use

Open any `.wboard` file. Wheel zooms, drag pans, double-click resets.

## Format

A `.wboard` is a ZIP. The preview entry is `preview.png` at the archive root, matching `BoardArchive.PreviewEntryName` in the desktop application.
