import { describe, expect, it } from 'vitest';
import { makeItem } from '../test/fixtures';
import { canEditComment, commentButtonLabel, DEFAULT_HUMAN_NAME, describeChanges, diffForUpdate } from './workItemDialog';

describe('diffForUpdate', () => {
  const item = makeItem({ id: 1, title: 'T', description: 'd', assignedTo: 'Ana', priority: 2 });
  const form = {
    title: 'T', description: 'd', type: item.type, state: item.state, priority: 2, severity: item.severity,
    iterationPath: item.iterationPath,
    program: null as string | null, programVersion: null as string | null, tags: [] as string[],
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
    expect(lines).toEqual(['Title set to "New title"', 'AssignedTo cleared (was "Ana")', 'State: "New" → "Active"']);
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
