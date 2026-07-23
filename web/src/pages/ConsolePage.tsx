import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { divIcon } from 'leaflet';
import { useMemo, useState } from 'react';
import { MapContainer, Marker, Polygon, Polyline, TileLayer, useMap } from 'react-leaflet';
import {
  fetchCorridor,
  dispositionLabel,
  evidenceLabel,
  fetchDeadLetters,
  fetchFloodState,
  fetchTickets,
  fetchTimeline,
  postAction,
  postOutcome,
  postOverride,
  postSeverity,
  severityLabel,
  ticketStatusLabel,
  type SeverityTier,
  type TicketListItem,
  type TimelineEntryDto,
} from '../api';
import { expresswaySegments } from '../corridor';
import { useConsoleHub } from '../useConsoleHub';

// Severity chrome: High is the loudest thing in the room (red), Medium amber, Low cool gray.
const sevPill: Record<SeverityTier, string> = {
  0: 'stamp stamp-tint bg-paper-sunken text-ink-soft',
  1: 'stamp stamp-tint bg-warn-tint text-warn',
  2: 'stamp stamp-tint bg-sev-tint text-sev',
};
const sevBar: Record<SeverityTier, string> = { 0: 'bg-ink-faint', 1: 'bg-warn', 2: 'bg-sev' };
const sevDot: Record<SeverityTier, string> = { 0: '#8a8a94', 1: '#e8b046', 2: '#f25c54' };

const shortId = (id: string) => `OG-${id.slice(0, 4).toUpperCase()}`;

// Corridor midpoint fallback (Kara bridge) when nothing is selected.
const corridorCenter: [number, number] = [6.68, 3.4];

