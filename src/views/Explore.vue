<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';

import AppCoverImage from '../components/common/AppCoverImage.vue';
import { useCollectionsStore } from '../features/collections/store';
import { useLibraryStore } from '../features/library/store';
import { usePlaybackController } from '../features/playback/usePlaybackController';
import { useOnlineDetailStore } from '../features/onlineDetail/store';
import { useNavigationStore } from '../shared/stores/navigation';
import { pluginsVersion } from '../services/pluginEngine';
import {
  loadExploreData,
  pluginResultToSong,
  type ExploreData,
  type ExplorePlaylist,
} from '../services/exploreService';
import type { PluginSearchResult, Song } from '../types';
import { getDisplayCoverUrl } from '../utils/coverProxy';

const router = useRouter();
const navigationStore = useNavigationStore();
const collectionsStore = useCollectionsStore();
const libraryStore = useLibraryStore();
const { playSong } = usePlaybackController();
const onlineDetailStore = useOnlineDetailStore();

const data = ref<ExploreData>({ songs: [], playlists: [], charts: [] });
const loading = ref(false);
const activeRecommendationTab = ref<'songs' | 'playlists'>('songs');
const activeChartPluginId = ref('');
const exploreSearchQuery = ref('');
const exploreSearchInputRef = ref<HTMLInputElement | null>(null);

let loadSequence = 0;
let disposed = false;
let refreshTimer: ReturnType<typeof setTimeout> | null = null;

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
    if (playlist.songs?.length) {
      playlist.songs.forEach(add);
    } else {
      playlist.songPaths.forEach(path => add(libraryStore.getSongByPath(path)));
    }
    if (result.length >= 80) break;
  }
  return result.slice(0, 80);
});

const chartGroups = computed(() => {
  const groups = new Map<string, { name: string; items: ExplorePlaylist[] }>();
  for (const item of data.value.charts) {
    const existing = groups.get(item.plugin.id);
    if (existing) {
      existing.items.push(item);
    } else {
      groups.set(item.plugin.id, {
        name: item.plugin.name.trim() || item.plugin.id,
        items: [item],
      });
    }
  }
  return [...groups.entries()].map(([id, group]) => ({ id, ...group }));
});

const activeCharts = computed(() => {
  const first = chartGroups.value[0];
  const selected = chartGroups.value.find(group => group.id === activeChartPluginId.value);
  return selected?.items ?? first?.items ?? [];
});

const visibleSongs = computed(() => data.value.songs.slice(0, 6));
const visiblePlaylists = computed(() => data.value.playlists.slice(0, 6));

const coverUrl = (value: string) => value ? getDisplayCoverUrl(value) : '';

const formatRecommendationDuration = (durationMs: number) => {
  const totalSeconds = Math.floor((Number(durationMs) || 0) / 1000);
  if (totalSeconds <= 0) return '--:--';
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
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
    if (!disposed && sequence === loadSequence) {
      // 刷新期间继续展示旧内容；插件请求失败或返回空结果时也不要清空已有推荐。
      if (hasExploreData(next) || !hasExploreData(data.value)) {
        data.value = {
          songs: next.songs.length > 0 ? next.songs : data.value.songs,
          playlists: next.playlists.length > 0 ? next.playlists : data.value.playlists,
          charts: next.charts.length > 0 ? next.charts : data.value.charts,
        };
        if (!activeChartPluginId.value || !next.charts.some(item => item.plugin.id === activeChartPluginId.value)) {
          activeChartPluginId.value = next.charts[0]?.plugin.id ?? data.value.charts[0]?.plugin.id ?? '';
        }
      }
    }
  } finally {
    if (!disposed && sequence === loadSequence) loading.value = false;
  }
};

