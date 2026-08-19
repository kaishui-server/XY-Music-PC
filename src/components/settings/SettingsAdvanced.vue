<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { open, save as saveDialog } from '@tauri-apps/plugin-dialog';
import { ChevronLeft, ChevronRight, FileDown, FileUp, History, Loader2, Plus, Trash2, X } from 'lucide-vue-next';

import { useToast } from '../../composables/toast';
import { useCollectionsStore } from '../../features/collections/store';
import { useLibraryStore } from '../../features/library/store';
import { useSettings } from '../../features/settings/useSettings';
import {
  analyzeApplicationLogs,
  formatApplicationLogExport,
  useApplicationLogs,
} from '../../services/applicationLogger';
import { getStoredAuth } from '../../services/auth/authService';
import { getStoredPlugins } from '../../services/pluginEngine';
import { getMyFeedback, submitFeedback, type MyFeedbackItem } from '../../services/usageStats';
import {
  describeBackupVersion,
  preparePluginBackupFile,
  type PreparedPluginBackupImport,
} from '../../services/pluginBackupImport';
import {
  exportAppBackup,
  parseAppBackup,
  importAppBackup,
  type AppBackupImportResult,
} from '../../services/appBackup';
import { readPluginFile } from '../../services/tauri/pluginApi';
import { debugApi } from '../../services/tauri/debugApi';
import ConfirmModal from '../overlays/ConfirmModal.vue';
import BackupImportResultModal from './BackupImportResultModal.vue';
import AppBackupResultModal from './AppBackupResultModal.vue';
import LogExportActions from './LogExportActions.vue';

const { showToast } = useToast();
const { patchSettings, replaceSettings } = useSettings();
const { entries, clearLogs } = useApplicationLogs();
const collectionsStore = useCollectionsStore();
const libraryStore = useLibraryStore();
const showDeleteConfirmation = ref(false);

// 使用本地 ref 存储 entryCount，避免模板直接依赖 entries 响应式源
const entryCount = ref(entries.value.length);
watch(
  () => entries.value.length,
  () => { entryCount.value = entries.value.length; },
  { flush: 'post' },
);
const importingBackup = ref(false);
const backupImportResult = ref<PreparedPluginBackupImport | null>(null);
const createdPlaylistCount = ref(0);
const showBackupImportResult = ref(false);

// ─── 问题反馈 ───
const feedbackType = ref<'problem' | 'suggestion'>('problem');
const feedbackContent = ref('');
const submittingFeedback = ref(false);
const feedbackAuth = ref(getStoredAuth());
const attachErrorLogs = ref(false);
const attachAllLogs = ref(false);
const feedbackImages = ref<string[]>([]);
const feedbackImageInput = ref<HTMLInputElement | null>(null);
const compressingImage = ref(false);
const showMyFeedback = ref(false);
const myFeedbackList = ref<MyFeedbackItem[]>([]);
const loadingMyFeedback = ref(false);
const feedbackViewerVisible = ref(false);
const feedbackViewerImages = ref<string[]>([]);
const feedbackViewerIndex = ref(0);

// 登录态可能在设置页面打开后变化（如用户在其他窗口登录），聚焦时刷新一次
const refreshFeedbackAuth = () => {
  feedbackAuth.value = getStoredAuth();
};
const isFeedbackLoggedIn = computed(() => !!feedbackAuth.value?.user?.ciyuanxi_id);
const hasAnyLogs = computed(() => entries.value.length > 0);
const hasErrorLogs = computed(() => entries.value.some(entry => entry.level === 'error'));

const compressFeedbackImage = (file: File): Promise<string> => new Promise((resolve, reject) => {
  const reader = new FileReader();
  reader.onload = () => {
    const image = new Image();
    image.onload = () => {
      const scale = Math.min(1, 1600 / image.width);
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(image.width * scale));
      canvas.height = Math.max(1, Math.round(image.height * scale));
      canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
      resolve(canvas.toDataURL('image/jpeg', 0.82));
    };
    image.onerror = () => reject(new Error('图片加载失败'));
    image.src = String(reader.result);
  };
  reader.onerror = () => reject(new Error('文件读取失败'));
  reader.readAsDataURL(file);
});

const onFeedbackImageChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  const files = Array.from(input.files ?? []);
  input.value = '';
  if (feedbackImages.value.length + files.length > 6) return showToast('最多上传 6 张图片', 'error');
  compressingImage.value = true;
  try {
    for (const file of files) {
      if (file.size > 8 * 1024 * 1024) { showToast(`${file.name} 超过 8MB，已跳过`, 'error'); continue; }
      feedbackImages.value.push(await compressFeedbackImage(file));
    }
  } finally { compressingImage.value = false; }
};

const openMyFeedback = async () => {
  if (!isFeedbackLoggedIn.value) return showToast('请先登录后再查看反馈', 'error');
  showMyFeedback.value = true; loadingMyFeedback.value = true;
  try { myFeedbackList.value = await getMyFeedback(); }
  catch (error: any) { showToast(`获取反馈失败：${error?.message || error}`, 'error'); }
  finally { loadingMyFeedback.value = false; }
};

const openFeedbackViewer = (images: string[], index: number) => {
  feedbackViewerImages.value = images;
  feedbackViewerIndex.value = index;
  feedbackViewerVisible.value = true;
};

const closeFeedbackViewer = () => {
  feedbackViewerVisible.value = false;
  feedbackViewerImages.value = [];
};

const showPreviousFeedbackImage = () => {
  if (feedbackViewerImages.value.length === 0) return;
  feedbackViewerIndex.value = (
    feedbackViewerIndex.value - 1 + feedbackViewerImages.value.length
  ) % feedbackViewerImages.value.length;
};

const showNextFeedbackImage = () => {
  if (feedbackViewerImages.value.length === 0) return;
  feedbackViewerIndex.value = (
    feedbackViewerIndex.value + 1
  ) % feedbackViewerImages.value.length;
};

const getFeedbackImageStackStyle = (index: number, total: number): Record<string, string> => {
  if (total <= 1) return {};
  const offset = Math.min(index, 3) * 5;
  return {
    left: `${offset}px`,
    top: `${offset}px`,
    zIndex: String(total - index),
  };
};

const feedbackStatus = (status: string) => ({ pending: '待处理', processing: '处理中', resolved: '已处理', rejected: '已拒绝' }[status] || status);

// 应用备份导出/导入状态
const exportingAppBackup = ref(false);
const importingAppBackup = ref(false);
const appBackupResult = ref<AppBackupImportResult | null>(null);
const showAppBackupResult = ref(false);

const confirmDeleteLogs = () => {
  clearLogs();
  showDeleteConfirmation.value = false;
  showToast('日志已全部删除', 'success');
};

const importPluginBackup = async () => {
  if (importingBackup.value) return;

  try {
    const selected = await open({
      multiple: false,
      title: '选择备份文件',
      filters: [{ name: '音乐软件备份', extensions: ['json'] }],
    });
    if (typeof selected !== 'string') return;

    importingBackup.value = true;
    const prepared = await preparePluginBackupFile(selected, getStoredPlugins());
    let created = 0;

    for (const playlist of prepared.playlists) {
      if (playlist.songs.length === 0) continue;
      const paths = playlist.songs.map(song => song.path);
      libraryStore.setExtraSongs(playlist.songs);
      const playlistId = collectionsStore.createPlaylist(playlist.name, paths, playlist.songs);
      if (playlistId) created += 1;
    }

    backupImportResult.value = prepared;
    createdPlaylistCount.value = created;
    showBackupImportResult.value = true;

    const versionNote = describeBackupVersion(prepared);
    if (prepared.importedSongCount > 0) {
      showToast(
        `${versionNote}｜已导入 ${prepared.importedSongCount} 首歌曲，${prepared.failures.length} 首未导入`,
        'success',
      );
    } else {
      showToast(`${versionNote}｜没有歌曲可以导入，请查看缺失插件说明`, 'info');
    }
  } catch (error: any) {
    showToast(`导入备份失败：${error?.message || error}`, 'error');
  } finally {
    importingBackup.value = false;
  }
};