export default function ConsolePage() {
  useConsoleHub();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [sevFilter, setSevFilter] = useState<SeverityTier | 'all'>('all');

  const tickets = useQuery({ queryKey: ['tickets'], queryFn: fetchTickets });
  const flood = useQuery({ queryKey: ['flood'], queryFn: fetchFloodState, refetchInterval: 15_000 });
  const deadLetters = useQuery({
    queryKey: ['dead-letters'],
    queryFn: fetchDeadLetters,
    refetchInterval: 30_000,
  });

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    return (tickets.data ?? [])
      .filter((t) => sevFilter === 'all' || t.severity === sevFilter)
      .filter(
        (t) =>
          q === '' ||
          t.summary.toLowerCase().includes(q) ||
          shortId(t.id).toLowerCase().includes(q) ||
          (t.flags ?? '').toLowerCase().includes(q),
      );
  }, [tickets.data, search, sevFilter]);

  const selected = tickets.data?.find((t) => t.id === selectedId);
  const open = (tickets.data ?? []).filter((t) => t.status === 0 || t.status === 1);
  const kpis = {
    active: open.length,
    high: open.filter((t) => t.severity === 2).length,
    undispatched: open.filter((t) => t.dispatch === 0).length,
    oldestWaitMin: open
      .filter((t) => t.dispatch === 0)
      .reduce((max, t) => Math.max(max, (Date.now() - +new Date(t.createdAt)) / 60_000), 0),
  };

  return (
    <div className="grid h-full grid-cols-[400px_minmax(0,1fr)] xl:grid-cols-[400px_minmax(0,1fr)_460px]">
      {/* ---- Queue ---- */}
      <section className="flex min-h-0 flex-col border-r border-hairline">
        {(deadLetters.data?.count ?? 0) > 0 && (
          <div className="m-3 mb-0 rounded-lg border border-sev/50 bg-sev-tint px-3 py-2 text-[0.8rem] font-semibold text-sev">
            ⛔ {deadLetters.data!.count} message{deadLetters.data!.count === 1 ? '' : 's'} dead-lettered —
            possible lost reports. Escalate for replay (D-008).
          </div>
        )}
        {flood.data?.active && (
          <div className="m-3 mb-0 rounded-lg border border-warn/50 bg-warn-tint px-3 py-2 text-[0.8rem] font-semibold text-warn">
            ⚠ Possible report flood: {flood.data.ticketsInWindow} in {flood.data.windowMinutes} min.
            New reports capped at Review.
          </div>
        )}

        {/* KPI strip */}
        <div className="grid grid-cols-4 gap-2 p-3">
          <Kpi label="Active" value={kpis.active} />
          <Kpi label="High sev" value={kpis.high} tone={kpis.high > 0 ? 'text-sev' : undefined} />
          <Kpi label="Unassigned" value={kpis.undispatched} tone={kpis.undispatched > 0 ? 'text-warn' : undefined} />
          <Kpi label="Oldest wait" value={formatWait(kpis.oldestWaitMin)} />
        </div>

        <div className="flex items-center gap-2 px-3 pb-2">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search id, summary, flags…"
            className="w-full rounded-lg border border-hairline bg-paper-raised px-3 py-2 text-[0.85rem] placeholder:text-ink-faint focus:border-hairline-strong focus:outline-none"
          />
        </div>
        <div className="flex gap-1.5 px-3 pb-3">
          {(['all', 2, 1, 0] as const).map((f) => (
            <button
              key={String(f)}
              className={`seg-btn ${sevFilter === f ? 'seg-btn-on' : ''}`}
              onClick={() => setSevFilter(f)}
            >
              {f === 'all' ? 'All' : { 2: 'High', 1: 'Med', 0: 'Low' }[f]}
            </button>
          ))}
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-3 pb-3">
          {tickets.isLoading && <p className="text-ink-faint">Loading…</p>}
          {tickets.error && <p className="font-semibold text-sev">Failed to load tickets.</p>}
          {visible.length === 0 && !tickets.isLoading && (
            <div className="rounded-xl border border-dashed border-hairline-strong p-8 text-center text-sm text-ink-faint">
              Nothing in the queue for this filter. Reports from the corridor arrive here live.
            </div>
          )}
          <ul className="m-0 flex list-none flex-col gap-2 p-0">
            {visible.map((t) => (
              <li key={t.id}>
                <QueueCard ticket={t} selected={t.id === selectedId} onSelect={() => setSelectedId(t.id)} />
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* ---- Map ---- */}
      <section className="relative min-h-0">
        <CorridorMap tickets={tickets.data ?? []} selectedId={selectedId} onSelect={setSelectedId} />
        {/* On smaller screens the detail panel overlays the map */}
        {selected && (
          <div className="absolute top-0 right-0 bottom-0 z-1000 w-115 max-w-full overflow-y-auto border-l border-hairline bg-paper xl:hidden">
            <DetailPanel ticket={selected} onClose={() => setSelectedId(null)} />
          </div>
        )}
      </section>

      {/* ---- Detail (wide screens) ---- */}
      <section className="hidden min-h-0 overflow-y-auto border-l border-hairline xl:block">
        {selected ? (
          <DetailPanel ticket={selected} onClose={() => setSelectedId(null)} />
        ) : (
          <div className="p-6 text-sm text-ink-faint">
            Select an incident — from the queue or the map — to see evidence, timeline, and actions.
          </div>
        )}
      </section>
    </div>
  );
}

function formatWait(mins: number) {
  if (mins < 120) return `${Math.round(mins)}m`;
  const h = mins / 60;
  return h < 48 ? `${Math.round(h)}h` : `${Math.round(h / 24)}d`;
}

function Kpi({ label, value, tone }: { label: string; value: number | string; tone?: string }) {
  return (
    <div className="panel px-3 py-2">
      <div className={`font-mono text-[1.25rem] leading-tight font-semibold ${tone ?? ''}`}>{value}</div>
      <div className="text-[0.62rem] font-semibold uppercase tracking-widest text-ink-faint">{label}</div>
    </div>
  );
}

function QueueCard({
  ticket: t,
  selected,
  onSelect,
}: {
  ticket: TicketListItem;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      onClick={onSelect}
      className={`panel relative block w-full cursor-pointer overflow-hidden px-4 py-3 pl-5 text-left transition-colors duration-100 hover:bg-paper-sunken ${
        selected ? 'border-hairline-strong bg-paper-sunken' : ''
      }`}
    >
      {t.severity !== null && (
        <span className={`absolute top-0 left-0 h-full w-1 ${sevBar[t.severity]}`} aria-hidden />
      )}
      <div className="flex items-center gap-2">
        <span className="font-mono text-[0.72rem] text-ink-faint">{shortId(t.id)}</span>
        {t.severity !== null && <span className={sevPill[t.severity]}>{severityLabel[t.severity]}</span>}
        <span className="stamp text-ink-faint">{dispositionLabel[t.disposition]}</span>
        <span className="ml-auto text-[0.7rem] text-ink-faint">{timeAgo(t.updatedAt)}</span>
      </div>
      <div className="mt-1 line-clamp-2 font-medium leading-snug">{t.summary}</div>
      {t.casualtyEstimate && (
        <div className="text-[0.78rem] font-semibold text-sev">casualties: {t.casualtyEstimate}</div>
      )}
      <div className="mt-1 flex flex-wrap items-center gap-1">
        <span className="stamp text-ink-faint">{evidenceLabel[t.evidence]}</span>
        {t.reporterCount > 1 && (
          <span className="stamp stamp-tint bg-act-tint text-act">{t.reporterCount} reporters</span>
        )}
        {t.locationResolvedAt && <span className="stamp text-ok">located</span>}
        {t.dispatch > 0 && (
          <span className="stamp stamp-solid bg-act">
            {['', 'dispatched', 'on scene', 'transported'][t.dispatch]}
          </span>
        )}
        {t.flags?.split(',').map((f) => (
          <span key={f} className="stamp text-sev">
            {f}
          </span>
        ))}
      </div>
      {t.pendingAsk && <div className="text-[0.75rem] text-warn italic">⏳ {t.pendingAsk}</div>}
      <div className="mt-0.5 font-mono text-[0.68rem] text-ink-faint">
        {ticketStatusLabel[t.status]} · {t.language ?? '—'}
      </div>
    </button>
  );
}

function timeAgo(iso: string) {
  const mins = Math.round((Date.now() - +new Date(iso)) / 60_000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const h = Math.floor(mins / 60);
  return h < 24 ? `${h}h ago` : `${Math.floor(h / 24)}d ago`;
}

// ---- Map ----

/**
 * The enforced-geofence band: centerline offset ±bufferKm using the SAME
 * equirectangular projection as the backend's CorridorGeofence, so what the
 * dispatcher sees shaded is what Stage 0 actually checks.
 */
function bufferBand(
  centerline: { lat: number; lng: number }[],
  bufferKm: number,
): [number, number][] {
  if (centerline.length < 2) return [];
  const R = 6371;
  const rad = Math.PI / 180;
  const cosLat = Math.cos(6.7 * rad);
  const proj = (p: { lat: number; lng: number }) => [p.lng * cosLat * rad * R, p.lat * rad * R];
  const unproj = (x: number, y: number): [number, number] => [y / (rad * R), x / (cosLat * rad * R)];

  const pts = centerline.map(proj);
  const left: [number, number][] = [];
  const right: [number, number][] = [];
  for (let i = 0; i < pts.length; i++) {
    // Vertex normal = normalized average of adjacent segment normals.
    let nx = 0;
    let ny = 0;
    if (i > 0) {
      const [dx, dy] = [pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]];
      const len = Math.hypot(dx, dy) || 1;
      nx += -dy / len;
      ny += dx / len;
    }
    if (i < pts.length - 1) {
      const [dx, dy] = [pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]];
      const len = Math.hypot(dx, dy) || 1;
      nx += -dy / len;
      ny += dx / len;
    }
    const len = Math.hypot(nx, ny) || 1;
    nx /= len;
    ny /= len;
    left.push(unproj(pts[i][0] + nx * bufferKm, pts[i][1] + ny * bufferKm));
    right.push(unproj(pts[i][0] - nx * bufferKm, pts[i][1] - ny * bufferKm));
  }
  return [...left, ...right.reverse()];
}