const scheduleRefresh = () => {
  if (refreshTimer) clearTimeout(refreshTimer);
  refreshTimer = setTimeout(() => {
    refreshTimer = null;
    void refresh(false);
  }, 260);
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

const focusSearch = () => {
  exploreSearchInputRef.value?.focus();
};

const submitSearch = () => {
  const query = exploreSearchQuery.value.trim();
  if (!query) {
    focusSearch();
    return;
  }

  navigationStore.setSearch(query);
  navigationStore.addSearchHistory(query);
  void router.push('/search');
};

const openAllRecommendations = () => {
  void router.push({
    path: '/explore/recommendations',
    query: { tab: activeRecommendationTab.value },
  });
};

watch(pluginsVersion, () => {
  // 插件刚加载完成且页面尚无内容时，允许自动完成首次生成；已有缓存时不重复请求。
  if (!hasExploreData(data.value)) scheduleRefresh();
});

onMounted(() => {
  void refresh();
});

onBeforeUnmount(() => {
  disposed = true;
  loadSequence += 1;
  if (refreshTimer) clearTimeout(refreshTimer);
});
</script>

<template>
  <!-- MainShell 使用 out-in 页面过渡，路由组件必须保持单一元素根节点。 -->
  <div class="explore-route-root h-full min-h-0">
    <div class="explore-page h-full overflow-y-auto custom-scrollbar px-6 pb-12 pt-5">
    <section class="explore-hero rounded-3xl px-8 py-8 md:px-10 md:py-10">
      <p class="text-xs font-semibold uppercase tracking-[0.28em] text-accent/80">Discover your sound</p>
      <h1 class="mt-3 text-4xl font-black tracking-[-0.05em] text-gray-900 dark:text-white">XY Music</h1>
      <p class="mt-2 text-base text-gray-600 dark:text-white/65">Music is part of my life.</p>
      <div class="explore-search-button mt-7" @click="focusSearch">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
          <path stroke-linecap="round" stroke-linejoin="round" d="m21 21-4.35-4.35m2.1-5.4a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z" />
        </svg>
        <input
          ref="exploreSearchInputRef"
          v-model="exploreSearchQuery"
          type="text"
          class="explore-search-input"
          placeholder="搜索网络歌曲、歌手、专辑或歌单"
          aria-label="搜索网络歌曲、歌手、专辑或歌单"
          @click.stop
          @keydown.enter.prevent="submitSearch"
          @blur="submitSearch"
        />
        <span class="ml-auto shrink-0 text-xs text-gray-400 dark:text-white/40">回车或失焦搜索</span>
      </div>
    </section>

    <section class="explore-panel mt-5">
      <div class="explore-section-header">
        <div>
          <h2>猜你想听</h2>
          <p>根据最近播放、收藏和歌单，为你寻找新的声音</p>
        </div>
        <button type="button" class="explore-icon-button" title="刷新推荐" :disabled="loading" @click="refresh(true)">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" :class="{ 'animate-spin': loading }" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M20 11a8 8 0 1 0 2 5m-2-5v-5m0 5h-5" />
          </svg>
        </button>
      </div>

      <div class="mt-4 flex items-center gap-2 border-b border-black/5 dark:border-white/8">
        <button type="button" class="explore-tab" :class="{ active: activeRecommendationTab === 'songs' }" @click="activeRecommendationTab = 'songs'">歌曲</button>
        <button type="button" class="explore-tab" :class="{ active: activeRecommendationTab === 'playlists' }" @click="activeRecommendationTab = 'playlists'">歌单</button>
      </div>

      <div v-if="loading && data.songs.length === 0 && data.playlists.length === 0" class="explore-empty">正在生成推荐…</div>
      <div v-else-if="activeRecommendationTab === 'songs' && visibleSongs.length" class="explore-song-list">
        <button v-for="(item, index) in visibleSongs" :key="`${item.pluginId}-${item.id}-${index}`" type="button" class="explore-song-row" @click="playRecommendation(item)">
          <span class="explore-song-index">{{ String(index + 1).padStart(2, '0') }}</span>
          <AppCoverImage :src="coverUrl(item.coverUrl)" class="explore-song-cover" loading="lazy" decoding="async">
            <span class="explore-cover-fallback text-base">{{ item.title.slice(0, 1) }}</span>
          </AppCoverImage>
          <span class="explore-song-main">
            <span class="explore-song-title">{{ item.title }}</span>
            <span class="explore-song-artist">{{ item.artist || '未知歌手' }}</span>
          </span>
          <span class="explore-song-album">{{ item.album || '未知专辑' }}</span>
          <span class="explore-song-source">{{ item.platform }}</span>
          <span class="explore-song-duration">{{ formatRecommendationDuration(item.duration) }}</span>
          <span class="explore-song-play" aria-hidden="true">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor"><path d="M6.3 3.8a1 1 0 0 1 1.55-.83l7.3 5.2a1 1 0 0 1 0 1.66l-7.3 5.2A1 1 0 0 1 6.3 14.2V3.8Z" /></svg>
          </span>
        </button>
      </div>
      <div v-else-if="activeRecommendationTab === 'playlists' && visiblePlaylists.length" class="explore-playlist-list">
        <button v-for="(item, index) in visiblePlaylists" :key="`${item.plugin.id}-${item.result.id}-${index}`" type="button" class="explore-playlist-row" @click="openPlaylist(item)">
          <span class="explore-playlist-index">{{ String(index + 1).padStart(2, '0') }}</span>
          <AppCoverImage :src="coverUrl(item.result.coverUrl)" class="explore-playlist-cover" loading="lazy" decoding="async">
            <span class="explore-cover-fallback text-base">{{ item.result.title.slice(0, 1) }}</span>
          </AppCoverImage>
          <span class="explore-playlist-main">
            <span class="explore-playlist-title">{{ item.result.title }}</span>
            <span class="explore-playlist-subtitle">{{ item.result.artist || '推荐歌单' }}</span>
          </span>
          <span class="explore-playlist-source">{{ item.plugin.name }}</span>
          <span class="explore-playlist-count">{{ item.result.trackCount ? `${item.result.trackCount} 首歌曲` : '歌单' }}</span>
          <span class="explore-playlist-arrow" aria-hidden="true">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="m9 18 6-6-6-6" /></svg>
          </span>
        </button>
      </div>
      <div v-else class="explore-empty">
        <span>{{ seedSongs.length ? '暂无可用推荐，请稍后刷新' : '多播放、收藏几首歌曲后，这里会根据你的偏好生成推荐' }}</span>
      </div>
      <div v-if="(activeRecommendationTab === 'songs' ? data.songs.length : data.playlists.length) > 6" class="explore-more-footer">
        <button type="button" class="explore-more-button" @click="openAllRecommendations">
          查看全部{{ activeRecommendationTab === 'songs' ? '歌曲' : '歌单' }}
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="m9 18 6-6 6 6" /></svg>
        </button>
      </div>
    </section>

    <section class="explore-panel mt-5">
      <div class="explore-section-header">
        <div>
          <h2>热门榜单</h2>
          <p>浏览已启用音乐插件提供的榜单</p>
        </div>
        <button type="button" class="explore-icon-button" title="刷新热门榜单" :disabled="loading" @click="refresh(true)">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" :class="{ 'animate-spin': loading }" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M20 11a8 8 0 1 0 2 5m-2-5v-5m0 5h-5" />
          </svg>
        </button>
      </div>
      <div v-if="chartGroups.length" class="mt-4">
        <div class="flex gap-2 overflow-x-auto pb-2 custom-scrollbar">
          <button v-for="group in chartGroups" :key="group.id" type="button" class="explore-source-tab" :class="{ active: (activeChartPluginId || chartGroups[0].id) === group.id }" @click="activeChartPluginId = group.id">
            {{ group.name }}
          </button>
        </div>
        <div class="mt-2 divide-y divide-black/5 dark:divide-white/8">
          <button v-for="(item, index) in activeCharts" :key="`${item.plugin.id}-${item.result.id}-${index}`" type="button" class="explore-chart-row" @click="openPlaylist(item)">
            <span class="w-7 text-center text-sm font-bold text-accent/75">{{ String(index + 1).padStart(2, '0') }}</span>
            <AppCoverImage :src="coverUrl(item.result.coverUrl)" class="h-12 w-12 shrink-0 rounded-lg" loading="lazy" decoding="async">
              <span class="explore-cover-fallback text-base">{{ item.result.title.slice(0, 1) }}</span>
            </AppCoverImage>
            <span class="min-w-0 flex-1 text-left">
              <span class="block truncate text-sm font-semibold text-gray-900 dark:text-white">{{ item.result.title }}</span>
              <span class="block truncate text-xs text-gray-500 dark:text-white/45">{{ item.result.artist || item.plugin.name }}<template v-if="item.result.trackCount"> · {{ item.result.trackCount }} 首</template></span>
            </span>
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 text-gray-400 dark:text-white/35" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="m9 18 6-6-6-6" /></svg>
          </button>
        </div>
      </div>
      <div v-else class="explore-empty">暂无可用的热门榜单，请检查是否已安装并启用 MusicFree 插件</div>
    </section>
    </div>

  </div>
</template>

<style scoped>
.explore-page {
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.10), transparent 35%);
}

