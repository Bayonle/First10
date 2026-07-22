import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import {
  dispositionColor,
  dispositionLabel,
  evidenceLabel,
  fetchFloodState,
  fetchTickets,
  fetchTimeline,
  mediaUrl,
  postAction,
  postOutcome,
  severityColor,
  severityLabel,
  ticketStatusLabel,
  type TicketListItem,
  type TimelineEntryDto,
} from '../api';
import { useConsoleHub } from '../useConsoleHub';

export default function ConsolePage() {
  useConsoleHub();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const tickets = useQuery({ queryKey: ['tickets'], queryFn: fetchTickets });
  const flood = useQuery({
    queryKey: ['flood'],
    queryFn: fetchFloodState,
    refetchInterval: 15_000,
  });
  const timeline = useQuery({
    queryKey: ['timeline', selectedId],
    queryFn: () => fetchTimeline(selectedId!),
    enabled: selectedId !== null,
  });

  return (
    <div>
      {flood.data?.active && (
        <div
          style={{
            background: '#fee',
            border: '1px solid #c00',
            color: '#900',
            padding: '0.6rem 1rem',
            borderRadius: 6,
            marginBottom: '1rem',
            fontWeight: 600,
          }}
        >
          ⚠ Possible report flood: {flood.data.ticketsInWindow} incidents in the last{' '}
          {flood.data.windowMinutes} minutes (threshold {flood.data.threshold}). New reports are
          capped at Review.
        </div>
      )}

      <div style={{ display: 'flex', gap: '1rem', alignItems: 'flex-start' }}>
        <section style={{ flex: '0 0 26rem' }}>
          <h2>Incidents</h2>
          {tickets.isLoading && <p>Loading…</p>}
          {tickets.error && <p style={{ color: 'crimson' }}>Failed to load tickets.</p>}
          {tickets.data?.length === 0 && <p>No incidents yet.</p>}
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {tickets.data?.map((t) => (
              <li key={t.id} style={{ marginBottom: '0.5rem' }}>
                <button
                  onClick={() => setSelectedId(t.id)}
                  style={{
                    width: '100%',
                    textAlign: 'left',
                    padding: '0.5rem',
                    border: t.id === selectedId ? '2px solid #0a6' : '1px solid #ccc',
                    borderRadius: 6,
                    cursor: 'pointer',
                    background: 'white',
                  }}
                >
                  <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap', marginBottom: 2 }}>
                    <Badge color={dispositionColor[t.disposition]}>
                      {dispositionLabel[t.disposition]}
                    </Badge>
                    {t.severity !== null && (
                      <Badge color={severityColor[t.severity]}>⚠ {severityLabel[t.severity]}</Badge>
                    )}
                    <Badge color="#555">{evidenceLabel[t.evidence]}</Badge>
                    {t.reporterCount > 1 && <Badge color="#608">👥 {t.reporterCount} reporters</Badge>}
                    {t.locationResolvedAt && <Badge color="#286">📍 located</Badge>}
                    {t.language && <Badge color="#777">{t.language}</Badge>}
                    {t.flags?.split(',').map((f) => (
                      <Badge key={f} color="#a40">
                        {f}
                      </Badge>
                    ))}
                  </div>
                  <div>{t.summary}</div>
                  {t.casualtyEstimate && (
                    <div style={{ color: '#900', fontSize: '0.85rem' }}>
                      casualties: {t.casualtyEstimate}
                    </div>
                  )}
                  <small>
                    {ticketStatusLabel[t.status]} · {new Date(t.updatedAt).toLocaleTimeString()}
                  </small>
                </button>
              </li>
            ))}
          </ul>
        </section>

        <section style={{ flex: 1 }}>
          {selectedId === null && (
            <>
              <h2>Timeline</h2>
              <p>Select an incident.</p>
            </>
          )}
          {selectedId !== null && (
            <>
              <DetailPanel ticket={tickets.data?.find((t) => t.id === selectedId)} />
              <h2>Timeline</h2>
              {timeline.data?.map((entry) => <TimelineRow key={entry.id} entry={entry} />)}
            </>
          )}
        </section>
      </div>
    </div>
  );
}