function FlyTo({ target }: { target: [number, number] | null }) {
  const map = useMap();
  if (target) map.flyTo(target, Math.max(map.getZoom(), 13), { duration: 0.6 });
  return null;
}

function CorridorMap({
  tickets,
  selectedId,
  onSelect,
}: {
  tickets: TicketListItem[];
  selectedId: string | null;
  onSelect: (id: string) => void;
}) {
  const located = tickets.filter(
    (t) => t.locationLat !== null && t.locationLng !== null && t.status !== 3 && t.status !== 4,
  );
  const selected = located.find((t) => t.id === selectedId);

  const corridor = useQuery({ queryKey: ['corridor'], queryFn: fetchCorridor, staleTime: Infinity });
  const band = useMemo(
    () => (corridor.data ? bufferBand(corridor.data.centerline, corridor.data.bufferKm) : []),
    [corridor.data],
  );

  return (
    <MapContainer
      center={corridorCenter}
      zoom={11}
      className="h-full w-full"
      zoomControl
      attributionControl
    >
      <TileLayer
        url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> &copy; <a href="https://carto.com/attributions">CARTO</a>'
      />
      {/* Enforced geofence band (from triage config) — reports pinned outside this get flagged */}
      {band.length > 0 && (
        <Polygon
          positions={band}
          pathOptions={{
            color: '#4f7fff',
            weight: 1,
            dashArray: '6 5',
            opacity: 0.45,
            fillColor: '#4f7fff',
            fillOpacity: 0.07,
            interactive: false,
          }}
        />
      )}
      {/* The expressway itself (OSM geometry, display-only): soft glow + crisp core */}
      <Polyline
        positions={expresswaySegments}
        pathOptions={{ color: '#7da2e8', weight: 7, opacity: 0.18, interactive: false }}
      />
      <Polyline
        positions={expresswaySegments}
        pathOptions={{ color: '#8fb0ea', weight: 2, opacity: 0.85, interactive: false }}
      />
      {located.map((t) => {
        const isSel = t.id === selectedId;
        const color = sevDot[t.severity ?? 0];
        return (
          <Marker
            key={t.id}
            position={[t.locationLat!, t.locationLng!]}
            eventHandlers={{ click: () => onSelect(t.id) }}
            icon={divIcon({
              className: '',
              iconSize: [isSel ? 26 : 16, isSel ? 26 : 16],
              html: `<div style="width:100%;height:100%;border-radius:9999px;background:${color};
                border:${isSel ? '4px solid rgba(242,92,84,0.35)' : '2px solid rgba(0,0,0,0.55)'};
                box-shadow:0 0 ${isSel ? '14px' : '6px'} ${color}66;"></div>`,
            })}
          />
        );
      })}
      <FlyTo target={selected ? [selected.locationLat!, selected.locationLng!] : null} />
    </MapContainer>
  );
}

