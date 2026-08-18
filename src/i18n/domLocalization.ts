import { onBeforeUnmount, onMounted, watch, type ComputedRef } from 'vue';

import type { AppLanguage } from '../types';
import { CORE_ENGLISH_SOURCE_TEXT } from './index';
import { LEGACY_ENGLISH_TEXT } from './legacyEnglishText';

const ENGLISH_TEXT = new Map<string, string>([
  ...Object.entries(LEGACY_ENGLISH_TEXT),
  ...Object.entries(CORE_ENGLISH_SOURCE_TEXT),
]);

const textSource = new WeakMap<Text, string>();
const textRendered = new WeakMap<Text, string>();
const attributeSource = new WeakMap<Element, Map<string, string>>();
const attributeRendered = new WeakMap<Element, Map<string, string>>();
const LOCALIZED_ATTRIBUTES = ['title', 'aria-label', 'placeholder', 'alt'] as const;

const preserveOuterWhitespace = (source: string, translated: string): string => {
  const leading = source.match(/^\s*/u)?.[0] ?? '';
  const trailing = source.match(/\s*$/u)?.[0] ?? '';
  return `${leading}${translated}${trailing}`;
};

const translateDynamicPart = (source: string): string => source
  .split('，')
  .map((part) => ENGLISH_TEXT.get(part) ?? translateDynamicText(part) ?? part)
  .join(', ');

