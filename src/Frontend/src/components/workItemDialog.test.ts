import { describe, expect, it } from 'vitest';
import { makeItem } from '../test/fixtures';
import { canEditComment, commentButtonLabel, DEFAULT_HUMAN_NAME, describeChanges, diffForUpdate } from './workItemDialog';

describe('diffForUpdate', () => {
  const item = makeItem({ id: 1, title: 'T', description: 'd', assignedTo: 'Ana', priority: 2 });
  const form = {
    title: 'T', description: 'd', type: item.type, state: item.state, priority: 2, severity: item.severity,
    testResult: null as 'Passed' | 'Failed' | null, testedInVersion: null as string | null,
    program: null as string | null, programVersion: null as string | null, fixedInVersion: null as string | null, tags: [] as string[],
  };

  it('returns an empty patch when nothing changed', () => {
    expect(diffForUpdate(item, { ...form, title: '  T  ' })).toEqual({});
  });

  it('includes only changed fields', () => {
    expect(diffForUpdate(item, { ...form, state: 'Active', priority: 1 })).toEqual({ state: 'Active', priority: 1 });
  });

  it('sets, changes and clears the program version, ignoring surrounding spaces', () => {
    expect(diffForUpdate(item, { ...form, programVersion: ' 2.4.1 ' })).toEqual({ programVersion: '2.4.1' });
    const versioned = { ...item, programVersion: '2.4.1' };
    expect(diffForUpdate(versioned, { ...form, programVersion: '2.4.1' })).toEqual({});
    expect(diffForUpdate(versioned, { ...form, programVersion: null })).toEqual({ programVersion: '' });
  });

  it('sets, changes and clears the program', () => {
    expect(diffForUpdate(item, { ...form, program: 'ProveOut' })).toEqual({ program: 'ProveOut' });
    const tagged = { ...item, program: 'ProveOut' };
    expect(diffForUpdate(tagged, { ...form, program: 'ProveOut' })).toEqual({});
    expect(diffForUpdate(tagged, { ...form, program: null })).toEqual({ program: '' });
  });

  it('sends the full tag list only when the set changed (ignoring order and case)', () => {
    const tagged = { ...item, tags: ['login', 'ui'] };
    expect(diffForUpdate(tagged, { ...form, tags: ['UI', 'login'] })).toEqual({});
    expect(diffForUpdate(tagged, { ...form, tags: ['ui'] })).toEqual({ tags: ['ui'] });
    expect(diffForUpdate(tagged, { ...form, tags: [] })).toEqual({ tags: [] });
  });

  it('sends an empty string to clear the description', () => {
    expect(diffForUpdate(item, { ...form, description: '' })).toEqual({ description: '' });
  });

  it('never touches the assignee or area path, which the board no longer edits', () => {
    const patch = diffForUpdate(item, { ...form, title: 'Changed' });
    expect(patch).toEqual({ title: 'Changed' });
    expect(patch).not.toHaveProperty('assignedTo');
    expect(patch).not.toHaveProperty('areaPath');
  });
});

describe('describeChanges', () => {
  it('describes set, cleared and changed fields', () => {
    const lines = describeChanges({
      id: 1, workItemId: 1, changeDate: '', author: 'a', isAiAction: false, agentName: null, comment: null,
      changedFields: {
        Title: { old: null, new: 'New title' },
        AssignedTo: { old: 'Ana', new: null },
        State: { old: 'New', new: 'Active' },
      },
    });
    // 1.19.0: states read as the board names them ("To Do" for New).
    expect(lines).toEqual(['Title set to "New title"', 'AssignedTo cleared (was "Ana")', 'State: "To Do" → "Active"']);
  });
});

describe('canEditComment (1.13.0)', () => {
  const entry = {
    id: 5, workItemId: 1, changeDate: '2026-10-02T10:00:00Z', author: 'Robert', isAiAction: false,
    agentName: null, changedFields: {}, comment: 'Mine',
  };

  it('lets the author edit, ignoring case and spaces', () => {
    expect(canEditComment(entry, 'Robert')).toBe(true);
    expect(canEditComment(entry, '  robert ')).toBe(true);
  });

  it('refuses other people, AI comments and entries without a comment', () => {
    expect(canEditComment(entry, 'Alex')).toBe(false);
    expect(canEditComment({ ...entry, isAiAction: true }, 'Robert')).toBe(false);
    expect(canEditComment({ ...entry, comment: null }, 'Robert')).toBe(false);
  });

  it('treats no name as the server default name', () => {
    const anonymous = { ...entry, author: DEFAULT_HUMAN_NAME };
    expect(canEditComment(anonymous, null)).toBe(true);
    expect(canEditComment(anonymous, '   ')).toBe(true);
    expect(canEditComment(entry, null)).toBe(false);
  });
});