// ---- Detail panel ----

const dispatchLabels = ['not dispatched', '🚑 dispatched', '📍 on scene', '🏥 transported'];

/** Hand the crew a paper copy — radio rooms still print. */
function printBriefing(ticket: TicketListItem) {
  const w = window.open('', '_blank', 'width=640,height=800');
  if (!w) return;
  w.document.write(`<!doctype html><title>Crew briefing ${ticket.id.slice(0, 8)}</title>
    <style>body{font-family:Georgia,serif;max-width:60ch;margin:2rem auto;line-height:1.5}
    h1{font-size:1rem;text-transform:uppercase;letter-spacing:0.1em;border-bottom:2px solid #000;padding-bottom:4px}
    pre{white-space:pre-wrap;font:inherit}</style>
    <h1>First10 · Crew briefing · incident ${ticket.id.slice(0, 8)}</h1>
    <pre>${ticket.crewBriefing?.replace(/</g, '&lt;') ?? ''}</pre>
    <p><small>Generated ${new Date().toLocaleString()} — verify all details on arrival.</small></p>`);
  w.document.close();
  w.print();
}

function DetailPanel({ ticket, onClose }: { ticket: TicketListItem; onClose: () => void }) {
  const queryClient = useQueryClient();
  const invalidate = () =>
    setTimeout(() => void queryClient.invalidateQueries({ queryKey: ['tickets'] }), 800);
  const action = useMutation({
    mutationFn: (a: 'dispatch' | 'arrive' | 'transport') => postAction(ticket.id, a),
    onSuccess: invalidate,
  });
  const outcome = useMutation({
    mutationFn: (o: 0 | 1 | 2) => postOutcome(ticket.id, o),
    onSuccess: invalidate,
  });
  const severity = useMutation({
    mutationFn: (s: SeverityTier) => postSeverity(ticket.id, s),
    onSuccess: invalidate,
  });
  const override = useMutation({
    mutationFn: ({ kind, reason }: { kind: 'promote' | 'reject'; reason: string }) =>
      postOverride(ticket.id, kind, reason),
    onSuccess: invalidate,
  });
  const askOverride = (kind: 'promote' | 'reject') => {
    const reason = window.prompt(
      kind === 'promote'
        ? 'Promote this ticket over triage — why? (recorded in the audit)'
        : 'Reject this ticket — why? (recorded in the audit)',
    );
    if (reason?.trim()) override.mutate({ kind, reason: reason.trim() });
  };

  const timeline = useQuery({
    queryKey: ['timeline', ticket.id],
    queryFn: () => fetchTimeline(ticket.id),
    refetchInterval: 10_000,
  });

  const closed = ticket.status === 4 || ticket.status === 3;
  const statement = timeline.data?.find((e) => e.direction === 0 && e.kind === 0 && e.text);
  const media = timeline.data?.filter((e) => e.direction === 0 && e.mediaUrl) ?? [];

  return (
    <div className="p-4">
      <div className="flex items-center gap-2">
        <span className="font-mono text-[0.8rem] text-ink-faint">{shortId(ticket.id)}</span>
        {ticket.severity !== null && (
          <span className={sevPill[ticket.severity]}>{severityLabel[ticket.severity]}</span>
        )}
        <span className="stamp stamp-tint bg-act-tint text-act">{ticketStatusLabel[ticket.status]}</span>
        <button className="ghost-btn ml-auto" onClick={onClose} aria-label="Close detail">
          ✕
        </button>
      </div>
      <h2 className="font-display mt-2 text-[1.3rem] leading-tight font-extrabold">{ticket.summary}</h2>
      <div className="mt-1 font-mono text-[0.78rem] text-ink-faint">
        {ticket.locationLat !== null
          ? `${ticket.locationLat.toFixed(4)}, ${ticket.locationLng!.toFixed(4)}`
          : 'no location pin yet'}
        {' · '}
        {dispositionLabel[ticket.disposition]} · {evidenceLabel[ticket.evidence]}
        {ticket.reporterCount > 1 ? ` · ${ticket.reporterCount} reporters` : ''}
      </div>

      {ticket.casualtyEstimate && (
        <div className="mt-2 text-[0.85rem] font-semibold text-sev">
          casualties: {ticket.casualtyEstimate}
        </div>
      )}
      {ticket.pendingAsk && <div className="mt-1 text-[0.8rem] text-warn italic">⏳ {ticket.pendingAsk}</div>}

      {statement && (
        <div className="mt-4">
          <span className="microlabel mb-1">Reported statement</span>
          <div className="panel px-3 py-2.5 text-[0.9rem]">“{statement.text}”</div>
        </div>
      )}

      {media.length > 0 && (
        <div className="mt-4">
          <span className="microlabel mb-1">Media · {media.length} attached (blurred at ingest)</span>
          <div className="grid grid-cols-3 gap-2">
            {media.map((m) =>
              m.kind === 1 ? (
                <a key={m.id} href={m.mediaUrl!} target="_blank" rel="noreferrer">
                  <img
                    src={m.mediaUrl!}
                    alt="scene evidence"
                    className="h-24 w-full rounded-lg border border-hairline object-cover"
                  />
                </a>
              ) : (
                <div key={m.id} className="panel col-span-3 px-2 py-1.5">
                  <audio controls src={m.mediaUrl!} className="h-8 w-full" />
                  <div className="mt-1 text-[0.78rem] text-ink-soft italic">
                    {m.transcriptText ? `“${m.transcriptText}”` : 'transcript unavailable — listen'}
                  </div>
                </div>
              ),
            )}
          </div>
        </div>
      )}

      {/* Severity re-grade — one click, audited server-side */}
      <div className="mt-4">
        <span className="microlabel mb-1">Triage severity</span>
        <div className="flex gap-1.5">
          {([2, 1, 0] as const).map((s) => (
            <button
              key={s}
              className={`seg-btn flex-1 ${ticket.severity === s ? (s === 2 ? 'seg-btn-on border-sev/60! text-sev!' : 'seg-btn-on') : ''}`}
              disabled={severity.isPending || closed}
              onClick={() => ticket.severity !== s && severity.mutate(s)}
            >
              {severityLabel[s]}
            </button>
          ))}
        </div>
      </div>

      {/* Progress rail */}
      <div className="mt-4">
        <span className="microlabel mb-1">Progress</span>
        <ProgressRail ticket={ticket} />
      </div>

      {/* Actions */}
      <div className="mt-4 flex flex-wrap items-center gap-2">
        <span className="font-display mr-1 text-[0.8rem] font-extrabold uppercase tracking-wide">
          {dispatchLabels[ticket.dispatch]}
        </span>
        {/* Loop closure: each click messages every reporter (R1e) */}
        <button
          className="action-btn action-btn-primary"
          disabled={ticket.dispatch !== 0 || closed || action.isPending}
          onClick={() => action.mutate('dispatch')}
        >
          Dispatch
        </button>
        <button
          className="action-btn"
          disabled={ticket.dispatch !== 1 || action.isPending}
          onClick={() => action.mutate('arrive')}
        >
          Arrived
        </button>
        <button
          className="action-btn"
          disabled={ticket.dispatch !== 2 || action.isPending}
          onClick={() => action.mutate('transport')}
        >
          Transported
        </button>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        {ticket.status !== 1 && !closed && (
          <button
            className="ghost-btn"
            disabled={override.isPending}
            onClick={() => askOverride('promote')}
            title="Overrule triage and promote this ticket"
          >
            ↑ promote
          </button>
        )}
        {ticket.dispatch === 0 && !closed && (
          <button
            className="ghost-btn text-sev"
            disabled={override.isPending}
            onClick={() => askOverride('reject')}
            title="Reject this ticket (duplicate / test / mistake) — reason required"
          >
            ✗ reject
          </button>
        )}
        <span className="ml-auto flex items-center gap-1 text-[0.75rem] text-ink-soft">
          outcome:
          {ticket.outcome !== null ? (
            <strong className="uppercase">{['real', 'false', 'unverifiable'][ticket.outcome]}</strong>
          ) : (
            <>
              <button className="ghost-btn" onClick={() => outcome.mutate(0)} disabled={outcome.isPending}>
                real
              </button>
              <button className="ghost-btn" onClick={() => outcome.mutate(1)} disabled={outcome.isPending}>
                false
              </button>
              <button className="ghost-btn" onClick={() => outcome.mutate(2)} disabled={outcome.isPending}>
                unverifiable
              </button>
            </>
          )}
        </span>
      </div>

      {ticket.timelineDigest && (
        <div className="panel mt-4 px-3 py-2.5 text-sm whitespace-pre-wrap">
          <span className="microlabel">Digest</span>
          {ticket.timelineDigest}
        </div>
      )}
      {ticket.contradictions && (
        <div className="mt-3 rounded-xl border border-warn/60 bg-warn-tint px-3 py-2.5 text-sm">
          <span className="microlabel text-warn">⚠ Contradictions — verify, don't assume</span>
          {ticket.contradictions}
        </div>
      )}
      {ticket.crewBriefing && (
        <details className="mt-3">
          <summary className="font-display cursor-pointer text-[0.7rem] font-bold tracking-widest text-act uppercase">
            Crew briefing
          </summary>
          <pre className="panel font-body mt-2 p-3 text-[0.85rem] whitespace-pre-wrap">
            {ticket.crewBriefing}
          </pre>
          <button className="ghost-btn mt-1" onClick={() => printBriefing(ticket)}>
            🖨 print briefing
          </button>
        </details>
      )}

      <h3 className="microlabel mt-5 mb-2 border-b border-hairline pb-1">Timeline</h3>
      {timeline.data?.map((entry) => <TimelineRow key={entry.id} entry={entry} />)}
    </div>
  );
}

