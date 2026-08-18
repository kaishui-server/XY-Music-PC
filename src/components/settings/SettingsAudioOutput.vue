<script setup lang="ts">
import { Check, ChevronDown, CircleAlert, ImagePlus, Minus, Plus, RotateCcw } from 'lucide-vue-next';
import { open } from '@tauri-apps/plugin-dialog';
import { useSettings } from '../../features/settings/useSettings';
import { usePlaybackStore } from '../../features/playback/store';
import { useToast } from '../../composables/toast';
import type {
  OnlineDefaultQuality,
  OnlineFailureBehavior,
  OnlineQualityFallbackBehavior,
  PlayerDetailCoverMode,
} from '../../types';
import { ALL_QUALITY_KEYS, QUALITY_META } from '../../types';
import { computed, onMounted, onScopeDispose, ref } from 'vue';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import { playbackApi } from '../../services/tauri/playbackApi';
import type { AudioOutputStatus, AudioDevice } from '../../services/tauri/contracts';
import { playerStorage, playerStorageKeys } from '../../services/storage/playerStorage';
import { toolboxApi } from '../../services/tauri/toolboxApi';
import { usePlayerDetailFallbackCover } from '../../composables/usePlayerDetailFallbackCover';
import {
  buildAudioOutputDeviceOptions,
  getSelectedOutputDeviceLabel,
} from './audioOutputDeviceLabels';
import SettingHint from './SettingHint.vue';
import {
  LYRICS_SYNC_OFFSET_MAX_MS,
  LYRICS_SYNC_OFFSET_MIN_MS,
  LYRICS_SYNC_OFFSET_STEP_MS,
  normalizeLyricsSyncOffsetMs,
} from '../../features/settings/lyricsSyncOffset';
import { useAppLanguage } from '../../i18n';
import { translateLegacyUiText } from '../../i18n/domLocalization';

const { settings, patchSettings } = useSettings();
const playbackStore = usePlaybackStore();
const { showToast } = useToast();
const { language } = useAppLanguage();
const localizeUi = (source: string): string => (
  language.value === 'en-US' ? translateLegacyUiText(source) : source
);

const volumeBalanceTip = '音量平衡会读取歌曲内置 ReplayGain 标签，在切歌时自动平衡音量。默认完全按标签播放，不改变歌曲内部动态。不存在标签时则无变化。';

type PlaybackDropdown = 'quality' | 'fallback' | 'failure' | 'device' | 'cover';
const activeDropdown = ref<PlaybackDropdown | null>(null);
const coverModeMenuOpen = computed(() => activeDropdown.value === 'cover');
const toggleDropdown = (dropdown: PlaybackDropdown) => {
  activeDropdown.value = activeDropdown.value === dropdown ? null : dropdown;
};
const closeDropdowns = () => {
  activeDropdown.value = null;
};
const fallbackCoverUrl = usePlayerDetailFallbackCover();
const isImportingFallbackCover = ref(false);

const coverModeOptions: Array<{ value: PlayerDetailCoverMode; label: string }> = [
  { value: 'show', label: '展示' },
  { value: 'hide', label: '隐藏' },
  { value: 'remember', label: '跟随上次选择' },
];

const currentCoverModeLabel = computed(() => (
  coverModeOptions.find(option => option.value === settings.value.playerDetailCoverMode)?.label
  ?? coverModeOptions[0].label
));

const selectPlayerDetailCoverMode = (value: PlayerDetailCoverMode) => {
  patchSettings({ playerDetailCoverMode: value });
  closeDropdowns();
};

const selectPlayerDetailFallbackCover = async () => {
  if (isImportingFallbackCover.value) return;
  try {
    const selected = await open({
      multiple: false,
      title: '选择歌曲无封面时显示的封面',
      filters: [{ name: '图片', extensions: ['jpg', 'jpeg', 'png', 'webp'] }],
    });
    if (typeof selected !== 'string') return;

    isImportingFallbackCover.value = true;
    const importedPath = await toolboxApi.importPlayerDetailFallbackCover(selected);
    patchSettings({ playerDetailFallbackCoverPath: importedPath });
    showToast('默认封面已更新', 'success');
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    showToast(`默认封面设置失败：${message}`, 'error');
  } finally {
    isImportingFallbackCover.value = false;
  }
};

const restoreBuiltInFallbackCover = async () => {
  patchSettings({ playerDetailFallbackCoverPath: '' });
  try {
    await toolboxApi.clearPlayerDetailFallbackCover();
  } catch (error) {
    console.warn('[Settings] 清理自定义默认封面失败:', error);
  }
  showToast('已恢复软件默认封面', 'success');
};

const FAILURE_BEHAVIOR_OPTIONS: { label: string; description: string; value: OnlineFailureBehavior }[] = [
  { label: '跳到下一首', description: '自动播放队列中的下一首歌曲', value: 'skip' },
  { label: '停止播放',   description: '停止播放，等待用户手动操作', value: 'stop' },
];

const QUALITY_FALLBACK_OPTIONS: { label: string; description: string; value: OnlineQualityFallbackBehavior }[] = [
  { label: '暂停',         description: '不尝试其他音质，暂停等待用户操作', value: 'pause' },
  { label: '播放更低音质', description: '自动降级到可用的更低音质',         value: 'lower' },
  { label: '播放更高音质', description: '自动升级到可用的更高音质',         value: 'higher' },
];

/** 检查当前是否正在播放在线歌曲 */
const isPlayingOnlineSong = () => {
  const song = playbackStore.currentSong;
  if (!song) return false;
  const path = song.cue_source_path || song.path;
  return path.startsWith('lx://') || path.startsWith('plugin://') || path.startsWith('http');
};

