import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiError, api, describeError, onSessionEnded } from './api';

function respond(status: number, body?: unknown, contentType = 'application/problem+json') {
  const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(
    new Response(body === undefined ? null : JSON.stringify(body), {
      status,
      headers: body === undefined ? {} : { 'content-type': contentType },
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('api', () => {
  it('sends JSON to the API on the same origin with the cookie', async () => {
    const fetchMock = respond(204);

    await api.signIn('olivia@northside.example', 'secret');

    expect(fetchMock).toHaveBeenCalledWith('/api/auth/sign-in', {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ email: 'olivia@northside.example', password: 'secret' }),
    });
  });

  it('sends no body or content type when there is nothing to send', async () => {
    const fetchMock = respond(200, { weekStart: '2026-11-16', published: true, publishedAt: null, shifts: [] }, 'application/json');

    await api.publish('2026-11-16');

    const init = fetchMock.mock.calls[0]?.[1];
    expect(init?.body).toBeUndefined();
    expect(init?.headers).toEqual({ Accept: 'application/json' });
  });

  it('returns the parsed body', async () => {
    respond(200, [{ day: 'Monday', from: '09:00', to: '17:00' }], 'application/json');

    await expect(api.myAvailability()).resolves.toEqual([{ day: 'Monday', from: '09:00', to: '17:00' }]);
  });

  it('turns a broken rule into an error with its code and every violation', async () => {
    respond(409, {
      status: 409,
      title: 'Amira is not available then.',
      code: 'shift.outside-availability',
      violations: [
        { code: 'shift.outside-availability', message: 'Amira is not available then.' },
        { code: 'shift.over-weekly-limit', message: 'Amira would be over 20 hours this week.' },
      ],
    });

    const error = await api.createShift({ start: 'x', end: 'y', staffMemberId: null }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, title: 'Amira is not available then.', code: 'shift.outside-availability' });
    expect((error as ApiError).violations).toHaveLength(2);
  });

  it('falls back to words for the status when there is no problem body', async () => {
    respond(401);

    const error = await api.me().catch((e: unknown) => e);

    expect(error).toMatchObject({ status: 401, code: null, title: 'Please sign in again.' });
  });

  it('tells listeners when the session has gone, but not for a wrong password', async () => {
    const ended = vi.fn();
    const unsubscribe = onSessionEnded(ended);

    respond(401, { title: 'Wrong email or password.' });
    await api.signIn('olivia@northside.example', 'wrong').catch(() => undefined);
    expect(ended).not.toHaveBeenCalled();

    respond(401);
    await api.week('2026-11-16').catch(() => undefined);
    expect(ended).toHaveBeenCalledOnce();

    unsubscribe();
    await api.week('2026-11-16').catch(() => undefined);
    expect(ended).toHaveBeenCalledOnce();
  });

  it('reports a network failure as status 0', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await api.me().catch((e: unknown) => e);

    expect(error).toMatchObject({ status: 0 });
    expect(describeError(error)).toMatch(/can't reach rotadesk/i);
  });

  it('keeps ids safe in the path', async () => {
    const fetchMock = respond(204);

    await api.deleteShift('a/b');

    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/shifts/a%2Fb');
  });
});
