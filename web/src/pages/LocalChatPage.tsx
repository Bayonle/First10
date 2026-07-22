import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { sendLocalChatMessage } from '../api';

const personas = ['persona-1', 'persona-2', 'persona-3'];

/**
 * Dev cockpit (D-006): messages sent here enter the exact same pipeline as WhatsApp.
 * Dev-only — the route is gated client-side and the API endpoint returns 404 outside
 * Development. M1 adds media upload, voice recording, map pins, and the scenario runner.
 */
export default function LocalChatPage() {
  const [sender, setSender] = useState(personas[0]);
  const [text, setText] = useState('');
  const [sent, setSent] = useState<{ sender: string; text: string; at: string }[]>([]);

  const send = useMutation({
    mutationFn: sendLocalChatMessage,
    onSuccess: (_, message) => {
      setSent((prev) => [
        { sender: message.senderId, text: message.text ?? '', at: new Date().toLocaleTimeString() },
        ...prev,
      ]);
      setText('');
    },
  });

  return (
    <div style={{ maxWidth: '40rem' }}>
      <h2>Local chat (dev)</h2>
      <p>
        Simulates a bystander on the corridor. Open <a href="/console">/console</a> in another tab
        to watch the ticket appear.
      </p>

      <label>
        Reporting as{' '}
        <select value={sender} onChange={(e) => setSender(e.target.value)}>
          {personas.map((p) => (
            <option key={p}>{p}</option>
          ))}
        </select>
      </label>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (text.trim()) send.mutate({ senderId: sender, text: text.trim() });
        }}
        style={{ display: 'flex', gap: '0.5rem', marginTop: '0.75rem' }}
      >
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Accident dey happen for Mowe o!"
          style={{ flex: 1, padding: '0.5rem' }}
        />
        <button type="submit" disabled={send.isPending}>
          Send
        </button>
      </form>
      {send.error && <p style={{ color: 'crimson' }}>Send failed: {String(send.error)}</p>}

      <h3>Sent</h3>
      <ul>
        {sent.map((m, i) => (
          <li key={i}>
            <small>{m.at}</small> <strong>{m.sender}:</strong> {m.text}
          </li>
        ))}
      </ul>
    </div>
  );
}