/** 切换在线音质：验证当前播放歌曲是否支持新音质，同时写入 settings store 和 localStorage */
const patchOnlineQuality = (value: OnlineDefaultQuality) => {
  patchSettings({ audio: { ...settings.value.audio, onlineDefaultQuality: value } });
  localStorage.setItem('online_quality', value);
  // [音质验证] 如果当前正在播放在线歌曲，提示新设置在下一首生效
  if (isPlayingOnlineSong()) {
    const available = playbackStore.currentAvailableQualities;
    if (available && !available.includes(value)) {
      showToast(`当前歌曲不支持 ${QUALITY_META[value].label}，新设置将在下一首生效`, 'info');
    } else {
      showToast('音质设置将在下一首歌曲生效', 'info');
    }
  }
};

/** 弹窗中选择音质 */
const handleQualitySelect = (value: OnlineDefaultQuality) => {
  closeDropdowns();
  patchOnlineQuality(value);
};

/** 弹窗中选择起播失败行为 */
const handleFailureBehaviorSelect = (value: OnlineFailureBehavior) => {
  closeDropdowns();
  patchSettings({ audio: { ...settings.value.audio, onlineFailureBehavior: value } });
};

/** 弹窗中选择音质回退行为 */
const handleFallbackBehaviorSelect = (value: OnlineQualityFallbackBehavior) => {
  closeDropdowns();
  patchQualityFallback(value);
};

/** 切换音质回退行为：验证当前播放歌曲的音质支持情况 */
const patchQualityFallback = (value: OnlineQualityFallbackBehavior) => {
  patchSettings({ audio: { ...settings.value.audio, onlineQualityFallbackBehavior: value } });
  if (isPlayingOnlineSong()) {
    showToast('回退行为将在下一首歌曲生效', 'info');
  }
};

/** 切换「播放失败自动换源」开关 */
const toggleAutoSwitchSource = () => {
  patchSettings({
    audio: {
      ...settings.value.audio,
      autoSwitchSourceOnFailure: !settings.value.audio.autoSwitchSourceOnFailure,
    },
  });
};

// --- 播放设置 ---
const autoPlay = ref(true);
const showLyricsSyncOffsetPanel = ref(false);
const audioOutputStatus = ref<AudioOutputStatus | null>(null);
const audioOutputDevices = ref<AudioDevice[]>([]);
const selectedOutputDeviceId = ref<string>('');
const isChangingOutputDevice = ref(false);
const wasapiExclusiveSideEffectTip = '开启后会独占播放设备：其他软件可能无声；设备断开或被占用时会自动回退默认播放。';
let unlistenAudioOutput: UnlistenFn | null = null;

const lyricsSyncOffsetMs = computed({
  get: () => normalizeLyricsSyncOffsetMs(settings.value.lyricsSyncOffset * 1000),
  set: (value: number | string) => {
    const numericValue = typeof value === 'string' ? parseFloat(value) : value;
    const next = normalizeLyricsSyncOffsetMs(numericValue);
    settings.value.lyricsSyncOffset = next / 1000;
  }
});

/** 输入浮点防御：四舍五入并回写显示值 */
const handleLyricsSyncOffsetChange = (event: Event) => {
  const target = event.target as HTMLInputElement;
  const numericValue = parseFloat(target.value);
  const next = normalizeLyricsSyncOffsetMs(numericValue);
  target.value = String(next);
  lyricsSyncOffsetMs.value = next;
};

const lyricsSyncOffsetLabel = computed(() => {
  const offset = lyricsSyncOffsetMs.value;
  if (offset === 0) return '0 ms';
  return `${offset > 0 ? '+' : ''}${offset} ms`;
});

const isWasapiExclusiveEnabled = computed(
  () => settings.value.audio.outputMode === 'wasapiExclusive',
);

const outputDeviceOptions = computed(() => buildAudioOutputDeviceOptions(audioOutputDevices.value));

const selectedOutputDeviceLabel = computed(() => localizeUi(
  getSelectedOutputDeviceLabel(
    outputDeviceOptions.value,
    selectedOutputDeviceId.value,
    audioOutputStatus.value,
  ),
));

const applyAudioOutputStatus = (status: AudioOutputStatus) => {
  audioOutputStatus.value = status;
  selectedOutputDeviceId.value = status.selected_device_id ?? '';

  // 同步 requested_output_mode 到设置（用户请求的输出模式）。
  // 不再在降级时关闭 DSD/Bit-perfect — 保留用户意图，等设备恢复后自动切回。
  // 底栏 UI 通过 playbackStore.activeOutputMode 判断是否真正在独占模式。
  if (settings.value.audio.outputMode !== status.requested_output_mode) {
    patchSettings({
      audio: {
        ...settings.value.audio,
        outputMode: status.requested_output_mode,
      },
    });
  }
};

const loadAudioOutputDevices = async () => {
  const [devices, status] = await Promise.all([
    playbackApi.getOutputDevices(),
    playbackApi.getCurrentOutputDevice(),
  ]);
  audioOutputDevices.value = devices;
  applyAudioOutputStatus(status);
};

const handleOutputDeviceSelect = async (deviceId: string) => {
  if (isChangingOutputDevice.value) return;
  closeDropdowns();
  if (deviceId === selectedOutputDeviceId.value) return;
  isChangingOutputDevice.value = true;
  try {
    const nextDeviceId = deviceId || null;
    await playbackApi.setOutputDevice(nextDeviceId);
    if (nextDeviceId) {
      playerStorage.setString(playerStorageKeys.outputDevice, nextDeviceId);
      playerStorage.setString(playerStorageKeys.outputDeviceMode, 'manual');
    } else {
      playerStorage.remove(playerStorageKeys.outputDevice);
      playerStorage.setString(playerStorageKeys.outputDeviceMode, 'default');
    }
    selectedOutputDeviceId.value = deviceId;
    applyAudioOutputStatus(await playbackApi.getCurrentOutputDevice());
  } catch (error) {
    console.error('Failed to update audio output device:', error);
    showToast('切换播放设备失败', 'error');
    selectedOutputDeviceId.value = audioOutputStatus.value?.selected_device_id ?? '';
  } finally {
    isChangingOutputDevice.value = false;
  }
};