.explore-hero,
.explore-panel {
  border: 1px solid rgba(255, 255, 255, 0.34);
  background: rgba(255, 255, 255, 0.38);
  box-shadow: 0 20px 50px rgba(15, 23, 42, 0.08);
  backdrop-filter: blur(22px) saturate(125%);
}

.explore-hero {
  background: radial-gradient(circle at 85% 20%, rgba(255, 145, 105, 0.22), transparent 35%), rgba(255, 255, 255, 0.38);
}

.explore-panel {
  padding: 18px 20px 20px;
  border-radius: 24px;
}

.explore-section-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.explore-section-header h2 {
  color: rgb(17 24 39);
  font-size: 1.25rem;
  font-weight: 800;
  letter-spacing: -0.025em;
}

.explore-section-header p {
  margin-top: 4px;
  color: rgba(75, 85, 99, 0.78);
  font-size: 0.78rem;
}

.explore-search-button {
  display: flex;
  width: min(100%, 620px);
  align-items: center;
  gap: 12px;
  border: 1px solid rgba(255, 255, 255, 0.48);
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.72);
  padding: 12px 14px;
  color: rgba(55, 65, 81, 0.78);
  font-size: 0.86rem;
  text-align: left;
  transition: 160ms ease;
}

.explore-search-button:hover { background: rgba(255, 255, 255, 0.92); transform: translateY(-1px); }
.explore-search-button:focus-within { border-color: color-mix(in srgb, var(--accent-color) 45%, transparent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent-color) 12%, transparent); }
.explore-search-input { min-width: 0; flex: 1; border: 0; outline: 0; background: transparent; color: rgb(31 41 55); font: inherit; }
.explore-search-input::placeholder { color: rgba(75, 85, 99, 0.66); }
.explore-icon-button,
.explore-close-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  color: rgba(75, 85, 99, 0.72);
  transition: 160ms ease;
}
.explore-icon-button { height: 32px; width: 32px; }
.explore-icon-button:hover { background: rgba(0, 0, 0, 0.06); color: var(--accent-color); }
.explore-icon-button:disabled { cursor: wait; opacity: 0.55; }
.explore-tab,
.explore-source-tab {
  border-bottom: 2px solid transparent;
  color: rgba(75, 85, 99, 0.68);
  font-size: 0.82rem;
  font-weight: 650;
  transition: 160ms ease;
}
.explore-tab { padding: 8px 12px 10px; }
.explore-tab.active { border-color: var(--accent-color); color: var(--accent-color); }
.explore-tab:hover,
.explore-source-tab:hover { color: var(--accent-color); }
.explore-source-tab { border: 1px solid rgba(107, 114, 128, 0.18); border-radius: 999px; padding: 7px 13px; white-space: nowrap; }
.explore-source-tab.active { border-color: color-mix(in srgb, var(--accent-color) 40%, transparent); background: color-mix(in srgb, var(--accent-color) 10%, transparent); color: var(--accent-color); }