function ProgressRail({ ticket }: { ticket: TicketListItem }) {
  const at = (iso: string | null) =>
    iso ? new Date(iso).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' }) : null;
  const steps: { label: string; time: string | null; done: boolean }[] = [
    { label: 'Report received', time: at(ticket.createdAt), done: true },
    {
      label: 'Triaged & verified',
      time: null,
      done: ticket.status === 1 || ticket.dispatch > 0 || ticket.status === 4,
    },
    { label: 'Unit dispatched', time: at(ticket.dispatchedAt), done: ticket.dispatch >= 1 },
    { label: 'Arrived on scene', time: at(ticket.arrivedAt), done: ticket.dispatch >= 2 },
    { label: 'Case resolved', time: at(ticket.transportedAt), done: ticket.dispatch >= 3 },
  ];
  return (
    <ol className="m-0 list-none p-0">
      {steps.map((s, i) => (
        <li key={s.label} className="relative flex items-baseline gap-3 pb-3 pl-5 last:pb-0">
          {i < steps.length - 1 && (
            <span className="absolute top-2 left-1.25 h-full w-px bg-hairline" aria-hidden />
          )}
          <span
            className={`absolute top-1 left-0 h-2.75 w-2.75 rounded-full ${
              s.done ? 'bg-act' : 'border border-hairline-strong bg-paper-sunken'
            }`}
            aria-hidden
          />
          <span className={`text-[0.85rem] ${s.done ? 'font-medium' : 'text-ink-faint'}`}>{s.label}</span>
          <span className="ml-auto font-mono text-[0.72rem] text-ink-faint">{s.time ?? '—'}</span>
        </li>
      ))}
    </ol>
  );
}

