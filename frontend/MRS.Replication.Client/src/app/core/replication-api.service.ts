import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { NodeInfo, QueryResult, ReplicationMode, SetupRequest } from './models';

export const BACKEND_BASE_URL = 'http://localhost:5080';

@Injectable({ providedIn: 'root' })
export class ReplicationApiService {
  private readonly http = inject(HttpClient);

  setup(request: SetupRequest) {
    return this.http.post(`${BACKEND_BASE_URL}/api/setup`, request);
  }

  setReplicaCount(count: number) {
    return this.http.post(`${BACKEND_BASE_URL}/api/replicas`, { count });
  }

  removeReplica(id: string) {
    return this.http.delete(`${BACKEND_BASE_URL}/api/replicas/${id}`);
  }

  changeMode(mode: ReplicationMode, syncTimeoutMs?: number) {
    return this.http.put(`${BACKEND_BASE_URL}/api/config/mode`, { mode, syncTimeoutMs });
  }

  getNodes() {
    return this.http.get<NodeInfo[]>(`${BACKEND_BASE_URL}/api/nodes`);
  }

  query(sql: string) {
    return this.http.post<QueryResult>(`${BACKEND_BASE_URL}/api/query`, { sql });
  }
}