const toggleWasapiExclusive = async () => {
  const outputMode = isWasapiExclusiveEnabled.value ? 'shared' : 'wasapiExclusive';
  settings.value.audio.outputMode = outputMode;
  try {
    await playbackApi.setAudioOutputMode(outputMode);
    applyAudioOutputStatus(await playbackApi.getCurrentOutputDevice());
  } catch (error) {
    console.error('Failed to update audio output mode:', error);
    showToast('切换音频输出模式失败', 'error');
  }
};

const resetLyricsSyncOffset = () => {
  lyricsSyncOffsetMs.value = 0;
};

const adjustLyricsSyncOffset = (delta: number) => {
  lyricsSyncOffsetMs.value = lyricsSyncOffsetMs.value + delta;
};

onMounted(async () => {
  document.addEventListener('click', closeDropdowns);
  await loadAudioOutputDevices().catch(error => {
    console.warn('Failed to load audio output devices:', error);
  });
  unlistenAudioOutput = await listen<AudioOutputStatus>('audio-output-device-changed', event => {
    applyAudioOutputStatus(event.payload);
  });
});

onScopeDispose(() => {
  document.removeEventListener('click', closeDropdowns);
  unlistenAudioOutput?.();
  unlistenAudioOutput = null;
});
</script>

<template>
  <div class="w-full space-y-8 animate-in fade-in slide-in-from-bottom-2 duration-300">
    <section class="space-y-3">
      <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
        <span class="h-4 w-1 rounded-full bg-accent"></span>
        音频处理
      </h2>
      <div class="flex flex-col rounded-xl overflow-hidden bg-white/20 dark:bg-black/10 border border-gray-200/40 dark:border-gray-800/40">
        <!-- 渐入渐出（淡入淡出）开关 -->
        <div class="desktop-setting-row">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">渐入渐出</div>
            <div class="text-xs text-gray-500 dark:text-gray-400">播放/暂停时音量平滑过渡，避免爆音</div>
          </div>
          <button
            type="button"
            class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none"
            :class="settings.audio.fadeInOutEnabled ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
            @click="settings.audio.fadeInOutEnabled = !settings.audio.fadeInOutEnabled"
          >
            <span
              class="inline-block h-4 w-4 transform rounded-full bg-white transition duration-200 ease-in-out shadow-sm"
              :class="settings.audio.fadeInOutEnabled ? 'translate-x-6' : 'translate-x-1'"
            />
          </button>
        </div>

        <!-- 渐入渐出时长设置子区域 -->
        <div
          v-if="settings.audio.fadeInOutEnabled"
          class="flex flex-col bg-white/20 transition-all duration-300 animate-in fade-in dark:bg-black/10"
        >
          <div class="desktop-setting-row pl-8">
            <div class="flex-1 space-y-1">
              <div class="text-sm font-medium text-gray-800 dark:text-gray-200 flex items-center gap-2">
                渐入渐出时长
                <span class="text-xs font-semibold px-2 py-0.5 rounded bg-gray-200 dark:bg-gray-800 text-gray-600 dark:text-gray-400">
                  {{ (settings.audio.fadeInOutDurationMs / 1000).toFixed(1) }} 秒
                </span>
              </div>
              <div class="text-xs text-gray-500 dark:text-gray-400 max-w-xl">
                设置音量从零渐变到目标值的过渡时间，范围 0.1 ~ 2 秒。
              </div>
            </div>
            <div class="flex items-center gap-3">
              <input
                type="range"
                min="100"
                max="2000"
                step="100"
                v-model.number="settings.audio.fadeInOutDurationMs"
                class="w-36 h-1 rounded-lg bg-gray-200 dark:bg-gray-700 appearance-none cursor-pointer accent-accent"
              />
            </div>
          </div>
        </div>

        <!-- 音量平衡主开关行 -->
        <div
          class="desktop-setting-row"
        >
          <div>
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">音量平衡</div>
          </div>
          <div class="flex items-center gap-3">
            <span
              class="audio-tip"
              :aria-label="volumeBalanceTip"
              tabindex="0"
            >
              <CircleAlert class="h-4 w-4" aria-hidden="true" />
              <span class="audio-tip-popover" role="tooltip">{{ volumeBalanceTip }}</span>
            </span>
            <button
              type="button"
              class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none"
              :class="settings.audio.volumeBalance.enabled ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
              @click="settings.audio.volumeBalance.enabled = !settings.audio.volumeBalance.enabled"
            >
              <span
                class="inline-block h-4 w-4 transform rounded-full bg-white transition duration-200 ease-in-out shadow-sm"
                :class="settings.audio.volumeBalance.enabled ? 'translate-x-6' : 'translate-x-1'"
              />
            </button>
          </div>
        </div>

        <!-- 高级音量平衡配置子区域 -->
        <div
          v-if="settings.audio.volumeBalance.enabled"
          class="flex flex-col bg-white/20 transition-all duration-300 animate-in fade-in dark:bg-black/10"
        >
          <!-- 整体增益偏移设置 -->
          <div class="desktop-setting-row pl-8">
            <div class="flex-1 space-y-1">
              <div class="text-sm font-medium text-gray-800 dark:text-gray-200 flex items-center gap-2">
                整体增益偏移
                <span class="text-xs font-semibold px-2 py-0.5 rounded bg-gray-200 dark:bg-gray-800 text-gray-600 dark:text-gray-400">
                  {{ settings.audio.volumeBalance.gainOffsetDb > 0 ? '+' : '' }}{{ settings.audio.volumeBalance.gainOffsetDb }} dB
                </span>
              </div>
              <div class="text-xs text-gray-500 dark:text-gray-400 max-w-xl">
                默认 0 dB，表示完全按 ReplayGain 标签播放。调高会整体更响，调低会保留更多余量。
              </div>
            </div>
            <div class="flex items-center gap-3">
              <input
                type="range"
                min="-12"
                max="6"
                step="1"
                v-model.number="settings.audio.volumeBalance.gainOffsetDb"
                class="w-36 h-1 rounded-lg bg-gray-200 dark:bg-gray-700 appearance-none cursor-pointer accent-accent"
              />
            </div>
          </div>

          <!-- 防削波保护开关 -->
          <div class="desktop-setting-row pl-8">
            <div class="flex-1 space-y-1">
              <div class="text-sm font-medium text-gray-800 dark:text-gray-200">
                防削波破音保护
              </div>
              <div class="text-xs text-gray-500 dark:text-gray-400 max-w-xl">
                当音量增益过大可能超出 0 dB 极限时自动降低音频信号。无峰值标签曲目会降级为不应用任何正增益。
              </div>
            </div>
            <div class="flex items-center gap-3">
              <button
                type="button"
                class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none"
                :class="settings.audio.volumeBalance.preventClipping ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
                @click="settings.audio.volumeBalance.preventClipping = !settings.audio.volumeBalance.preventClipping"
              >
                <span
                  class="inline-block h-4 w-4 transform rounded-full bg-white transition duration-200 ease-in-out shadow-sm"
                  :class="settings.audio.volumeBalance.preventClipping ? 'translate-x-6' : 'translate-x-1'"
                />
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- 在线播放设置 -->
    <section class="space-y-3">
      <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
        <span class="h-4 w-1 rounded-full bg-accent"></span>
        在线播放
      </h2>
      <div class="flex flex-col rounded-xl overflow-visible bg-white/20 dark:bg-black/10 border border-gray-200/40 dark:border-gray-800/40">

        <!-- 默认播放音质 -->
        <div class="desktop-setting-row">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">默认播放音质</div>
          </div>
          <div class="relative z-50 shrink-0">
            <button
              type="button"
              aria-haspopup="listbox"
              :aria-expanded="activeDropdown === 'quality'"
              class="playback-select-trigger"
              @click.stop="toggleDropdown('quality')"
              @keydown.esc.stop="closeDropdowns"
            >
              <span>{{ localizeUi(QUALITY_META[settings.audio.onlineDefaultQuality].label) }}</span>
              <span class="text-xs font-normal text-gray-400">{{ QUALITY_META[settings.audio.onlineDefaultQuality].description }}</span>
              <ChevronDown class="h-4 w-4 text-gray-400 transition-transform duration-200" :class="activeDropdown === 'quality' ? 'rotate-180 text-accent' : ''" aria-hidden="true" />
            </button>
            <Transition name="playback-dropdown">
              <div v-if="activeDropdown === 'quality'" role="listbox" class="playback-dropdown-panel grid w-[390px] grid-cols-3 gap-1.5 p-2">
                <button
                  v-for="key in ALL_QUALITY_KEYS"
                  :key="key"
                  type="button"
                  role="option"
                  :aria-selected="settings.audio.onlineDefaultQuality === key"
                  class="playback-dropdown-option flex-col items-start gap-0.5"
                  :class="settings.audio.onlineDefaultQuality === key ? 'playback-dropdown-option--active' : ''"
                  :title="QUALITY_META[key].description"
                  @click.stop="handleQualitySelect(key)"
                >
                  <span>{{ localizeUi(QUALITY_META[key].label) }}</span>
                  <span class="text-[10px] font-normal opacity-65">{{ QUALITY_META[key].description }}</span>
                </button>
              </div>
            </Transition>
          </div>
        </div>

        <!-- 默认音质播放失败行为 -->
        <div class="desktop-setting-row">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">默认音质播放失败行为</div>
          </div>
          <div class="relative z-40 shrink-0">
            <button
              type="button"
              aria-haspopup="listbox"
              :aria-expanded="activeDropdown === 'fallback'"
              class="playback-select-trigger min-w-44 justify-between"
              @click.stop="toggleDropdown('fallback')"
              @keydown.esc.stop="closeDropdowns"
            >
              <span>{{ localizeUi(QUALITY_FALLBACK_OPTIONS.find(o => o.value === settings.audio.onlineQualityFallbackBehavior)?.label ?? '') }}</span>
              <ChevronDown class="h-4 w-4 text-gray-400 transition-transform duration-200" :class="activeDropdown === 'fallback' ? 'rotate-180 text-accent' : ''" aria-hidden="true" />
            </button>
            <Transition name="playback-dropdown">
              <div v-if="activeDropdown === 'fallback'" role="listbox" class="playback-dropdown-panel w-80 p-1.5">
                <button
                  v-for="option in QUALITY_FALLBACK_OPTIONS"
                  :key="option.value"
                  type="button"
                  role="option"
                  :aria-selected="settings.audio.onlineQualityFallbackBehavior === option.value"
                  class="playback-dropdown-option"
                  :class="settings.audio.onlineQualityFallbackBehavior === option.value ? 'playback-dropdown-option--active' : ''"
                  @click.stop="handleFallbackBehaviorSelect(option.value)"
                >
                  <span class="min-w-0 flex-1 text-left">
                    <span class="block text-sm font-medium">{{ localizeUi(option.label) }}</span>
                    <span class="mt-0.5 block text-xs font-normal opacity-60">{{ localizeUi(option.description) }}</span>
                  </span>
                  <Check class="h-4 w-4 shrink-0 transition-opacity" :class="settings.audio.onlineQualityFallbackBehavior === option.value ? 'opacity-100' : 'opacity-0'" />
                </button>
              </div>
            </Transition>
          </div>
        </div>

        <!-- 播放失败自动换源 -->
        <div class="desktop-setting-row">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">播放失败自动换源</div>
            <div class="text-xs text-gray-500 dark:text-gray-400 max-w-xl">
              在线播放失败时，自动尝试其他落雪音源播放同一首歌（仅落雪歌曲生效）。
            </div>
          </div>
          <button
            type="button"
            class="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none"
            :class="settings.audio.autoSwitchSourceOnFailure ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
            @click="toggleAutoSwitchSource"
          >
            <span
              class="inline-block h-4 w-4 transform rounded-full bg-white transition duration-200 ease-in-out shadow-sm"
              :class="settings.audio.autoSwitchSourceOnFailure ? 'translate-x-6' : 'translate-x-1'"
            />
          </button>
        </div>

        <!-- 起播失败行为 -->
        <div class="desktop-setting-row">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">起播失败行为</div>
            <div class="text-xs text-gray-500 dark:text-gray-400 max-w-xl">
              在线引擎完全无法生效时的处理方式。
            </div>
          </div>
          <div class="relative z-30 shrink-0">
            <button
              type="button"
              aria-haspopup="listbox"
              :aria-expanded="activeDropdown === 'failure'"
              class="playback-select-trigger min-w-40 justify-between"
              @click.stop="toggleDropdown('failure')"
              @keydown.esc.stop="closeDropdowns"
            >
              <span>{{ localizeUi(FAILURE_BEHAVIOR_OPTIONS.find(o => o.value === settings.audio.onlineFailureBehavior)?.label ?? '') }}</span>
              <ChevronDown class="h-4 w-4 text-gray-400 transition-transform duration-200" :class="activeDropdown === 'failure' ? 'rotate-180 text-accent' : ''" aria-hidden="true" />
            </button>
            <Transition name="playback-dropdown">
              <div v-if="activeDropdown === 'failure'" role="listbox" class="playback-dropdown-panel w-80 p-1.5">
                <button
                  v-for="option in FAILURE_BEHAVIOR_OPTIONS"
                  :key="option.value"
                  type="button"
                  role="option"
                  :aria-selected="settings.audio.onlineFailureBehavior === option.value"
                  class="playback-dropdown-option"
                  :class="settings.audio.onlineFailureBehavior === option.value ? 'playback-dropdown-option--active' : ''"
                  @click.stop="handleFailureBehaviorSelect(option.value)"
                >
                  <span class="min-w-0 flex-1 text-left">
                    <span class="block text-sm font-medium">{{ localizeUi(option.label) }}</span>
                    <span class="mt-0.5 block text-xs font-normal opacity-60">{{ localizeUi(option.description) }}</span>
                  </span>
                  <Check class="h-4 w-4 shrink-0 transition-opacity" :class="settings.audio.onlineFailureBehavior === option.value ? 'opacity-100' : 'opacity-0'" />
                </button>
              </div>
            </Transition>
          </div>
        </div>

      </div>
    </section>

    <!-- 均衡器配置区已移除 -->

    <!-- 播放设置 -->
    <section class="space-y-3">
      <h2 class="text-sm font-bold text-gray-800 dark:text-gray-200 flex items-center gap-2">
        <span class="w-1 h-4 bg-accent rounded-full"></span>
        播放设置
      </h2>
      <div class="settings-playback-group flex flex-col overflow-visible rounded-xl border border-gray-200/40 bg-white/20 dark:border-gray-800/40 dark:bg-black/10">
        <div class="desktop-setting-row">
          <div>
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">自动播放</div>
          </div>
           <button @click="autoPlay = !autoPlay" class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none" :class="autoPlay ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'">
            <span class="inline-block h-4 w-4 transform rounded-full bg-white transition duration-200 ease-in-out shadow-sm" :class="autoPlay ? 'translate-x-6' : 'translate-x-1'" />
          </button>
        </div>
        <div class="desktop-setting-row">
          <div>
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">播放时阻止电脑睡眠</div>
          </div>
          <button
            type="button"
            role="switch"
            aria-label="播放时阻止电脑睡眠"
            :aria-checked="settings.preventComputerSleepWhilePlaying"
            class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none"
            :class="settings.preventComputerSleepWhilePlaying ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'"
            @click="patchSettings({ preventComputerSleepWhilePlaying: !settings.preventComputerSleepWhilePlaying })"
          >
            <span
              class="inline-block h-4 w-4 transform rounded-full bg-white shadow-sm transition duration-200 ease-in-out"
              :class="settings.preventComputerSleepWhilePlaying ? 'translate-x-6' : 'translate-x-1'"
            />
          </button>
        </div>
        <div class="desktop-setting-row relative z-20">
          <div>
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">播放设备</div>
          </div>
          <div class="relative shrink-0">
            <button
              type="button"
              :disabled="isChangingOutputDevice"
              aria-haspopup="listbox"
              :aria-expanded="activeDropdown === 'device'"
              class="playback-select-trigger max-w-[260px] min-w-44 justify-between disabled:cursor-not-allowed disabled:opacity-60"
              @click.stop="toggleDropdown('device')"
              @keydown.esc.stop="closeDropdowns"
            >
              <span class="truncate">{{ selectedOutputDeviceLabel }}</span>
              <ChevronDown class="h-4 w-4 shrink-0 text-gray-400 transition-transform duration-200" :class="activeDropdown === 'device' ? 'rotate-180 text-accent' : ''" aria-hidden="true" />
            </button>
            <Transition name="playback-dropdown">
              <div v-if="activeDropdown === 'device'" role="listbox" class="playback-dropdown-panel max-h-72 w-80 overflow-y-auto p-1.5 custom-scrollbar">
                <button
                  v-for="device in outputDeviceOptions"
                  :key="device.id || 'default'"
                  type="button"
                  role="option"
                  :aria-selected="selectedOutputDeviceId === device.id"
                  class="playback-dropdown-option"
                  :class="selectedOutputDeviceId === device.id ? 'playback-dropdown-option--active' : ''"
                  @click.stop="handleOutputDeviceSelect(device.id)"
                >
                  <span class="min-w-0 flex-1 truncate text-left">{{ localizeUi(device.name) }}</span>
                  <Check class="h-4 w-4 shrink-0 transition-opacity" :class="selectedOutputDeviceId === device.id ? 'opacity-100' : 'opacity-0'" />
                </button>
              </div>
            </Transition>
          </div>
        </div>
        <div class="desktop-setting-row">
          <div>
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">WASAPI 独占模式</div>
          </div>
          <div class="flex items-center gap-3">
            <SettingHint severity="warning" :text="wasapiExclusiveSideEffectTip" />
            <button @click="toggleWasapiExclusive" class="relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none" :class="isWasapiExclusiveEnabled ? 'bg-accent' : 'bg-gray-300 dark:bg-gray-700'">
              <span class="inline-block h-4 w-4 transform rounded-full bg-white transition duration-200 ease-in-out shadow-sm" :class="isWasapiExclusiveEnabled ? 'translate-x-6' : 'translate-x-1'" />
            </button>
          </div>
        </div>
        <div>
          <button
            type="button"
            @click="showLyricsSyncOffsetPanel = !showLyricsSyncOffsetPanel"
            class="desktop-setting-row w-full"
          >
            <div class="min-w-0">
              <div class="text-sm font-medium text-gray-800 dark:text-gray-200">歌词同步补偿</div>
            </div>
            <div class="flex items-center gap-3 shrink-0">
              <SettingHint
                text="正值让歌词更晚显示，负值让歌词更早显示。用于修正不同输出设备的播放缓冲差异，默认值为 0 ms。"
                :focusable="false"
              />
              <div class="text-xs font-medium text-gray-600 dark:text-gray-300 tabular-nums">
                {{ lyricsSyncOffsetLabel }}
              </div>
              <svg
                xmlns="http://www.w3.org/2000/svg"
                class="h-4 w-4 text-gray-400 transition-transform duration-200"
                :class="showLyricsSyncOffsetPanel ? 'rotate-180' : ''"
                viewBox="0 0 20 20"
                fill="currentColor"
              >
                <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 011.06.02L10 11.168l3.71-3.938a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z" clip-rule="evenodd" />
              </svg>
            </div>
          </button>
          <transition name="settings-pop-panel">
            <div v-if="showLyricsSyncOffsetPanel" class="px-4 pb-4">
              <div class="settings-expand-panel">
                <div class="flex flex-col gap-4 md:flex-row md:items-center">
                  <div class="flex min-w-[240px] flex-1 items-center gap-2">
                    <button
                      type="button"
                      class="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-gray-200/40 bg-white/20 text-gray-600 transition hover:border-accent hover:bg-white/30 hover:text-accent disabled:cursor-not-allowed disabled:opacity-35 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-300"
                      :disabled="lyricsSyncOffsetMs <= LYRICS_SYNC_OFFSET_MIN_MS"
                      aria-label="歌词偏移减少 5 毫秒"
                      @click="adjustLyricsSyncOffset(-LYRICS_SYNC_OFFSET_STEP_MS)"
                    >
                      <Minus class="h-4 w-4" />
                    </button>
                    <input
                      v-model="lyricsSyncOffsetMs"
                      type="range"
                      :min="LYRICS_SYNC_OFFSET_MIN_MS"
                      :max="LYRICS_SYNC_OFFSET_MAX_MS"
                      :step="LYRICS_SYNC_OFFSET_STEP_MS"
                      class="settings-slider min-w-0 flex-1"
                    />
                    <button
                      type="button"
                      class="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-gray-200/40 bg-white/20 text-gray-600 transition hover:border-accent hover:bg-white/30 hover:text-accent disabled:cursor-not-allowed disabled:opacity-35 dark:border-gray-800/40 dark:bg-black/10 dark:text-gray-300"
                      :disabled="lyricsSyncOffsetMs >= LYRICS_SYNC_OFFSET_MAX_MS"
                      aria-label="歌词偏移增加 5 毫秒"
                      @click="adjustLyricsSyncOffset(LYRICS_SYNC_OFFSET_STEP_MS)"
                    >
                      <Plus class="h-4 w-4" />
                    </button>
                  </div>
                  <div class="flex items-center gap-3">
                    <input
                      :value="lyricsSyncOffsetMs"
                      type="number"
                      :min="LYRICS_SYNC_OFFSET_MIN_MS"
                      :max="LYRICS_SYNC_OFFSET_MAX_MS"
                      :step="LYRICS_SYNC_OFFSET_STEP_MS"
                      class="h-8 rounded-lg border border-black/10 bg-white/45 px-3 text-xs text-gray-800 outline-none transition placeholder:text-gray-400 focus:border-accent/50 focus:bg-white/70 focus:ring-2 focus:ring-accent/10 dark:border-white/10 dark:bg-white/5 dark:text-gray-100 dark:placeholder:text-white/35 dark:focus:bg-white/10"
                      @change="handleLyricsSyncOffsetChange"
                    />
                    <button
                      type="button"
                      @click="resetLyricsSyncOffset"
                      class="settings-action-button settings-action-button--soft"
                    >
                      恢复默认
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </transition>
        </div>
      </div>
    </section>

    <!-- 播放详情页设置 -->
    <section class="space-y-3">
      <h2 class="flex items-center gap-2 text-sm font-bold text-gray-800 dark:text-gray-200">
        <span class="h-4 w-1 rounded-full bg-accent"></span>
        播放详情页设置
      </h2>
      <div class="flex flex-col rounded-xl border border-gray-200/40 bg-white/20 dark:border-gray-800/40 dark:bg-black/10">
        <div class="desktop-setting-row overflow-visible">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">播放详情页封面</div>
            <div class="text-xs leading-5 text-gray-500 dark:text-gray-400">
              设置每次打开歌曲播放详情页时的封面显示方式。
            </div>
          </div>
          <div class="relative z-30 shrink-0">
            <button
              type="button"
              aria-label="播放详情页封面"
              aria-haspopup="listbox"
              :aria-expanded="coverModeMenuOpen"
              class="group flex min-w-40 items-center justify-between gap-3 rounded-xl border px-3.5 py-2.5 text-sm font-medium outline-none transition-all duration-200"
              :class="coverModeMenuOpen
                ? 'border-accent/60 bg-accent/10 text-accent shadow-lg shadow-accent/10 ring-2 ring-accent/10'
                : 'border-gray-200/60 bg-white/55 text-gray-700 shadow-sm hover:border-accent/35 hover:bg-white/80 dark:border-white/10 dark:bg-white/[0.06] dark:text-gray-200 dark:hover:bg-white/10'"
              @click.stop="toggleDropdown('cover')"
              @keydown.esc.stop="closeDropdowns"
            >
              <span>{{ localizeUi(currentCoverModeLabel) }}</span>
              <ChevronDown
                class="h-4 w-4 transition-transform duration-300 ease-out"
                :class="coverModeMenuOpen ? 'rotate-180 text-accent' : 'text-gray-400 group-hover:text-accent'"
              />
            </button>

            <Transition name="cover-mode-menu">
              <div
                v-if="coverModeMenuOpen"
                role="listbox"
                aria-label="播放详情页封面选项"
                class="absolute bottom-[calc(100%+0.5rem)] right-0 w-48 overflow-hidden rounded-xl border border-gray-200/60 bg-white/95 p-1.5 shadow-xl shadow-black/10 backdrop-blur-xl dark:border-white/10 dark:bg-[#252525]/95 dark:shadow-black/30"
              >
                <button
                  v-for="option in coverModeOptions"
                  :key="option.value"
                  type="button"
                  role="option"
                  :aria-selected="settings.playerDetailCoverMode === option.value"
                  class="group/option flex w-full items-center justify-between rounded-lg px-3 py-2.5 text-left text-sm transition-all duration-150"
                  :class="settings.playerDetailCoverMode === option.value
                    ? 'bg-accent/12 font-medium text-accent'
                    : 'text-gray-700 hover:translate-x-0.5 hover:bg-black/5 dark:text-gray-200 dark:hover:bg-white/[0.07]'"
                  @click="selectPlayerDetailCoverMode(option.value)"
                >
                  <span>{{ localizeUi(option.label) }}</span>
                  <Check
                    class="h-4 w-4 transition-all duration-200"
                    :class="settings.playerDetailCoverMode === option.value ? 'scale-100 opacity-100' : 'scale-75 opacity-0'"
                  />
                </button>
              </div>
            </Transition>
          </div>
        </div>
        <div class="desktop-setting-row gap-4">
          <div class="min-w-0 flex-1 space-y-1 pr-3">
            <div class="text-sm font-medium text-gray-800 dark:text-gray-200">歌曲无封面时默认显示封面</div>
            <div class="text-xs leading-5 text-gray-500 dark:text-gray-400">
              仅在歌曲自身没有获取到封面时显示，可上传一张图片替换软件默认封面。
            </div>
          </div>
          <div class="flex shrink-0 items-center gap-2.5">
            <div class="grid h-12 w-12 shrink-0 place-items-center overflow-hidden rounded-xl border border-gray-200/60 bg-gradient-to-br from-zinc-100 to-zinc-200 text-zinc-400 shadow-sm dark:border-white/10 dark:from-zinc-700 dark:to-zinc-800">
              <img
                v-if="fallbackCoverUrl"
                :src="fallbackCoverUrl"
                alt="自定义默认封面预览"
                class="h-full w-full object-cover"
              />
              <svg v-else xmlns="http://www.w3.org/2000/svg" class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.7" d="M9 19V6l12-3v13M9 19c0 1.105-1.343 2-3 2s-3-.895-3-2 1.343-2 3-2 3 .895 3 2zm12-3c0 1.105-1.343 2-3 2s-3-.895-3-2 1.343-2 3-2 3 .895 3 2zM9 10l12-3" />
              </svg>
            </div>
            <button
              type="button"
              :disabled="isImportingFallbackCover"
              class="settings-action-button settings-action-button--soft flex items-center gap-1.5 disabled:cursor-not-allowed disabled:opacity-50"
              @click="selectPlayerDetailFallbackCover"
            >
              <ImagePlus class="h-4 w-4" />
              {{ isImportingFallbackCover ? '导入中' : '上传封面' }}
            </button>
            <button
              v-if="settings.playerDetailFallbackCoverPath"
              type="button"
              class="settings-action-button settings-action-button--soft flex items-center gap-1.5"
              @click="restoreBuiltInFallbackCover"
            >
              <RotateCcw class="h-4 w-4" />
              恢复默认
            </button>
          </div>
        </div>
      </div>
    </section>

  </div>