function TimelineRow({ entry }: { entry: TimelineEntryDto }) {
  const isSystem = entry.direction === 2;
  const isAlarm = isSystem && entry.text?.startsWith('⚠');
  const who =
    entry.direction === 0
      ? `reporter ${entry.conversationId.slice(0, 8)}`
      : entry.direction === 1
        ? 'system → reporter'
        : 'file note';

  return (
    <div
      className={`timeline-entry mb-2 rounded-lg px-3 py-2 ${
        isAlarm
          ? 'border border-warn/60 bg-warn-tint'
          : isSystem
            ? 'bg-paper-sunken text-[0.85rem] text-ink-soft italic'
            : entry.direction === 0
              ? 'border border-hairline bg-paper-raised'
              : 'border border-dashed border-hairline-strong'
      }`}
    >
      <span className="mb-0.5 block font-mono text-[0.7rem] text-ink-faint">
        <strong className={entry.direction === 0 ? 'text-act' : entry.direction === 1 ? 'text-ok' : ''}>
          {who}
        </strong>{' '}
        · {new Date(entry.occurredAt).toLocaleTimeString()}
      </span>
      <EntryBody entry={entry} />
    </div>
  );
}

function EntryBody({ entry }: { entry: TimelineEntryDto }) {
  switch (entry.kind) {
    case 1: // Image — blurred version only, by construction (D-009)
      return entry.mediaUrl ? (
        <img
          src={entry.mediaUrl}
          alt="scene"
          className="mt-1 block max-h-60 max-w-80 rounded-lg border border-hairline-strong"
        />
      ) : (
        <em>[photo]</em>
      );
    case 2: // Voice — playable audio beside transcript (D-013: audio is ground truth)
      return (
        <div className="mt-1">
          {entry.mediaUrl ? <audio controls src={entry.mediaUrl} /> : <em>[voice note]</em>}
          <div className="mt-0.5 text-[0.85rem] text-ink-soft italic">
            {entry.transcriptText
              ? `“${entry.transcriptText}”`
              : 'transcript unavailable — listen to audio'}
          </div>
        </div>
      );
    case 3:
      return <div className="font-mono text-[0.85rem]">📍 {entry.text}</div>;
    default:
      return <div>{entry.text}</div>;
  }
}
