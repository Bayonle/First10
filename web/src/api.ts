export type TicketStatus = 0 | 1 | 2 | 3 | 4; // Provisional | Promoted | ExpiredUnverified | Rejected | Closed
export type Disposition = 0 | 1 | 2 | 3 | 4 | 5; // None | Drop | Challenge | Review | FastTrack | AutoVerify
export type EvidenceLevel = 0 | 1 | 2 | 3 | 4; // None | TextOnly | VoiceOnly | Photo | PhotoPlus

export const ticketStatusLabel: Record<TicketStatus, string> = {
  0: 'Provisional',
  1: 'Promoted',
  2: 'Expired (unverified)',
  3: 'Rejected',
  4: 'Closed',
};

export const dispositionLabel: Record<Disposition, string> = {
  0: '—',
  1: 'Dropped',
  2: 'Challenge sent',
  3: 'Review',
  4: 'Fast track',
  5: 'Auto-verified',
};

export const dispositionColor: Record<Disposition, string> = {
  0: '#999',
  1: '#999',
  2: '#c80', // awaiting evidence
  3: '#06c', // needs human review
  4: '#0a6', // strong evidence
  5: '#080',
};

export const evidenceLabel: Record<EvidenceLevel, string> = {
  0: 'no evidence',
  1: 'text only',
  2: 'voice only',
  3: 'photo',
  4: 'photo+',
};

export type SeverityTier = 0 | 1 | 2; // Low | Medium | High

export const severityLabel: Record<SeverityTier, string> = {
  0: 'low',
  1: 'medium',
  2: 'HIGH',
};

export const severityColor: Record<SeverityTier, string> = {
  0: '#690',
  1: '#c80',
  2: '#c00',
};

export type DispatchState = 0 | 1 | 2 | 3; // None | Dispatched | Arrived | Transported
export type TicketOutcome = 0 | 1 | 2; // Real | False | Unverifiable

export interface TicketListItem {
  id: string;
  status: TicketStatus;
  disposition: Disposition;
  evidence: EvidenceLevel;
  severity: SeverityTier | null;
  casualtyEstimate: string | null;
  reporterCount: number;
  language: string | null;
  flags: string | null;
  summary: string;
  locationResolvedAt: string | null;
  dispatch: DispatchState;
  outcome: TicketOutcome | null;
  timelineDigest: string | null;
  contradictions: string | null;
  crewBriefing: string | null;
  pendingAsk: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface StatsDto {
  reportsTotal: number;
  verifiedReports: number;
  multiReporterIncidents: number;
  openQueueDepth: number;
  dispatchedCount: number;
  medianTimeToDispatchMinutes: number | null;
  medianInstructionLatencySeconds: number | null;
  instructionCoverageRate: number;
  loopClosureRate: number;
  falsePositiveRate: number | null;
  outcomesMarked: number;
}

export const fetchStats = () => fetch('/api/stats').then((r) => json<StatsDto>(r));

export const postAction = (ticketId: string, action: 'dispatch' | 'arrive' | 'transport') =>
  fetch(`/api/tickets/${ticketId}/actions/${action}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ officer: 'duty-officer' }),
  }).then((r) => {
    if (!r.ok) throw new Error(`${r.status}`);
  });

export const postOutcome = (ticketId: string, outcome: TicketOutcome) =>
  fetch(`/api/tickets/${ticketId}/actions/outcome`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ outcome, officer: 'duty-officer' }),
  }).then((r) => {
    if (!r.ok) throw new Error(`${r.status}`);
  });

export interface TimelineEntryDto {
  id: string;
  conversationId: string;
  direction: 0 | 1 | 2; // Inbound | Outbound | System
  kind: 0 | 1 | 2 | 3 | 4; // Text | Image | Voice | LocationPin | StatusChange
  text: string | null;
  mediaRef: string | null;
  /** Short-lived signed URL (~5 min) minted per fetch — the only way media is served. */
  mediaUrl: string | null;
  transcriptText: string | null;
  occurredAt: string;
}

export interface ConversationEntryDto extends TimelineEntryDto {
  ticketId: string | null;
}

export interface FloodState {
  active: boolean;
  ticketsInWindow: number;
  threshold: number;
  windowMinutes: number;
}

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json() as Promise<T>;
}

export const fetchTickets = () =>
  fetch('/api/tickets').then((r) => json<TicketListItem[]>(r));

export const fetchTimeline = (ticketId: string) =>
  fetch(`/api/tickets/${ticketId}/timeline`).then((r) => json<TimelineEntryDto[]>(r));

export const fetchFloodState = () =>
  fetch('/api/system/flood').then((r) => json<FloodState>(r));

export const fetchDeadLetters = () =>
  fetch('/api/system/dead-letters').then((r) => json<{ count: number | null }>(r));

export const fetchConversation = (senderId: string) =>
  fetch(`/api/local-chat/${encodeURIComponent(senderId)}/timeline`).then((r) =>
    json<ConversationEntryDto[]>(r),
  );

export const fetchScenarios = () =>
  fetch('/api/local-chat/scenarios').then((r) => json<string[]>(r));

export const runScenario = (name: string) =>
  fetch(`/api/local-chat/scenarios/${encodeURIComponent(name)}`, { method: 'POST' }).then((r) => {
    if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  });

export interface LocalChatMessage {
  senderId: string;
  text?: string;
  mediaRef?: string;
  latitude?: number;
  longitude?: number;
  kind?: number; // 0 text, 1 image, 2 voice, 3 pin
}

export const sendLocalChatMessage = (message: LocalChatMessage) =>
  fetch('/api/local-chat/messages', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(message),
  }).then((r) => {
    if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  });

export const uploadMedia = async (file: Blob, fileName: string): Promise<string> => {
  const form = new FormData();
  form.append('file', file, fileName);
  const r = await fetch('/api/local-chat/media', { method: 'POST', body: form });
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  const body = (await r.json()) as { mediaRef: string };
  return body.mediaRef;
};
