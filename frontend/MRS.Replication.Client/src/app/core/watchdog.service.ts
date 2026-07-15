import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { NodeEvent, NodeInfo, ReplicationConfig } from './models';

// Watchdog is a standalone service (spec §3.3) — the browser talks to it directly,
// the same way any other future SLICES-RI resource monitor consumer would.
export const WATCHDOG_BASE_URL = 'http://localhost:5081';

@Injectable({ providedIn: 'root' })
export class WatchdogService {
  private readonly http = inject(HttpClient);

  getStatus() {
    return this.http.get<NodeInfo[]>(`${WATCHDOG_BASE_URL}/status`);
  }

  getConfig() {
    return this.http.get<ReplicationConfig>(`${WATCHDOG_BASE_URL}/config`);
  }

  /** Subscribes to the live event stream; returns an unsubscribe function. */
  connectEvents(onEvent: (evt: NodeEvent) => void, onError?: () => void): () => void {
    const source = new EventSource(`${WATCHDOG_BASE_URL}/events`);

    source.onmessage = (message: MessageEvent<string>) => {
      try {
        onEvent(JSON.parse(message.data) as NodeEvent);
      } catch {
        // ignore malformed / keep-alive comment lines
      }
    };

    source.onerror = () => onError?.();

    return () => source.close();
  }
}
