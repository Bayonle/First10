import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
  fetchConversation,
  fetchScenarios,
  mediaUrl,
  runScenario,
  sendLocalChatMessage,
  uploadMedia,
} from '../api';
import { useConsoleHub } from '../useConsoleHub';

const defaultPersonas = ['persona-1', 'persona-2', 'persona-3'];

// Corridor presets stand in for a map picker (M1); Abuja exercises the geofence flag.
const locationPresets: Record<string, [number, number]> = {
  'Berger interchange': [6.6435, 3.3655],
  'Kara bridge': [6.665, 3.383],
  Ibafo: [6.743, 3.43],
  'Mowe toll area': [6.806, 3.437],
  'Abuja (off corridor)': [9.0765, 7.3986],
};

/**
 * Dev cockpit (D-006). Everything sent here traverses the real pipeline.
 * Open /console in a second tab to watch triage happen.
 */
export default function LocalChatPage() {
  useConsoleHub(); // ticketChanged also implies our conversation may have new outbound entries
  const queryClient = useQueryClient();
  const [personas, setPersonas] = useState(defaultPersonas);
  const [sender, setSender] = useState(defaultPersonas[0]);
  const [text, setText] = useState('');
  const [recording, setRecording] = useState(false);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const conversation = useQuery({
    queryKey: ['conversation', sender],
    queryFn: () => fetchConversation(sender),
    refetchInterval: 3000, // cheap dev fallback alongside hub invalidation
  });

  useEffect(() => {
    const t = setInterval(
      () => queryClient.invalidateQueries({ queryKey: ['conversation', sender] }),
      3000,
    );
    return () => clearInterval(t);
  }, [queryClient, sender]);

  const scenarios = useQuery({ queryKey: ['scenarios'], queryFn: fetchScenarios });

  const send = useMutation({
    mutationFn: sendLocalChatMessage,
    onSuccess: () => {
      setText('');
      setTimeout(
        () => queryClient.invalidateQueries({ queryKey: ['conversation', sender] }),
        600,
      );
    },
  });

  const scenario = useMutation({ mutationFn: runScenario });

  async function sendImage(file: Blob, name: string) {
    const ref = await uploadMedia(file, name);
    send.mutate({ senderId: sender, kind: 1, mediaRef: ref });
  }

  /** Deterministic-ish "stock crash photo": same n → same pixels → near-identical pHash. */
  async function sendStockPhoto(n: number) {
    const canvas = document.createElement('canvas');
    canvas.width = 320;
    canvas.height = 240;
    const ctx = canvas.getContext('2d')!;
    const hues = [15, 200, 120];
    ctx.fillStyle = `hsl(${hues[n % 3]}, 60%, 40%)`;
    ctx.fillRect(0, 0, 320, 240);
    ctx.fillStyle = '#222';
    ctx.fillRect(20 + n * 30, 120, 140, 70); // "vehicle"
    ctx.fillStyle = 'white';
    ctx.font = '20px sans-serif';
    ctx.fillText(`stock crash photo #${n}`, 40, 60);
    const blob = await new Promise<Blob>((resolve) =>
      canvas.toBlob((b) => resolve(b!), 'image/jpeg', 0.9),
    );
    await sendImage(blob, `stock-${n}.jpg`);
  }

  async function toggleRecording() {
    if (recording) {
      recorderRef.current?.stop();
      setRecording(false);
      return;
    }
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    const recorder = new MediaRecorder(stream, { mimeType: 'audio/webm' });
    const chunks: Blob[] = [];
    recorder.ondataavailable = (e) => chunks.push(e.data);
    recorder.onstop = async () => {
      stream.getTracks().forEach((t) => t.stop());
      const blob = new Blob(chunks, { type: 'audio/webm' });
      const ref = await uploadMedia(blob, 'voice-note.webm');
      send.mutate({ senderId: sender, kind: 2, mediaRef: ref });
    };
    recorder.start();
    recorderRef.current = recorder;
    setRecording(true);
  }

  return (
    <div style={{ display: 'flex', gap: '1.5rem', alignItems: 'flex-start' }}>
      <div style={{ flex: '0 0 24rem' }}>
        <h2>Local chat (dev)</h2>

        <label>
          Reporting as{' '}
          <select value={sender} onChange={(e) => setSender(e.target.value)}>
            {personas.map((p) => (
              <option key={p}>{p}</option>
            ))}
          </select>
        </label>{' '}
        <button
          onClick={() => {
            const name = `persona-${personas.length + 1}`;
            setPersonas([...personas, name]);
            setSender(name);
          }}
        >
          + persona
        </button>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (text.trim()) send.mutate({ senderId: sender, text: text.trim(), kind: 0 });
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

        <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap', marginTop: '0.6rem' }}>
          <button onClick={() => fileInputRef.current?.click()}>📷 upload photo</button>
          <input
            ref={fileInputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void sendImage(f, f.name);
              e.target.value = '';
            }}
          />
          {[1, 2, 3].map((n) => (
            <button key={n} onClick={() => void sendStockPhoto(n)}>
              🖼 stock #{n}
            </button>
          ))}
          <button onClick={() => void toggleRecording()}>
            {recording ? '⏹ stop & send' : '🎤 record voice'}
          </button>
        </div>

        <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap', marginTop: '0.6rem' }}>
          {Object.entries(locationPresets).map(([name, [lat, lng]]) => (
            <button
              key={name}
              onClick={() => send.mutate({ senderId: sender, kind: 3, latitude: lat, longitude: lng })}
            >
              📍 {name}
            </button>
          ))}
        </div>

        <h3 style={{ marginTop: '1.2rem' }}>Scenarios</h3>
        <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap' }}>
          {scenarios.data?.map((name) => (
            <button key={name} onClick={() => scenario.mutate(name)} disabled={scenario.isPending}>
              ▶ {name}
            </button>
          ))}
        </div>
        {send.error && <p style={{ color: 'crimson' }}>Send failed: {String(send.error)}</p>}
        {scenario.error && <p style={{ color: 'crimson' }}>Scenario failed: {String(scenario.error)}</p>}
      </div>

      <div style={{ flex: 1, maxWidth: '34rem' }}>
        <h3>Conversation — {sender}</h3>
        <div
          style={{
            border: '1px solid #ddd',
            borderRadius: 8,
            padding: '0.75rem',
            minHeight: '20rem',
            background: '#fafafa',
          }}
        >
          {conversation.data?.length === 0 && <p style={{ color: '#888' }}>No messages yet.</p>}
          {conversation.data?.map((entry) => (
            <div
              key={entry.id}
              style={{
                display: 'flex',
                justifyContent: entry.direction === 0 ? 'flex-end' : 'flex-start',
                marginBottom: '0.4rem',
              }}
            >
              <div
                style={{
                  maxWidth: '75%',
                  padding: '0.45rem 0.6rem',
                  borderRadius: 10,
                  background: entry.direction === 0 ? '#d3f2d9' : 'white',
                  border: '1px solid #ddd',
                }}
              >
                {entry.kind === 1 && entry.mediaRef ? (
                  <img src={mediaUrl(entry.mediaRef)} alt="sent" style={{ maxWidth: '100%' }} />
                ) : entry.kind === 2 && entry.mediaRef ? (
                  <audio controls src={mediaUrl(entry.mediaRef)} style={{ maxWidth: '100%' }} />
                ) : entry.kind === 3 ? (
                  <span>📍 {entry.text}</span>
                ) : (
                  <span>{entry.text}</span>
                )}
                <div style={{ fontSize: '0.7rem', color: '#888', marginTop: 2 }}>
                  {new Date(entry.occurredAt).toLocaleTimeString()}
                </div>
              </div>
            </div>
          ))}
        </div>
        <p style={{ fontSize: '0.8rem', color: '#777' }}>
          Replies here are what a real bystander would receive on WhatsApp — challenges, canned
          replies, and (from M2) safety micro-instructions.
        </p>
      </div>
    </div>
  );
}
