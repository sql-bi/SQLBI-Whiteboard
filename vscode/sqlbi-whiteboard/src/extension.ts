import * as vscode from 'vscode';
import { WboardPreviewEditorProvider } from './previewEditor';

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(WboardPreviewEditorProvider.register());
}

export function deactivate(): void {
}
