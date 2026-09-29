import { describe, expect, it } from 'vitest';
import { makeItem } from '../test/fixtures';
import { describeChanges, diffForUpdate } from './workItemDialog';

describe('diffForUpdate', () => {
  const item = makeItem({ id: 1, title: 'T', description: 'd', assignedTo: 'Ana', priority: 2 });
  const form = {
    title: 'T', description: 'd', type: item.type, state: item.state, priority: 2, severity: item.severity,
    assignedTo: 'Ana', areaPath: item.areaPath, iterationPath: item.iterationPath,
    program: null as string | null, tags: [] as string[],
  };

  it('returns an empty patch when nothing changed', () => {
    expect(diffForUpdate(item, { ...form, title: '  T  ' })).toEqual({});
  });

  it('includes only changed fields', () => {
    expect(diffForUpdate(item, { ...form, state: 'Active', priority: 1 })).toEqual({ state: 'Active', priority: 1 });
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

  it('sends empty strings to clear assignee and description', () => {
    expect(diffForUpdate(item, { ...form, assignedTo: ' ', description: '' })).toEqual({ assignedTo: '', description: '' });
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
