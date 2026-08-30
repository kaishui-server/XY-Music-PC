<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import AppCoverImage from '../components/common/AppCoverImage.vue';
import { useCollectionsStore } from '../features/collections/store';
import { useLibraryStore } from '../features/library/store';
import { usePlaybackController } from '../features/playback/usePlaybackController';
import { useOnlineDetailStore } from '../features/onlineDetail/store';
import { pluginsVersion } from '../services/pluginEngine';
import {
  loadExploreData,
  pluginResultToSong,
  type ExploreData,
  type ExplorePlaylist,
} from '../services/exploreService';
import type { PluginSearchResult, Song } from '../types';
import { getDisplayCoverUrl } from '../utils/coverProxy';

const route = useRoute();
const router = useRouter();
const collectionsStore = useCollectionsStore();
const libraryStore = useLibraryStore();
const { playSong } = usePlaybackController();
const onlineDetailStore = useOnlineDetailStore();

const data = ref<ExploreData>({ songs: [], playlists: [], charts: [] });
const loading = ref(false);
const activeTab = ref<'songs' | 'playlists'>(route.query.tab === 'playlists' ? 'playlists' : 'songs');

let loadSequence = 0;
let disposed = false;

const seedSongs = computed<Song[]>(() => {
  const result: Song[] = [];
  const seen = new Set<string>();
  const add = (song: Song | null | undefined) => {
    if (!song?.path || seen.has(song.path) || !song.title?.trim()) return;
    seen.add(song.path);
    result.push(song);
  };

  for (const item of collectionsStore.recentSongs.slice(0, 30)) {
    add(collectionsStore.recentSongMeta[item.path] ?? libraryStore.getSongByPath(item.path));
  }
  for (const path of collectionsStore.favoritePaths) {
    add(collectionsStore.favoriteSongMeta[path] ?? libraryStore.getSongByPath(path));
  }
  for (const playlist of collectionsStore.playlists) {
    if (playlist.songs?.length) playlist.songs.forEach(add);
    else playlist.songPaths.forEach(path => add(libraryStore.getSongByPath(path)));
    if (result.length >= 80) break;
  }
  return result.slice(0, 80);
});

const coverUrl = (value: string) => value ? getDisplayCoverUrl(value) : '';
const formatDuration = (durationMs: number) => {
  const totalSeconds = Math.floor((Number(durationMs) || 0) / 1000);
  if (totalSeconds <= 0) return '--:--';
  return `${Math.floor(totalSeconds / 60)}:${String(totalSeconds % 60).padStart(2, '0')}`;
};

const hasExploreData = (value: ExploreData) => (
  value.songs.length > 0 || value.playlists.length > 0 || value.charts.length > 0
);

const refresh = async (forceRefresh = false) => {
  const sequence = ++loadSequence;
  loading.value = true;
  try {
    const next = await loadExploreData(seedSongs.value, {
      forceRefresh,
      playlistNames: collectionsStore.playlists.map(playlist => playlist.name),
    });
    if (!disposed && sequence === loadSequence && (hasExploreData(next) || !hasExploreData(data.value))) {
      // 新推荐完成前保留旧列表；空响应也不能覆盖当前可用内容。
      data.value = {
        songs: next.songs.length > 0 ? next.songs : data.value.songs,
        playlists: next.playlists.length > 0 ? next.playlists : data.value.playlists,
        charts: next.charts.length > 0 ? next.charts : data.value.charts,
      };
    }
  } finally {
    if (!disposed && sequence === loadSequence) loading.value = false;
  }
};

const playRecommendation = (item: PluginSearchResult) => {
  void playSong(pluginResultToSong(item), { insertAfterCurrent: true });
};

const openPlaylist = (playlist: ExplorePlaylist) => {
  onlineDetailStore.setContext({
    type: 'playlist',
    title: playlist.result.title,
    subtitle: playlist.result.trackCount ? `${playlist.result.trackCount} 首` : (playlist.result.artist || ''),
    coverUrl: playlist.result.coverUrl,
    pluginSource: playlist.plugin,
    rawData: playlist.result.rawData,
    sourceSearchType: 'playlist',
    engineType: 'musicfree',
  });
  void router.push({ path: '/online-detail', query: { type: 'playlist' } });
};

const goBack = () => {
  if (window.history.length > 1) router.back();
  else void router.push('/explore');
};

watch(() => route.query.tab, value => {
  activeTab.value = value === 'playlists' ? 'playlists' : 'songs';
});
watch(pluginsVersion, () => {
  if (!hasExploreData(data.value)) void refresh(false);
});

onMounted(() => { void refresh(); });
onBeforeUnmount(() => {
  disposed = true;
  loadSequence += 1;
});
</script>

