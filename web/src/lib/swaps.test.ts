import { describe, expect, it } from 'vitest';

import type { Shift, Swap, SwapStatus } from './api';
import { describeSwapShift, returnOptions, splitSwaps, swapActions, swapState } from './swaps';

const ben = { id: 'ben', name: 'Ben' };
const amira = { id: 'amira', name: 'Amira' };

function swap(status: SwapStatus, extra: Partial<Swap> = {}): Swap {
  return {
    id: `swap-${status}`,
    status,
    requester: ben,
    colleague: amira,
    offered: { id: 'sat', start: '2026-11-21T08:00:00Z', end: '2026-11-21T16:00:00Z' },
    return: null,
    createdAt: '2026-11-17T09:00:00Z',
    respondedAt: null,
    decidedAt: null,
    decidedBy: null,
    warnings: [],
    ...extra,
  };
}

const asBen = { staffMemberId: 'ben', role: 'Staff' as const };
const asAmira = { staffMemberId: 'amira', role: 'Staff' as const };
const asMarcus = { staffMemberId: 'marcus', role: 'Manager' as const };
const asOtherStaff = { staffMemberId: 'hira', role: 'Staff' as const };

describe('swapActions', () => {
  it('lets the colleague answer a request, and the person who asked cancel it', () => {
    expect(swapActions(swap('Requested'), asAmira)).toEqual(['accept', 'decline']);
    expect(swapActions(swap('Requested'), asBen)).toEqual(['cancel']);
    expect(swapActions(swap('Requested'), asMarcus)).toEqual([]);
  });

  it('lets a manager decide an accepted swap, and the person who asked still cancel it', () => {
    expect(swapActions(swap('Accepted'), asMarcus)).toEqual(['approve', 'reject']);
    expect(swapActions(swap('Accepted'), asBen)).toEqual(['cancel']);
    expect(swapActions(swap('Accepted'), asAmira)).toEqual([]);
    expect(swapActions(swap('Accepted'), asOtherStaff)).toEqual([]);
  });

  it("doesn't let a manager decide a swap they're part of", () => {
    const own = swap('Accepted', { colleague: { id: 'marcus', name: 'Marcus' } });

    expect(swapActions(own, asMarcus)).toEqual([]);
  });

  it('offers nothing once a swap is decided', () => {
    for (const status of ['Declined', 'Approved', 'Rejected', 'Cancelled'] as const) {
      expect(swapActions(swap(status), asMarcus)).toEqual([]);
      expect(swapActions(swap(status), asBen)).toEqual([]);
    }
  });
});

describe('swapState', () => {
  it('says who the swap is waiting on, or how it ended', () => {
    expect(swapState(swap('Requested'))).toBe('Waiting for Amira');
    expect(swapState(swap('Accepted'))).toBe('Waiting for a manager');
    expect(swapState(swap('Approved', { decidedBy: 'Marcus' }))).toBe('Approved by Marcus');
    expect(swapState(swap('Declined'))).toBe('Declined by Amira');
    expect(swapState(swap('Cancelled'))).toBe('Cancelled by Ben');
  });
});

describe('describeSwapShift', () => {
  it('names the day and times on the business clock', () => {
    expect(describeSwapShift(swap('Requested').offered, 'Europe/London')).toBe('Sat 21 Nov, 08:00–16:00');
    expect(describeSwapShift(swap('Requested').offered, 'Asia/Karachi')).toBe('Sat 21 Nov, 13:00–21:00');
  });
});

describe('splitSwaps', () => {
  it('puts open swaps first, each group newest first', () => {
    const old = swap('Requested', { id: 'old', createdAt: '2026-11-16T09:00:00Z' });
    const recent = swap('Accepted', { id: 'recent', createdAt: '2026-11-17T09:00:00Z' });
    const done = swap('Approved', { id: 'done' });

    const { open, done: finished } = splitSwaps([old, done, recent]);

    expect(open.map((s) => s.id)).toEqual(['recent', 'old']);
    expect(finished.map((s) => s.id)).toEqual(['done']);
  });
});

describe('returnOptions', () => {
  it("lists only the colleague's shifts, earliest first", () => {
    const shift = (id: string, start: string, staffMemberId: string | null): Shift => ({
      id,
      start,
      end: start,
      staffMemberId,
      staffName: null,
      warnings: [],
    });
    const shifts = [
      shift('late', '2026-11-20T09:00:00Z', 'amira'),
      shift('bens', '2026-11-18T09:00:00Z', 'ben'),
      shift('early', '2026-11-18T09:00:00Z', 'amira'),
      shift('open', '2026-11-19T09:00:00Z', null),
    ];

    expect(returnOptions(shifts, 'amira').map((s) => s.id)).toEqual(['early', 'late']);
  });
});
