// @vitest-environment jsdom
import { cleanup, configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { App } from './App';
import type { Me, RotaWeek, StaffMember, Swap } from './lib/api';
import { SessionProvider } from './session';

// The first render of a run pays for loading React into jsdom, which can take a few seconds on a busy machine.
configure({ asyncUtilTimeout: 5000 });

// The pages run for real; only the network is faked, by a function from each request to its answer.
type Answer = { status: number; body?: unknown };
type Handler = (method: string, path: string, body: unknown) => Answer | undefined;

interface Call {
  method: string;
  path: string;
  body: unknown;
}

function fakeApi(handler: Handler): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      const path = String(input);
      const body: unknown = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      calls.push({ method, path, body });
      const answer = handler(method, path, body) ?? { status: 404, body: { title: `Not faked: ${method} ${path}` } };
      return answer.body === undefined
        ? new Response(null, { status: answer.status })
        : new Response(JSON.stringify(answer.body), {
            status: answer.status,
            headers: { 'content-type': 'application/json' },
          });
    }),
  );
  return calls;
}

function show(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <SessionProvider>
        <App />
      </SessionProvider>
    </MemoryRouter>,
  );
}

const northside = { id: 'northside', name: 'Northside Café', timeZone: 'Europe/London' };
const marcus: Me = { staffMemberId: 'marcus', name: 'Marcus', email: 'marcus@northside.example', role: 'Manager', business: northside };
const amiraMe: Me = { staffMemberId: 'amira', name: 'Amira', email: 'amira@northside.example', role: 'Staff', business: northside };

const staff: StaffMember[] = [
  { id: 'amira', name: 'Amira', email: 'amira@northside.example', role: 'Staff', weeklyLimitHours: 20 },
  { id: 'ben', name: 'Ben', email: 'ben@northside.example', role: 'Staff', weeklyLimitHours: 40 },
];

const draftWeek: RotaWeek = {
  weekStart: '2026-11-16',
  published: false,
  publishedAt: null,
  shifts: [
    {
      id: 's1',
      start: '2026-11-16T09:00:00Z',
      end: '2026-11-16T17:00:00Z',
      staffMemberId: 'amira',
      staffName: 'Amira',
      warnings: ["Outside Amira's availability on Monday."],
    },
  ],
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('signing in', () => {
  it('shows the demo people by business when nobody is signed in', async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 401 };
      if (path === '/api/demo/accounts') {
        return {
          status: 200,
          body: [
            { email: 'olivia@northside.example', name: 'Olivia', role: 'Owner', business: 'Northside Café' },
            { email: 'sana@harbourbooks.example', name: 'Sana', role: 'Owner', business: 'Harbour Books' },
          ],
        };
      }
      return undefined;
    });

    show('/rota');

    const northsideList = (await screen.findByRole('heading', { name: 'Northside Café' })).parentElement!;
    expect(within(northsideList).getByRole('button', { name: /Olivia/ })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Harbour Books' })).toBeTruthy();
  });

  it('shows the reason when the password is wrong', async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 401 };
      if (path === '/api/auth/sign-in') return { status: 401, body: { title: 'Wrong email or password.' } };
      return undefined;
    });

    show('/sign-in');
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: 'marcus@northside.example' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'nope' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect((await screen.findByRole('alert')).textContent).toBe('Wrong email or password.');
  });

  it('goes to the page that was asked for once signed in', async () => {
    let signedIn = false;
    fakeApi((method, path) => {
      if (path === '/api/auth/me') return signedIn ? { status: 200, body: marcus } : { status: 401 };
      if (path === '/api/demo/accounts') {
        return { status: 200, body: [{ email: marcus.email, name: 'Marcus', role: 'Manager', business: 'Northside Café' }] };
      }
      if (method === 'POST' && path === '/api/demo/sign-in') {
        signedIn = true;
        return { status: 204 };
      }
      if (path === '/api/availability/mine') return { status: 200, body: [] };
      return undefined;
    });

    show('/availability');
    fireEvent.click(await screen.findByRole('button', { name: /Marcus/ }));

    expect(await screen.findByRole('heading', { name: 'Your availability' })).toBeTruthy();
  });

  it('starts the next person on the rota after someone signs out', async () => {
    let signedIn: Me | null = amiraMe;
    fakeApi((method, path, body) => {
      if (path === '/api/auth/me') return signedIn ? { status: 200, body: signedIn } : { status: 401 };
      if (path === '/api/auth/sign-out') {
        signedIn = null;
        return { status: 204 };
      }
      if (path === '/api/demo/accounts') {
        return { status: 200, body: [{ email: marcus.email, name: 'Marcus', role: 'Manager', business: 'Northside Café' }] };
      }
      if (method === 'POST' && path === '/api/demo/sign-in' && (body as { email: string }).email === marcus.email) {
        signedIn = marcus;
        return { status: 204 };
      }
      if (path === '/api/availability/mine') return { status: 200, body: [] };
      if (path.startsWith('/api/weeks/')) return { status: 200, body: { ...draftWeek, shifts: [] } };
      if (path === '/api/staff') return { status: 200, body: staff };
      return undefined;
    });

    show('/availability');
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }));
    fireEvent.click(await screen.findByRole('button', { name: /Marcus/ }));

    expect(await screen.findByRole('complementary', { name: 'Hours this week' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Your availability' })).toBeNull();
  });
});