<template>
  <div class="explore-recommendations-page h-full overflow-y-auto custom-scrollbar px-6 pb-12 pt-5">
    <header class="recommendations-header">
      <button type="button" class="recommendations-back" aria-label="返回探索" @click="goBack">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="m15 18-6-6 6-6" /></svg>
        <span>探索</span>
      </button>
      <div class="recommendations-heading">
        <h1>猜你想听</h1>
        <p>根据最近播放、收藏和歌单，为你寻找新的声音</p>
      </div>
      <button type="button" class="recommendations-refresh" title="刷新推荐" :disabled="loading" @click="refresh(true)">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" :class="{ 'animate-spin': loading }" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M20 11a8 8 0 1 0 2 5m-2-5v-5m0 5h-5" /></svg>
      </button>
    </header>

    <div class="recommendations-tabs">
      <button type="button" :class="{ active: activeTab === 'songs' }" @click="activeTab = 'songs'">歌曲</button>
      <button type="button" :class="{ active: activeTab === 'playlists' }" @click="activeTab = 'playlists'">歌单</button>
    </div>

    <div v-if="loading && data.songs.length === 0 && data.playlists.length === 0" class="recommendations-empty">正在生成推荐…</div>
    <div v-else-if="activeTab === 'songs' && data.songs.length" class="recommendations-list">
      <button v-for="(item, index) in data.songs" :key="`${item.pluginId}-${item.id}-${index}`" type="button" class="recommendation-song-row" @click="playRecommendation(item)">
        <span class="recommendation-index">{{ String(index + 1).padStart(2, '0') }}</span>
        <AppCoverImage :src="coverUrl(item.coverUrl)" class="recommendation-song-cover" loading="lazy" decoding="async"><span class="recommendation-cover-fallback">{{ item.title.slice(0, 1) }}</span></AppCoverImage>
        <span class="recommendation-song-main"><span class="recommendation-title">{{ item.title }}</span><span class="recommendation-subtitle">{{ item.artist || '未知歌手' }}</span></span>
        <span class="recommendation-album">{{ item.album || '未知专辑' }}</span>
        <span class="recommendation-source">{{ item.platform }}</span>
        <span class="recommendation-duration">{{ formatDuration(item.duration) }}</span>
        <span class="recommendation-action" aria-hidden="true"><svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor"><path d="M6.3 3.8a1 1 0 0 1 1.55-.83l7.3 5.2a1 1 0 0 1 0 1.66l-7.3 5.2A1 1 0 0 1 6.3 14.2V3.8Z" /></svg></span>
      </button>
    </div>
    <div v-else-if="activeTab === 'playlists' && data.playlists.length" class="recommendations-list">
      <button v-for="(item, index) in data.playlists" :key="`${item.plugin.id}-${item.result.id}-${index}`" type="button" class="recommendation-playlist-row" @click="openPlaylist(item)">
        <span class="recommendation-index">{{ String(index + 1).padStart(2, '0') }}</span>
        <AppCoverImage :src="coverUrl(item.result.coverUrl)" class="recommendation-playlist-cover" loading="lazy" decoding="async"><span class="recommendation-cover-fallback">{{ item.result.title.slice(0, 1) }}</span></AppCoverImage>
        <span class="recommendation-song-main"><span class="recommendation-title">{{ item.result.title }}</span><span class="recommendation-subtitle">{{ item.result.artist || '推荐歌单' }}</span></span>
        <span class="recommendation-source">{{ item.plugin.name }}</span>
        <span class="recommendation-count">{{ item.result.trackCount ? `${item.result.trackCount} 首歌曲` : '歌单' }}</span>
        <span class="recommendation-action" aria-hidden="true"><svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="m9 18 6-6-6-6" /></svg></span>
      </button>
    </div>
    <div v-else class="recommendations-empty">{{ seedSongs.length ? '暂无可用推荐，请稍后刷新' : '多播放、收藏几首歌曲后，这里会根据你的偏好生成推荐' }}</div>

  </div>
</template>

