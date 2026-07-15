import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ReplicationApiService } from '../../core/replication-api.service';
import { ReplicationMode, SetupRequest } from '../../core/models';

@Component({
  selector: 'app-setup-wizard',
  imports: [FormsModule],
  templateUrl: './setup-wizard.html',
  styleUrl: './setup-wizard.css'
})
export class SetupWizard {
  private readonly api = inject(ReplicationApiService);
  private readonly router = inject(Router);

  postgresUser = 'mrs_user';
  postgresPassword = 'mrs_password';
  postgresDb = 'mrs_db';
  replicaCount = 2;

  mode: ReplicationMode = 'Async';
  syncTimeoutMs = 5000;
  healthCheckIntervalMs = 3000;
  failuresBeforeInactive = 3;
  minActiveNodes = 1;
  maxInactiveNodes = 0;

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    this.error.set(null);
    this.submitting.set(true);

    const request: SetupRequest = {
      postgresUser: this.postgresUser,
      postgresPassword: this.postgresPassword,
      postgresDb: this.postgresDb,
      replicaCount: this.replicaCount,
      config: {
        mode: this.mode,
        syncTimeoutMs: this.syncTimeoutMs,
        healthCheckIntervalMs: this.healthCheckIntervalMs,
        failuresBeforeInactive: this.failuresBeforeInactive,
        delayedLagBytesThreshold: 8 * 1024 * 1024,
        resyncCaughtUpLagBytesThreshold: 64 * 1024,
        resyncTimeoutMs: 120_000,
        minActiveNodes: this.minActiveNodes,
        maxInactiveNodes: this.maxInactiveNodes
      }
    };

    this.api.setup(request).subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigateByUrl('/dashboard');
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(err?.error?.error ?? err?.message ?? 'Setup failed');
      }
    });
  }
}
