<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useBanDialog } from '../../composables/useBanDialog';
import { submitAppeal } from '../../services/usageStats';
import { useToast } from '../../composables/toast';

const { showToast } = useToast();
const { banDialogState, resolveBanDialog } = useBanDialog();
const appealText = ref('');
const appealing = ref(false);
const submitting = ref(false);
const title = computed(() => banDialogState.value.banType === 'device' ? '设备已被封禁' : '账号已被封禁');
watch(() => banDialogState.value.visible, visible => { if (!visible) { appealText.value = ''; appealing.value = false; submitting.value = false; } });

async function handleSubmit() {
  const content = appealText.value.trim();
  if (!content) return showToast('请填写申诉内容', 'error');
  submitting.value = true;
  try {
    if (banDialogState.value.debug) await new Promise(resolve => setTimeout(resolve, 600));
    else await submitAppeal(banDialogState.value.ciyuanxiId, banDialogState.value.nickname, content);
    showToast(banDialogState.value.debug ? '（调试）申诉流程已完成' : '申诉已提交，请耐心等待处理', 'success');
    resolveBanDialog(false);
  } catch (error) {
    showToast(error instanceof Error ? error.message : '申诉提交失败', 'error');
  } finally { submitting.value = false; }
}
</script>

<template>
  <Teleport to="body"><Transition name="ban-modal" appear>
    <div v-if="banDialogState.visible" class="ban-overlay">
      <div class="ban-card">
        <div class="ban-icon">!</div>
        <h3>{{ title }}</h3>
        <p class="ban-account">{{ banDialogState.ciyuanxiId ? `弦予号 ${banDialogState.ciyuanxiId}` : '当前设备已受限' }}</p>
        <div class="ban-content">
          <p v-if="!appealing">{{ banDialogState.reason || '如有疑问，请联系管理员或提交申诉。' }}</p>
          <template v-else><textarea v-model="appealText" maxlength="1000" rows="4" placeholder="请填写申诉理由…"></textarea><small>{{ appealText.length }} / 1000</small></template>
        </div>
        <div class="ban-actions" v-if="!appealing"><button @click="appealing = true">申诉</button><button class="primary" @click="resolveBanDialog(true)">确认</button></div>
        <div class="ban-actions" v-else><button :disabled="submitting" @click="appealing = false">取消</button><button class="primary" :disabled="submitting" @click="handleSubmit">{{ submitting ? '提交中…' : '提交申诉' }}</button></div>
      </div>
    </div>
  </Transition></Teleport>
</template>

<style>
.ban-overlay{position:fixed;inset:0;z-index:10000;display:grid;place-items:center;padding:1rem;background:rgb(0 0 0/.4);backdrop-filter:blur(4px)}
.ban-card{width:min(90vw,400px);padding:24px 22px 20px;border:1px solid rgb(0 0 0/.06);border-radius:16px;background:#fff;color:#1f2937;text-align:center;box-shadow:0 20px 60px rgb(0 0 0/.18)}
.ban-icon{display:grid;place-items:center;width:48px;height:48px;margin:0 auto 14px;border-radius:999px;background:rgb(var(--theme-accent-rgb)/.12);color:var(--theme-accent);font-size:24px;font-weight:800}.ban-card h3{margin:0;font-size:1.05rem}.ban-account{margin:6px 0 16px;color:#6b7280;font-size:.78rem}.ban-content{margin-bottom:18px;padding:12px 14px;border-radius:12px;background:rgb(0 0 0/.03);text-align:left}.ban-content p{margin:0;font-size:.85rem;line-height:1.55;white-space:pre-line}.ban-content textarea{box-sizing:border-box;width:100%;min-height:96px;resize:vertical;border:1px solid rgb(0 0 0/.1);border-radius:10px;padding:10px 12px;outline:none}.ban-content textarea:focus{border-color:var(--theme-accent)}.ban-content small{display:block;margin-top:6px;text-align:right;color:#6b7280}.ban-actions{display:flex;gap:10px}.ban-actions button{flex:1;height:40px;border:1px solid rgb(148 163 184/.24);border-radius:999px;background:transparent;cursor:pointer}.ban-actions .primary{border-color:transparent;background:var(--theme-accent);color:#fff}.ban-actions .primary:hover{background:var(--theme-accent-hover)}html.dark .ban-card{border-color:rgb(255 255 255/.08);background:#262626;color:rgb(255 255 255/.92)}html.dark .ban-content{background:rgb(255 255 255/.04)}html.dark .ban-content textarea{border-color:rgb(255 255 255/.12);background:rgb(255 255 255/.05);color:#fff}.ban-modal-enter-active,.ban-modal-leave-active{transition:opacity .2s ease}.ban-modal-enter-from,.ban-modal-leave-to{opacity:0}
</style>
