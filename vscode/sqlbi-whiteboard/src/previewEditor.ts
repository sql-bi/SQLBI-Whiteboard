import * as vscode from 'vscode';
import { extractPreview } from './extractPreview';

export class WboardPreviewEditorProvider implements vscode.CustomReadonlyEditorProvider {
  public static readonly viewType = 'sqlbi.whiteboard.preview';

  public static register(): vscode.Disposable {
    return vscode.window.registerCustomEditorProvider(
      WboardPreviewEditorProvider.viewType,
      new WboardPreviewEditorProvider(),
      {
        supportsMultipleEditorsPerDocument: true,
        webviewOptions: {
          retainContextWhenHidden: true,
        },
      });
  }

  public async openCustomDocument(uri: vscode.Uri): Promise<vscode.CustomDocument> {
    return { uri, dispose() { } };
  }

  public async resolveCustomEditor(
    document: vscode.CustomDocument,
    webviewPanel: vscode.WebviewPanel): Promise<void> {
    webviewPanel.webview.options = {
      enableScripts: true,
    };

    const render = async () => {
      webviewPanel.webview.html = await this.renderHtml(document.uri);
    };

    await render();

    const name = document.uri.path.split('/').pop() ?? '*.wboard';
    const watcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(vscode.Uri.joinPath(document.uri, '..'), name));
    const refreshIfSame = (uri: vscode.Uri) => {
      if (uri.toString() === document.uri.toString()) {
        void render();
      }
    };

    webviewPanel.onDidDispose(() => watcher.dispose());
    watcher.onDidChange(refreshIfSame);
    watcher.onDidCreate(refreshIfSame);
  }

  private async renderHtml(uri: vscode.Uri): Promise<string> {
    let archive: Uint8Array;
    try {
      archive = await vscode.workspace.fs.readFile(uri);
    } catch {
      return this.messageHtml('This .wboard file could not be read.');
    }

    const result = extractPreview(archive);
    if (result.kind === 'invalid') {
      return this.messageHtml('This file is not a readable .wboard archive.');
    }

    if (result.kind === 'missing') {
      return this.messageHtml(
        'This board has no embedded preview. Save it from SQLBI Whiteboard to create one. Older and empty boards have none.');
    }

    const encoded = Buffer.from(result.bytes).toString('base64');
    const nonce = getNonce();
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';" />
  <style>
    html, body {
      margin: 0;
      height: 100%;
      overflow: hidden;
      background: var(--vscode-editor-background);
      color: var(--vscode-foreground);
      font-family: var(--vscode-font-family);
    }
    .stage {
      width: 100%;
      height: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: grab;
    }
    .stage.panning { cursor: grabbing; }
    img {
      max-width: 100%;
      max-height: 100%;
      transform-origin: center center;
      user-select: none;
      -webkit-user-drag: none;
    }
  </style>
</head>
<body>
  <div class="stage" id="stage">
    <img id="preview" alt="Whiteboard preview" src="data:image/png;base64,${encoded}" />
  </div>
  <script nonce="${nonce}">
    const stage = document.getElementById('stage');
    const image = document.getElementById('preview');
    let scale = 1;
    let x = 0;
    let y = 0;
    let dragging = false;
    let lastX = 0;
    let lastY = 0;

    function apply() {
      image.style.transform = 'translate(' + x + 'px, ' + y + 'px) scale(' + scale + ')';
    }

    stage.addEventListener('wheel', (event) => {
      event.preventDefault();
      const next = event.deltaY < 0 ? scale * 1.1 : scale / 1.1;
      scale = Math.min(8, Math.max(0.2, next));
      apply();
    }, { passive: false });

    stage.addEventListener('pointerdown', (event) => {
      dragging = true;
      lastX = event.clientX;
      lastY = event.clientY;
      stage.classList.add('panning');
      stage.setPointerCapture(event.pointerId);
    });

    stage.addEventListener('pointermove', (event) => {
      if (!dragging) {
        return;
      }
      x += event.clientX - lastX;
      y += event.clientY - lastY;
      lastX = event.clientX;
      lastY = event.clientY;
      apply();
    });

    const endPan = () => {
      dragging = false;
      stage.classList.remove('panning');
    };
    stage.addEventListener('pointerup', endPan);
    stage.addEventListener('pointercancel', endPan);
    stage.addEventListener('dblclick', () => {
      scale = 1;
      x = 0;
      y = 0;
      apply();
    });
  </script>
</body>
</html>`;
  }

  private messageHtml(message: string): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';" />
  <style>
    html, body {
      margin: 0;
      height: 100%;
      background: var(--vscode-editor-background);
      color: var(--vscode-descriptionForeground);
      font-family: var(--vscode-font-family);
      font-size: var(--vscode-font-size);
    }
    .message {
      box-sizing: border-box;
      max-width: 36rem;
      margin: 20vh auto 0;
      padding: 0 1.5rem;
      line-height: 1.5;
    }
  </style>
</head>
<body>
  <p class="message">${escapeHtml(message)}</p>
</body>
</html>`;
  }
}

function escapeHtml(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function getNonce(): string {
  const bytes = Buffer.allocUnsafe(16);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = Math.floor(Math.random() * 256);
  }
  return bytes.toString('hex');
}