<style scoped>
.explore-recommendations-page { background: linear-gradient(180deg, rgba(255, 255, 255, 0.10), transparent 35%); }
.recommendations-header { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 18px; max-width: 1180px; margin: 0 auto; padding: 8px 2px 18px; }
.recommendations-back, .recommendations-refresh { display: inline-flex; align-items: center; gap: 6px; color: rgba(75, 85, 99, 0.72); font-size: .8rem; transition: 160ms ease; }
.recommendations-back:hover, .recommendations-refresh:hover { color: var(--accent-color); }
.recommendations-refresh { justify-self: end; justify-content: center; height: 32px; width: 32px; border-radius: 10px; }
.recommendations-refresh:hover { background: rgba(0, 0, 0, .06); }
.recommendations-refresh:disabled { cursor: wait; opacity: .55; }
.recommendations-heading { text-align: center; }
.recommendations-heading h1 { color: rgb(17 24 39); font-size: 1.35rem; font-weight: 800; letter-spacing: -.025em; }
.recommendations-heading p { margin-top: 4px; color: rgba(75, 85, 99, .68); font-size: .76rem; }
.recommendations-tabs { display: flex; gap: 4px; max-width: 1180px; margin: 0 auto 12px; border-bottom: 1px solid rgba(0, 0, 0, .06); }
.recommendations-tabs button { border-bottom: 2px solid transparent; padding: 9px 14px 10px; color: rgba(75, 85, 99, .68); font-size: .82rem; font-weight: 650; }
.recommendations-tabs button.active { border-color: var(--accent-color); color: var(--accent-color); }
.recommendations-list { display: grid; gap: 4px; max-width: 1180px; margin: 0 auto; }
.recommendation-song-row, .recommendation-playlist-row { display: grid; width: 100%; min-width: 0; align-items: center; gap: 12px; border-radius: 12px; padding: 6px 10px 6px 4px; text-align: left; transition: 160ms ease; }
.recommendation-song-row { grid-template-columns: 28px 44px minmax(180px, 1.4fr) minmax(120px, 1fr) 92px 54px 24px; }
.recommendation-playlist-row { grid-template-columns: 28px 48px minmax(180px, 1.5fr) minmax(120px, 1fr) 92px 24px; }
.recommendation-song-row:hover, .recommendation-playlist-row:hover { background: rgba(0, 0, 0, .055); }
.recommendation-index, .recommendation-duration, .recommendation-count { color: rgba(75, 85, 99, .52); font: .72rem ui-monospace, SFMono-Regular, Menlo, monospace; text-align: center; }
.recommendation-song-cover, .recommendation-playlist-cover { display: flex; flex-shrink: 0; align-items: center; justify-content: center; overflow: hidden; border-radius: 9px; background: rgba(0, 0, 0, .08); }
.recommendation-song-cover { width: 44px; height: 44px; }
.recommendation-playlist-cover { width: 48px; height: 48px; border-radius: 10px; }
.recommendation-cover-fallback { display: flex; width: 100%; height: 100%; align-items: center; justify-content: center; background: linear-gradient(135deg, rgba(236, 65, 65, .25), rgba(247, 178, 103, .24)); color: var(--accent-color); font-size: 1.4rem; font-weight: 900; }
.recommendation-song-main { display: flex; min-width: 0; flex-direction: column; gap: 2px; }
.recommendation-title, .recommendation-subtitle, .recommendation-album, .recommendation-source { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.recommendation-title { color: rgb(31 41 55); font-size: .86rem; font-weight: 650; line-height: 1.3; }
.recommendation-subtitle, .recommendation-album { color: rgba(75, 85, 99, .66); font-size: .74rem; line-height: 1.25; }
.recommendation-source { justify-self: start; max-width: 120px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; border: 1px solid color-mix(in srgb, var(--accent-color) 22%, transparent); border-radius: 999px; padding: 2px 7px; color: var(--accent-color); font-size: .66rem; font-weight: 650; }
.recommendation-duration, .recommendation-count { justify-self: end; white-space: nowrap; }
.recommendation-action { display: flex; align-items: center; justify-content: center; color: var(--accent-color); opacity: 0; transform: translateX(-3px); transition: 160ms ease; }
.recommendation-song-row:hover .recommendation-action, .recommendation-playlist-row:hover .recommendation-action { opacity: 1; transform: translateX(0); }
.recommendations-empty { display: flex; min-height: 180px; align-items: center; justify-content: center; padding: 24px; color: rgba(75, 85, 99, .65); font-size: .86rem; text-align: center; }
html.dark .recommendations-back, html.dark .recommendations-refresh, html.dark .recommendations-heading p { color: rgba(255, 255, 255, .58); }
html.dark .recommendations-heading h1, html.dark .recommendation-title { color: rgba(255, 255, 255, .92); }
html.dark .recommendations-tabs { border-color: rgba(255, 255, 255, .08); }
html.dark .recommendations-tabs button { color: rgba(255, 255, 255, .58); }
html.dark .recommendation-subtitle, html.dark .recommendation-album { color: rgba(255, 255, 255, .52); }
html.dark .recommendation-index, html.dark .recommendation-duration, html.dark .recommendation-count { color: rgba(255, 255, 255, .42); }
html.dark .recommendation-song-row:hover, html.dark .recommendation-playlist-row:hover { background: rgba(255, 255, 255, .08); }
@media (max-width: 900px) {
  .recommendation-song-row { grid-template-columns: 24px 40px minmax(0, 1fr) 58px 24px; gap: 9px; }
  .recommendation-playlist-row { grid-template-columns: 24px 44px minmax(0, 1fr) 64px 24px; gap: 9px; }
  .recommendation-song-cover { width: 40px; height: 40px; }
  .recommendation-playlist-cover { width: 44px; height: 44px; }
  .recommendation-album, .recommendation-song-row .recommendation-source, .recommendation-playlist-row .recommendation-source { display: none; }
}
@media (max-width: 560px) {
  .recommendations-header { grid-template-columns: auto 1fr auto; }
  .recommendations-heading { text-align: left; }
  .recommendation-song-row { grid-template-columns: 40px minmax(0, 1fr) 52px 20px; gap: 10px; padding-left: 6px; }
  .recommendation-playlist-row { grid-template-columns: 44px minmax(0, 1fr) 56px 20px; gap: 10px; padding-left: 6px; }
  .recommendation-index { display: none; }
}
</style>
