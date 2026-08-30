<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue';
import { useRouter } from 'vue-router';

import AppCoverImage from '../components/common/AppCoverImage.vue';
import { useCoverCache } from '../composables/useCoverCache';
import { useHomeNavigation } from '../composables/useHomeNavigation';
import { useSidebarPlaylistCovers } from '../composables/useSidebarPlaylistCovers';
import { useToast } from '../composables/toast';
import { useLibraryCollections } from '../features/collections/useLibraryCollections';

const PlaylistModal = defineAsyncComponent(() => import('../components/overlays/PlaylistModal.vue'));

const router = useRouter();
const { openHomePlaylist } = useHomeNavigation(router);
const { playlists, createPlaylist } = useLibraryCollections();
const { loadCover, primeCoverPath } = useCoverCache();
const { showToast } = useToast();

const showPlaylistModal = ref(false);
const searchQuery = ref('');
// 复用侧边栏的封面缓存逻辑，保证在线歌单、本地歌单和自定义封面显示一致。
const { playlistCoverCacheVersion, getPlaylistCover } = useSidebarPlaylistCovers({
  playlists,
  loadCover,
  primeCoverPath,
});
const playlistCount = computed(() => playlists.value.length);
const filteredPlaylists = computed(() => {
  const query = searchQuery.value.trim().toLocaleLowerCase();
  if (!query) return playlists.value;
  return playlists.value.filter(playlist => playlist.name.toLocaleLowerCase().includes(query));
});

const openPlaylist = (id: string) => {
  void openHomePlaylist(id);
};

const confirmCreatePlaylist = (name: string) => {
  const id = createPlaylist(name);
  if (id) {
    showPlaylistModal.value = false;
    showToast('歌单创建成功', 'success');
  }
};

const coverFor = (id: string) => {
  void playlistCoverCacheVersion.value;
  return getPlaylistCover(id);
};
</script>

<template>
  <div class="h-full overflow-y-auto custom-scrollbar px-[clamp(1.5rem,3vw,4rem)] pb-16 pt-8">
    <header class="mx-auto flex max-w-6xl flex-wrap items-end justify-between gap-4 border-b border-black/8 pb-5 dark:border-white/10">
      <div>
        <p class="text-xs font-semibold uppercase tracking-[0.24em] text-accent">Library</p>
        <h1 class="mt-2 text-2xl font-bold tracking-tight text-gray-900 dark:text-white">我的歌单</h1>
        <p class="mt-1 text-sm text-gray-500 dark:text-white/55">
          {{ searchQuery.trim() ? `找到 ${filteredPlaylists.length} 个匹配歌单` : `管理和查看你的全部歌单，共 ${playlistCount} 个` }}
        </p>
      </div>
      <div class="flex min-w-0 flex-wrap items-center justify-end gap-2">
        <label class="relative flex h-9 w-[min(18rem,70vw)] items-center">
          <svg xmlns="http://www.w3.org/2000/svg" class="pointer-events-none absolute left-3 h-4 w-4 text-gray-400 dark:text-white/40" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="7" /><path stroke-linecap="round" d="m20 20-4-4" /></svg>
          <input v-model="searchQuery" type="search" placeholder="搜索歌单" class="h-full w-full rounded-full border border-black/10 bg-white/50 pl-9 pr-3 text-xs text-gray-800 outline-none transition placeholder:text-gray-400 focus:border-accent/50 focus:bg-white/80 dark:border-white/10 dark:bg-white/5 dark:text-white/90 dark:placeholder:text-white/35 dark:focus:bg-white/10" />
        </label>
        <button
          type="button"
          class="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-full bg-accent px-4 text-xs font-semibold text-white shadow-sm transition hover:bg-accent-hover active:scale-95"
          @click="showPlaylistModal = true"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" /></svg>
          新建歌单
        </button>
      </div>
    </header>

    <section v-if="filteredPlaylists.length > 0" class="mx-auto flex max-w-6xl flex-col gap-2 pt-6">
      <button
        v-for="playlist in filteredPlaylists"
        :key="playlist.id"
        type="button"
        class="group flex min-w-0 items-center gap-4 rounded-xl px-4 py-3 text-left transition hover:bg-black/5 dark:hover:bg-white/8"
        @click="openPlaylist(playlist.id)"
      >
        <div class="relative h-14 w-14 shrink-0 overflow-hidden rounded-lg bg-black/8 shadow-sm ring-1 ring-black/5 dark:bg-white/8 dark:ring-white/10">
          <AppCoverImage :src="coverFor(playlist.id)" class="h-full w-full object-cover transition duration-300 group-hover:scale-105" alt="歌单封面" loading="lazy" decoding="async">
            <div class="flex h-full w-full items-center justify-center bg-gradient-to-br from-accent/20 to-orange-300/20 text-2xl font-black text-accent">{{ playlist.name.slice(0, 1) }}</div>
          </AppCoverImage>
        </div>
        <div class="min-w-0 flex-1">
          <div class="truncate text-sm font-semibold text-gray-800 dark:text-white/90">{{ playlist.name }}</div>
          <div class="mt-1 text-xs text-gray-500 dark:text-white/50">{{ playlist.songPaths.length }} 首歌曲</div>
        </div>
        <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5 shrink-0 text-gray-400 transition group-hover:translate-x-1 group-hover:text-accent dark:text-white/35" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"><path stroke-linecap="round" stroke-linejoin="round" d="m9 18 6-6-6-6" /></svg>
      </button>
    </section>

    <section v-else class="mx-auto flex max-w-6xl flex-col items-center justify-center py-28 text-center">
      <div class="grid h-16 w-16 place-items-center rounded-2xl bg-accent/10 text-accent">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-8 w-8" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.7"><path stroke-linecap="round" stroke-linejoin="round" d="M9 18V5l12-3v13M9 18c0 1.105-1.343 2-3 2s-3-.895-3-2 1.343-2 3-2 3 .895 3 2zm12-3c0 1.105-1.343 2-3 2s-3-.895-3-2 1.343-2 3-2 3 .895 3 2zM9 9l12-3" /></svg>
      </div>
      <h2 class="mt-5 text-base font-semibold text-gray-800 dark:text-white/90">{{ searchQuery.trim() ? '没有找到匹配歌单' : '还没有歌单' }}</h2>
      <p class="mt-1 text-sm text-gray-500 dark:text-white/50">{{ searchQuery.trim() ? '换个关键词试试' : '创建一个歌单，把喜欢的歌曲整理起来' }}</p>
      <button v-if="searchQuery.trim()" type="button" class="mt-5 rounded-full border border-black/10 px-4 py-2 text-xs font-semibold text-gray-700 transition hover:border-accent/40 hover:text-accent dark:border-white/15 dark:text-white/75" @click="searchQuery = ''">清除搜索</button>
      <button v-else type="button" class="mt-5 rounded-full bg-accent px-4 py-2 text-xs font-semibold text-white transition hover:bg-accent-hover" @click="showPlaylistModal = true">创建第一个歌单</button>
    </section>

    <PlaylistModal
      v-if="showPlaylistModal"
      v-model:visible="showPlaylistModal"
      :playlists="playlists"
      mode="create"
      @create="confirmCreatePlaylist"
    />
  </div>
</template>
