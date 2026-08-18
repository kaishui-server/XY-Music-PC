import { onBeforeUnmount, onMounted, watch, type WatchSource } from 'vue';

const STORED_TITLE_ATTRIBUTE = 'data-button-hover-title';
const INTERACTIVE_TITLE_SELECTOR = 'button[title], [role="button"][title]';
const STORED_TITLE_SELECTOR = `[${STORED_TITLE_ATTRIBUTE}]`;

const shouldKeepHoverDetails = (element: Element) => (
  element.matches('.setting-hint, [data-keep-hover-details]')
  || element.closest('.setting-hint, [data-keep-hover-details]') !== null
);

const suppressElementTitle = (element: Element) => {
  if (!element.matches(INTERACTIVE_TITLE_SELECTOR) || shouldKeepHoverDetails(element)) return;
  const title = element.getAttribute('title');
  if (title === null) return;
  element.setAttribute(STORED_TITLE_ATTRIBUTE, title);
  element.removeAttribute('title');
};

const suppressTitlesWithin = (root: ParentNode) => {
  if (root instanceof Element) suppressElementTitle(root);
  root.querySelectorAll(INTERACTIVE_TITLE_SELECTOR).forEach(suppressElementTitle);
};

const restoreTitlesWithin = (root: ParentNode) => {
  root.querySelectorAll(STORED_TITLE_SELECTOR).forEach((element) => {
    const title = element.getAttribute(STORED_TITLE_ATTRIBUTE);
    if (title !== null && !element.hasAttribute('title')) element.setAttribute('title', title);
    element.removeAttribute(STORED_TITLE_ATTRIBUTE);
  });
};

/**
 * 根据极简设置统一控制按钮的原生悬停文字。
 * SettingHint 使用独立弹层，并通过选择器明确排除，不受此开关影响。
 */
export function useButtonHoverDetails(enabled: WatchSource<boolean>) {
  let observer: MutationObserver | null = null;

  const stopSuppressing = () => {
    observer?.disconnect();
    observer = null;
  };

  const apply = (shouldShow: boolean) => {
    stopSuppressing();
    if (shouldShow) {
      restoreTitlesWithin(document);
      return;
    }

    suppressTitlesWithin(document);
    observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'attributes') {
          suppressElementTitle(mutation.target as Element);
          continue;
        }
        mutation.addedNodes.forEach((node) => {
          if (node instanceof Element) suppressTitlesWithin(node);
        });
      }
    });
    observer.observe(document.documentElement, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['title'],
    });
  };

  let stopWatch: (() => void) | null = null;
  onMounted(() => {
    stopWatch = watch(enabled, apply, { immediate: true });
  });

  onBeforeUnmount(() => {
    stopWatch?.();
    stopWatch = null;
    stopSuppressing();
    restoreTitlesWithin(document);
  });
}
