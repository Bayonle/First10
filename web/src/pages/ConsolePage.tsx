import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
  dispositionLabel,
  evidenceLabel,
  fetchDeadLetters,
  fetchFloodState,
  fetchTickets,
  fetchTimeline,
  postAction,
  postOutcome,
  severityLabel,
  ticketStatusLabel,
  type Disposition,
  type SeverityTier,
  type TicketListItem,
  type TimelineEntryDto,
} from '../api';
import { useConsoleHub } from '../useConsoleHub';

const sevStampClass: Record<SeverityTier, string> = {
  0: 'bg-ok',
  1: 'bg-warn',
  2: 'bg-sev',
};

const dispositionStampClass: Record<Disposition, string> = {
  0: 'text-ink-faint',
  1: 'text-ink-faint',
  2: 'text-warn-deep',
  3: 'text-act',
  4: 'text-ok',
  5: 'text-ok bg-ok-tint',
};

export default function ConsolePage() {
  useConsoleHub();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const tickets = useQuery({ queryKey: ['tickets'], queryFn: fetchTickets });
  const flood = useQuery({
    queryKey: ['flood'],
    queryFn: fetchFloodState,
    refetchInterval: 15_000,
  });
  const deadLetters = useQuery({
    queryKey: ['dead-letters'],
    queryFn: fetchDeadLetters,
    refetchInterval: 30_000,
  });
  const timeline = useQuery({
    queryKey: ['timeline', selectedId],
    queryFn: () => fetchTimeline(selectedId!),
    enabled: selectedId !== null,
  });

  return (
    <div>
      {(deadLetters.data?.count ?? 0) > 0 && (
        <div className="mb-6 border-2 border-sev bg-sev-tint px-4 py-3 font-semibold text-sev">
          ⛔ {deadLetters.data!.count} report message{deadLetters.data!.count === 1 ? '' : 's'} dead-lettered —
          possible lost reports. Escalate to engineering for dead-letter replay (D-008: never silent).
        </div>
      )}
      {flood.data?.active && (
        <div className="mb-6 flex items-center gap-3 border-[1.5px] border-sev bg-sev-tint px-4 py-3 font-semibold text-sev">
          ⚠ Possible report flood: {flood.data.ticketsInWindow} incidents in the last{' '}
          {flood.data.windowMinutes} minutes (threshold {flood.data.threshold}). New reports are
          capped at Review.
        </div>
      )}

      {/* Independent scroll containers: the queue can be 100 tickets deep, but the
          selected incident's action panel must NEVER scroll out of reach. */}
      <div className="grid h-[calc(100vh-7.5rem)] grid-cols-[minmax(320px,400px)_1fr] items-start gap-8">
        <section className="h-full overflow-y-auto pr-1">
          <h2 className="microlabel mb-3 border-b border-hairline-strong pb-1">
            Incident queue
          </h2>
          {tickets.isLoading && <p className="text-ink-faint">Loading…</p>}
          {tickets.error && <p className="font-semibold text-sev">Failed to load tickets.</p>}
          {tickets.data?.length === 0 && (
            <div className="border border-dashed border-hairline-strong p-8 text-center text-sm text-ink-faint">
              No incidents in the queue. Reports from the corridor arrive here in real time.
            </div>
          )}
          <ul className="m-0 list-none p-0">
            {tickets.data?.map((t, i) => (
              <li key={t.id}>
                <button
                  onClick={() => setSelectedId(t.id)}
                  className={`block w-full cursor-pointer border border-hairline bg-paper-raised px-4 py-3 text-left transition-colors duration-100 hover:bg-paper-sunken ${
                    i !== 0 ? 'border-t-0' : ''
                  } ${t.id === selectedId ? 'outline-2 -outline-offset-2 outline-ink' : ''}`}
                >
                  <div className="flex flex-wrap gap-1">
                    {t.severity !== null && (
                      <span className={`stamp stamp-solid ${sevStampClass[t.severity]}`}>
                        {severityLabel[t.severity]}
                      </span>
                    )}
                    <span className={`stamp ${dispositionStampClass[t.disposition]}`}>
                      {dispositionLabel[t.disposition]}
                    </span>
                    <span className="stamp text-ink-soft">{evidenceLabel[t.evidence]}</span>
                    {t.reporterCount > 1 && (
                      <span className="stamp bg-act-tint text-act">
                        {t.reporterCount} reporters
                      </span>
                    )}
                    {t.locationResolvedAt && <span className="stamp text-ok">located</span>}
                    {t.dispatch > 0 && (
                      <span className="stamp bg-ink text-paper-raised border-transparent">
                        {['', 'dispatched', 'arrived', 'transported'][t.dispatch]}
                      </span>
                    )}
                    {t.flags?.split(',').map((f) => (
                      <span key={f} className="stamp text-sev">
                        {f}
                      </span>
                    ))}
                  </div>
                  <div className="mt-1 line-clamp-2 font-medium leading-snug">{t.summary}</div>
                  {t.casualtyEstimate && (
                    <div className="text-[0.8rem] font-semibold text-sev">
                      casualties: {t.casualtyEstimate}
                    </div>
                  )}
                  {t.pendingAsk && (
                    <div className="text-[0.78rem] text-warn-deep italic">⏳ {t.pendingAsk}</div>
                  )}
                  <div className="font-mono text-[0.72rem] text-ink-faint">
                    {ticketStatusLabel[t.status]} · {t.language ?? '—'} ·{' '}
                    {new Date(t.updatedAt).toLocaleTimeString()}
                  </div>
                </button>
              </li>
            ))}
          </ul>
        </section>

        <section className="h-full overflow-y-auto pr-1">
          {selectedId === null ? (
            <>
              <h2 className="microlabel mb-3 border-b border-hairline-strong pb-1">Timeline</h2>
              <div className="border border-dashed border-hairline-strong p-8 text-center text-sm text-ink-faint">
                Select an incident to see its full timeline — every message, both directions,
                with system annotations kept visually distinct.
              </div>
            </>
          ) : (
            <>
              <DetailPanel ticket={tickets.data?.find((t) => t.id === selectedId)} />
              <h2 className="microlabel mb-3 border-b border-hairline-strong pb-1">Timeline</h2>
              {timeline.data?.map((entry) => <TimelineRow key={entry.id} entry={entry} />)}
            </>
          )}
        </section>
      </div>
    </div>
  );
}

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

