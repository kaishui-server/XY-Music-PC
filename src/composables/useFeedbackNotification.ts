import { ref } from 'vue';
import { getStoredAuth, signedRequest } from '../services/auth/authService';
import type { Announcement } from '../utils/announcement';

const feedbackVisible = ref(false);
const currentFeedbackNotification = ref<Announcement | null>(null);
const currentNotificationId = ref(0);
const isFetchingFeedback = ref(false);

interface FeedbackNotificationRaw { id: number; title: string; assignee: string; resolve_note: string; resolve_images: string[]; replied_at: string; updated_at: string }

export function useFeedbackNotification() {
  const checkFeedbackNotification = async (announcementVisible = false) => {
    if (isFetchingFeedback.value || feedbackVisible.value || announcementVisible) return;
    const auth = getStoredAuth();
    if (!auth?.user) return;
    isFetchingFeedback.value = true;
    try {
      const data = await signedRequest<{ list: FeedbackNotificationRaw[] }>('get_my_feedback_notifications', { xymusic_id: auth.user.ciyuanxi_id ?? auth.user.xymusic_id ?? auth.user.id ?? '' }, { fetchTimeoutMs: 15_000, timeoutMs: 18_000 });
      const item = data?.list?.[0];
      if (!item) return;
      currentNotificationId.value = item.id;
      currentFeedbackNotification.value = { id: `feedback-${item.id}`, title: '反馈处理完成', content: `您提交的反馈「${item.title || '无标题'}」已处理完成。\n\n处理管理员：${item.assignee || '管理员'}\n完成说明：${item.resolve_note || '（无说明）'}`, type: 'info', date: item.replied_at?.slice(0, 10) || '', updatedAt: item.updated_at, images: Array.isArray(item.resolve_images) ? item.resolve_images : [] };
      feedbackVisible.value = true;
    } catch (error) { console.error('[FeedbackNotification] 获取通知失败', error); }
    finally { isFetchingFeedback.value = false; }
  };
  const closeFeedbackNotification = async () => {
    const auth = getStoredAuth();
    if (currentNotificationId.value > 0) {
      try { await signedRequest('confirm_feedback_notification', { id: currentNotificationId.value, xymusic_id: auth?.user?.ciyuanxi_id ?? auth?.user?.xymusic_id ?? auth?.user?.id ?? '' }); } catch (error) { console.error('[FeedbackNotification] 确认通知失败', error); }
    }
    feedbackVisible.value = false; currentFeedbackNotification.value = null; currentNotificationId.value = 0;
  };
  return { feedbackVisible, currentFeedbackNotification, checkFeedbackNotification, closeFeedbackNotification };
}
