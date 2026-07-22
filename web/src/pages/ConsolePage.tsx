import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import {
  fetchTickets,
  fetchTimeline,
  ticketStatusLabel,
  type TimelineEntryDto,
} from '../api';
import { useConsoleHub } from '../useConsoleHub';

export default function ConsolePage() {
  useConsoleHub();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const tickets = useQuery({ queryKey: ['tickets'], queryFn: fetchTickets });
  const timeline = useQuery({
    queryKey: ['timeline', selectedId],
    queryFn: () => fetchTimeline(selectedId!),
    enabled: selectedId !== null,
  });

  return (
    <div style={{ display: 'flex', gap: '1rem', alignItems: 'flex-start' }}>
      <section style={{ flex: '0 0 24rem' }}>
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
                <strong>{ticketStatusLabel[t.status]}</strong>
                <div>{t.summary}</div>
                <small>{new Date(t.updatedAt).toLocaleTimeString()}</small>
              </button>
            </li>
          ))}
        </ul>
      </section>

      <section style={{ flex: 1 }}>
        <h2>Timeline</h2>
        {selectedId === null && <p>Select an incident.</p>}
        {timeline.data?.map((entry) => <TimelineRow key={entry.id} entry={entry} />)}
      </section>
    </div>
  );
}

function TimelineRow({ entry }: { entry: TimelineEntryDto }) {
  const directionLabel = ['⬅ reporter', '➡ system reply', '⚙ system'][entry.direction];
  return (
    <div
      style={{
        padding: '0.5rem',
        marginBottom: '0.4rem',
        borderLeft: entry.direction === 0 ? '3px solid #06c' : '3px solid #999',
        background: entry.direction === 2 ? '#f4f4f4' : 'white',
      }}
    >
      <small>
        {directionLabel} · {new Date(entry.occurredAt).toLocaleTimeString()} · reporter{' '}
        {entry.conversationId.slice(0, 8)}
      </small>
      <div>{entry.text ?? <em>[{['text', 'photo', 'voice note', 'location pin', 'status'][entry.kind]}]</em>}</div>
    </div>
  );
}