</template>

<style scoped>
.desktop-setting-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px;
  text-align: left;
  transition: background-color 160ms ease;
}

.desktop-setting-row:hover {
  background: rgba(255, 255, 255, 0.4);
}

:global(.dark) .desktop-setting-row:hover {
  background: rgba(255, 255, 255, 0.1);
}

.audio-tip {
  position: relative;
  display: inline-flex;
  height: 20px;
  width: 20px;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  color: #9ca3af;
  outline: none;
}

.audio-tip-popover {
  position: absolute;
  right: 0;
  top: calc(100% + 8px);
  z-index: 30;
  width: min(300px, calc(100vw - 48px));
  max-width: calc(100vw - 48px);
  pointer-events: none;
  border: 1px solid rgba(148, 163, 184, 0.22);
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.96);
  box-shadow: 0 18px 42px rgba(15, 23, 42, 0.16);
  color: rgb(31 41 55);
  font-size: 12px;
  font-weight: 500;
  line-height: 1.55;
  opacity: 0;
  padding: 10px 12px;
  transform: translateY(-4px);
  transition: opacity 160ms ease, transform 160ms ease;
  white-space: normal;
}

.audio-tip:hover .audio-tip-popover,
.audio-tip:focus-visible .audio-tip-popover {
  opacity: 1;
  transform: translateY(0);
}

