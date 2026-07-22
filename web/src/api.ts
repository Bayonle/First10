export type TicketStatus = 0 | 1 | 2 | 3 | 4; // Provisional | Promoted | ExpiredUnverified | Rejected | Closed

export const ticketStatusLabel: Record<TicketStatus, string> = {
  0: 'Provisional',
  1: 'Promoted',
  2: 'Expired (unverified)',
  3: 'Rejected',
  4: 'Closed',
};

export interface TicketListItem {
  id: string;
  status: TicketStatus;
  summary: string;
  createdAt: string;
  updatedAt: string;
}

export interface TimelineEntryDto {
  id: string;
  conversationId: string;
  direction: 0 | 1 | 2; // Inbound | Outbound | System
  kind: 0 | 1 | 2 | 3 | 4; // Text | Image | Voice | LocationPin | StatusChange
  text: string | null;
  mediaRef: string | null;
  occurredAt: string;
}

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json() as Promise<T>;
}

export const fetchTickets = () =>
  fetch('/api/tickets').then((r) => json<TicketListItem[]>(r));

export const fetchTimeline = (ticketId: string) =>
  fetch(`/api/tickets/${ticketId}/timeline`).then((r) => json<TimelineEntryDto[]>(r));

export interface LocalChatMessage {
  senderId: string;
  text?: string;
  kind?: number;
}

export const sendLocalChatMessage = (message: LocalChatMessage) =>
  fetch('/api/local-chat/messages', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(message),
  }).then((r) => {
    if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  });
