import { useEffect, useState } from 'react';
import { Link, Navigate, useParams } from 'react-router';

import { ShiftEditor } from '../components/ShiftEditor';
import { SwapOffer } from '../components/SwapOffer';
import type { Editing } from '../components/ShiftEditor';
import { api, describeError } from '../lib/api';
import type { Me, RotaWeek, Shift, StaffMember } from '../lib/api';
import { draftFromShift, hasStarted, hoursByPerson, openShiftCount, shiftTimes, shiftsByDay } from '../lib/rota';
import {
  addDays,
  formatDay,
  formatHours,
  formatWeek,
  hoursBetween,
  isLocalDate,
  isMonday,
  mondayOf,
  today,
  utcToZoned,
} from '../lib/time';
import type { LocalDate } from '../lib/time';
import { isManager, useMe } from '../session';
import { NotFoundPage } from './NotFoundPage';

export function RotaPage() {
  const me = useMe();
  const { monday: param } = useParams();

  if (param !== undefined && !isLocalDate(param)) {
    return <NotFoundPage message="That isn't a date there's a rota for." />;
  }
  if (param !== undefined && !isMonday(param)) {
    return <Navigate to={`/rota/${mondayOf(param)}`} replace />;
  }

  const monday = param ?? mondayOf(today(me.business.timeZone));
  // A new key per week starts each week's page afresh, so nothing from the last week shows while this one loads.
  return <RotaWeekView key={monday} monday={monday} me={me} />;
}

function RotaWeekView({ monday, me }: { monday: LocalDate; me: Me }) {
  const zone = me.business.timeZone;
  const manager = isManager(me);
  const [week, setWeek] = useState<RotaWeek | null>(null);
  const [staff, setStaff] = useState<StaffMember[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [offering, setOffering] = useState<Shift | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    // Everyone gets the staff list: managers to assign shifts, staff to offer one to a colleague.
    Promise.all([api.week(monday), api.staff()]).then(
      ([loadedWeek, loadedStaff]) => {
        if (live) {
          setWeek(loadedWeek);
          setStaff(loadedStaff);
        }
      },
      (error: unknown) => live && setLoadError(describeError(error)),
    );
    return () => {
      live = false;
    };
  }, [monday]);

  async function act(action: () => Promise<RotaWeek>) {
    setBusy(true);
    setActionError(null);
    try {
      setWeek(await action());
    } catch (error) {
      setActionError(describeError(error));
    } finally {
      setBusy(false);
    }
  }

  function publish() {
    if (window.confirm(`Publish the rota for ${formatWeek(monday)}? Your team will get an email.`)) {
      void act(() => api.publish(monday));
    }
  }

  // The dialog stays open until the week has reloaded, so the change is on screen when it closes.
  async function saved() {
    try {
      setWeek(await api.week(monday));
    } catch (error) {
      setActionError(describeError(error));
    }
    setEditing(null);
  }

  const thisMonday = mondayOf(today(zone));

  return (
    <div className="rota">
      <div className="week-header">
        <div className="week-nav">
          <Link className="icon-link" to={`/rota/${addDays(monday, -7)}`} aria-label="Previous week">
            ←
          </Link>
          <h1>{formatWeek(monday)}</h1>
          <Link className="icon-link" to={`/rota/${addDays(monday, 7)}`} aria-label="Next week">
            →
          </Link>
          {monday !== thisMonday && (
            <Link className="this-week" to="/rota">
              This week
            </Link>
          )}
        </div>
        {week && <WeekStatus week={week} zone={zone} manager={manager} />}
        {week && manager && (
          <div className="week-actions">
            {!week.published && week.shifts.length === 0 && (
              <button type="button" disabled={busy} onClick={() => void act(() => api.copyPreviousWeek(monday))}>
                Copy last week
              </button>
            )}
            {!week.published && (
              <button type="button" className="primary" disabled={busy} onClick={publish}>
                Publish
              </button>
            )}
          </div>
        )}
      </div>

      {actionError && (
        <p className="error" role="alert">
          {actionError}
        </p>
      )}
      {notice && (
        <p className="notice-bar" role="status">
          {notice} <Link to="/swaps">See your swaps</Link>
        </p>
      )}

      {loadError ? (
        <p className="error" role="alert">
          {loadError}
        </p>
      ) : !week ? (
        <p className="page-message">Loading the rota…</p>
      ) : !week.published && !manager ? (
        <div className="card page-message">
          <p>The rota for {formatWeek(monday)} isn't published yet. You'll get an email when it is.</p>
        </div>
      ) : (
        <div className="rota-body">
          <WeekGrid
            week={week}
            monday={monday}
            me={me}
            onAdd={
              manager
                ? (date) => setEditing({ shift: null, draft: { date, start: '09:00', end: '17:00', staffMemberId: '' } })
                : undefined
            }
            onEdit={manager ? (shift) => setEditing({ shift, draft: draftFromShift(shift, zone) }) : undefined}
            onOffer={
              !manager && week.published
                ? (shift) => {
                    setNotice(null);
                    setOffering(shift);
                  }
                : undefined
            }
          />
          {manager ? <HoursPanel week={week} staff={staff} /> : <MyWeekPanel week={week} me={me} />}
        </div>
      )}

      {editing && (
        <ShiftEditor
          editing={editing}
          monday={monday}
          zone={zone}
          staff={staff}
          onClose={() => setEditing(null)}
          onSaved={saved}
        />
      )}

      {offering && (
        <SwapOffer
          shift={offering}
          week={week?.shifts ?? []}
          staff={staff.filter((person) => person.id !== me.staffMemberId)}
          zone={zone}
          onClose={() => setOffering(null)}
          onSent={(colleague) => {
            setOffering(null);
            setNotice(`Sent to ${colleague}. They'll get an email.`);
          }}
        />
      )}
    </div>
  );
}

