import { ref } from 'vue';

export type BanType = 'account' | 'device';
export interface BanDialogMeta { ciyuanxiId: string; nickname: string }
export interface BanDialogState {
  visible: boolean;
  banType: BanType;
  reason: string;
  ciyuanxiId: string;
  nickname: string;
  debug: boolean;
  resolver: ((confirmed: boolean) => void) | null;
}

const banDialogState = ref<BanDialogState>({ visible: false, banType: 'account', reason: '', ciyuanxiId: '', nickname: '', debug: false, resolver: null });

export function showBanDialog(banType: BanType, reason: string, meta: BanDialogMeta = { ciyuanxiId: '', nickname: '' }, options: { debug?: boolean } = {}): Promise<boolean> {
  banDialogState.value.resolver?.(false);
  return new Promise(resolve => {
    banDialogState.value = { visible: true, banType, reason, ciyuanxiId: meta.ciyuanxiId || '', nickname: meta.nickname || '', debug: options.debug === true, resolver: resolve };
  });
}

export function resolveBanDialog(confirmed: boolean): void {
  const state = banDialogState.value;
  state.resolver?.(confirmed);
  banDialogState.value = { visible: false, banType: state.banType, reason: '', ciyuanxiId: '', nickname: '', debug: false, resolver: null };
}

export function useBanDialog() { return { banDialogState, resolveBanDialog }; }
