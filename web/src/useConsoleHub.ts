import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

/**
 * Server push -> TanStack Query invalidation (D-004). The hub never carries data,
 * only "something changed" signals; Query refetches through the normal read API.
 */
export function useConsoleHub() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/console')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('ticketChanged', (ticketId: string) => {
      void queryClient.invalidateQueries({ queryKey: ['tickets'] });
      void queryClient.invalidateQueries({ queryKey: ['kpis'] });
      void queryClient.invalidateQueries({ queryKey: ['timeline', ticketId] });
    });

    connection.start().catch((err) => console.error('SignalR connect failed', err));
    return () => {
      void connection.stop();
    };
  }, [queryClient]);
}
