import { describe, expect, it } from 'vitest';

import source from './App.vue?raw';

describe('App text selection behavior', () => {
  it('prevents browser text selection across the app shell by default', () => {
    expect(source).toContain('html,\nbody,\n#app');
    expect(source).toContain('-webkit-user-select: none;');
    expect(source).toContain('user-select: none;');
  });

  it('keeps editable text controls selectable', () => {
    expect(source).toContain('input,\ntextarea,\n[contenteditable="true"]');
    expect(source).toContain('-webkit-user-select: text;');
    expect(source).toContain('user-select: text;');
  });
});

describe('App imported lyrics fonts registration', () => {
  it('does not skip registration in the desktop lyrics window', () => {
    expect(source).not.toContain('if (!isDesktopLyricsWindow)');
  });
});

describe('App Tauri window lifecycle', () => {
  it('guards native window APIs outside the Tauri runtime', () => {
    expect(source).toContain("import { isTauri } from '@tauri-apps/api/core';");
    expect(source).toContain('const runningInTauri = isTauri();');
    expect(source).toContain('if (runningInTauri) {');
  });

  it('catches asynchronous listener cleanup failures during HMR', () => {
    expect(source).toContain('const disposeTauriListener = (');
    expect(source).toContain('.then(() => unlisten())');
    expect(source).toContain("disposeTauriListener(unlistenCloseRequested, 'close-requested');");
    expect(source).toContain("disposeTauriListener(unlistenFocusChanged, 'focus-changed');");
  });
});
