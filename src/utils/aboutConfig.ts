import { ref } from 'vue';
import { signedRequest } from '../services/auth/authService';
import { normalizeBrandText } from './brand';

export interface AboutConfig {
  officialSiteUrl: string;
  officialSiteText: string;
  updateEnabled: boolean;
  updateText: string;
  projectUrl: string;
  projectText: string;
  referenceProjectUrl: string;
  referenceProjectText: string;
  joinGroupUrl: string;
  joinGroupText: string;
}

export const DEFAULT_ABOUT_CONFIG: AboutConfig = {
  officialSiteUrl: 'https://xymusic.cc',
  officialSiteText: '前往官网',
  updateEnabled: true,
  updateText: '检查更新',
  projectUrl: 'https://github.com/kaishui-server/XY-Music-PC',
  projectText: '开源地址',
  referenceProjectUrl: 'https://github.com/Billy636/LyciaMusic',
  referenceProjectText: '参考项目',
  joinGroupUrl: '656117919',
  joinGroupText: '加入QQ群',
};

export const aboutConfig = ref<AboutConfig>({ ...DEFAULT_ABOUT_CONFIG });

function asText(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim() ? normalizeBrandText(value.trim()) : fallback;
}

export async function fetchAboutConfig(): Promise<AboutConfig> {
  try {
    const data = await signedRequest<Record<string, unknown>>(
      'get_about_config',
      {},
      { fetchTimeoutMs: 8_000, timeoutMs: 10_000 },
    );

    return {
      officialSiteUrl: DEFAULT_ABOUT_CONFIG.officialSiteUrl,
      officialSiteText: asText(data.officialSiteText, DEFAULT_ABOUT_CONFIG.officialSiteText),
      updateEnabled: typeof data.updateEnabled === 'boolean'
        ? data.updateEnabled
        : DEFAULT_ABOUT_CONFIG.updateEnabled,
      updateText: asText(data.updateText, DEFAULT_ABOUT_CONFIG.updateText),
      projectUrl: DEFAULT_ABOUT_CONFIG.projectUrl,
      projectText: asText(data.projectText, DEFAULT_ABOUT_CONFIG.projectText),
      referenceProjectUrl: asText(data.referenceProjectUrl, DEFAULT_ABOUT_CONFIG.referenceProjectUrl),
      referenceProjectText: asText(data.referenceProjectText, DEFAULT_ABOUT_CONFIG.referenceProjectText),
      joinGroupUrl: DEFAULT_ABOUT_CONFIG.joinGroupUrl,
      joinGroupText: DEFAULT_ABOUT_CONFIG.joinGroupText,
    };
  } catch (error) {
    console.warn('[AboutConfig] 获取关于页配置失败，使用默认配置', error);
    return { ...DEFAULT_ABOUT_CONFIG };
  }
}

const POLL_INTERVAL_MS = 30_000;
let pollTimer: ReturnType<typeof setInterval> | null = null;

export async function refreshAboutConfig(): Promise<void> {
  aboutConfig.value = await fetchAboutConfig();
}

export function startAboutConfigPolling(): void {
  if (pollTimer) return;
  void refreshAboutConfig();
  pollTimer = setInterval(() => void refreshAboutConfig(), POLL_INTERVAL_MS);
}

export function stopAboutConfigPolling(): void {
  if (!pollTimer) return;
  clearInterval(pollTimer);
  pollTimer = null;
}
