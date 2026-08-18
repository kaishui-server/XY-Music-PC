import { ref } from 'vue';

import { fetchHotComment, type HotComment } from '../services/hotCommentService';

export const HOT_COMMENT_AUTO_REFRESH_MS = 3 * 60 * 1000;

// 模块级单例状态：页面切换导致组件重新挂载时，继续显示同一句热评。
const hotComment = ref<HotComment | null>(null);
const isLoading = ref(false);
const errorMessage = ref('');

let initialized = false;
let refreshTimer: ReturnType<typeof setTimeout> | null = null;
let pendingRefresh: Promise<void> | null = null;

const clearRefreshTimer = () => {
  if (!refreshTimer) return;
  clearTimeout(refreshTimer);
  refreshTimer = null;
};

const scheduleAutoRefresh = () => {
  clearRefreshTimer();
  refreshTimer = setTimeout(() => {
    refreshTimer = null;
    void refreshHotComment();
  }, HOT_COMMENT_AUTO_REFRESH_MS);
};

/**
 * 更换一条热评，并从本次更换完成后重新计算三分钟。
 * 自动刷新和“换一下”共用这一入口，避免产生并发请求。
 */
export const refreshHotComment = (): Promise<void> => {
  if (pendingRefresh) return pendingRefresh;

  clearRefreshTimer();
  isLoading.value = true;
  errorMessage.value = '';

  pendingRefresh = (async () => {
    try {
      hotComment.value = await fetchHotComment();
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '热评加载失败';
    } finally {
      isLoading.value = false;
      pendingRefresh = null;
      scheduleAutoRefresh();
    }
  })();

  return pendingRefresh;
};

/** 仅在应用内第一次显示热评模块时加载，后续页面切换不会重新请求。 */
export const ensureHotCommentRecommendation = (): Promise<void> => {
  if (initialized) return pendingRefresh ?? Promise.resolve();
  initialized = true;
  return refreshHotComment();
};

export function useHotCommentRecommendation() {
  return {
    hotComment,
    isLoading,
    errorMessage,
    ensureHotCommentRecommendation,
    refreshHotComment,
  };
}

if (import.meta.hot) {
  import.meta.hot.dispose(clearRefreshTimer);
}
