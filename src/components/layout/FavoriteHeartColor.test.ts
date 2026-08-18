import { describe, expect, it } from 'vitest';

import footerControlSource from './FooterControlItem.vue?raw';
import miniPlayerSource from './MiniPlayerWindow.vue?raw';
import trayMenuSource from './TrayMenuWindow.vue?raw';
import recognitionSource from '../overlays/SongRecognitionPanel.vue?raw';
import songTableSource from '../song-list/SongTable.vue?raw';

describe('favorite heart color', () => {
  it('keeps favorite hearts red instead of following the accent theme', () => {
    expect(footerControlSource).toContain("? 'text-[#EC4141]");
    expect(songTableSource).toContain('h-4 w-4 text-[#EC4141]');
    expect(miniPlayerSource).toContain("isFavorite ? 'text-[#EC4141]");
    expect(trayMenuSource).toContain('transport-circle--favorite-active');
    expect(trayMenuSource).toContain('color: #ec4141');
    expect(recognitionSource).toContain("? 'text-[#EC4141]");
  });
});
