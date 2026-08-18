import { onMounted, onUnmounted, watch } from 'vue';
import { useAuthStore } from '../features/auth/store';
import { checkBanStatus } from '../services/auth/authService';
import { showBanDialog } from './useBanDialog';

const BAN_CHECK_INTERVAL_MS = 30_000;

export function useBanCheck() {
  const authStore = useAuthStore();
  let timer: ReturnType<typeof setInterval> | null = null;
  let bannedShown = false;

  async function runCheck() {
    if (!authStore.isLoggedIn) return;
    const status = await checkBanStatus();
    if (!status.banned || bannedShown) return;
    bannedShown = true;
    authStore.reset();
    await showBanDialog(status.type, status.reason, { ciyuanxiId: status.ciyuanxiId, nickname: status.nickname });
  }
  function stop() { if (timer) clearInterval(timer); timer = null; }
  function start() { stop(); void runCheck(); timer = setInterval(() => void runCheck(), BAN_CHECK_INTERVAL_MS); }
  onMounted(() => watch(() => authStore.isLoggedIn, loggedIn => { if (loggedIn) { bannedShown = false; start(); } else stop(); }, { immediate: true }));
  onUnmounted(stop);
  return { runCheck };
}
