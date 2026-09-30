/** Shared test data builders for unit tests. */
import type { Board, BoardColumn, WorkItem, WorkItemState } from '../services/types';

let nextId = 1;

export function makeItem(overrides: Partial<WorkItem> = {}): WorkItem {
  const id = overrides.id ?? nextId++;
  return {
    id,
    title: `Item ${id}`,
    description: null,
    type: 'Bug',
    state: 'New',
    priority: 2,
    severity: '3 - Medium',
    assignedTo: null,
    areaPath: 'Tools\\QA',
    iterationPath: 'Current',
    program: null,
    programVersion: null,
    tags: [],
    aiModified: false,
    aiAgentIdentity: null,
    lastModifiedBy: 'tester',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

export function makeColumn(state: WorkItemState, items: WorkItem[] = [], wipLimit: number | null = null, itemCount?: number): BoardColumn {
  const count = itemCount ?? items.length;
  return {
    id: ['New', 'Active', 'Resolved', 'Closed'].indexOf(state) + 1,
    name: state,
    state,
    wipLimit,
    itemCount: count,
    isOverWipLimit: wipLimit !== null && count > wipLimit,
    items,
  };
}

export function makeBoard(columns: Partial<Record<'New' | 'Active' | 'Resolved' | 'Closed', WorkItem[]>> = {}, activeCount?: number): Board {
  return {
    name: 'QA Tools',
    columns: [
      makeColumn('New', columns.New ?? []),
      makeColumn('Active', columns.Active ?? [], 5, activeCount),
      makeColumn('Resolved', columns.Resolved ?? [], 5),
      makeColumn('Closed', columns.Closed ?? []),
    ],
    swimlanes: [{ id: 1, name: 'Default', sortOrder: 0, isDefault: true }],
    removedCount: 0,
    metadata: {
      types: ['Bug', 'Feature', 'UserStory', 'Epic', 'Task'],
      states: ['New', 'Active', 'Resolved', 'Closed', 'Removed'],
      severities: ['1 - Critical', '2 - High', '3 - Medium', '4 - Low'],
      priorities: { '1': 'Critical', '2': 'High', '3': 'Medium', '4': 'Low' },
      assignees: [],
      programs: ['ProveOut', 'CallOut'],
      tags: [],
    },
  };
}
