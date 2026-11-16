// A thin client for the Rotadesk API. The session lives in an HttpOnly cookie on the same origin, so there's no
// token to store; every call just sends the cookie.

export type Role = 'Staff' | 'Manager' | 'Owner';

export type Day = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

export interface Business {
  id: string;
  name: string;
  timeZone: string;
}

export interface Me {
  staffMemberId: string;
  name: string;
  email: string;
  role: Role;
  business: Business;
}

export interface StaffMember {
  id: string;
  name: string;
  email: string;
  role: Role;
  weeklyLimitHours: number;
}

export interface Shift {
  id: string;
  start: string;
  end: string;
  staffMemberId: string | null;
  staffName: string | null;
  warnings: string[];
}

export interface RotaWeek {
  weekStart: string;
  published: boolean;
  publishedAt: string | null;
  shifts: Shift[];
}

export interface ShiftInput {
  start: string;
  end: string;
  staffMemberId: string | null;
}

/** "HH:mm" on the business's clock; "24:00" as the end means until midnight. */
export interface AvailabilityWindow {
  day: Day;
  from: string;
  to: string;
}

export interface DemoAccount {
  email: string;
  name: string;
  role: Role;
  business: string;
}

export interface Violation {
  code: string;
  message: string;
}

/** A refused request, read from the API's ProblemDetails. `code` is what to switch on; `title` is for people. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly violations: Violation[];

  constructor(status: number, title: string, code: string | null = null, violations: Violation[] = []) {
    super(title);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.violations = violations;
  }

  get title(): string {
    return this.message;
  }
}

function fallbackTitle(status: number): string {
  if (status === 401) return 'Please sign in again.';
  if (status === 403) return "You don't have permission to do that.";
  if (status === 404) return 'That could not be found.';
  if (status >= 500) return 'Something went wrong on our side. Please try again.';
  return `The request failed (${status}).`;
}

function isViolation(value: unknown): value is Violation {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as Violation).code === 'string' &&
    typeof (value as Violation).message === 'string'
  );
}

async function toError(response: Response): Promise<ApiError> {
  let problem: Record<string, unknown> = {};
  if (response.headers.get('content-type')?.includes('json')) {
    try {
      const body: unknown = await response.json();
      if (typeof body === 'object' && body !== null) {
        problem = body as Record<string, unknown>;
      }
    } catch {
      // A body that isn't JSON after all still leaves the status to go on.
    }
  }

  const title = typeof problem.title === 'string' && problem.title ? problem.title : fallbackTitle(response.status);
  const code = typeof problem.code === 'string' ? problem.code : null;
  const violations = Array.isArray(problem.violations) ? problem.violations.filter(isViolation) : [];
  return new ApiError(response.status, title, code, violations);
}

const sessionEnded = new Set<() => void>();

/** Calls back whenever the API answers 401, which means nobody is signed in any more. Returns an unsubscribe. */
export function onSessionEnded(callback: () => void): () => void {
  sessionEnded.add(callback);
  return () => {
    sessionEnded.delete(callback);
  };
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, "Can't reach Rotadesk. Check your connection and try again.");
  }

  if (!response.ok) {
    // A wrong password is a 401 too, but nobody was signed in to begin with.
    if (response.status === 401 && path !== '/auth/sign-in') {
      sessionEnded.forEach((callback) => callback());
    }
    throw await toError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

const id = (value: string) => encodeURIComponent(value);

export const api = {
  me: () => request<Me>('GET', '/auth/me'),
  signIn: (email: string, password: string) => request<void>('POST', '/auth/sign-in', { email, password }),
  signOut: () => request<void>('POST', '/auth/sign-out'),

  demoAccounts: () => request<DemoAccount[]>('GET', '/demo/accounts'),
  demoSignIn: (email: string) => request<void>('POST', '/demo/sign-in', { email }),

  staff: () => request<StaffMember[]>('GET', '/staff'),
  staffAvailability: (staffId: string) => request<AvailabilityWindow[]>('GET', `/staff/${id(staffId)}/availability`),

  week: (monday: string) => request<RotaWeek>('GET', `/weeks/${id(monday)}`),
  publish: (monday: string) => request<RotaWeek>('POST', `/weeks/${id(monday)}/publish`),
  copyPreviousWeek: (monday: string) => request<RotaWeek>('POST', `/weeks/${id(monday)}/copy-previous`),

  createShift: (shift: ShiftInput) => request<Shift>('POST', '/shifts', shift),
  updateShift: (shiftId: string, shift: ShiftInput) => request<Shift>('PUT', `/shifts/${id(shiftId)}`, shift),
  deleteShift: (shiftId: string) => request<void>('DELETE', `/shifts/${id(shiftId)}`),

  myAvailability: () => request<AvailabilityWindow[]>('GET', '/availability/mine'),
  saveMyAvailability: (windows: AvailabilityWindow[]) =>
    request<AvailabilityWindow[]>('PUT', '/availability/mine', windows),
};

/** Words for any error, for showing to people. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.title;
  }
  return 'Something went wrong. Please try again.';
}
