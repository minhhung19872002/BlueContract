import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { App } from 'antd';
import { useEffect, useRef } from 'react';
import { authStore } from '../auth/authStore';

export interface RealtimeMessage {
  type: string;
  title: string;
  body?: string | null;
  link?: string | null;
}

/** Links are stored for the internal portal; supplier users open the same pages under /ncc. */
export function portalLink(link: string | null | undefined, isSupplier: boolean): string | null {
  if (!link) return null;
  return isSupplier && link.startsWith('/') && !link.startsWith('/ncc') ? `/ncc${link}` : link;
}

/**
 * Connects to /hubs/notifications while signed in (spec §9.3): every push refreshes the inbox, the bell and the
 * open document, and shows a toast. Reconnects automatically; the access token is read on every (re)connect.
 */
export function useRealtime(enabled: boolean, onOpen: (link: string | null) => void) {
  const qc = useQueryClient();
  const { notification } = App.useApp();
  const openRef = useRef(onOpen);
  useEffect(() => {
    openRef.current = onOpen;
  }, [onOpen]);

  useEffect(() => {
    if (!enabled) return undefined;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', { accessTokenFactory: () => authStore.getAccessToken() ?? '' })
      .withAutomaticReconnect([0, 2_000, 10_000, 30_000, 60_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('notification', (m: RealtimeMessage) => {
      void qc.invalidateQueries();
      notification.info({
        message: m.title,
        description: m.body ?? undefined,
        placement: 'bottomRight',
        duration: 6,
        onClick: () => openRef.current(m.link ?? null),
        style: { cursor: m.link ? 'pointer' : undefined },
      });
    });
    connection.onreconnected(() => {
      void qc.invalidateQueries();
    });

    let stopped = false;
    const start = async (attempt = 0) => {
      try {
        await connection.start();
      } catch {
        // Hub unavailable (API restarting): retry with back-off; polling keeps the UI correct meanwhile.
        if (!stopped && attempt < 10)
          setTimeout(() => void start(attempt + 1), Math.min(60_000, 2_000 * 2 ** attempt));
      }
    };
    void start();

    return () => {
      stopped = true;
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
    };
  }, [enabled, qc, notification]);
}