:global(.dark) .audio-tip {
  color: #9ca3af;
}

:global(.dark) .audio-tip-popover {
  border-color: rgba(255, 255, 255, 0.1);
  background: rgba(31, 31, 31, 0.96);
  box-shadow: 0 18px 42px rgba(0, 0, 0, 0.28);
  color: rgba(255, 255, 255, 0.92);
}

.settings-expand-panel {
  margin-top: 2px;
  padding: 18px 16px 0;
}

.settings-playback-group {
  overflow: visible;
}

.playback-select-trigger {
  display: flex;
  min-height: 42px;
  flex-shrink: 0;
  align-items: center;
  gap: 10px;
  border: 1px solid rgba(148, 163, 184, 0.3);
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.55);
  padding: 9px 14px;
  color: rgb(55 65 81);
  font-size: 14px;
  font-weight: 500;
  outline: none;
  box-shadow: 0 1px 2px rgba(15, 23, 42, 0.04);
  transition: border-color 180ms ease, background-color 180ms ease, color 180ms ease, box-shadow 180ms ease;
}

.playback-select-trigger:hover,
.playback-select-trigger[aria-expanded='true'] {
  border-color: rgb(var(--theme-accent-rgb) / 0.55);
  background: rgb(var(--theme-accent-rgb) / 0.08);
  color: var(--theme-accent);
  box-shadow: 0 10px 24px rgb(var(--theme-accent-rgb) / 0.08), 0 0 0 2px rgb(var(--theme-accent-rgb) / 0.08);
}

