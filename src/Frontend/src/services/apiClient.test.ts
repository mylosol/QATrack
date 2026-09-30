import { describe, expect, it, vi } from 'vitest';
import { ApiClient, ApiError, buildQuery, describeProblem } from './apiClient';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('buildQuery', () => {
  it('returns empty string when no filters are set', () => {
    expect(buildQuery({})).toBe('');
    expect(buildQuery({ type: '', state: '', aiModified: false })).toBe('');
  });

  it('serializes all spec filters', () => {
    expect(buildQuery({ type: 'Bug', state: 'Active', aiModified: true })).toBe(
      '?type=Bug&state=Active&aiModified=true',
    );
  });

  it('serializes the discussion filter (1.9.0)', () => {
    expect(buildQuery({ discussion: 'AwaitingAgent' })).toBe('?discussion=AwaitingAgent');
  });

  it('serializes the program and tag filters (1.4.0)', () => {
    expect(buildQuery({ program: 'ProveOut', tag: 'ui & api' })).toBe('?program=ProveOut&tag=ui+%26+api');
  });

  it('encodes special characters', () => {
    expect(buildQuery({ tag: 'a&b=c' })).toBe('?tag=a%26b%3Dc');
  });
});

describe('describeProblem', () => {
  it('prefers field errors', () => {
    expect(describeProblem(400, { title: 'Bad', errors: { Title: ['Title is required.'] } })).toBe('Title is required.');
  });

  it('prefixes the field when the message does not mention it', () => {
    expect(describeProblem(400, { errors: { Severity: ['must be one of ...'] } })).toBe('Severity: must be one of ...');
  });

  it('falls back to detail, title, then status', () => {
    expect(describeProblem(404, { title: 'Not found', detail: 'Work item 9 was not found.' })).toBe('Work item 9 was not found.');
    expect(describeProblem(404, { title: 'Not found' })).toBe('Not found');
    expect(describeProblem(502, {})).toBe('Request failed with status 502.');
  });
});

describe('ApiClient', () => {
  it('uploads images as multipart form data with the anti-forgery header', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ url: 'api/ui/attachments/1', fileName: 'a.png' }, 201));
    const api = new ApiClient({ fetchImpl });
    const file = new File([new Uint8Array([0x89, 0x50])], 'a.png', { type: 'image/png' });

    const info = await api.uploadAttachment(file);

    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(url).toBe('api/ui/attachments');
    expect((init as RequestInit).body).toBeInstanceOf(FormData);
    expect(((init as RequestInit).body as FormData).get('file')).toBeInstanceOf(File);
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers['X-Requested-With']).toBe('QATrack');
    // The browser must set the multipart boundary itself.
    expect(headers['Content-Type']).toBeUndefined();
    expect(info.url).toBe('api/ui/attachments/1');
  });

  it('marks a card read with a bodiless POST that is not an edit', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const api = new ApiClient({ fetchImpl });

    await api.markRead(12);

    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(url).toBe('api/ui/workitems/12/read');
    expect((init as RequestInit).method).toBe('POST');
    expect((init as RequestInit).body).toBeUndefined();
  });

  it('creates programs with a JSON body', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ id: 3, name: 'FieldTest', sortOrder: 2 }, 201));
    const api = new ApiClient({ fetchImpl });

    expect((await api.createProgram('FieldTest')).name).toBe('FieldTest');
    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(url).toBe('api/ui/programs');
    expect((init as RequestInit).body).toBe(JSON.stringify({ name: 'FieldTest' }));
  });

  it('sends the anti-forgery header on every request and uses relative URLs', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ columns: [] }));
    const api = new ApiClient({ fetchImpl });

    await api.getBoard({ type: 'Bug' });

    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(url).toBe('api/ui/board?type=Bug');
    expect((init as RequestInit).headers).toMatchObject({ 'X-Requested-With': 'QATrack', Accept: 'application/json' });
    expect((init as RequestInit).method).toBe('GET');
  });

  it('sends JSON bodies for mutations', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ id: 5 }));
    const api = new ApiClient({ fetchImpl });

    await api.updateWorkItem(5, { state: 'Active' });

    const [url, init] = fetchImpl.mock.calls[0]!;
    expect(url).toBe('api/ui/workitems/5');
    expect((init as RequestInit).method).toBe('PATCH');
    expect((init as RequestInit).body).toBe('{"state":"Active"}');
    expect((init as RequestInit).headers).toMatchObject({ 'Content-Type': 'application/json' });
  });

  it('percent-encodes the display name header', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({}));
    const api = new ApiClient({ fetchImpl, getDisplayName: () => ' José ' });

    await api.addComment(1, 'hi');

    const init = fetchImpl.mock.calls[0]![1] as RequestInit;
    expect(init.headers).toMatchObject({ 'X-User-Display-Name': 'Jos%C3%A9' });
    expect(init.body).toBe('{"text":"hi"}');
  });

  it('omits the display name header when blank', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({}));
    await new ApiClient({ fetchImpl, getDisplayName: () => '   ' }).getWorkItem(1);
    expect((fetchImpl.mock.calls[0]![1] as RequestInit).headers).not.toHaveProperty('X-User-Display-Name');
  });

  it('throws ApiError with parsed problem details', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ title: 'Validation', errors: { Title: ['Title is required.'] } }, 400));
    const api = new ApiClient({ fetchImpl });

    const error = await api.createWorkItem({ title: '', type: 'Bug' }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).message).toBe('Title is required.');
  });

  it('handles non-JSON error bodies', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response('<html>IIS error</html>', { status: 500 }));
    const error = await new ApiClient({ fetchImpl }).getBoard().catch((e: unknown) => e);
    expect((error as ApiError).message).toBe('Request failed with status 500.');
  });

  it('calls onUnauthorized when board data is refused with 401', async () => {
    const onUnauthorized = vi.fn();
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ title: 'Sign-in required' }, 401));
    const error = await new ApiClient({ fetchImpl, onUnauthorized }).getBoard().catch((e: unknown) => e);
    expect((error as ApiError).status).toBe(401);
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });

  it('does not call onUnauthorized for a wrong password (login 401)', async () => {
    const onUnauthorized = vi.fn();
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse({ title: 'Incorrect password' }, 401));
    await new ApiClient({ fetchImpl, onUnauthorized }).login('x').catch(() => undefined);
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('uses the auth endpoints with the anti-forgery header', async () => {
    const fetchImpl = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ required: true, authenticated: false }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    const api = new ApiClient({ fetchImpl });

    expect(await api.getAuthStatus()).toEqual({ required: true, authenticated: false });
    await api.login('pw');
    await api.logout();

    expect(fetchImpl.mock.calls.map((c) => [c[0], (c[1] as RequestInit).method])).toEqual([
      ['api/auth/status', 'GET'],
      ['api/auth/login', 'POST'],
      ['api/auth/logout', 'POST'],
    ]);
    expect((fetchImpl.mock.calls[1]![1] as RequestInit).body).toBe('{"password":"pw"}');
    expect((fetchImpl.mock.calls[1]![1] as RequestInit).headers).toMatchObject({ 'X-Requested-With': 'QATrack' });
  });

  it('maps network failures to a friendly ApiError', async () => {
    const fetchImpl = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    const error = await new ApiClient({ fetchImpl }).getBoard().catch((e: unknown) => e);
    expect((error as ApiError).status).toBe(0);
    expect((error as ApiError).message).toMatch(/Could not reach/);
  });
});