describe('the rota', () => {
  it("doesn't show staff a week before it's published", async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: { ...draftWeek, shifts: [] } };
      if (path === '/api/staff') return { status: 200, body: staff };
      return undefined;
    });

    show('/rota/2026-11-16');

    expect(await screen.findByText(/isn't published yet/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Publish' })).toBeNull();
  });

  it("shows a manager the shifts on the business clock, their warnings and everyone's hours", async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: marcus };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: draftWeek };
      if (path === '/api/staff') return { status: 200, body: staff };
      return undefined;
    });

    show('/rota/2026-11-16');

    const monday = await screen.findByRole('region', { name: 'Mon 16 Nov' });
    expect(within(monday).getByText('09:00–17:00')).toBeTruthy();
    expect(within(monday).getByText("Outside Amira's availability on Monday.")).toBeTruthy();
    const hours = screen.getByRole('complementary', { name: 'Hours this week' });
    expect(within(hours).getByRole('row', { name: /Amira/ }).textContent).toContain('8 h');
    expect(screen.getByRole('button', { name: 'Publish' })).toBeTruthy();
  });

  it('moves a date in the middle of a week to its Monday', async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: marcus };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: draftWeek };
      if (path === '/api/staff') return { status: 200, body: staff };
      return undefined;
    });

    show('/rota/2026-11-19');

    expect(await screen.findByRole('heading', { name: '16–22 Nov 2026' })).toBeTruthy();
  });

  it('sends a night shift to the API as UTC instants, ending the next morning', async () => {
    const calls = fakeApi((method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: marcus };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: draftWeek };
      if (path === '/api/staff') return { status: 200, body: staff };
      if (method === 'POST' && path === '/api/shifts') return { status: 201, body: draftWeek.shifts[0] };
      return undefined;
    });

    show('/rota/2026-11-16');
    const monday = await screen.findByRole('region', { name: 'Mon 16 Nov' });
    fireEvent.click(within(monday).getByRole('button', { name: '+ Add shift' }));
    const dialog = screen.getByRole('dialog', { name: 'Add shift' });
    fireEvent.change(within(dialog).getByLabelText('Starts'), { target: { value: '22:00' } });
    fireEvent.change(within(dialog).getByLabelText('Ends'), { target: { value: '06:00' } });
    fireEvent.change(within(dialog).getByLabelText('Who'), { target: { value: 'ben' } });
    expect(within(dialog).getByText('Ends the next morning.')).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add shift' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/shifts')?.body).toEqual({
      start: '2026-11-16T22:00:00.000Z',
      end: '2026-11-17T06:00:00.000Z',
      staffMemberId: 'ben',
    });
  });

  it('lists every rule a shift breaks', async () => {
    fakeApi((method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: marcus };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: draftWeek };
      if (path === '/api/staff') return { status: 200, body: staff };
      if (path === '/api/staff/amira/availability') return { status: 200, body: [] };
      if (method === 'PUT' && path === '/api/shifts/s1') {
        return {
          status: 409,
          body: {
            title: 'Amira already has a shift then.',
            code: 'shift.overlap',
            violations: [
              { code: 'shift.overlap', message: 'Amira already has a shift then.' },
              { code: 'shift.over-weekly-limit', message: 'Amira would be over 20 hours this week.' },
            ],
          },
        };
      }
      return undefined;
    });

    show('/rota/2026-11-16');
    fireEvent.click(await screen.findByRole('button', { name: /Edit shift 09:00–17:00, Amira/ }));
    const dialog = screen.getByRole('dialog', { name: 'Edit shift' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

    const alert = await within(dialog).findByRole('alert');
    expect(alert.textContent).toContain('Amira already has a shift then.');
    expect(alert.textContent).toContain('Amira would be over 20 hours this week.');
  });
});