const translateDynamicText = (source: string): string | null => {
  const rules: Array<[RegExp, (...groups: string[]) => string]> = [
    [/^共\s*(\d+)\s*首歌曲$/u, count => `${count} songs`],
    [/^共\s*(\d+)\s*首$/u, count => `${count} songs`],
    [/^(\d+)\s*首歌曲$/u, count => `${count} songs`],
    [/^已选择\s*(\d+)\s*首歌曲?$/u, count => `${count} songs selected`],
    [/^已选择\s*(\d+)\s*项$/u, count => `${count} selected`],
    [/^共\s*(\d+)\s*项$/u, count => `${count} items`],
    [/^共\s*(\d+)\s*个插件$/u, count => `${count} plugins`],
    [/^(\d+)\s*个插件$/u, count => `${count} plugins`],
    [/^创建于\s*(.+)$/u, value => `Created ${value}`],
    [/^更新于\s*(.+)$/u, value => `Updated ${value}`],
    [/^搜索歌曲：(.+)$/u, value => `Search for: ${value}`],
    [/^已自动更新\s*(\d+)\s*个插件$/u, count => `${count} plugins updated automatically`],
    [/^欢迎使用弦予音乐概念版，当前版本\s*(.+)$/u, version => `Welcome to XianYu Music Concept, version ${version}`],
    [/^找到\s*(\d+)\s*项设置$/u, count => `${count} settings found`],
    [/^(\d+)\s*分钟前$/u, count => `${count} min ago`],
    [/^(\d+)\s*小时前$/u, count => `${count} hr ago`],
    [/^(\d+)\s*天前$/u, count => `${count} days ago`],
    [/^(\d+)\s*分钟$/u, count => `${count} min`],
    [/^(\d+)\s*小时$/u, count => `${count} hr`],
    [/^(\d+)\s*次$/u, count => `${count} plays`],
    [/^已跳过\s*(\d+)\s*首本地歌曲$/u, count => `Skipped ${count} local songs`],
    [/^开始批量下载\s*(\d+)\s*首歌曲（同时\s*(\d+)\s*首）$/u, (count, concurrent) => `Downloading ${count} songs (${concurrent} at a time)`],
    [/^已导入\s*(\d+)\s*首歌曲$/u, count => `Imported ${count} songs`],
    [/^成功导入\s*(\d+)\s*首歌曲$/u, count => `Imported ${count} songs`],
    [/^成功读取\s*(\d+)\s*首歌曲$/u, count => `Loaded ${count} songs`],
    [/^已载入\s*(\d+)\s*首歌曲并开始播放$/u, count => `Loaded ${count} songs and started playback`],
    [/^已删除\s*(\d+)\s*首本地歌曲$/u, count => `Deleted ${count} local songs`],
    [/^(\d+)\s*首歌曲删除失败$/u, count => `Could not delete ${count} songs`],
    [/^已忽略\s*(\d+)\s*个不支持的文件$/u, count => `Ignored ${count} unsupported files`],
    [/^已导入\s*(\d+)\s*个文件夹$/u, count => `Imported ${count} folders`],
    [/^已添加\s*(\d+)\s*个文件夹到本地目录视图$/u, count => `Added ${count} folders to the local view`],
    [/^已同步\s*(\d+)\s*首远程歌曲$/u, count => `Synced ${count} remote songs`],
    [/^已恢复\s*(\d+)\s*个文件夹$/u, count => `Restored ${count} folders`],
    [/^已创建文件夹:\s*(.+)$/u, name => `Created folder: ${name}`],
    [/^正在播放\s*(.+)$/u, title => `Playing ${title}`],
    [/^已卸载\s*(.+)$/u, name => `Uninstalled ${name}`],
    [/^已启用\s*(.+)$/u, name => `Enabled ${name}`],
    [/^已禁用\s*(.+)$/u, name => `Disabled ${name}`],
    [/^(.+)\s*已更新到\s*(v.+)$/u, (name, version) => `${name} updated to ${version}`],
    [/^当前歌曲不支持\s*(.+)，新设置将在下一首生效$/u, quality => `The current song does not support ${quality}. The new setting applies to the next song.`],
    [/^已将专辑《(.+)》添加到播放队尾$/u, album => `Added “${album}” to the end of the queue`],
    [/^已应用“(.+)”的歌词$/u, title => `Applied lyrics for “${title}”`],
    [/^无法获取播放URL[：:]\s*(.+)$/u, detail => `Unable to get playback URL: ${detail}`],
    [/^播放失败[：:]\s*(.+)$/u, detail => `Playback failed: ${detail}`],
    [/^加载失败[：:]\s*(.+)$/u, detail => `Loading failed: ${detail}`],
    [/^查看歌手失败[：:]\s*(.+)$/u, detail => `Could not open artist: ${detail}`],
    [/^查看专辑失败[：:]\s*(.+)$/u, detail => `Could not open album: ${detail}`],
    [/^导入失败[：:]\s*(.+)$/u, detail => `Import failed: ${detail}`],
    [/^读取文件夹失败[：:]\s*(.+)$/u, detail => `Could not read folder: ${detail}`],
    [/^刷新失败[：:]\s*(.+)$/u, detail => `Refresh failed: ${detail}`],
    [/^删除失败[：:]\s*(.+)$/u, detail => `Delete failed: ${detail}`],
    [/^安装失败[：:]\s*(.+)$/u, detail => `Installation failed: ${detail}`],
    [/^更新失败[：:]\s*(.+)$/u, detail => `Update failed: ${detail}`],
    [/^选择路径失败[：:]\s*(.+)$/u, detail => `Could not choose path: ${detail}`],
    [/^选择文件夹失败[：:]\s*(.+)$/u, detail => `Could not choose folder: ${detail}`],
    [/^日志导出失败[：:]\s*(.+)$/u, detail => `Log export failed: ${detail}`],
    [/^本地\s*(\d+)\s*首$/u, count => `Local: ${count}`],
    [/^在线\s*(\d+)\s*首$/u, count => `Online: ${count}`],
    [/^备份包含\s*(\d+)\s*个插件，成功导入\s*(\d+)\s*个，跳过\s*(\d+)\s*个（已存在或加载失败）。$/u,
      (total, imported, skipped) => `${total} plugins in the backup; ${imported} imported and ${skipped} skipped (already installed or failed to load).`],
    [/^已识别\s*(.+)\s*备份，并完成插件匹配与歌单导入。$/u,
      format => `${format} backup recognized. Plugin matching and playlist import are complete.`],
    [/^需要可处理“(.+)”的插件$/u, platform => `Requires a plugin that supports “${platform}”`],
    [/^影响\s*(\d+)\s*首$/u, count => `${count} affected`],
    [/^(.+)\s*·\s*(\d+)\s*首$/u, (platform, count) => `${platform} · ${count} songs`],
    [/^全部\s*(\d+)\s*首歌曲均已成功导入并关联到已安装插件。$/u,
      count => `All ${count} songs were imported and linked to installed plugins.`],
    [/^上传\s*(\d+)\s*个歌单$/u, count => `${count} playlists uploaded`],
    [/^下载\s*(\d+)\s*个歌单$/u, count => `${count} playlists downloaded`],
    [/^上传\s*(\d+)\s*个插件$/u, count => `${count} plugins uploaded`],
    [/^恢复\s*(\d+)\s*个插件$/u, count => `${count} plugins restored`],
    [/^(\d+)\s*个错误$/u, count => `${count} errors`],
    [/^上次[：:]\s*(.+)$/u, value => `Last: ${translateDynamicPart(value)}`],
    [/^下次同步[：:]\s*(.+)$/u, value => `Next sync: ${value}`],
    [/^更多菜单\s*(\d+)\s*项$/u, count => `${count} items in More`],
    [/^(\d+)\s*首歌曲$/u, count => `${count} songs`],
    [/^上次同步[：:]\s*(.+)\s*·\s*(\d+)\s*个$/u, (time, count) => `Last synced: ${time} · ${count} items`],
    [/^共\s*(\d+)\s*首$/u, count => `${count} songs`],
    [/^已扫描\s*(\d+)\s*首歌曲，发生变化\s*(\d+)\s*首。$/u,
      (total, changed) => `${total} songs scanned; ${changed} changed.`],
    [/^(\d+)\s*项$/u, count => `${count} items`],
    [/^(\d+)\s*张$/u, count => `${count} images`],
    [/^上传者[：:]\s*(.+)$/u, uploader => `Uploaded by: ${uploader}`],
    [/^已保存\s*(\d+)\s*张，已选择\s*(\d+)\s*张$/u,
      (saved, selected) => `${saved} saved, ${selected} selected`],
    [/^本地[：:]\s*(.+)$/u, path => `Local: ${path}`],
    [/^下载失败[：:]\s*(.+)$/u, detail => `Download failed: ${detail}`],
    [/^处理说明（(.+)）[：:]\s*(.+)$/u, (time, note) => `Resolution (${time}): ${note}`],
    [/^(\d+(?:\.\d+)?)\s*秒$/u, seconds => `${seconds} sec`],
    [/^(.+)（拖拽调整位置）$/u, label => `${translateDynamicPart(label)} (drag to reposition)`],
    [/^更多工具（固定）：已收纳\s*(\d+)\s*个控件$/u, count => `More tools (fixed): ${count} controls`],
  ];

  for (const [pattern, render] of rules) {
    const match = source.match(pattern);
    if (match) return render(...match.slice(1));
  }
  return null;
};

