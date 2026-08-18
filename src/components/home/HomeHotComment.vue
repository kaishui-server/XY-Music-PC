<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { RefreshCw, Search } from 'lucide-vue-next';
import { useRouter } from 'vue-router';

import { useNavigationStore } from '../../shared/stores/navigation';
import {
  formatHotCommentForDisplay,
} from '../../services/hotCommentService';
import { useHotCommentRecommendation } from '../../composables/useHotCommentRecommendation';
import { useAppLanguage } from '../../i18n';

const router = useRouter();
const navigationStore = useNavigationStore();
const { t } = useAppLanguage();

const {
  hotComment,
  isLoading,
  errorMessage,
  ensureHotCommentRecommendation,
  refreshHotComment,
} = useHotCommentRecommendation();

const displayedComment = computed(() => (
  hotComment.value ? formatHotCommentForDisplay(hotComment.value.comment) : ''
));

const searchSong = () => {
  const songTitle = hotComment.value?.songTitle?.trim();
  if (!songTitle) return;

  navigationStore.setSearch(songTitle);
  navigationStore.addSearchHistory(songTitle);
  void router.push('/search');
};

onMounted(() => {
  void ensureHotCommentRecommendation();
});
</script>

<template>
  <section class="home-hot-comment animate-fade-in-up px-[clamp(1rem,2.5vw,3rem)] py-[clamp(0.75rem,1.4vw,1.25rem)]">
    <div class="mb-3 flex items-center justify-between gap-4">
      <div class="text-base font-semibold tracking-[0.22em] text-gray-900 dark:text-white">
        <span>{{ t('home.hotComments') }}</span>
      </div>
      <button
        type="button"
        class="flex items-center gap-1.5 text-xs font-medium text-gray-500 transition hover:text-accent disabled:cursor-wait disabled:opacity-50 dark:text-white/50 dark:hover:text-accent"
        :disabled="isLoading"
        @click="refreshHotComment"
      >
        <RefreshCw class="h-3.5 w-3.5" :class="isLoading ? 'animate-spin' : ''" aria-hidden="true" />
        {{ t('home.anotherComment') }}
      </button>
    </div>

    <div v-if="isLoading && !hotComment" class="space-y-3 py-3" :aria-label="t('home.loadingHotComment')">
      <div class="h-5 w-full animate-pulse rounded bg-black/5 dark:bg-white/10"></div>
      <div class="h-5 w-4/5 animate-pulse rounded bg-black/5 dark:bg-white/10"></div>
      <div class="h-4 w-28 animate-pulse rounded bg-black/5 dark:bg-white/10"></div>
    </div>

    <div v-else-if="errorMessage && !hotComment" class="py-3 text-sm text-gray-500 dark:text-white/50">
      <p>{{ errorMessage }}</p>
      <button type="button" class="mt-2 font-medium text-accent hover:text-accent-hover" @click="refreshHotComment">
        {{ t('home.clickToRetry') }}
      </button>
    </div>

    <button
      v-else-if="hotComment"
      type="button"
      class="group block w-full text-left"
      :class="hotComment.songTitle ? 'cursor-pointer' : 'cursor-default'"
      :disabled="!hotComment.songTitle"
      :title="hotComment.songTitle ? t('home.searchSong', { title: hotComment.songTitle }) : undefined"
      @click="searchSong"
    >
      <p class="text-[clamp(1.05rem,1.8vw,1.35rem)] font-medium leading-relaxed text-gray-800 transition-colors group-hover:text-accent dark:text-white/85 dark:group-hover:text-accent">
        {{ displayedComment }}
      </p>
      <div v-if="hotComment.songTitle" class="mt-3 flex items-center gap-2 text-sm text-gray-500 dark:text-white/50">
        <span>《{{ hotComment.songTitle }}》</span>
        <span class="flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
          <Search class="h-3.5 w-3.5" aria-hidden="true" />
          {{ t('home.clickToSearch') }}
        </span>
      </div>
    </button>
  </section>
</template>
