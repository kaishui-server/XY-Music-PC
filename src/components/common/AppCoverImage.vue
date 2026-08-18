<script setup lang="ts">
import { computed, ref, watch } from 'vue';

import { useDefaultCover } from '../../composables/usePlayerDetailFallbackCover';

defineOptions({ inheritAttrs: false });

const props = withDefaults(defineProps<{
  src?: string | null;
  alt?: string;
}>(), {
  src: '',
  alt: '',
});

const emit = defineEmits<{
  (event: 'primary-error', value: Event): void;
  (event: 'fallback-error', value: Event): void;
}>();

const defaultCoverUrl = useDefaultCover();
const primaryFailed = ref(false);
const fallbackFailed = ref(false);

const primaryUrl = computed(() => props.src?.trim() ?? '');
const displayedUrl = computed(() => {
  if (primaryUrl.value && !primaryFailed.value) return primaryUrl.value;
  if (
    defaultCoverUrl.value
    && defaultCoverUrl.value !== primaryUrl.value
    && !fallbackFailed.value
  ) {
    return defaultCoverUrl.value;
  }
  return '';
});

watch([primaryUrl, defaultCoverUrl], () => {
  primaryFailed.value = false;
  fallbackFailed.value = false;
});

const handleError = (event: Event) => {
  if (displayedUrl.value === primaryUrl.value) {
    primaryFailed.value = true;
    emit('primary-error', event);
    return;
  }

  fallbackFailed.value = true;
  emit('fallback-error', event);
};
</script>

<template>
  <img
    v-if="displayedUrl"
    v-bind="$attrs"
    :src="displayedUrl"
    :alt="alt"
    @error="handleError"
  />
  <slot v-else />
</template>