.playback-dropdown-panel {
  position: absolute;
  right: 0;
  top: calc(100% + 8px);
  z-index: 80;
  overflow: hidden;
  border: 1px solid rgba(148, 163, 184, 0.28);
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.96);
  box-shadow: 0 20px 46px rgba(15, 23, 42, 0.18);
  backdrop-filter: blur(20px);
}

.playback-dropdown-option {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border-radius: 8px;
  padding: 10px 12px;
  color: rgb(55 65 81);
  font-size: 14px;
  text-align: left;
  transition: color 150ms ease, background-color 150ms ease, transform 150ms ease;
}

.playback-dropdown-option:hover {
  background: rgba(15, 23, 42, 0.05);
  transform: translateX(2px);
}

.playback-dropdown-option--active {
  background: rgb(var(--theme-accent-rgb) / 0.12);
  color: var(--theme-accent);
  font-weight: 600;
}

.playback-dropdown-enter-active,
.playback-dropdown-leave-active {
  transform-origin: top right;
  transition: opacity 180ms ease, transform 220ms cubic-bezier(0.16, 1, 0.3, 1);
}

.playback-dropdown-enter-from,
.playback-dropdown-leave-to {
  opacity: 0;
  transform: translateY(-6px) scale(0.96);
}

:global(.dark) .playback-select-trigger {
  border-color: rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.06);
  color: rgb(229 231 235);
}

