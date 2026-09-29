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
      h('span', {}, `#${item.id}`),
      h('span', { title: `Priority ${item.priority} - ${priorityLabel}` }, `P${item.priority} ${priorityLabel}`),
      item.type === 'Bug' ? h('span', {}, `Severity ${item.severity}`) : null,
      h('span', {}, item.assignedTo ? `Assigned to ${item.assignedTo}` : 'Unassigned'),
    ),
  );

  return h('li', { class: 'card-item', 'data-card-item': item.id }, article);
}