describe('swaps', () => {
  // Saturday before the week, so its shifts haven't started. Only Date is faked; Testing Library's timers run as usual.
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-14T12:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const publishedWeek: RotaWeek = {
    ...draftWeek,
    published: true,
    publishedAt: '2026-11-13T10:00:00Z',
    shifts: [
      { ...draftWeek.shifts[0]!, warnings: [] },
      {
        id: 's2',
        start: '2026-11-18T09:00:00Z',
        end: '2026-11-18T17:00:00Z',
        staffMemberId: 'ben',
        staffName: 'Ben',
        warnings: [],
      },
    ],
  };

  const accepted: Swap = {
    id: 'w1',
    status: 'Accepted',
    requester: { id: 'ben', name: 'Ben' },
    colleague: { id: 'amira', name: 'Amira' },
    offered: { id: 's2', start: '2026-11-18T09:00:00Z', end: '2026-11-18T17:00:00Z' },
    return: null,
    createdAt: '2026-11-16T12:00:00Z',
    respondedAt: '2026-11-16T13:00:00Z',
    decidedAt: null,
    decidedBy: null,
    warnings: [],
  };

  it('lets staff offer their own shift to a colleague, for one of theirs', async () => {
    const calls = fakeApi((method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: publishedWeek };
      if (path === '/api/staff') return { status: 200, body: staff };
      if (method === 'POST' && path === '/api/swaps') return { status: 201, body: accepted };
      return undefined;
    });

    show('/rota/2026-11-16');
    // Only her own shift can be offered; Ben's is just shown.
    fireEvent.click(await screen.findByRole('button', { name: /Offer your shift 09:00–17:00/ }));
    expect(screen.queryAllByRole('button', { name: /Offer your shift/ })).toHaveLength(1);
    const dialog = screen.getByRole('dialog', { name: 'Offer your shift' });
    expect(within(dialog).queryByRole('option', { name: 'Amira' })).toBeNull();
    fireEvent.change(within(dialog).getByLabelText('To'), { target: { value: 'ben' } });
    fireEvent.change(within(dialog).getByLabelText('In return'), { target: { value: 's2' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Send offer' }));

    expect((await screen.findByRole('status')).textContent).toContain("Sent to Ben. They'll get an email.");
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/swaps')?.body).toEqual({
      offeredShiftId: 's1',
      colleagueId: 'ben',
      returnShiftId: 's2',
    });
  });

  it("doesn't offer a shift that has already started", async () => {
    vi.setSystemTime(new Date('2026-11-16T10:00:00Z'));
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/weeks/2026-11-16') return { status: 200, body: publishedWeek };
      if (path === '/api/staff') return { status: 200, body: staff };
      return undefined;
    });

    show('/rota/2026-11-16');
    const monday = await screen.findByRole('region', { name: 'Mon 16 Nov' });

    expect(within(monday).getByText('09:00–17:00')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Offer your shift/ })).toBeNull();
  });

  it('shows a manager the swaps waiting on them, and approving moves it to finished', async () => {
    const calls = fakeApi((method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: marcus };
      if (path === '/api/swaps') return { status: 200, body: [accepted] };
      if (method === 'POST' && path === '/api/swaps/w1/approve') {
        return { status: 200, body: { ...accepted, status: 'Approved', decidedBy: 'Marcus' } };
      }
      return undefined;
    });

    show('/swaps');
    const card = await screen.findByRole('article', { name: 'Swap from Ben to Amira' });
    expect(within(card).getByText('Needs you')).toBeTruthy();
    expect(card.textContent).toContain('Ben offers Wed 18 Nov, 09:00–17:00 to Amira, with nothing back.');
    fireEvent.click(within(card).getByRole('button', { name: 'Approve' }));

    const finished = await screen.findByRole('region', { name: 'Finished' });
    expect(within(finished).getByText('Approved by Marcus')).toBeTruthy();
    expect(calls.some((c) => c.path === '/api/swaps/w1/approve')).toBe(true);
  });

  it("gives staff no buttons on a swap that's waiting for a manager", async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/swaps') return { status: 200, body: [accepted] };
      return undefined;
    });

    show('/swaps');
    const card = await screen.findByRole('article', { name: 'Swap from Ben to Amira' });

    expect(card.textContent).toContain('Waiting for a manager');
    expect(card.textContent).toContain('to you');
    expect(within(card).queryAllByRole('button')).toHaveLength(0);
  });
});