const submitUserFeedback = async () => {
  if (submittingFeedback.value) return;

  const title = feedbackType.value === 'suggestion' ? '功能建议' : '问题反馈';
  const content = feedbackContent.value.trim();

  if (!content) {
    showToast('请填写反馈内容', 'error');
    return;
  }
  if (content.length > 1000) {
    showToast('内容不能超过 1000 字', 'error');
    return;
  }

  submittingFeedback.value = true;
  try {
    let errorLogsText: string | undefined;
    let allLogsText: string | undefined;
    if (feedbackType.value === 'problem' && (attachErrorLogs.value || attachAllLogs.value)) {
      const analysis = analyzeApplicationLogs(entries.value);
      if (attachErrorLogs.value) {
        errorLogsText = formatApplicationLogExport(entries.value, 'error', analysis);
      }
      if (attachAllLogs.value) {
        allLogsText = formatApplicationLogExport(entries.value, 'all', analysis);
      }
    }
    await submitFeedback(title, content, { feedbackType: feedbackType.value, errorLogs: errorLogsText, allLogs: allLogsText, images: feedbackType.value === 'suggestion' ? [...feedbackImages.value] : undefined });
    showToast('反馈已提交，感谢您的支持', 'success');
    feedbackContent.value = '';
    feedbackImages.value = [];
    attachErrorLogs.value = false;
    attachAllLogs.value = false;
  } catch (error: any) {
    showToast(`提交失败：${error?.message || error}`, 'error');
  } finally {
    submittingFeedback.value = false;
  }
};

// ==================== 应用备份导出 ====================

const handleExportAppBackup = async () => {
  if (exportingAppBackup.value) return;

  try {
    const filePath = await saveDialog({
      defaultPath: `xianyu-backup-${new Date().toISOString().slice(0, 10)}.json`,
      filters: [{ name: '应用备份文件', extensions: ['json'] }],
    });
    if (!filePath) return;

    exportingAppBackup.value = true;

    const { json, summary } = await exportAppBackup(collectionsStore.playlists, {
      includePlugins: true,
      includeSettings: true,
      resolveSongsByPaths: libraryStore.resolveSongsByPaths,
    });

    await debugApi.writeLogExport(filePath, json);

    showToast(
      `已导出 ${summary.playlistCount} 个歌单、${summary.pluginCount} 个插件${summary.hasSettings ? '及设置' : ''}`,
      'success',
    );
  } catch (error: any) {
    showToast(`导出备份失败：${error?.message || error}`, 'error');
  } finally {
    exportingAppBackup.value = false;
  }
};

// ==================== 应用备份导入 ====================

const handleImportAppBackup = async () => {
  if (importingAppBackup.value) return;

  try {
    const selected = await open({
      multiple: false,
      title: '选择应用备份文件',
      filters: [{ name: '应用备份文件', extensions: ['json'] }],
    });
    if (typeof selected !== 'string') return;

    importingAppBackup.value = true;

    const content = await readPluginFile(selected);
    const backup = parseAppBackup(content);

    const result = await importAppBackup(backup, collectionsStore, libraryStore, {
      patchSettings,
      replaceSettings,
    }, {
      includePlaylists: true,
      includePlugins: true,
      includeSettings: true,
    });

    appBackupResult.value = result;
    showAppBackupResult.value = true;

    const parts: string[] = [];
    if (result.importedPlaylists > 0) parts.push(`${result.importedPlaylists} 个歌单`);
    if (result.importedPlugins > 0) parts.push(`${result.importedPlugins} 个插件`);
    if (result.settingsApplied) parts.push('设置');
    if (parts.length > 0) {
      showToast(`已导入 ${parts.join('、')}`, 'success');
    } else {
      showToast('备份中无新数据可导入', 'info');
    }
  } catch (error: any) {
    showToast(`导入备份失败：${error?.message || error}`, 'error');
  } finally {
    importingAppBackup.value = false;
  }
};
</script>

