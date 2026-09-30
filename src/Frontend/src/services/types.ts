/**
 * TypeScript mirrors of the backend DTOs (KanbanBoard.Api/Models/Dtos.cs).
 * Enum values travel as their string names.
 */

export const WORK_ITEM_TYPES = ['Bug', 'Feature', 'UserStory', 'Epic', 'Task'] as const;
export type WorkItemType = (typeof WORK_ITEM_TYPES)[number];

export const WORK_ITEM_STATES = ['New', 'Active', 'Resolved', 'Closed', 'Removed'] as const;
export type WorkItemState = (typeof WORK_ITEM_STATES)[number];

/** States that have a board column (Removed items are hidden from the board). */
export const BOARD_STATES: readonly WorkItemState[] = ['New', 'Active', 'Resolved', 'Closed'];

export const SEVERITIES = ['1 - Critical', '2 - High', '3 - Medium', '4 - Low'] as const;

export const PRIORITY_LABELS: Readonly<Record<number, string>> = {
  1: 'Critical',
  2: 'High',
  3: 'Medium',
  4: 'Low',
};

/** Human-friendly labels for type names. */
export const TYPE_LABELS: Readonly<Record<WorkItemType, string>> = {
  Bug: 'Bug',
  Feature: 'Feature',
  UserStory: 'User Story',
  Epic: 'Epic',
  Task: 'Task',
};

export interface FieldChange {
  old: string | null;
  new: string | null;
}

export interface WorkItemHistoryEntry {
  id: number;
  workItemId: number;
  changeDate: string;
  author: string;
  isAiAction: boolean;
  agentName: string | null;
  changedFields: Record<string, FieldChange>;
  comment: string | null;
}

export interface WorkItem {
  id: number;
  title: string;
  description: string | null;
  type: WorkItemType;
  state: WorkItemState;
  priority: number;
  severity: string;
  assignedTo: string | null;
  areaPath: string;
  iterationPath: string;
  /** Program name (1.4.0), or null. */
  program: string | null;
  /** Version of the program a bug was found in (1.7.0), or null. */
  programVersion: string | null;
  /** Tags, sorted (1.4.0). */
  tags: string[];
  aiModified: boolean;
  aiAgentIdentity: string | null;
  /** When a human last commented (1.8.0; used by agents), or null. */
  lastHumanCommentAt?: string | null;
  lastHumanCommentBy?: string | null;
  lastModifiedBy: string;
  createdAt: string;
  updatedAt: string;
  history?: WorkItemHistoryEntry[] | null;
}

export interface BoardColumn {
  id: number;
  name: string;
  state: WorkItemState;
  wipLimit: number | null;
  itemCount: number;
  isOverWipLimit: boolean;
  items: WorkItem[];
}

export interface Swimlane {
  id: number;
  name: string;
  sortOrder: number;
  isDefault: boolean;
}

export interface BoardMetadata {
  types: string[];
  states: string[];
  severities: string[];
  priorities: Record<string, string>;
  assignees: string[];
  programs: string[];
  tags: string[];
}

export interface Board {
  name: string;
  columns: BoardColumn[];
  swimlanes: Swimlane[];
  removedCount: number;
  metadata: BoardMetadata;
}

/** Quick-filter toolbar state (spec 5.1). Empty/undefined means "no filter". */
export interface BoardFilter {
  type?: WorkItemType | '';
  state?: WorkItemState | '';
  aiModified?: boolean;
  program?: string;
  tag?: string;
}

export interface CreateWorkItemRequest {
  title: string;
  type: WorkItemType;
  description?: string | null;
  state?: WorkItemState;
  priority?: number;
  severity?: string;
  assignedTo?: string | null;
  areaPath?: string;
  iterationPath?: string;
  program?: string | null;
  programVersion?: string;
  tags?: string[];
  comment?: string;
}

export type UpdateWorkItemRequest = Partial<{
  title: string;
  description: string;
  type: WorkItemType;
  state: WorkItemState;
  priority: number;
  severity: string;
  assignedTo: string;
  areaPath: string;
  iterationPath: string;
  /** '' removes the program. */
  program: string;
  /** '' clears it. */
  programVersion: string;
  /** Replaces all tags. */
  tags: string[];
  comment: string;
}>;

/** A Program dropdown option. */
export interface ProgramInfo {
  id: number;
  name: string;
  sortOrder: number;
}

/** An uploaded image (POST api/ui/attachments). */
export interface AttachmentInfo {
  id?: string;
  fileName: string;
  contentType?: string;
  length?: number;
  /** Relative URL, e.g. api/ui/attachments/{id}. */
  url: string;
  markdown?: string;
}

/** RFC 7807 problem body returned by the API on errors. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
