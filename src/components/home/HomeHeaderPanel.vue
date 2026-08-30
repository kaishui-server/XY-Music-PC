<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue';

import type { FolderNode, Song } from '../../types';
import { useAddToPlaylistDialog } from '../../features/collections/addToPlaylistDialog';
import { useToast } from '../../composables/toast';

const DetailHeader = defineAsyncComponent(() => import('../headers/DetailHeader.vue'));
const FoldersHeader = defineAsyncComponent(() => import('../headers/FoldersHeader.vue'));
const LocalMusicHeader = defineAsyncComponent(() => import('../headers/LocalMusicHeader.vue'));

interface PlaylistDetail {
  name: string;
  date: string;
}

interface Props {
  localViewMode: string;
  isBatchMode: boolean;
  isManagementMode: boolean;
  activeRootPath: string;
  selectedCount: number;
  folderTree: FolderNode[];
  currentFolderFilter: string;
  playlistDetail: PlaylistDetail | null;
  localSongList: Song[];
  localSongPaths?: string[];
  playlistId?: string;
  resolveSongByPath?: (path: string) => Song | null;
  /** 整页滚动容器引用，透传给 DetailHeader 用于驱动封面收缩效果 */
  scrollContainerRef?: HTMLElement | null;
}

const props = defineProps<Props>();
const { openAddToPlaylistDialog } = useAddToPlaylistDialog();
const { showToast } = useToast();

const emit = defineEmits<{
  (event: 'update:isBatchMode', value: boolean): void;
  (event: 'update:isManagementMode', value: boolean): void;
  (event: 'playAll'): void;
  (event: 'batchPlay'): void;
  (event: 'showAddToPlaylist'): void;
  (event: 'batchDelete'): void;
  (event: 'folderBatchDelete'): void;
  (event: 'batchMove'): void;
  (event: 'batchDownload'): void;
  (event: 'rootCreatePlaylist', path: string, name: string): void;
  (event: 'addFolder'): void;
  (event: 'refreshFolder'): void;
  (event: 'removeFolder', path: string, name?: string): void;
  (event: 'rootCreateFolder', path: string): void;
  (event: 'rootDeleteFolder', path: string): void;
  (event: 'activeRootChange', value: string): void;
  (event: 'renamePlaylist'): void;
  (event: 'refreshAll'): void;
  (event: 'selectAll'): void;
  (event: 'batchAddToFavorites'): void;
}>();

const isBatchModeModel = computed({
  get: () => props.isBatchMode,
  set: (value: boolean) => emit('update:isBatchMode', value),
});

const isManagementModeModel = computed({
  get: () => props.isManagementMode,
  set: (value: boolean) => emit('update:isManagementMode', value),
});

const handleMergeToPlaylist = () => {
  const songPaths = props.localSongPaths ?? props.localSongList.map(song => song.path);
  const songs = songPaths
    .map(path => props.resolveSongByPath?.(path) ?? props.localSongList.find(song => song.path === path))
    .filter((song): song is Song => Boolean(song));

  if (songPaths.length === 0) {
    showToast('当前歌单暂无可合并的歌曲', 'info');
    return;
  }

  openAddToPlaylistDialog(songPaths, {
    songs,
    excludedPlaylistId: props.playlistId ?? null,
  });
};
</script>

<template>
  <FoldersHeader
    v-if="localViewMode === 'folder'"
    v-model:isBatchMode="isBatchModeModel"
    :selectedCount="selectedCount"
    :currentFolderFilter="currentFolderFilter"
    @playAll="$emit('playAll')"
    @batchPlay="$emit('batchPlay')"
    @addToPlaylist="$emit('showAddToPlaylist')"
    @batchDelete="$emit('folderBatchDelete')"
    @batchMove="$emit('batchMove')"
    @addFolder="$emit('addFolder')"
    @refreshFolder="$emit('refreshFolder')"
    v-model:isManagementMode="isManagementModeModel"
  />

  <DetailHeader
    v-else-if="localViewMode === 'playlist'"
    v-model:isBatchMode="isBatchModeModel"
    :title="playlistDetail?.name || ''"
    :subtitle="playlistDetail?.date ? `创建于 ${playlistDetail.date}` : ''"
    :songs="localSongList"
    :selectedCount="selectedCount"
    :totalSongCount="localSongPaths?.length ?? localSongList.length"
    :showRename="true"
    :showAddToPlaylist="true"
    :showHeaderAddToPlaylist="true"
    headerAddToPlaylistLabel="合并到歌单"
    :scrollContainerRef="scrollContainerRef"
    @playAll="$emit('playAll')"
    @batchPlay="$emit('batchPlay')"
    @openAddToPlaylist="handleMergeToPlaylist"
    @batchDelete="$emit('batchDelete')"
    @batchAddToFavorites="$emit('batchAddToFavorites')"
    @batchDownload="$emit('batchDownload')"
    @rename="$emit('renamePlaylist')"
    @selectAll="$emit('selectAll')"
  />

  <LocalMusicHeader
    v-else-if="!['statistics', 'artist', 'album'].includes(localViewMode)"
    v-model:isBatchMode="isBatchModeModel"
    :selectedCount="selectedCount"
    :totalSongCount="localSongList.length"
    @playAll="$emit('playAll')"
    @selectAll="$emit('selectAll')"
    @addToPlaylist="$emit('showAddToPlaylist')"
    @batchDelete="$emit('batchDelete')"
    @batchMove="$emit('batchMove')"
    @batchDownload="$emit('batchDownload')"
    @refreshAll="$emit('refreshAll')"
  />
</template>
