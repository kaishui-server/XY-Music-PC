<script setup lang="ts">
import { computed } from 'vue';
import { useProfileLimitDialog } from '../../composables/useProfileLimitDialog';

const { profileLimitDialogState, resolveProfileLimitDialog } = useProfileLimitDialog();

const copy = computed(() => {
  const targetName = profileLimitDialogState.value.target === 'avatar' ? '头像' : '昵称';
  if (profileLimitDialogState.value.blocked) {
    return {
      badge: '',
      title: `${targetName}暂不能修改`,
      description: profileLimitDialogState.value.message || `${targetName}今日已修改过，请明天再试。`,
      confirmText: '我知道了',
    };
  }
  return {
    badge: '',
    title: profileLimitDialogState.value.target === 'avatar' ? '更换头像提示' : '修改昵称提示',
    description: `${targetName}每日只能修改 1 次，提交后需要等待管理员审核。审核通过前会继续显示当前${targetName}。`,
    confirmText: profileLimitDialogState.value.target === 'avatar' ? '继续选择头像' : '继续修改昵称',
  };
});
</script>

<template>
  <Teleport to="body">
    <Transition name="profile-limit-modal" appear>
      <div
        v-if="profileLimitDialogState.visible"
        class="profile-limit-overlay"
      >
        <div class="profile-limit-card">
          <div v-if="copy.badge" class="profile-limit-badge">{{ copy.badge }}</div>
          <h3>{{ copy.title }}</h3>
          <p>{{ copy.description }}</p>
          <div class="profile-limit-note">请确认本次修改内容无误后再继续。</div>
          <div class="profile-limit-actions" :class="{ single: profileLimitDialogState.blocked }">
            <button
              v-if="!profileLimitDialogState.blocked"
              type="button"
              class="profile-limit-button secondary"
              @click="resolveProfileLimitDialog(false)"
            >取消</button>
            <button
              type="button"
              class="profile-limit-button primary"
              @click="resolveProfileLimitDialog(!profileLimitDialogState.blocked)"
            >{{ copy.confirmText }}</button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style>
.profile-limit-overlay { position: fixed; inset: 0; z-index: 320; display: grid; place-items: center; padding: 1rem; background: rgb(0 0 0 / 0.4); backdrop-filter: blur(4px); }
.profile-limit-card { width: min(90vw, 400px); padding: 24px 22px 20px; border: 1px solid rgb(0 0 0 / 0.06); border-radius: 16px; background: #fff; color: #1f2937; text-align: center; box-shadow: 0 20px 60px rgb(0 0 0 / 0.18), 0 4px 16px rgb(0 0 0 / 0.08); }
.profile-limit-badge { display: inline-flex; align-items: center; height: 24px; margin-bottom: 10px; padding: 0 10px; border-radius: 999px; background: rgb(var(--theme-accent-rgb) / 0.1); color: var(--theme-accent); font-size: 12px; font-weight: 700; }
.profile-limit-card h3 { margin: 0; color: #111827; font-size: 19px; font-weight: 800; }
.profile-limit-card p { margin: 10px 0 0; color: rgb(31 41 55 / 0.68); font-size: 14px; line-height: 1.7; }
.profile-limit-note { margin-top: 16px; padding: 10px 12px; border-radius: 14px; background: rgb(245 158 11 / 0.1); color: #b45309; font-size: 12px; }
.profile-limit-actions { display: grid; grid-template-columns: 1fr 1.25fr; gap: 10px; margin-top: 20px; }
.profile-limit-actions.single { grid-template-columns: 1fr; }
.profile-limit-button { height: 40px; border: 1px solid transparent; border-radius: 999px; font-size: .85rem; font-weight: 600; cursor: pointer; transition: 0.18s ease; }
.profile-limit-button:active { transform: scale(0.97); }
.profile-limit-button.secondary { background: rgb(17 24 39 / 0.06); color: rgb(31 41 55 / 0.72); }
.profile-limit-button.primary { background: var(--theme-accent); color: #fff; box-shadow: 0 14px 28px rgb(var(--theme-accent-rgb) / 0.24); }
.profile-limit-button.primary:hover { background: var(--theme-accent-hover); }
html.dark .profile-limit-overlay { background: rgb(0 0 0 / 0.56); }
html.dark .profile-limit-card { border-color: rgb(255 255 255 / 0.08); background: #262626; color: rgb(255 255 255 / 0.92); }
html.dark .profile-limit-card h3 { color: rgb(255 255 255 / 0.96); }
html.dark .profile-limit-card p { color: rgb(255 255 255 / 0.6); }
html.dark .profile-limit-note { background: rgb(245 158 11 / 0.12); color: #fbbf24; }
html.dark .profile-limit-button.secondary { background: rgb(255 255 255 / 0.08); color: rgb(255 255 255 / 0.72); }
.profile-limit-modal-enter-active, .profile-limit-modal-leave-active { transition: opacity 0.24s ease; }
.profile-limit-modal-enter-from, .profile-limit-modal-leave-to { opacity: 0; }
</style>