function DetailPanel({ ticket }: { ticket: TicketListItem | undefined }) {
  const queryClient = useQueryClient();
  const invalidate = () =>
    setTimeout(() => void queryClient.invalidateQueries({ queryKey: ['tickets'] }), 800);
  const action = useMutation({
    mutationFn: (a: 'dispatch' | 'arrive' | 'transport') => postAction(ticket!.id, a),
    onSuccess: invalidate,
  });
  const outcome = useMutation({
    mutationFn: (o: 0 | 1 | 2) => postOutcome(ticket!.id, o),
    onSuccess: invalidate,
  });
  const override = useMutation({
    mutationFn: ({ kind, reason }: { kind: 'promote' | 'reject'; reason: string }) =>
      postOverride(ticket!.id, kind, reason),
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

  if (!ticket) return null;
  const closed = ticket.status === 4 || ticket.status === 3;

  return (
    <div className="mb-6 border border-hairline-strong bg-paper-raised p-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-display mr-2 text-[0.9rem] font-extrabold uppercase tracking-wide">
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

        {/* Manual triage override — the dispatcher is the final gate (D-008) */}
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
            <strong className="uppercase">
              {['real', 'false', 'unverifiable'][ticket.outcome]}
            </strong>
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
        <div className="mt-3 text-sm whitespace-pre-wrap">
          <span className="microlabel">Digest</span>
          {ticket.timelineDigest}
        </div>
      )}
      {ticket.contradictions && (
        <div className="mt-3 border-[1.5px] border-warn bg-warn-tint px-3 py-2 text-sm">
          <span className="microlabel text-warn-deep">⚠ Contradictions — verify, don't assume</span>
          {ticket.contradictions}
        </div>
      )}
      {ticket.crewBriefing && (
        <details className="mt-3">
          <summary className="font-display cursor-pointer text-[0.7rem] font-bold uppercase tracking-widest text-act">
            Crew briefing
          </summary>
          <pre className="mt-2 border border-hairline bg-paper-sunken p-3 font-body text-[0.85rem] whitespace-pre-wrap">
            {ticket.crewBriefing}
          </pre>
          <button className="ghost-btn mt-1" onClick={() => printBriefing(ticket)}>
            🖨 print briefing
          </button>
        </details>
      )}
    </div>
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
      className={`timeline-entry mb-2 px-3 py-2 ${
        isAlarm
          ? 'border-[1.5px] border-warn bg-warn-tint'
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
          className="mt-1 block max-h-60 max-w-80 border border-hairline-strong"
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
