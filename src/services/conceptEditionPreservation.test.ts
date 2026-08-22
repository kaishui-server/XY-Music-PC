import { describe, expect, it } from 'vitest';

import appSource from '../App.vue?raw';
import downloadDialogSource from '../components/overlays/DownloadDialog.vue?raw';
import homeItemsSource from '../features/settings/homeItems.ts?raw';
import aboutSource from '../components/settings/SettingsAbout.vue?raw';
import mainShellSource from '../components/layout/MainShell.vue?raw';
import tauriConfigSource from '../../src-tauri/tauri.conf.json?raw';

describe('concept edition preservation', () => {
  it('keeps concept edition branding after upstream feature ports', () => {
    expect(tauriConfigSource).toContain('XY Music');
    expect(tauriConfigSource).toContain('com.xymusic.concept');
    expect(tauriConfigSource).not.toContain('com.xymusic.desktop');
    expect(appSource).toContain('XY Music');
  });

  it('keeps concept home modules and download dialog behavior', () => {
    expect(homeItemsSource).toContain("key: 'nowPlaying'");
    expect(homeItemsSource).toContain("key: 'hotComment'");
    expect(downloadDialogSource).toContain('downloadExtraLyrics');
    expect(downloadDialogSource).toContain('downloadExtraCover');
    expect(downloadDialogSource).toContain('embedLyrics: true');
    expect(downloadDialogSource).toContain('embedCover: true');
  });

  it('keeps concept about behavior and excludes unrelated official layout ports', () => {
    expect(aboutSource).toContain("id: 'concept'");
    expect(aboutSource).toContain("id: 'xianyu-music'");
    expect(aboutSource).toContain('维护中，暂不可用');
    expect(mainShellSource).not.toContain('TopBarControlItem');
    expect(mainShellSource).not.toContain('SettingsTopBarLayout');
  });
});