:global(.dark) .playback-select-trigger:hover,
:global(.dark) .playback-select-trigger[aria-expanded='true'] {
  border-color: rgb(var(--theme-accent-rgb) / 0.58);
  background: rgb(var(--theme-accent-rgb) / 0.1);
  color: var(--theme-accent);
}

:global(.dark) .playback-dropdown-panel {
  border-color: rgba(255, 255, 255, 0.1);
  background: rgba(37, 37, 37, 0.96);
  box-shadow: 0 20px 46px rgba(0, 0, 0, 0.32);
}

:global(.dark) .playback-dropdown-option {
  color: rgb(229 231 235);
}

:global(.dark) .playback-dropdown-option:hover {
  background: rgba(255, 255, 255, 0.07);
}

:global(.dark) .playback-dropdown-option--active {
  background: rgb(var(--theme-accent-rgb) / 0.14);
  color: var(--theme-accent);
}

.settings-slider {
  height: 6px;
  cursor: pointer;
  accent-color: var(--theme-accent);
}

.settings-number-input {
  width: 98px;
  border: 1px solid rgba(148, 163, 184, 0.24);
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.72);
  padding: 10px 12px;
  color: rgb(55 65 81);
  font-size: 13px;
  outline: none;
  transition: border-color 160ms ease, box-shadow 160ms ease, background-color 160ms ease;
}

