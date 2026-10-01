/**
 * Work item card rendering (Azure DevOps style). Pure DOM factory: behaviour
 * (drag-and-drop, keyboard moves, opening) is wired by the board.
 */
import { PRIORITY_LABELS, TYPE_LABELS, type WorkItem } from '../services/types';
import { h } from './dom';

/** Tooltip text mandated by spec 4.1. */
export function aiTooltipText(agentIdentity: string | null): string {
  return `Updated by AI Agent: ${agentIdentity ?? 'Unknown agent'}`;
}

/** Purple robot badge for AI-modified items (4.5:1+ contrast in both themes). */
export function createAiBadge(item: Pick<WorkItem, 'id' | 'aiAgentIdentity'>, idPrefix = 'card'): HTMLElement {
  const tooltipId = `${idPrefix}-ai-tip-${item.id}`;
  const text = aiTooltipText(item.aiAgentIdentity);
  return h(
    'span',
    { class: 'ai-badge-wrap' },
    h(
      'span',
      { class: 'ai-badge', title: text, 'aria-describedby': tooltipId, 'data-testid': 'ai-badge' },
      h('span', { 'aria-hidden': 'true' }, '🤖'),
      'AI',
      h('span', { class: 'sr-only' }, `. ${text}`),
    ),
    h('span', { class: 'ai-tooltip', role: 'tooltip', id: tooltipId }, text),
  );
}

/**
 * Discussion pills for a card: "Unread" when this browser has not read its
 * newest comment (1.10.0), "Waiting for AI" when a person commented and no AI
 * agent has replied yet (1.9.0).
 */
export function createDiscussionPills(item: Pick<WorkItem, 'discussionStatus' | 'unread'>): HTMLElement[] {
  const pills: HTMLElement[] = [];
  if (item.unread) {
    pills.push(h('span', {
      class: 'discussion-pill discussion-unread', 'data-testid': 'unread-pill', title: 'New comments since you last opened this card',
    }, 'Unread', h('span', { class: 'sr-only' }, ': new comments since you last opened it')));
  }
  if (item.discussionStatus === 'AwaitingAgent') {
    pills.push(h('span', {
      class: 'discussion-pill discussion-waiting', 'data-testid': 'discussion-pill', title: 'A person commented and no AI agent has replied yet',
    }, 'Waiting for AI'));
  }
  return pills;
}

/** Builds the list item for one card. */
export function createCard(item: WorkItem): HTMLLIElement {
  const titleId = `card-title-${item.id}`;
  const metaId = `card-meta-${item.id}`;
  const priorityLabel = PRIORITY_LABELS[item.priority] ?? String(item.priority);

  const article = h(
    'article',
    {
      class: `card card-type-${item.type}`,
      tabindex: 0,
      draggable: 'true',
      'data-card-id': item.id,
      'data-state': item.state,
      'data-discussion': item.discussionStatus ?? null,
      'data-unread': item.unread ? 'true' : null,
      'aria-labelledby': titleId,
      'aria-describedby': `${metaId} kb-instructions`,
      'aria-roledescription': 'movable card',
    },
    h(
      'div',
      { class: 'flex items-center justify-between gap-2' },
      h('span', { class: 'card-type-label' }, TYPE_LABELS[item.type] ?? item.type),
      item.aiModified ? createAiBadge(item) : null,
    ),
    h('button', { type: 'button', class: 'card-title', id: titleId, 'data-action': 'open' }, item.title),
    h(
      'div',
      { class: 'card-meta', id: metaId },
      ...createDiscussionPills(item),
      h('span', {}, `#${item.id}`),
      h('span', { title: `Priority ${item.priority} - ${priorityLabel}` }, `P${item.priority} ${priorityLabel}`),
      item.type === 'Bug' ? h('span', {}, `Severity ${item.severity}`) : null,
      item.type === 'Bug' && item.programVersion
        ? h('span', { 'data-testid': 'card-program-version', title: 'Program version the bug was found in' }, `Version ${item.programVersion}`)
        : null,
      item.commentCount
        ? h('span', {
            class: item.unread ? 'comments-unread' : 'comments-read', 'data-testid': 'card-comments',
            title: `${item.commentCount} comment${item.commentCount === 1 ? '' : 's'}${item.unread ? ', some unread' : ', all read'}`,
          },
            h('span', { 'aria-hidden': 'true' }, `💬 ${item.commentCount}`),
            h('span', { class: 'sr-only' }, `${item.commentCount} comment${item.commentCount === 1 ? '' : 's'}`))
        : null,
      item.program ? h('span', { class: 'card-program', 'data-testid': 'card-program' }, h('span', { class: 'sr-only' }, 'Program '), item.program) : null,
      item.tags?.length
        ? h(
            'span',
            { class: 'card-tags', 'data-testid': 'card-tags' },
            h('span', { class: 'sr-only' }, 'Tags: '),
            ...item.tags.map((t, i) => h('span', { class: 'tag-chip' }, t, i < item.tags.length - 1 ? h('span', { class: 'sr-only' }, ', ') : null)),
          )
        : null,
    ),
  );

  return h('li', { class: 'card-item', 'data-card-item': item.id }, article);
}
