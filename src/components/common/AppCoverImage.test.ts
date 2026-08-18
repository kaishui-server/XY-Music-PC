import { readFileSync } from 'node:fs';

import { describe, expect, it } from 'vitest';

import componentSource from './AppCoverImage.vue?raw';

const readSource = (relativePath: string) => readFileSync(
  new URL(relativePath, import.meta.url),
  'utf8',
);

describe('app-wide default cover', () => {
  it('prefers the real cover and falls back when it cannot be loaded', () => {
    expect(componentSource).toContain('if (primaryUrl.value && !primaryFailed.value)');
    expect(componentSource).toContain('defaultCoverUrl.value !== primaryUrl.value');
    expect(componentSource).toContain("emit('primary-error', event)");
    expect(componentSource).toContain('<slot v-else />');
  });

  it('is wired into every music artwork surface', () => {
    const files = [
      '../song-list/SongTable.vue',
      '../song-list/OnlineSongList.vue',
      '../../views/Search.vue',
      '../../views/Albums.vue',
      '../../views/Artists.vue',
      '../../views/OnlineDetailView.vue',
      '../headers/DetailHeader.vue',
      '../headers/AlbumDetailHeader.vue',
      '../headers/ArtistDetailHeader.vue',
      '../layout/SidebarPlaylists.vue',
      '../layout/MiniPlayerWindow.vue',
      '../layout/TaskbarControlWindow.vue',
      '../layout/TrayMenuWindow.vue',
      '../overlays/SongInfoModal.vue',
      '../overlays/SongRecognitionPanel.vue',
      '../overlays/PlaylistEditInfoModal.vue',
      '../overlays/AddToPlaylistModal.vue',
      '../overlays/MoveToFolderModal.vue',
      './FavoritesGrid.vue',
      './FolderTreeItem.vue',
      './DragGhost.vue',
      '../recent/RecentCollectionGrid.vue',
      '../home/ArtistAlbumGrid.vue',
    ];

    for (const file of files) {
      expect(readSource(file), file).toContain('AppCoverImage');
    }
  });

  it('also applies the fallback to artwork-derived app backgrounds', () => {
    const globalBackground = readSource('../layout/GlobalBackground.vue');
    const miniPlayer = readSource('../layout/MiniPlayerWindow.vue');
    expect(globalBackground).toContain("currentCover.value || (currentSong.value ? defaultCoverUrl.value : '')");
    expect(miniPlayer).toContain("currentSong.value ? localCoverUrl.value || defaultCoverUrl.value : ''");
  });
});