.settings-number-input:focus {
  border-color: rgb(var(--theme-accent-rgb) / 0.3);
  box-shadow: 0 0 0 3px rgb(var(--theme-accent-rgb) / 0.08);
}

.settings-action-button {
  min-height: 38px;
  padding: 0 16px;
  border: 1px solid rgb(var(--theme-accent-rgb) / 0.14);
  border-radius: 999px;
  font-size: 12px;
  font-weight: 600;
  transition:
    border-color 160ms ease,
    background-color 160ms ease,
    color 160ms ease,
    box-shadow 160ms ease,
    transform 160ms ease;
}

.settings-action-button:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 10px 20px rgb(var(--theme-accent-rgb) / 0.08);
}

.settings-action-button--soft {
  background: rgb(var(--theme-accent-rgb) / 0.06);
  color: var(--theme-accent);
}

.settings-action-button--soft:hover:not(:disabled) {
  border-color: rgb(var(--theme-accent-rgb) / 0.34);
  background: rgb(var(--theme-accent-rgb) / 0.1);
}

.settings-action-button--disabled {
  border-color: rgba(148, 163, 184, 0.12);
  background: rgba(255, 255, 255, 0.36);
  color: rgba(100, 116, 139, 0.8);
  cursor: not-allowed;
  box-shadow: none;
}

.settings-pop-panel-enter-active,
.settings-pop-panel-leave-active {
  transition:
    opacity 220ms ease,
    transform 240ms ease,
    max-height 240ms ease;
  transform-origin: top center;
  overflow: hidden;
}

.settings-pop-panel-enter-from,
.settings-pop-panel-leave-to {
  opacity: 0;
  transform: translateY(-10px) scale(0.97);
  max-height: 0;
}

.settings-pop-panel-enter-to,
.settings-pop-panel-leave-from {
  opacity: 1;
  transform: translateY(0) scale(1);
  max-height: 240px;
}

.cover-mode-menu-enter-active,
.cover-mode-menu-leave-active {
  transform-origin: bottom right;
  transition: opacity 180ms ease, transform 220ms cubic-bezier(0.16, 1, 0.3, 1);
}

.cover-mode-menu-enter-from,
.cover-mode-menu-leave-to {
  opacity: 0;
  transform: translateY(6px) scale(0.96);
}

:global(.dark) .settings-expand-panel {
}

:global(.dark) .settings-number-input {
  border-color: rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.05);
  color: rgba(255, 255, 255, 0.92);
}

:global(.dark) .settings-number-input:focus {
  border-color: rgb(var(--theme-accent-rgb) / 0.34);
  box-shadow: 0 0 0 3px rgb(var(--theme-accent-rgb) / 0.12);
}

:global(.dark) .settings-action-button--disabled {
  border-color: rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.05);
  color: rgba(255, 255, 255, 0.45);
}
</style>
