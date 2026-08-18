import { describe, expect, it } from 'vitest';

import footerSource from './PlayerFooter.vue?raw';
import controlItemSource from './FooterControlItem.vue?raw';
import dialogSource from '../overlays/DownloadDialog.vue?raw';

describe('player detail footer download', () => {
  it('hides the download control while local music is playing', () => {
    expect(controlItemSource).toContain("itemKey === 'download' && isOnlineSong");
    expect(controlItemSource).not.toContain("itemKey === 'download'\" class=");
  });

  it('hides the quality selector while local music is playing', () => {
    expect(controlItemSource).toContain("itemKey === 'quality' && isQualitySelectableSong");
    expect(controlItemSource).not.toContain('本地音质');
  });

  it('always opens the original detailed download dialog', () => {
    expect(footerSource).not.toContain('openDownloadByBehavior');
    expect(footerSource).toMatch(/const handleDownloadClick = \(\) => \{[\s\S]*openDownloadDialog\(currentSong\.value\);[\s\S]*\};/);
    expect(footerSource).toMatch(/const handleConfirmRedownload = \(\) => \{[\s\S]*openDownloadDialog\(currentSong\.value\);[\s\S]*\};/);
  });

  it('keeps the original modal layout and exposes only the two extra-download buttons', () => {
    expect(dialogSource).toContain('w-[520px] max-w-[90vw]');
    expect(dialogSource).toContain('>下载歌曲</h3>');
    expect(dialogSource).toContain('name="quality-dropdown"');
    expect(dialogSource).toContain(':aria-expanded="isQualityMenuOpen"');
    expect(dialogSource).toContain('v-for="key in orderedQualityKeys"');
    expect(dialogSource).toContain('...ALL_QUALITY_KEYS.filter(key => !unsupported.has(key))');
    expect(dialogSource).toContain('...ALL_QUALITY_KEYS.filter(key => unsupported.has(key))');
    expect(dialogSource).toContain(':disabled="unsupportedQualityKeys.includes(key)"');
    expect(dialogSource).toContain('quality-dropdown-enter-active');
    expect(dialogSource).not.toContain('grid grid-cols-4 gap-1.5');
    expect(dialogSource).not.toContain('type="checkbox"');
    expect(dialogSource).not.toContain('v-model="downloadAudio"');
    expect(dialogSource).toContain('额外下载独立歌词（LRC）');
    expect(dialogSource).toContain('额外下载封面');
    expect(dialogSource).toContain(':aria-pressed="downloadExtraLyrics"');
    expect(dialogSource).toContain(':aria-pressed="downloadExtraCover"');
    expect(dialogSource).toContain("downloadExtraLyrics ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'");
    expect(dialogSource).toContain("downloadExtraCover ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'");
  });

  it('always downloads audio with embedded lyrics and cover while extras remain optional', () => {
    expect(dialogSource).toContain('downloadAudio: true');
    expect(dialogSource).toContain('downloadLyrics: downloadExtraLyrics.value');
    expect(dialogSource).toContain('downloadCover: downloadExtraCover.value');
    expect(dialogSource).toContain("lyricsFormat: 'lrc'");
    expect(dialogSource).toContain('embedLyrics: true');
    expect(dialogSource).toContain('embedCover: true');
  });

  it('retains the updated download probe and file-size information', () => {
    expect(dialogSource).toContain('probeDownloadableQualities');
    expect(dialogSource).toContain('preResolvedUrls');
    expect(dialogSource).toContain('qualityExtraText(key)');
  });
});