describe('the demo inbox', () => {
  it('appears only on the demo, and opens an email', async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/demo/accounts') return { status: 200, body: [] };
      if (path === '/api/availability/mine') return { status: 200, body: [] };
      if (path === '/api/inbox') {
        return {
          status: 200,
          body: [
            {
              id: 'e1',
              messageId: 'm1',
              subject: 'Your rota for 16–22 Nov',
              body: 'Hi Amira,\nMon 16 Nov 09:00–17:00',
              deliveredAt: '2026-11-13T10:00:05Z',
            },
          ],
        };
      }
      return undefined;
    });

    show('/availability');
    fireEvent.click(await screen.findByRole('link', { name: 'Inbox' }));
    fireEvent.click(await screen.findByRole('button', { name: /Your rota for 16–22 Nov/ }));

    expect(screen.getByText(/Mon 16 Nov 09:00–17:00/)).toBeTruthy();
  });

  it('has no link when the demo is off', async () => {
    fakeApi((_method, path) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/availability/mine') return { status: 200, body: [] };
      return undefined;
    });

    show('/availability');
    await screen.findByRole('heading', { name: 'Your availability' });

    expect(screen.queryByRole('link', { name: 'Inbox' })).toBeNull();
  });
});

describe('availability', () => {
  function fakeAvailability() {
    return fakeApi((method, path, body) => {
      if (path === '/api/auth/me') return { status: 200, body: amiraMe };
      if (path === '/api/availability/mine') return { status: 200, body: method === 'PUT' ? body : [] };
      return undefined;
    });
  }

  it("won't save a window that ends before it starts", async () => {
    const calls = fakeAvailability();

    show('/availability');
    const monday = await screen.findByRole('region', { name: 'Monday' });
    fireEvent.click(within(monday).getByRole('button', { name: '+ Add time' }));
    fireEvent.change(within(monday).getByLabelText('To'), { target: { value: '08:00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(within(monday).getByText(/The end has to be after the start/)).toBeTruthy();
    expect(calls.some((c) => c.method === 'PUT')).toBe(false);
  });

  it('saves an evening until midnight as 24:00', async () => {
    const calls = fakeAvailability();

    show('/availability');
    const friday = await screen.findByRole('region', { name: 'Friday' });
    fireEvent.click(within(friday).getByRole('button', { name: '+ Add time' }));
    fireEvent.change(within(friday).getByLabelText('From'), { target: { value: '18:00' } });
    fireEvent.change(within(friday).getByLabelText('To'), { target: { value: '00:00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect((await screen.findByRole('status')).textContent).toBe('Saved.');
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual([{ day: 'Friday', from: '18:00', to: '24:00' }]);
  });
});
