import { describe, expect, it } from 'vitest';
import { makeItem } from '../test/fixtures';
import { aiTooltipText, createCard } from './card';

describe('createCard', () => {
  it('renders title, id and priority as text (no HTML injection)', () => {
    const li = createCard(makeItem({ id: 42, title: '<img src=x onerror=alert(1)>', priority: 1, assignedTo: 'Ana' }));
    const article = li.querySelector('article')!;

    expect(article.dataset.cardId).toBe('42');
    expect(article.getAttribute('tabindex')).toBe('0');
    expect(article.getAttribute('draggable')).toBe('true');
    expect(li.querySelector('img')).toBeNull();
    expect(li.querySelector('.card-title')!.textContent).toBe('<img src=x onerror=alert(1)>');
    expect(li.textContent).toContain('#42');
    expect(li.textContent).toContain('P1 Critical');
    // Assignee is no longer shown (1.6.0): AI agents identify themselves with the AI badge.
    expect(li.textContent).not.toContain('Ana');
    expect(li.textContent).not.toContain('Unassigned');
  });

  it('shows the program and tags as text inside the described meta (1.4.0)', () => {
    const li = createCard(makeItem({ id: 9, program: 'ProveOut', tags: ['login', '<b>x</b>'] }));
    const meta = li.querySelector('#card-meta-9')!;
    expect(meta.querySelector('[data-testid="card-program"]')!.textContent).toBe('Program ProveOut');
    const chips = [...meta.querySelectorAll('.tag-chip')].map((c) => c.textContent);
    expect(chips).toEqual(['login, ', '<b>x</b>']);
    expect(meta.querySelector('b')).toBeNull();
  });

  it('shows the program version on Bug cards only (1.7.0)', () => {
    const bug = createCard(makeItem({ type: 'Bug', programVersion: '2.4.1' }));
    expect(bug.querySelector('[data-testid="card-program-version"]')!.textContent).toBe('Version 2.4.1');

    const feature = createCard(makeItem({ type: 'Feature', programVersion: '2.4.1' }));
    expect(feature.querySelector('[data-testid="card-program-version"]')).toBeNull();
    expect(createCard(makeItem({ type: 'Bug', programVersion: null })).querySelector('[data-testid="card-program-version"]')).toBeNull();
  });

  it('shows "Unread" and a bold comment count when this browser has unread comments (1.10.0)', () => {
    const li = createCard(makeItem({ id: 5, unread: true, commentCount: 2 }));
    const pill = li.querySelector('#card-meta-5 [data-testid="unread-pill"]')!;
    expect(pill.textContent).toBe('Unread: new comments since you last opened it');
    expect(li.querySelector('article')!.dataset.unread).toBe('true');
    const count = li.querySelector('[data-testid="card-comments"]')!;
    expect(count.classList.contains('comments-unread')).toBe(true);
    expect(count.getAttribute('title')).toBe('2 comments, some unread');
  });

  it('shows a plain count when every comment is read', () => {
    const li = createCard(makeItem({ unread: false, commentCount: 3 }));
    expect(li.querySelector('[data-testid="unread-pill"]')).toBeNull();
    expect(li.querySelector('[data-testid="card-comments"]')!.classList.contains('comments-read')).toBe(true);
    expect(li.querySelector('article')!.hasAttribute('data-unread')).toBe(false);
  });

  it('can be unread and waiting for an AI at the same time', () => {
    const li = createCard(makeItem({ unread: true, discussionStatus: 'AwaitingAgent', commentCount: 2 }));
    expect(li.querySelector('[data-testid="unread-pill"]')).not.toBeNull();
    expect(li.querySelector('[data-testid="discussion-pill"]')!.textContent).toBe('Waiting for AI');
  });

  it('shows "Waiting for AI" when a person commented last, and nothing when quiet', () => {
    const waiting = createCard(makeItem({ discussionStatus: 'AwaitingAgent', commentCount: 1 }));
    expect(waiting.querySelector('[data-testid="discussion-pill"]')!.textContent).toBe('Waiting for AI');
    expect(waiting.querySelector('[data-testid="card-comments"] .sr-only')!.textContent).toBe('1 comment');

    const quiet = createCard(makeItem({ discussionStatus: null, commentCount: 0 }));
    expect(quiet.querySelector('[data-testid="discussion-pill"]')).toBeNull();
    expect(quiet.querySelector('[data-testid="card-comments"]')).toBeNull();
    expect(quiet.querySelector('article')!.hasAttribute('data-discussion')).toBe(false);
  });

  it('omits program and tag markup when there are none', () => {
    const li = createCard(makeItem({ program: null, tags: [] }));
    expect(li.querySelector('[data-testid="card-program"]')).toBeNull();
    expect(li.querySelector('[data-testid="card-tags"]')).toBeNull();
  });

  it('is labelled by its title and described by meta + keyboard instructions', () => {
    const article = createCard(makeItem({ id: 7 })).querySelector('article')!;
    expect(article.getAttribute('aria-labelledby')).toBe('card-title-7');
    expect(article.getAttribute('aria-describedby')).toBe('card-meta-7 kb-instructions');
  });

  it('shows severity only for bugs', () => {
    expect(createCard(makeItem({ type: 'Bug', severity: '1 - Critical' })).textContent).toContain('Severity 1 - Critical');
    expect(createCard(makeItem({ type: 'Task', severity: '1 - Critical' })).textContent).not.toContain('Severity');
  });

  it('has no AI badge for human-only items', () => {
    expect(createCard(makeItem()).querySelector('[data-testid="ai-badge"]')).toBeNull();
  });

  it('renders the accessible purple AI badge with the spec tooltip', () => {
    const li = createCard(makeItem({ id: 9, aiModified: true, aiAgentIdentity: 'Codex-Fixer' }));
    const badge = li.querySelector<HTMLElement>('[data-testid="ai-badge"]')!;
    const tooltip = li.querySelector<HTMLElement>('[role="tooltip"]')!;

    expect(badge.classList).toContain('ai-badge');
    expect(badge.getAttribute('title')).toBe('Updated by AI Agent: Codex-Fixer');
    expect(badge.getAttribute('aria-describedby')).toBe(tooltip.id);
    expect(tooltip.textContent).toBe('Updated by AI Agent: Codex-Fixer');
    expect(badge.textContent).toContain('🤖');
    expect(badge.querySelector('[aria-hidden="true"]')!.textContent).toBe('🤖');
    expect(badge.querySelector('.sr-only')!.textContent).toContain('Updated by AI Agent: Codex-Fixer');
  });

  it('falls back when the agent identity is missing', () => {
    expect(aiTooltipText(null)).toBe('Updated by AI Agent: Unknown agent');
  });
});