.explore-song-list { display: grid; gap: 4px; padding-top: 14px; }
.explore-song-row {
  display: grid;
  width: 100%;
  min-width: 0;
  grid-template-columns: 28px 44px minmax(180px, 1.4fr) minmax(120px, 1fr) 92px 54px 24px;
  align-items: center;
  gap: 12px;
  border-radius: 12px;
  padding: 6px 10px 6px 4px;
  text-align: left;
  transition: 160ms ease;
}
.explore-song-row:hover { background: rgba(0, 0, 0, 0.055); }
.explore-song-index,
.explore-song-duration { color: rgba(75, 85, 99, 0.52); font-size: 0.72rem; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.explore-song-index { text-align: center; }
.explore-song-cover { display: flex; height: 44px; width: 44px; flex-shrink: 0; align-items: center; justify-content: center; overflow: hidden; border-radius: 9px; background: rgba(0, 0, 0, 0.08); }
.explore-song-main { display: flex; min-width: 0; flex-direction: column; justify-content: center; gap: 2px; }
.explore-song-title,
.explore-song-artist,
.explore-song-album,
.explore-song-source { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.explore-song-title { color: rgb(31 41 55); font-size: 0.86rem; font-weight: 650; line-height: 1.3; }
.explore-song-artist,
.explore-song-album { color: rgba(75, 85, 99, 0.66); font-size: 0.74rem; line-height: 1.25; }
.explore-song-source { justify-self: start; max-width: 92px; border: 1px solid color-mix(in srgb, var(--accent-color) 22%, transparent); border-radius: 999px; padding: 2px 7px; color: var(--accent-color); font-size: 0.66rem; font-weight: 650; }
.explore-song-duration { justify-self: end; white-space: nowrap; }
.explore-song-play { display: flex; align-items: center; justify-content: center; color: var(--accent-color); opacity: 0; transform: translateX(-3px); transition: 160ms ease; }
.explore-song-row:hover .explore-song-play { opacity: 1; transform: translateX(0); }
.explore-playlist-list { display: grid; gap: 4px; padding-top: 14px; }
.explore-playlist-row {
  display: grid;
  width: 100%;
  min-width: 0;
  grid-template-columns: 28px 48px minmax(180px, 1.5fr) minmax(120px, 1fr) 92px 24px;
  align-items: center;
  gap: 12px;
  border-radius: 12px;
  padding: 6px 10px 6px 4px;
  text-align: left;
  transition: 160ms ease;
}
.explore-playlist-row:hover { background: rgba(0, 0, 0, 0.055); }
.explore-playlist-index,
.explore-playlist-count { color: rgba(75, 85, 99, 0.52); font-size: 0.72rem; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.explore-playlist-index { text-align: center; }
.explore-playlist-cover { display: flex; height: 48px; width: 48px; flex-shrink: 0; align-items: center; justify-content: center; overflow: hidden; border-radius: 10px; background: rgba(0, 0, 0, 0.08); }
.explore-playlist-main { display: flex; min-width: 0; flex-direction: column; justify-content: center; gap: 2px; }
.explore-playlist-title,
.explore-playlist-subtitle,
.explore-playlist-source,
.explore-playlist-count { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.explore-playlist-title { color: rgb(31 41 55); font-size: 0.86rem; font-weight: 650; line-height: 1.3; }
.explore-playlist-subtitle { color: rgba(75, 85, 99, 0.66); font-size: 0.74rem; line-height: 1.25; }
.explore-playlist-source { justify-self: start; max-width: 120px; border: 1px solid color-mix(in srgb, var(--accent-color) 22%, transparent); border-radius: 999px; padding: 2px 7px; color: var(--accent-color); font-size: 0.66rem; font-weight: 650; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.explore-playlist-count { justify-self: end; white-space: nowrap; }
.explore-playlist-arrow { display: flex; align-items: center; justify-content: center; color: var(--accent-color); opacity: 0; transform: translateX(-3px); transition: 160ms ease; }
.explore-playlist-row:hover .explore-playlist-arrow { opacity: 1; transform: translateX(0); }
.explore-more-footer { display: flex; justify-content: center; padding-top: 14px; }
.explore-more-button { display: inline-flex; align-items: center; gap: 5px; border-radius: 999px; padding: 7px 13px; color: var(--accent-color); font-size: 0.78rem; font-weight: 650; transition: 160ms ease; }
.explore-more-button:hover { background: color-mix(in srgb, var(--accent-color) 9%, transparent); }
.explore-cover { display: flex; align-items: center; justify-content: center; overflow: hidden; border-radius: 14px; background: rgba(0, 0, 0, 0.08); }
.explore-cover-fallback { display: flex; height: 100%; width: 100%; align-items: center; justify-content: center; background: linear-gradient(135deg, rgba(236, 65, 65, 0.25), rgba(247, 178, 103, 0.24)); color: var(--accent-color); font-size: 2rem; font-weight: 900; }
.explore-empty { display: flex; min-height: 130px; align-items: center; justify-content: center; padding: 24px; color: rgba(75, 85, 99, 0.65); font-size: 0.86rem; text-align: center; }
.explore-chart-row { display: flex; width: 100%; align-items: center; gap: 12px; border-radius: 12px; padding: 9px 8px; transition: 160ms ease; }
.explore-chart-row:hover { background: rgba(0, 0, 0, 0.05); }

html.dark .explore-hero,
html.dark .explore-panel { border-color: rgba(255, 255, 255, 0.10); background-color: rgba(10, 10, 14, 0.34); box-shadow: 0 20px 50px rgba(0, 0, 0, 0.16); }
html.dark .explore-section-header h2 { color: white; }
html.dark .explore-section-header p,
html.dark .explore-search-button { color: rgba(255, 255, 255, 0.58); }
html.dark .explore-search-button { border-color: rgba(255, 255, 255, 0.12); background: rgba(255, 255, 255, 0.08); }
html.dark .explore-search-button:hover { background: rgba(255, 255, 255, 0.13); }
html.dark .explore-icon-button:hover,
html.dark .explore-song-row:hover,
html.dark .explore-chart-row:hover { background: rgba(255, 255, 255, 0.08); }
html.dark .explore-song-title { color: rgba(255, 255, 255, 0.92); }
html.dark .explore-song-artist,
html.dark .explore-song-album,
html.dark .explore-playlist-subtitle { color: rgba(255, 255, 255, 0.52); }
html.dark .explore-song-index,
html.dark .explore-song-duration,
html.dark .explore-playlist-index,
html.dark .explore-playlist-count { color: rgba(255, 255, 255, 0.42); }
html.dark .explore-playlist-title { color: rgba(255, 255, 255, 0.92); }

@media (max-width: 900px) {
  .explore-song-row { grid-template-columns: 24px 40px minmax(0, 1fr) 58px 24px; gap: 9px; padding-right: 6px; }
  .explore-song-cover { height: 40px; width: 40px; }
  .explore-song-album,
  .explore-song-source { display: none; }
  .explore-playlist-row { grid-template-columns: 24px 44px minmax(0, 1fr) 64px 24px; gap: 9px; padding-right: 6px; }
  .explore-playlist-cover { height: 44px; width: 44px; }
  .explore-playlist-source { display: none; }
}

@media (max-width: 560px) {
  .explore-song-index { display: none; }
  .explore-song-row { grid-template-columns: 40px minmax(0, 1fr) 52px 20px; gap: 10px; padding-left: 6px; }
  .explore-playlist-index { display: none; }
  .explore-playlist-row { grid-template-columns: 44px minmax(0, 1fr) 56px 20px; gap: 10px; padding-left: 6px; }
}
</style>