const dispatchLabels = ['—', '🚑 Dispatched', '📍 Arrived', '🏥 Transported'];

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

  if (!ticket) return null;
  const closed = ticket.status === 4 || ticket.status === 3;

  return (
    <div style={{ border: '1px solid #ccc', borderRadius: 8, padding: '0.75rem', marginBottom: '1rem', background: '#fcfcfc' }}>
      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
        <strong>{dispatchLabels[ticket.dispatch]}</strong>
        {/* Loop-closure: each click sends the status to every reporter (R1e) */}
        <button disabled={ticket.dispatch !== 0 || closed || action.isPending} onClick={() => action.mutate('dispatch')}>
          🚑 Dispatch
        </button>
        <button disabled={ticket.dispatch !== 1 || action.isPending} onClick={() => action.mutate('arrive')}>
          📍 Arrived
        </button>
        <button disabled={ticket.dispatch !== 2 || action.isPending} onClick={() => action.mutate('transport')}>
          🏥 Transported
        </button>
        <span style={{ marginLeft: 'auto', fontSize: '0.8rem', color: '#666' }}>
          outcome:
          {ticket.outcome !== null ? (
            <strong> {['real', 'FALSE', 'unverifiable'][ticket.outcome]}</strong>
          ) : (
            <>
              <button onClick={() => outcome.mutate(0)} disabled={outcome.isPending}> real</button>
              <button onClick={() => outcome.mutate(1)} disabled={outcome.isPending}> false</button>
              <button onClick={() => outcome.mutate(2)} disabled={outcome.isPending}> unverifiable</button>
            </>
          )}
        </span>
      </div>

      {ticket.timelineDigest && (
        <div style={{ marginTop: '0.5rem', fontSize: '0.9rem', whiteSpace: 'pre-wrap' }}>
          <strong>Digest:</strong> {ticket.timelineDigest}
        </div>
      )}
      {ticket.contradictions && (
        <div style={{ marginTop: '0.4rem', padding: '0.4rem', background: '#fff3e0', border: '1px solid #e90', borderRadius: 6, fontSize: '0.9rem' }}>
          <strong>⚠ Contradictions:</strong> {ticket.contradictions}
        </div>
      )}
      {ticket.crewBriefing && (
        <details style={{ marginTop: '0.4rem' }}>
          <summary style={{ cursor: 'pointer' }}>🚑 Crew briefing</summary>
          <pre style={{ whiteSpace: 'pre-wrap', fontSize: '0.85rem', background: '#f4f4f4', padding: '0.5rem', borderRadius: 6 }}>
            {ticket.crewBriefing}
          </pre>
        </details>
      )}
    </div>
  );
}

function Badge({ color, children }: { color: string; children: React.ReactNode }) {
  return (
    <span
      style={{
        background: color,
        color: 'white',
        borderRadius: 4,
        padding: '0.05rem 0.4rem',
        fontSize: '0.72rem',
      }}
    >
      {children}
    </span>
  );
}

function TimelineRow({ entry }: { entry: TimelineEntryDto }) {
  const directionLabel = ['⬅ reporter', '➡ system reply', '⚙ system'][entry.direction];
  return (
    <div
      style={{
        padding: '0.5rem',
        marginBottom: '0.4rem',
        borderLeft:
          entry.direction === 0
            ? '3px solid #06c'
            : entry.direction === 1
              ? '3px solid #0a6'
              : '3px solid #bbb',
        background: entry.direction === 2 ? '#f4f4f4' : 'white',
        fontStyle: entry.direction === 2 ? 'italic' : 'normal', // AI/system visually distinct (D-013)
      }}
    >
      <small>
        {directionLabel} · {new Date(entry.occurredAt).toLocaleTimeString()} · reporter{' '}
        {entry.conversationId.slice(0, 8)}
      </small>
      <EntryBody entry={entry} />
    </div>
  );
}

function EntryBody({ entry }: { entry: TimelineEntryDto }) {
  switch (entry.kind) {
    case 1: // Image — blurred version only, by construction (D-009)
      return entry.mediaRef ? (
        <img
          src={mediaUrl(entry.mediaRef)}
          alt="scene"
          style={{ maxWidth: '20rem', maxHeight: '15rem', display: 'block', marginTop: 4 }}
        />
      ) : (
        <em>[photo]</em>
      );
    case 2: // Voice — playable audio BESIDE transcript (D-013: audio is ground truth)
      return (
        <div style={{ marginTop: 4 }}>
          {entry.mediaRef ? <audio controls src={mediaUrl(entry.mediaRef)} /> : <em>[voice note]</em>}
          <div style={{ fontStyle: 'italic', color: '#555', fontSize: '0.9rem' }}>
            {entry.transcriptText
              ? `“${entry.transcriptText}”`
              : 'transcript unavailable — listen to audio'}
          </div>
        </div>
      );
    case 3:
      return (
        <div>
          📍 <code>{entry.text}</code>
        </div>
      );
    default:
      return <div>{entry.text}</div>;
  }
}