<template>
  <div class="space-y-8">
    <section class="space-y-3">
      <div>
        <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
          <span class="h-4 w-1 rounded-full bg-accent"></span>
          应用备份
        </h2>
        <p class="mt-1 text-xs leading-5 text-gray-500 dark:text-white/45">
          将歌单（自动区分本地/在线/混合）、插件和本地设置导出为单个 JSON 文件，可快速导入恢复。
        </p>
      </div>
      <div class="flex flex-wrap gap-3">
        <button
          type="button"
          :disabled="exportingAppBackup"
          class="inline-flex flex-1 items-center justify-center gap-2 rounded-xl border border-gray-200/40 bg-white/20 px-6 py-3 text-sm font-medium text-gray-800 shadow-sm transition hover:border-accent/25 hover:bg-white/30 disabled:cursor-wait disabled:opacity-55 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-100 dark:hover:bg-white/8"
          @click="handleExportAppBackup"
        >
          <Loader2 v-if="exportingAppBackup" class="h-4 w-4 animate-spin" />
          <FileUp v-else class="h-4 w-4 text-accent" />
          {{ exportingAppBackup ? '正在导出…' : '导出备份' }}
        </button>
        <button
          type="button"
          :disabled="importingAppBackup"
          class="inline-flex flex-1 items-center justify-center gap-2 rounded-xl border border-gray-200/40 bg-white/20 px-6 py-3 text-sm font-medium text-gray-800 shadow-sm transition hover:border-accent/25 hover:bg-white/30 disabled:cursor-wait disabled:opacity-55 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-100 dark:hover:bg-white/8"
          @click="handleImportAppBackup"
        >
          <Loader2 v-if="importingAppBackup" class="h-4 w-4 animate-spin" />
          <FileDown v-else class="h-4 w-4 text-accent" />
          {{ importingAppBackup ? '正在导入…' : '导入备份' }}
        </button>
      </div>
      <p class="text-[11px] leading-5 text-gray-400 dark:text-white/35">
        导入时会自动恢复歌单、插件（跳过已存在的）和应用设置；在线歌曲需对应插件已安装才能播放。
      </p>
    </section>

    <section class="space-y-3">
      <div>
        <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
          <span class="h-4 w-1 rounded-full bg-accent"></span>
          从其他软件导入
        </h2>
        <p class="mt-1 text-xs leading-5 text-gray-500 dark:text-white/45">
          从 BakaMusic 或 MusicFree 软件导入歌单。系统会按歌曲来源检查已安装插件，只导入能够关联到插件的歌曲。
        </p>
      </div>
      <button
        type="button"
        :disabled="importingBackup"
        class="inline-flex items-center gap-2 rounded-xl border border-gray-200/40 bg-white/20 px-4 py-3 text-sm font-medium text-gray-800 shadow-sm transition hover:border-accent/25 hover:bg-white/30 disabled:cursor-wait disabled:opacity-55 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-100 dark:hover:bg-white/8"
        @click="importPluginBackup"
      >
        <Loader2 v-if="importingBackup" class="h-4 w-4 animate-spin" />
        <FileDown v-else class="h-4 w-4 text-accent" />
        {{ importingBackup ? '正在检查插件并导入…' : '从其他软件导入歌单' }}
      </button>
      <p class="text-[11px] leading-5 text-gray-400 dark:text-white/35">
        导入完成后会统一列出成功关联的插件、缺失插件，以及所有未能导入的歌曲。
      </p>
    </section>

    <section class="space-y-3">
      <div>
        <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
          <span class="h-4 w-1 rounded-full bg-accent"></span>
          导出日志
        </h2>
        <p class="mt-1 text-xs text-gray-500 dark:text-white/45">导出本机保留的应用日志，便于反馈问题或自行排查。</p>
      </div>
      <LogExportActions />
    </section>

    <section class="space-y-3">
      <div>
        <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
          <span class="h-4 w-1 rounded-full bg-accent"></span>
          日志管理
        </h2>
        <p class="mt-1 text-xs text-gray-500 dark:text-white/45">删除后无法恢复，建议先导出需要保留的日志。</p>
      </div>
      <button
        type="button"
        :disabled="entryCount === 0"
        class="inline-flex items-center gap-2 rounded-xl border border-accent/50 bg-accent px-4 py-3 text-sm font-medium text-white transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-40"
        @click="showDeleteConfirmation = true"
      >
        <Trash2 class="h-4 w-4" />
        删除全部日志
      </button>
    </section>

    <div class="space-y-3">
      <div>
        <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
          <span class="h-4 w-1 rounded-full bg-accent"></span>
          问题反馈
        </h2>
        <p class="mt-1 text-xs leading-5 text-gray-500 dark:text-white/45">
          提交使用中遇到的问题或功能建议，我们会认真查看每一条反馈。
        </p>
      </div>

      <section
        class="space-y-3 rounded-xl border border-gray-200/40 bg-white/20 p-5 dark:border-gray-800/40 dark:bg-black/10"
        @focusin="refreshFeedbackAuth"
      >
      <!-- 未登录提示 -->
      <div
        v-if="!isFeedbackLoggedIn"
        class="rounded-xl border border-amber-500/25 bg-amber-500/[0.06] px-4 py-3 text-xs text-amber-700 dark:text-amber-300"
      >
        请先登录账号后再提交反馈。
      </div>

      <!-- 反馈表单（未登录时禁用） -->
      <div class="space-y-3" :class="{ 'pointer-events-none opacity-50': !isFeedbackLoggedIn }">
        <div>
          <span class="text-xs text-gray-500 dark:text-white/45">反馈类型</span>
          <div class="mt-2 grid grid-cols-2 gap-2">
            <button type="button" class="rounded-lg border px-3 py-2 text-sm transition" :class="feedbackType === 'problem' ? 'border-accent bg-accent text-white' : 'border-black/10 dark:border-white/10'" @click="feedbackType = 'problem'">问题反馈</button>
            <button type="button" class="rounded-lg border px-3 py-2 text-sm transition" :class="feedbackType === 'suggestion' ? 'border-accent bg-accent text-white' : 'border-black/10 dark:border-white/10'" @click="feedbackType = 'suggestion'">功能建议</button>
          </div>
        </div>

        <label class="block">
          <span class="text-xs text-gray-500 dark:text-white/45">详细内容</span>
          <textarea
            v-model="feedbackContent"
            rows="5"
            maxlength="1000"
            :placeholder="feedbackType === 'suggestion' ? '请描述你希望新增或改进的功能' : '请详细描述问题现象和复现步骤'"
            class="mt-2 w-full resize-y rounded-lg border border-black/10 bg-white/45 px-3 py-2 text-sm leading-6 text-gray-800 outline-none transition placeholder:text-gray-400 focus:border-accent/50 focus:bg-white/70 focus:ring-2 focus:ring-accent/10 dark:border-white/10 dark:bg-white/5 dark:text-gray-100 dark:placeholder:text-white/35 dark:focus:bg-white/10"
          />
          <span class="mt-1 block text-right text-[11px] text-gray-400 dark:text-white/35">
            {{ feedbackContent.length }} / 1000
          </span>
        </label>

        <!-- 日志附送勾选 -->
        <div v-if="feedbackType === 'problem' && (hasErrorLogs || hasAnyLogs)" class="flex flex-wrap items-center gap-4">
          <label v-if="hasErrorLogs" class="flex cursor-pointer items-center gap-2 text-xs text-gray-600 dark:text-white/55">
            <input
              type="checkbox"
              class="h-3.5 w-3.5 rounded border-gray-300 text-accent focus:ring-accent/30 dark:border-gray-600 dark:bg-gray-700"
              :checked="attachErrorLogs"
              @change="attachErrorLogs = ($event.target as HTMLInputElement).checked"
            />
            附上错误日志
          </label>
          <label v-if="hasAnyLogs" class="flex cursor-pointer items-center gap-2 text-xs text-gray-600 dark:text-white/55">
            <input
              type="checkbox"
              class="h-3.5 w-3.5 rounded border-gray-300 text-accent focus:ring-accent/30 dark:border-gray-600 dark:bg-gray-700"
              :checked="attachAllLogs"
              @change="attachAllLogs = ($event.target as HTMLInputElement).checked"
            />
            附上全部日志
          </label>
        </div>

        <div v-if="feedbackType === 'suggestion'" class="space-y-2">
          <div class="flex items-center justify-between gap-3">
            <span class="text-xs text-gray-500 dark:text-white/45">上传图片（可选，最多 6 张）</span>
            <button type="button" :disabled="compressingImage || feedbackImages.length >= 6" class="inline-flex items-center gap-1 rounded-lg border border-black/10 px-3 py-1.5 text-xs dark:border-white/10" @click="feedbackImageInput?.click()"><Plus v-if="!compressingImage" class="h-3.5 w-3.5" /><Loader2 v-else class="h-3.5 w-3.5 animate-spin" />添加图片</button>
          </div>
          <div v-if="feedbackImages.length" class="grid grid-cols-3 gap-2 sm:grid-cols-6">
            <div v-for="(image, index) in feedbackImages" :key="index" class="relative aspect-square overflow-hidden rounded-lg"><img :src="image" alt="反馈图片" class="h-full w-full object-cover" /><button type="button" class="absolute right-1 top-1 grid h-5 w-5 place-items-center rounded-full bg-black/65 text-white" @click="feedbackImages.splice(index, 1)"><X class="h-3 w-3" /></button></div>
          </div>
          <input ref="feedbackImageInput" type="file" accept="image/*" multiple class="hidden" @change="onFeedbackImageChange" />
        </div>

        <div class="flex flex-wrap gap-3">
          <button type="button" :disabled="submittingFeedback" class="inline-flex items-center gap-2 rounded-xl border border-gray-200/40 bg-white/20 px-4 py-3 text-sm font-medium text-gray-800 shadow-sm transition hover:border-accent/25 hover:bg-white/30 disabled:cursor-wait disabled:opacity-55 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-100" @click="submitUserFeedback"><Loader2 v-if="submittingFeedback" class="h-4 w-4 animate-spin" />{{ submittingFeedback ? '正在提交…' : '提交反馈' }}</button>
          <button type="button" class="inline-flex items-center gap-2 rounded-xl border border-gray-200/40 bg-white/20 px-4 py-3 text-sm font-medium text-gray-800 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-100" @click="openMyFeedback"><History class="h-4 w-4 text-accent" />查看我的反馈</button>
        </div>
      </div>
    </section>
    </div>

    <Teleport to="body">
      <Transition name="modal-fade">
        <div v-if="showMyFeedback" class="fixed inset-0 z-[1300] grid place-items-center bg-black/45 p-6 backdrop-blur-sm" @click.self="showMyFeedback = false">
          <div class="flex max-h-[78vh] w-[min(92vw,560px)] flex-col overflow-hidden rounded-2xl border border-black/10 bg-white shadow-2xl dark:border-white/10 dark:bg-neutral-800">
            <div class="flex items-center justify-between border-b border-black/10 px-5 py-4 dark:border-white/10"><h3 class="font-bold">我的反馈</h3><button @click="showMyFeedback = false"><X class="h-4 w-4" /></button></div>
            <div class="overflow-y-auto p-5">
              <div v-if="loadingMyFeedback" class="flex items-center justify-center gap-2 py-12"><Loader2 class="h-5 w-5 animate-spin" />加载中…</div>
              <p v-else-if="myFeedbackList.length === 0" class="py-12 text-center text-sm text-gray-500">暂无反馈记录</p>
              <div v-else class="space-y-3">
                <article v-for="item in myFeedbackList" :key="item.id" class="rounded-xl border border-black/10 p-4 dark:border-white/10">
                  <div class="flex items-center justify-between gap-3"><span class="text-xs font-semibold text-accent">{{ item.feedbackType === 'suggestion' ? '功能建议' : '问题反馈' }}</span><span class="text-xs text-gray-500">{{ feedbackStatus(item.status) }}</span></div>
                  <div class="mt-3 flex gap-4">
                    <div class="min-w-0 flex-1">
                      <p class="whitespace-pre-wrap text-sm leading-6">{{ item.content }}</p>
                      <div v-if="item.status === 'resolved' && (item.resolveNote || (item.resolveImages && item.resolveImages.length > 0))" class="mt-3 rounded-lg bg-black/[.03] p-3 text-xs dark:bg-white/[.04]">
                        <span v-if="item.resolveNote">处理说明（{{ item.assignee || '管理员' }}）：{{ item.resolveNote }}</span>
                        <div v-if="item.resolveImages && item.resolveImages.length > 0" class="fb-my-resolve-imgs">
                          <a
                            v-for="(rimg, ri) in item.resolveImages"
                            :key="ri"
                            :href="rimg"
                            target="_blank"
                            rel="noopener"
                            class="fb-my-resolve-img"
                          >
                            <img :src="rimg" alt="处理图片" loading="lazy" />
                          </a>
                        </div>
                      </div>
                      <p class="mt-2 text-[11px] text-gray-400">{{ item.createdAt }}</p>
                    </div>
                    <button
                      v-if="item.images?.length"
                      type="button"
                      class="relative h-[72px] w-[72px] shrink-0 cursor-zoom-in"
                      aria-label="查看反馈图片"
                      @click="openFeedbackViewer(item.images, 0)"
                    >
                      <img
                        v-for="(image, index) in item.images"
                        :key="index"
                        :src="image"
                        alt="反馈图片"
                        class="absolute left-0 top-0 h-[72px] w-[72px] rounded-lg border border-black/10 bg-white object-cover dark:border-white/10 dark:bg-neutral-800"
                        :style="getFeedbackImageStackStyle(index, item.images.length)"
                      />
                      <span
                        v-if="item.images.length > 1"
                        class="absolute -bottom-1.5 -right-1.5 z-20 h-5 min-w-5 rounded-full bg-black/65 px-1.5 text-center text-[11px] font-semibold leading-5 text-white shadow-md"
                      >{{ item.images.length }}</span>
                    </button>
                  </div>
                </article>
              </div>
            </div>
          </div>
        </div>
      </Transition>
    </Teleport>

    <Teleport to="body">
      <Transition name="feedback-viewer-fade">
        <div
          v-if="feedbackViewerVisible"
          class="fixed inset-0 z-[1400] flex items-center justify-center bg-black/85 backdrop-blur-md"
          @click.self="closeFeedbackViewer"
        >
          <button
            type="button"
            class="absolute right-[18px] top-[18px] z-10 flex h-10 w-10 items-center justify-center rounded-full bg-white/10 text-white transition hover:bg-white/25"
            aria-label="关闭图片预览"
            @click="closeFeedbackViewer"
          >
            <X class="h-6 w-6" />
          </button>
          <button
            v-if="feedbackViewerImages.length > 1"
            type="button"
            class="absolute left-[18px] top-1/2 z-10 flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-white/10 text-white transition hover:bg-white/25"
            aria-label="上一张反馈图片"
            @click="showPreviousFeedbackImage"
          >
            <ChevronLeft class="h-7 w-7" />
          </button>
          <img
            v-if="feedbackViewerImages[feedbackViewerIndex]"
            :src="feedbackViewerImages[feedbackViewerIndex]"
            alt="反馈图片预览"
            class="max-h-[84vh] max-w-[88vw] rounded-lg object-contain shadow-2xl"
          />
          <button
            v-if="feedbackViewerImages.length > 1"
            type="button"
            class="absolute right-[18px] top-1/2 z-10 flex h-11 w-11 -translate-y-1/2 items-center justify-center rounded-full bg-white/10 text-white transition hover:bg-white/25"
            aria-label="下一张反馈图片"
            @click="showNextFeedbackImage"
          >
            <ChevronRight class="h-7 w-7" />
          </button>
          <div
            v-if="feedbackViewerImages.length > 1"
            class="absolute bottom-5 left-1/2 -translate-x-1/2 rounded-full bg-black/55 px-3.5 py-1 text-[13px] text-white"
          >
            {{ feedbackViewerIndex + 1 }} / {{ feedbackViewerImages.length }}
          </div>
        </div>
      </Transition>
    </Teleport>

    <ConfirmModal
      :visible="showDeleteConfirmation"
      title="确认删除全部日志"
      content="此操作会永久删除当前设备上保存的全部应用日志，且无法恢复。确定继续吗？"
      @confirm="confirmDeleteLogs"
      @cancel="showDeleteConfirmation = false"
    />

    <BackupImportResultModal
      :visible="showBackupImportResult"
      :result="backupImportResult"
      :created-playlist-count="createdPlaylistCount"
      @close="showBackupImportResult = false"
    />

    <AppBackupResultModal
      :visible="showAppBackupResult"
      :result="appBackupResult"
      @close="showAppBackupResult = false"
    />
  </div>
</template>

<style scoped>
.feedback-viewer-fade-enter-active,
.feedback-viewer-fade-leave-active {
  transition: opacity 0.2s ease;
}

.feedback-viewer-fade-enter-from,
.feedback-viewer-fade-leave-to {
  opacity: 0;
}

.fb-my-resolve-imgs {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(56px, 1fr));
  gap: 6px;
  margin-top: 4px;
}
.fb-my-resolve-img {
  position: relative;
  display: block;
  aspect-ratio: 1;
  border-radius: 8px;
  overflow: hidden;
  cursor: zoom-in;
  border: 1px solid rgba(0, 0, 0, 0.08);
  background: #fff;
}
.fb-my-resolve-img img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
  transition: transform 140ms ease;
}
.fb-my-resolve-img:hover img {
  transform: scale(1.06);
}
:global(.dark) .fb-my-resolve-img,
.dark .fb-my-resolve-img {
  border-color: rgba(255, 255, 255, 0.1);
  background: #262626;
}
</style>
