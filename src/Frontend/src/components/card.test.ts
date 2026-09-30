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
