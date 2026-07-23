import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import {
  fetchConversation,
  fetchScenarios,
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
  useConsoleHub();
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
    <div className="grid grid-cols-[minmax(300px,380px)_1fr] items-start gap-8 p-6">
      <div>
        {/* A stale tab against a stopped stack must never LOOK alive — messages sent
            here would go nowhere. Loud banner beats a small "Send failed" line. */}
        {conversation.isError && (
          <div className="mb-4 rounded-lg border border-sev/60 bg-sev-tint px-3 py-2.5 text-[0.85rem] font-semibold text-sev">
            ⛔ Backend unreachable — nothing sent from this page is being delivered.
            Start the stack with <code className="font-mono">dotnet run --project src/First10.AppHost</code>{' '}
            and reload.
          </div>
        )}
        <h2 className="font-display text-base font-extrabold uppercase tracking-wide">
          Local chat <span className="text-ink-faint">(dev)</span>
        </h2>

        <div className="mt-3 flex items-center gap-2 text-sm">
          <label className="text-ink-soft">Reporting as</label>
          <select
            value={sender}
            onChange={(e) => setSender(e.target.value)}
            className="cursor-pointer border border-hairline-strong bg-paper-raised px-2 py-1"
          >
            {personas.map((p) => (
              <option key={p}>{p}</option>
            ))}
          </select>
          <button
            className="ghost-btn"
            onClick={() => {
              const name = `persona-${personas.length + 1}`;
              setPersonas([...personas, name]);
              setSender(name);
            }}
          >
            + persona
          </button>
        </div>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (text.trim()) send.mutate({ senderId: sender, text: text.trim(), kind: 0 });
          }}
          className="mt-3 flex gap-2"
        >
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="Accident dey happen for Mowe o!"
            className="flex-1 border border-hairline-strong bg-paper-raised px-3 py-2 focus:outline-2 focus:-outline-offset-1 focus:outline-act"
          />
          <button type="submit" disabled={send.isPending} className="action-btn action-btn-primary">
            Send
          </button>
        </form>

        <div className="mt-3 flex flex-wrap gap-1">
          <button className="ghost-btn" onClick={() => fileInputRef.current?.click()}>
            📷 photo / 🎬 video
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp,video/mp4,video/quicktime,video/webm"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void sendImage(f, f.name);
              e.target.value = '';
            }}
          />
          {[1, 2, 3].map((n) => (
            <button key={n} className="ghost-btn" onClick={() => void sendStockPhoto(n)}>
              🖼 stock #{n}
            </button>
          ))}
          <button className="ghost-btn" onClick={() => void toggleRecording()}>
            {recording ? '⏹ stop & send' : '🎤 record voice'}
          </button>
        </div>

        <div className="mt-2 flex flex-wrap gap-1">
          {Object.entries(locationPresets).map(([name, [lat, lng]]) => (
            <button
              key={name}
              className="ghost-btn"
              onClick={() =>
                send.mutate({ senderId: sender, kind: 3, latitude: lat, longitude: lng })
              }
            >
              📍 {name}
            </button>
          ))}
        </div>

        <h3 className="microlabel mt-6 mb-2">Scenarios</h3>
        <div className="flex flex-wrap gap-1">
          {scenarios.data?.map((name) => (
            <button
              key={name}
              className="ghost-btn"
              onClick={() => scenario.mutate(name)}
              disabled={scenario.isPending}
            >
              ▶ {name}
            </button>
          ))}
        </div>

        {send.error && (
          <p className="mt-2 text-sm font-semibold text-sev">Send failed: {String(send.error)}</p>
        )}
        {scenario.error && (
          <p className="mt-2 text-sm font-semibold text-sev">
            Scenario failed: {String(scenario.error)}
          </p>
        )}
      </div>

      <div className="max-w-2xl">
        <h3 className="microlabel mb-2">Conversation — {sender}</h3>
        <div className="min-h-88 border border-hairline-strong bg-paper-sunken p-3">
          {conversation.data?.length === 0 && (
            <p className="text-sm text-ink-faint">
              No messages yet. Whatever you send goes through the real triage pipeline.
            </p>
          )}
          {conversation.data?.map((entry) => (
            <div
              key={entry.id}
              className={`mb-2 flex ${entry.direction === 0 ? 'justify-end' : 'justify-start'}`}
            >
              <div
                className={`max-w-[75%] border px-3 py-2 text-[0.9rem] ${
                  entry.direction === 0
                    ? 'border-ok bg-ok-tint'
                    : 'border-hairline bg-paper-raised'
                }`}
              >
                {entry.kind === 1 && entry.mediaUrl ? (
                  <img src={entry.mediaUrl} alt="sent" className="max-w-full" />
                ) : entry.kind === 2 && entry.mediaUrl ? (
                  <audio controls src={entry.mediaUrl} className="max-w-full" />
                ) : entry.kind === 3 ? (
                  <span className="font-mono text-[0.85rem]">📍 {entry.text}</span>
                ) : (
                  <span>{entry.text}</span>
                )}
                <div className="mt-0.5 font-mono text-[0.65rem] text-ink-faint">
                  {new Date(entry.occurredAt).toLocaleTimeString()}
                </div>
              </div>
            </div>
          ))}
        </div>
        <p className="mt-2 text-[0.8rem] text-ink-faint">
          Replies here are what a real bystander would receive on WhatsApp — challenges, acks,
          safety micro-instructions, and dispatch updates.
        </p>
      </div>
    </div>
  );
}