describe('commentButtonLabel (1.13.0)', () => {
  it('says what the click will do', () => {
    expect(commentButtonLabel(true, '')).toBe('Add comment');
    expect(commentButtonLabel(false, '')).toBe('Add comment');
    expect(commentButtonLabel(true, 'Closed')).toBe('Comment & move to Closed');
    expect(commentButtonLabel(false, 'Resolved')).toBe('Move to Resolved');
  });
});

describe('describeChanges for files (1.14.0)', () => {
  const entry = (old: string | null, nw: string | null) => ({
    id: 1, workItemId: 1, changeDate: '2026-10-05T10:00:00Z', author: 'R', isAiAction: false, agentName: null,
    changedFields: { Files: { old, new: nw } }, comment: null,
  });

  it('says attached or removed', () => {
    expect(describeChanges(entry(null, 'app.log'))).toEqual(['Attached file "app.log"']);
    expect(describeChanges(entry('app.log', null))).toEqual(['Removed file "app.log"']);
  });
});

describe('Fixed in version (1.17.0)', () => {
  const item = makeItem({ id: 9, type: 'Bug', fixedInVersion: null });
  const form = {
    title: item.title, description: item.description, type: item.type, state: item.state, priority: item.priority,
    severity: item.severity, testResult: null as 'Passed' | 'Failed' | null, testedInVersion: null as string | null, program: item.program ?? null,
    programVersion: item.programVersion ?? null, fixedInVersion: null as string | null, tags: item.tags ?? [],
  };

  it('sends a newly entered version, trimmed', () => {
    expect(diffForUpdate(item, { ...form, fixedInVersion: ' 2.4.3 ' })).toEqual({ fixedInVersion: '2.4.3' });
  });

  it('clears it with an empty string', () => {
    const fixed = { ...item, fixedInVersion: '2.4.3' };
    expect(diffForUpdate(fixed, { ...form, fixedInVersion: null })).toEqual({ fixedInVersion: '' });
    expect(diffForUpdate(fixed, { ...form, fixedInVersion: '2.4.3' })).toEqual({});
  });
});

describe('Test result (1.19.0)', () => {
  const resolved = makeItem({ id: 21, type: 'Bug', state: 'Resolved', fixedInVersion: '2.4.3' });
  const form = {
    title: resolved.title, description: resolved.description, type: resolved.type, state: resolved.state,
    priority: resolved.priority, severity: resolved.severity, program: resolved.program ?? null,
    programVersion: resolved.programVersion ?? null, fixedInVersion: '2.4.3' as string | null,
    testResult: null as 'Passed' | 'Failed' | null, testedInVersion: null as string | null, tags: resolved.tags ?? [],
  };

  it('Failed in a version sends the card back to To Do', () => {
    expect(diffForUpdate(resolved, { ...form, testResult: 'Failed', testedInVersion: '2.4.3' }))
      .toEqual({ testResult: 'Failed', testedInVersion: '2.4.3', state: 'New' });
  });

  it('Passed in a version closes the card', () => {
    expect(diffForUpdate(resolved, { ...form, testResult: 'Passed', testedInVersion: '2.4.3' }))
      .toEqual({ testResult: 'Passed', testedInVersion: '2.4.3', state: 'Closed' });
  });

  it('the result wins over a different State choice, and an unchanged result moves nothing', () => {
    expect(diffForUpdate(resolved, { ...form, state: 'Active', testResult: 'Passed', testedInVersion: '2.4.3' }).state).toBe('Closed');
    const failedBefore = { ...resolved, state: 'Active' as const, testResult: 'Failed' as const, testedInVersion: '2.4.3' };
    expect(diffForUpdate(failedBefore, { ...form, state: 'Active', testResult: 'Failed', testedInVersion: '2.4.3' })).toEqual({});
  });

  it('a new version for the same result moves the card again', () => {
    const failedBefore = { ...resolved, state: 'Resolved' as const, testResult: 'Failed' as const, testedInVersion: '2.4.3' };
    expect(diffForUpdate(failedBefore, { ...form, testResult: 'Failed', testedInVersion: '2.4.4' }))
      .toEqual({ testedInVersion: '2.4.4', state: 'New' });
  });
});
