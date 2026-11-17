import type { Me, Shift, Swap, SwapAction, SwapShift } from './api';
import { shiftTimes } from './rota';
import { formatDay, utcToZoned } from './time';

/** A swap still waiting on someone: the colleague's answer, then a manager's. */
export function isOpen(swap: Swap): boolean {
  return swap.status === 'Requested' || swap.status === 'Accepted';
}

/**
 * What this person may do with the swap now, following the API's state machine: the colleague answers a request,
 * the person who asked can cancel until it's decided, and a manager who isn't part of it decides an accepted one.
 */
export function swapActions(swap: Swap, me: Pick<Me, 'staffMemberId' | 'role'>): SwapAction[] {
  const asked = swap.requester.id === me.staffMemberId;
  const askedOf = swap.colleague.id === me.staffMemberId;
  if (swap.status === 'Requested') {
    if (askedOf) return ['accept', 'decline'];
    if (asked) return ['cancel'];
  }
  if (swap.status === 'Accepted') {
    if (asked) return ['cancel'];
    if (me.role !== 'Staff' && !askedOf) return ['approve', 'reject'];
  }
  return [];
}

/** Where the swap stands, in a few words. */
export function swapState(swap: Swap): string {
  switch (swap.status) {
    case 'Requested':
      return `Waiting for ${swap.colleague.name}`;
    case 'Accepted':
      return 'Waiting for a manager';
    case 'Declined':
      return `Declined by ${swap.colleague.name}`;
    case 'Approved':
      return swap.decidedBy ? `Approved by ${swap.decidedBy}` : 'Approved';
    case 'Rejected':
      return swap.decidedBy ? `Rejected by ${swap.decidedBy}` : 'Rejected';
    case 'Cancelled':
      return `Cancelled by ${swap.requester.name}`;
  }
}

/** "Sat 21 Nov, 08:00–16:00" on the business clock. */
export function describeSwapShift(shift: SwapShift, zone: string): string {
  return `${formatDay(utcToZoned(shift.start, zone).date)}, ${shiftTimes(shift, zone)}`;
}

/** Open swaps first, then the rest, each newest first. */
export function splitSwaps(swaps: Swap[]): { open: Swap[]; done: Swap[] } {
  const newest = (a: Swap, b: Swap) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
  return {
    open: swaps.filter(isOpen).sort(newest),
    done: swaps.filter((s) => !isOpen(s)).sort(newest),
  };
}

/** The colleague's shifts this week that could come back in return, earliest first. */
export function returnOptions(shifts: Shift[], colleagueId: string): Shift[] {
  return shifts
    .filter((s) => s.staffMemberId === colleagueId)
    .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime());
}
