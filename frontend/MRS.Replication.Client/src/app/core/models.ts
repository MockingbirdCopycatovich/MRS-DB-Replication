export type NodeRole = 'Primary' | 'Replica';

export type NodeStatus = 'Provisioning' | 'Active' | 'Delayed' | 'Resyncing' | 'Inactive' | 'Failed';

export type ReplicationMode = 'Sync' | 'Async';

export type NodeEventType =
  | 'Registered'
  | 'StatusChanged'
  | 'FailoverStarted'
  | 'FailoverCompleted'
  | 'ResyncStarted'
  | 'ResyncCompleted'
  | 'Removed'
  | 'Alert';

export interface NodeInfo {
  id: string;
  name: string;
  role: NodeRole;
  status: NodeStatus;
  host: string;
  port: number;
  lagBytes: number;
  lagMs: number;
  consecutiveFailures: number;
  queueDepth: number;
  priority: number;
  registeredAt: string;
  lastCheckedAt: string;
}

export interface NodeEvent {
  nodeId: string;
  type: NodeEventType;
  timestampUtc: string;
  message: string;
  node?: NodeInfo;
}

export interface ReplicationConfig {
  mode: ReplicationMode;
  syncTimeoutMs: number;
  healthCheckIntervalMs: number;
  failuresBeforeInactive: number;
  delayedLagBytesThreshold: number;
  resyncCaughtUpLagBytesThreshold: number;
  resyncTimeoutMs: number;
  minActiveNodes: number;
  maxInactiveNodes: number;
}

export interface QueryResult {
  success: boolean;
  error?: string;
  columns: string[];
  rows: Record<string, unknown>[];
  rowsAffected: number;
  targetNodeId: string;
  targetNodeName: string;
  elapsedMs: number;
}

export interface SetupRequest {
  postgresUser: string;
  postgresPassword: string;
  postgresDb: string;
  replicaCount: number;
  config: ReplicationConfig;
}

export const DEFAULT_REPLICATION_CONFIG: ReplicationConfig = {
  mode: 'Async',
  syncTimeoutMs: 5000,
  healthCheckIntervalMs: 3000,
  failuresBeforeInactive: 3,
  delayedLagBytesThreshold: 8 * 1024 * 1024,
  resyncCaughtUpLagBytesThreshold: 64 * 1024,
  resyncTimeoutMs: 120_000,
  minActiveNodes: 1,
  maxInactiveNodes: 0
};
