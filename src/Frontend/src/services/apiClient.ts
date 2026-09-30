/**
 * Thin, typed client for the browser endpoints under `api/ui`.
 *
 * Security notes:
 * - Every request sends `X-Requested-With: QATrack`; the server rejects
 *   mutations without it (CSRF defence, see UiRequestGuardMiddleware).
 * - URLs are relative (`api/ui/...`, no leading slash) so the SPA keeps
 *   working when IIS hosts it under a virtual directory.
 * - The optional display name is percent-encoded because HTTP header values
 *   must be ISO-8859-1; the server decodes it.
 */
import type {
  AttachmentInfo,
  Board,
  BoardFilter,
  CreateWorkItemRequest,
  ProblemDetails,
  ProgramInfo,
  UpdateWorkItemRequest,
  WorkItem,
  WorkItemHistoryEntry,
} from './types';

export const UI_API_BASE = 'api/ui/';
export const AUTH_API_BASE = 'api/auth/';

/** Response of GET api/auth/status. */
export interface AuthStatus {
  required: boolean;
  authenticated: boolean;
}
export const REQUESTED_WITH_HEADER = 'X-Requested-With';
export const REQUESTED_WITH_VALUE = 'QATrack';
export const DISPLAY_NAME_HEADER = 'X-User-Display-Name';

/** Error thrown for non-2xx responses, carrying the parsed problem details. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly problem: ProblemDetails,
  ) {
    super(describeProblem(status, problem));
    this.name = 'ApiError';
  }
}

/** Builds a readable one-line message from a problem response. */
export function describeProblem(status: number, problem: ProblemDetails): string {
  const fieldErrors = problem.errors
    ? Object.entries(problem.errors).flatMap(([field, messages]) =>
        messages.map((m) => (field && !m.includes(field) ? `${field}: ${m}` : m)),
      )
    : [];
  if (fieldErrors.length > 0) {
    return fieldErrors.join(' ');
  }
  return problem.detail ?? problem.title ?? `Request failed with status ${status}.`;
}

/**
 * Serializes board filters into a query string. Empty values are omitted so
 * the server treats them as "no filter". Returns '' or '?a=b&c=d'.
 */
export function buildQuery(filter: BoardFilter): string {
  const params = new URLSearchParams();
  if (filter.type) params.set('type', filter.type);
  if (filter.state) params.set('state', filter.state);
  if (filter.aiModified) params.set('aiModified', 'true');
  if (filter.program) params.set('program', filter.program);
  if (filter.tag) params.set('tag', filter.tag);
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

export interface ApiClientOptions {
  /** Base URL for the UI API; defaults to the relative `api/ui/`. */
  baseUrl?: string;
  /** Injectable fetch for tests. */
  fetchImpl?: typeof fetch;
  /** Returns the user's self-reported name (or null) for audit attribution. */
  getDisplayName?: () => string | null;
  /**
   * Called when board data is refused with 401 (no or expired sign-in, or
   * the shared password was changed). The app shows its sign-in dialog.
   */
  onUnauthorized?: () => void;
  /** Base URL for the sign-in endpoints; defaults to the relative `api/auth/`. */
  authBaseUrl?: string;
}

export class ApiClient {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;
  private readonly getDisplayName: () => string | null;
  private readonly onUnauthorized: () => void;
  private readonly authBaseUrl: string;

  constructor(options: ApiClientOptions = {}) {
    this.baseUrl = options.baseUrl ?? UI_API_BASE;
    // Bind so window.fetch keeps its required `this`.
    this.fetchImpl = options.fetchImpl ?? window.fetch.bind(window);
    this.getDisplayName = options.getDisplayName ?? (() => null);
    this.onUnauthorized = options.onUnauthorized ?? (() => undefined);
    this.authBaseUrl = options.authBaseUrl ?? AUTH_API_BASE;
  }

  /** GET the board (columns, WIP status, filtered cards). */
  getBoard(filter: BoardFilter = {}): Promise<Board> {
    return this.request<Board>('GET', `board${buildQuery(filter)}`);
  }

  /** GET one work item with full history. */
  getWorkItem(id: number): Promise<WorkItem> {
    return this.request<WorkItem>('GET', `workitems/${encodeURIComponent(String(id))}`);
  }

  createWorkItem(body: CreateWorkItemRequest): Promise<WorkItem> {
    return this.request<WorkItem>('POST', 'workitems', body);
  }

  updateWorkItem(id: number, body: UpdateWorkItemRequest): Promise<WorkItem> {
    return this.request<WorkItem>('PATCH', `workitems/${encodeURIComponent(String(id))}`, body);
  }

  addComment(id: number, text: string): Promise<WorkItemHistoryEntry> {
    return this.request<WorkItemHistoryEntry>('POST', `workitems/${encodeURIComponent(String(id))}/comments`, { text });
  }

  /** Adds a Program dropdown option (returns the existing one for a duplicate name). */
  createProgram(name: string): Promise<ProgramInfo> {
    return this.request<ProgramInfo>('POST', 'programs', { name });
  }

  /** Uploads an image for a description or comment. */
  uploadAttachment(file: File): Promise<AttachmentInfo> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.request<AttachmentInfo>('POST', 'attachments', form);
  }

  /** Whether a shared password is configured and whether this browser is signed in. */
  getAuthStatus(): Promise<AuthStatus> {
    return this.send<AuthStatus>('GET', this.authBaseUrl + 'status');
  }

  /** Signs in with the shared password (throws ApiError 401 / 429 on failure). */
  async login(password: string): Promise<void> {
    await this.send<void>('POST', this.authBaseUrl + 'login', { password });
  }

  /** Ends this browser's session. */
  async logout(): Promise<void> {
    await this.send<void>('POST', this.authBaseUrl + 'logout');
  }

  private request<T>(method: string, path: string, body?: unknown): Promise<T> {
    return this.send<T>(method, this.baseUrl + path, body, true);
  }

  private async send<T>(method: string, url: string, body?: unknown, isBoardData = false): Promise<T> {
    const headers: Record<string, string> = {
      Accept: 'application/json',
      [REQUESTED_WITH_HEADER]: REQUESTED_WITH_VALUE,
    };
    const name = this.getDisplayName()?.trim();
    if (name) {
      headers[DISPLAY_NAME_HEADER] = encodeURIComponent(name);
    }
    // FormData sets its own multipart Content-Type (with the boundary).
    const isForm = typeof FormData !== 'undefined' && body instanceof FormData;
    if (body !== undefined && !isForm) {
      headers['Content-Type'] = 'application/json';
    }

    let response: Response;
    try {
      response = await this.fetchImpl(url, {
        method,
        headers,
        body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
        credentials: 'same-origin',
        cache: 'no-store',
      });
    } catch {
      throw new ApiError(0, { title: 'Network error', detail: 'Could not reach the QATrack server. Check your connection and try again.' });
    }

    if (!response.ok) {
      let problem: ProblemDetails = {};
      try {
        problem = (await response.json()) as ProblemDetails;
      } catch {
        // Non-JSON error body (e.g. IIS error page) - fall back to the status.
      }
      if (response.status === 401 && isBoardData) this.onUnauthorized();
      throw new ApiError(response.status, problem);
    }

    if (response.status === 204) {
      return undefined as T;
    }
    return (await response.json()) as T;
  }
}