function WeekStatus({ week, zone, manager }: { week: RotaWeek; zone: string; manager: boolean }) {
  if (week.published && week.publishedAt) {
    const at = utcToZoned(week.publishedAt, zone);
    return (
      <p className="status published">
        Published {formatDay(at.date)} at {at.time}
        {manager && <span className="muted"> · changes now email the people affected</span>}
      </p>
    );
  }
  return manager ? <p className="status draft">Draft · only managers can see it until it's published</p> : null;
}

interface WeekGridProps {
  week: RotaWeek;
  monday: LocalDate;
  me: Me;
  onAdd?: (date: LocalDate) => void;
  onEdit?: (shift: Shift) => void;
  /** Staff pick one of their own shifts to offer to a colleague. */
  onOffer?: (shift: Shift) => void;
}

function WeekGrid({ week, monday, me, onAdd, onEdit, onOffer }: WeekGridProps) {
  const zone = me.business.timeZone;
  const todayDate = today(zone);

  return (
    <div className="week-grid">
      {shiftsByDay(week.shifts, monday, zone).map((day) => (
        <section key={day.date} className={day.date === todayDate ? 'day today' : 'day'} aria-label={formatDay(day.date)}>
          <h2 className="day-title">
            <span>{formatDay(day.date)}</span>
            {day.date === todayDate && <span className="today-tag">Today</span>}
          </h2>
          {day.shifts.length === 0 ? (
            <p className="empty">No shifts</p>
          ) : (
            <ul className="shifts">
              {day.shifts.map((shift) => {
                const mine = shift.staffMemberId === me.staffMemberId;
                return (
                  <li key={shift.id}>
                    <ShiftCard
                      shift={shift}
                      zone={zone}
                      mine={mine}
                      onEdit={onEdit && (() => onEdit(shift))}
                      onOffer={mine && onOffer && !hasStarted(shift) ? () => onOffer(shift) : undefined}
                    />
                  </li>
                );
              })}
            </ul>
          )}
          {onAdd && (
            <button type="button" className="add-shift" onClick={() => onAdd(day.date)}>
              + Add shift
            </button>
          )}
        </section>
      ))}
    </div>
  );
}

interface ShiftCardProps {
  shift: Shift;
  zone: string;
  mine: boolean;
  onEdit?: () => void;
  onOffer?: () => void;
}

function ShiftCard({ shift, zone, mine, onEdit, onOffer }: ShiftCardProps) {
  const classes = ['shift'];
  if (!shift.staffMemberId) classes.push('open');
  if (mine) classes.push('mine');
  if (shift.warnings.length > 0) classes.push('warned');

  const times = shiftTimes(shift, zone);
  const person = shift.staffName ?? 'Open shift';
  const body = (
    <>
      <span className="shift-time">{times}</span>
      <span className="shift-person">
        {person}
        {mine && ' (you)'}
      </span>
      {shift.warnings.map((warning) => (
        <span key={warning} className="shift-warning">
          {warning}
        </span>
      ))}
    </>
  );

  if (onEdit) {
    return (
      <button type="button" className={classes.join(' ')} onClick={onEdit} aria-label={`Edit shift ${times}, ${person}`}>
        {body}
      </button>
    );
  }
  if (onOffer) {
    return (
      <button type="button" className={classes.join(' ')} onClick={onOffer} aria-label={`Offer your shift ${times} to a colleague`}>
        {body}
      </button>
    );
  }
  return <div className={classes.join(' ')}>{body}</div>;
}

function HoursPanel({ week, staff }: { week: RotaWeek; staff: StaffMember[] }) {
  const open = openShiftCount(week.shifts);
  return (
    <aside className="card side-panel" aria-labelledby="hours-heading">
      <h2 id="hours-heading">Hours this week</h2>
      <table className="hours">
        <tbody>
          {hoursByPerson(week.shifts, staff).map((person) => (
            <tr key={person.id} className={person.hours > person.limitHours ? 'over' : undefined}>
              <th scope="row">{person.name}</th>
              <td>
                {formatHours(person.hours)} <span className="muted">/ {person.limitHours}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className={open > 0 ? 'open-count' : 'muted'}>
        {week.shifts.length === 0
          ? 'No shifts yet.'
          : open === 0
            ? 'Every shift has someone.'
            : `${open} open ${open === 1 ? 'shift' : 'shifts'} to fill.`}
      </p>
    </aside>
  );
}

function MyWeekPanel({ week, me }: { week: RotaWeek; me: Me }) {
  const mine = week.shifts.filter((shift) => shift.staffMemberId === me.staffMemberId);
  const hours = mine.reduce((sum, shift) => sum + hoursBetween(shift.start, shift.end), 0);
  return (
    <aside className="card side-panel" aria-labelledby="my-week-heading">
      <h2 id="my-week-heading">Your week</h2>
      <p>
        {mine.length === 0
          ? 'No shifts for you this week.'
          : `${mine.length} ${mine.length === 1 ? 'shift' : 'shifts'}, ${formatHours(hours)}.`}
      </p>
      {mine.length > 0 && week.published && (
        <p className="muted">Can't make one? Click it to offer it to a colleague, any time before it starts.</p>
      )}
    </aside>
  );
}