export const translateLegacyUiText = (source: string): string => {
  const trimmed = source.trim();
  if (!trimmed) return source;
  const translated = ENGLISH_TEXT.get(trimmed) ?? translateDynamicText(trimmed);
  return translated ? preserveOuterWhitespace(source, translated) : source;
};

const shouldSkipTextNode = (node: Text): boolean => {
  const parent = node.parentElement;
  return !parent || Boolean(parent.closest('script, style, textarea, [contenteditable="true"], [data-no-ui-translation]'));
};

const localizeTextNode = (node: Text, language: AppLanguage) => {
  if (shouldSkipTextNode(node)) return;
  const current = node.data;
  const previousRendered = textRendered.get(node);
  let source = textSource.get(node);

  if (source === undefined || (current !== previousRendered && current !== source)) {
    source = current;
    textSource.set(node, source);
  }

  const next = language === 'en-US' ? translateLegacyUiText(source) : source;
  textRendered.set(node, next);
  if (current !== next) node.data = next;
};

const localizeAttribute = (
  element: Element,
  name: (typeof LOCALIZED_ATTRIBUTES)[number],
  language: AppLanguage,
) => {
  const current = element.getAttribute(name);
  if (current === null) return;

  let sources = attributeSource.get(element);
  let renderedValues = attributeRendered.get(element);
  if (!sources) {
    sources = new Map();
    attributeSource.set(element, sources);
  }
  if (!renderedValues) {
    renderedValues = new Map();
    attributeRendered.set(element, renderedValues);
  }

  const previousRendered = renderedValues.get(name);
  let source = sources.get(name);
  if (source === undefined || (current !== previousRendered && current !== source)) {
    source = current;
    sources.set(name, source);
  }

  const next = language === 'en-US' ? translateLegacyUiText(source) : source;
  renderedValues.set(name, next);
  if (current !== next) element.setAttribute(name, next);
};

const localizeElement = (element: Element, language: AppLanguage) => {
  for (const name of LOCALIZED_ATTRIBUTES) localizeAttribute(element, name, language);
};

const localizeTree = (root: Node, language: AppLanguage) => {
  if (root.nodeType === Node.TEXT_NODE) {
    localizeTextNode(root as Text, language);
    return;
  }
  if (root.nodeType !== Node.ELEMENT_NODE && root.nodeType !== Node.DOCUMENT_NODE) return;

  if (root.nodeType === Node.ELEMENT_NODE) localizeElement(root as Element, language);
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  let current = walker.nextNode();
  while (current) {
    if (current.nodeType === Node.TEXT_NODE) localizeTextNode(current as Text, language);
    else localizeElement(current as Element, language);
    current = walker.nextNode();
  }
};

export const useDomLocalization = (language: ComputedRef<AppLanguage>) => {
  let observer: MutationObserver | null = null;
  let activeLanguage = language.value;

  const refresh = () => {
    activeLanguage = language.value;
    document.documentElement.lang = activeLanguage;
    if (document.body) localizeTree(document.body, activeLanguage);
  };

  const stopLanguageWatch = watch(language, refresh, { flush: 'post' });

  onMounted(() => {
    refresh();
    if (!document.body) return;
    observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'characterData') {
          localizeTextNode(mutation.target as Text, activeLanguage);
        } else if (mutation.type === 'attributes') {
          localizeElement(mutation.target as Element, activeLanguage);
        } else {
          for (const node of mutation.addedNodes) localizeTree(node, activeLanguage);
        }
      }
    });
    observer.observe(document.body, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: [...LOCALIZED_ATTRIBUTES],
    });
  });

  onBeforeUnmount(() => {
    stopLanguageWatch();
    observer?.disconnect();
    observer = null;
  });
};
