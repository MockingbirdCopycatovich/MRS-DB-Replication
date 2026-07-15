import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ReplicationApiService } from '../../core/replication-api.service';
import { WatchdogService } from '../../core/watchdog.service';
import { DEFAULT_REPLICATION_CONFIG, NodeInfo, QueryResult, ReplicationConfig } from '../../core/models';

@Component({
  selector: 'app-dashboard',
  imports: [FormsModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css'
})
export class Dashboard implements OnInit, OnDestroy {
  private readonly api = inject(ReplicationApiService);
  private readonly watchdog = inject(WatchdogService);

  readonly nodes = signal<NodeInfo[]>([]);
  readonly config = signal<ReplicationConfig>(DEFAULT_REPLICATION_CONFIG);
  readonly result = signal<QueryResult | null>(null);
  readonly running = signal(false);
  readonly connectionLost = signal(false);

  sql = 'SELECT * FROM datasets ORDER BY id;';

  private unsubscribeEvents?: () => void;
  private pollHandle?: ReturnType<typeof setInterval>;

  readonly activeCount = computed(() => this.nodes().filter((n) => n.status === 'Active').length);
  readonly inactiveCount = computed(
    () => this.nodes().filter((n) => n.status === 'Inactive' || n.status === 'Failed').length
  );

  readonly alertMessage = computed(() => {
    const cfg = this.config();
    if (this.activeCount() < cfg.minActiveNodes) {
      return `Only ${this.activeCount()} active node(s) — below the configured minimum of ${cfg.minActiveNodes}`;
    }
    if (this.inactiveCount() > cfg.maxInactiveNodes) {
      return `${this.inactiveCount()} inactive/failed node(s) — above the configured maximum of ${cfg.maxInactiveNodes}`;
    }
    return null;
  });

  ngOnInit(): void {
    this.refreshNodes();
    this.watchdog.getConfig().subscribe((cfg) => this.config.set(cfg));

    this.unsubscribeEvents = this.watchdog.connectEvents(
      () => this.refreshNodes(),
      () => this.connectionLost.set(true)
    );

    // Safety-net poll in case an SSE event is missed — the sidebar stays eventually consistent either way.
    this.pollHandle = setInterval(() => this.refreshNodes(), 5000);
  }

  ngOnDestroy(): void {
    this.unsubscribeEvents?.();
    if (this.pollHandle) {
      clearInterval(this.pollHandle);
    }
  }

  private refreshNodes(): void {
    this.api.getNodes().subscribe({
      next: (nodes) => {
        this.nodes.set(nodes);
        this.connectionLost.set(false);
      },
      error: () => this.connectionLost.set(true)
    });
  }

  runQuery(): void {
    const sql = this.sql.trim();
    if (!sql || this.running()) {
      return;
    }
    this.running.set(true);
    this.api.query(sql).subscribe({
      next: (res) => {
        this.result.set(res);
        this.running.set(false);
      },
      error: (err) => {
        this.result.set({
          success: false,
          error: err?.error?.error ?? err?.message ?? 'Query failed',
          columns: [],
          rows: [],
          rowsAffected: 0,
          targetNodeId: '',
          targetNodeName: '',
          elapsedMs: 0
        });
        this.running.set(false);
      }
    });
  }

  statusClass(node: NodeInfo): string {
    return `status status-${node.status.toLowerCase()}`;
  }
}
