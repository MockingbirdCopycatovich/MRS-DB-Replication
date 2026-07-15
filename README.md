# MRS Fault-Tolerant DB Replication

Fault-tolerant PostgreSQL replication for the **Metadata Registry System (MRS)**, part of
the Data Management Infrastructure (DMI) for [SLICES Research Infrastructure](https://doc.slices-ri.eu).

Configurable sync/async replication, automatic failover, mandatory re-sync of returning
nodes, a Docker Engine API-driven Replica Manager, a standalone Watchdog monitoring
service, and an Angular frontend (Setup Wizard + live dashboard).

## Background

MRS centralizes and manages metadata for datasets, publications, software tools, and
other resources across SLICES-RI, following the FAIR principles (Findable, Accessible,
Interoperable, Reusable). It is built on PostgreSQL, connected to a backend that exposes
its functionality to the MRS Portal front-end. As a piece of shared research
infrastructure, a single point of failure in the database layer is not acceptable — this
project implements the replication, monitoring, and automatic recovery needed to keep MRS
available when a node goes down.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Angular Frontend                       │
│   (Setup Wizard  |  SQL Console + live sidebar dashboard)     │
└───────────┬─────────────────────────────────────┬───────────┘
            │ REST (backend)                       │ REST + SSE (watchdog, direct)
┌───────────▼─────────────────────┐   ┌────────────▼────────────┐
│   MRS.Replication.Api             │   │   MRS.Replication.Watchdog │
│   ┌────────────┐ ┌─────────────┐ │   │  health checks, failover,  │
│   │Query Router │ │Replica      │ │◄──┤  mandatory resync,         │
│   │/ Proxy      │ │Manager      │ │──►│  status API + SSE events   │
│   │(per-node    │ │(Docker      │ │   └────────────────────────┘
│   │ queues)     │ │ Engine API) │ │
│   └─────┬───────┘ └──────┬──────┘ │
└─────────┼────────────────┼────────┘
          │                │ creates/removes containers
          │                ▼
          │      ┌──────────────────────┐
          │      │ Docker containers      │
          │      │ mrs-postgres-primary   │
          │      │ mrs-postgres-replica-N │
          └─────►│ (streaming replication)│
                 └──────────────────────┘
```

- **MRS.Replication.Api** (`backend/MRS.Replication.Api`) hosts two modules in one process:
  the **Replica Manager** (Docker Engine API via `Docker.DotNet`, reconciliation loop) and
  the **Query Router / Proxy** (routes writes to the current primary, reads round-robin
  across `Active` nodes, per-node request queues).
- **MRS.Replication.Watchdog** (`backend/MRS.Replication.Watchdog`) is a fully separate
  service, deliberately decoupled from the API so it can be reused later to monitor other
  SLICES-RI resources beyond Postgres. It owns the node status state machine, automatic
  failover, and mandatory re-sync.
- **MRS.Replication.Shared** is a small class library with the DTOs/enums both services
  share, so the wire contract can't drift between them.
- Postgres nodes (`docker/postgres`) run a custom entrypoint on top of `postgres:16-alpine`
  that bootstraps replicas via `pg_basebackup -R` against the primary.
- Backend and Watchdog talk to each other over REST + Server-Sent Events, so neither one
  has to poll the other for state changes.

## Repository layout

```
backend/
  MRS.Replication.Shared/     # shared DTOs/enums
  MRS.Replication.Api/        # Query Router + Replica Manager
  MRS.Replication.Watchdog/   # standalone monitoring/failover service
frontend/MRS.Replication.Client/  # Angular app (Setup Wizard + Dashboard)
docker/
  postgres/                   # Postgres node image (primary/replica entrypoint, dummy schema)
  docker-compose.yml
```

## Running locally

Requires Docker (with the daemon reachable at `/var/run/docker.sock`).

```bash
# 1. Build the Postgres node image (Replica Manager creates containers from this image
#    on demand — it isn't a docker-compose service, so it needs to exist before setup).
docker build -t mrs-postgres-node:latest docker/postgres

# 2. Bring up the backend, watchdog and frontend
docker compose -f docker/docker-compose.yml up --build

# Frontend:  http://localhost:4200
# Backend:   http://localhost:5080  (Query Router + Replica Manager, Swagger at /swagger)
# Watchdog:  http://localhost:5081  (status API + SSE events, Swagger at /swagger)
```

### ⚠️ Docker socket mount

`docker-compose.yml` mounts the host's `/var/run/docker.sock` into the `backend` container
so the Replica Manager can call the Docker Engine API — this is effectively root-equivalent
access to the host's Docker daemon. It's the standard pattern for a local "control-plane
container managing sibling containers" demo, but it is **not** something to ship to a
shared/production host as-is.

## Using it: the Setup Wizard

Open the frontend and fill in the **Setup Wizard**:

- Postgres connection details (user / password / database name);
- desired number of replicas (N);
- replication mode — synchronous or asynchronous;
- alert thresholds — how many consecutive failed health checks mark a node `Inactive`,
  and how many active/inactive nodes should trigger a warning banner in the UI.

On submit, the Replica Manager provisions `mrs-postgres-primary` plus N
`mrs-postgres-replica-*` containers on the `mrs-net` Docker network itself and registers
each one with the Watchdog — no manual container or SQL setup needed.

The **Dashboard** screen that follows shows a live node sidebar (fed by the Watchdog SSE
stream, no polling) with each node's name, status, and queue depth, plus a SQL console that
runs queries through the Query Router against the demo tables (`datasets`, `publications`,
`resources`).

You can drive the same flow without the UI, e.g.:

```bash
curl -X POST http://localhost:5080/api/setup -H 'Content-Type: application/json' -d '{
  "postgresUser": "mrs_user", "postgresPassword": "mrs_password", "postgresDb": "mrs_db",
  "replicaCount": 2,
  "config": { "mode": "Async", "syncTimeoutMs": 5000, "healthCheckIntervalMs": 3000,
    "failuresBeforeInactive": 3, "delayedLagBytesThreshold": 8388608,
    "resyncCaughtUpLagBytesThreshold": 65536, "resyncTimeoutMs": 120000,
    "minActiveNodes": 1, "maxInactiveNodes": 0 }
}'

curl -X POST http://localhost:5080/api/query -H 'Content-Type: application/json' \
  -d '{"sql":"SELECT * FROM datasets;"}'

curl http://localhost:5081/status
```

## Replication strategy (sync/async)

`PUT /api/config/mode` (and the Setup Wizard) switches the primary between:

- **Async** (default): `synchronous_commit = off`. The primary confirms a write
  immediately, without waiting for any replica ("fire-and-forget"); data still streams to
  the replicas continuously, typically with a delay of a few milliseconds. Watchdog
  measures and logs this lag from `pg_stat_replication` every health-check cycle.
- **Sync**: `synchronous_commit = on` and `synchronous_standby_names = 'ANY 1 (...)'`
  listing every current replica's `application_name` (which is always its node id). A
  write only commits once **at least one** replica has acknowledged it — not all of them,
  since with N replicas that would let any single replica going down block every write.

Both are applied via `ALTER SYSTEM` + `pg_reload_conf()` — no container rebuild or restart.
The Replica Manager also reapplies the standby list on every reconciliation pass in Sync
mode, so it stays correct as replicas are added/removed.

## Query routing and per-node queues

The Query Router is the single entry point for SQL from the frontend console:

- **Writes** always go to the current primary.
- **Reads** are distributed round-robin across every `Active` node (primary and replicas
  alike), spreading load across the whole cluster instead of always hitting the same node.
- Each node has its own request queue with a concurrency limit, so a burst of parallel
  requests can't overwhelm a single node; current queue depth per node is reported in
  `GET /api/nodes` and shown live in the dashboard sidebar.

## Node status and lag monitoring

Watchdog continuously measures each replica's lag behind the primary and assigns every
node one of these statuses:

| Status | Meaning |
|---|---|
| `Provisioning` | Container created, not yet health-checked. |
| `Active` | Healthy and in rotation for reads (and writes, if primary). |
| `Delayed` | Active, but replication lag exceeds `DelayedLagBytesThreshold`. |
| `Resyncing` | Rejoining after a failure; excluded from routing until caught up. |
| `Inactive` | Failed `FailuresBeforeInactive` consecutive health checks. |
| `Failed` | Re-sync did not catch up within `ResyncTimeoutMs`; stays out of rotation. |

All of the thresholds above are part of `ReplicationConfig` and configurable from the
Setup Wizard / `PUT /config` without rebuilding containers, including the frontend alert
thresholds (`MinActiveNodes`, `MaxInactiveNodes`) that drive the warning banner.

## Failover

Watchdog polls every registered node every `HealthCheckIntervalMs`. A node goes
`Active → Inactive` after `FailuresBeforeInactive` consecutive failed probes. If that node
was the primary:

1. Watchdog picks the `Active` replica with the lowest lag (tie-break: registration order).
2. Calls `SELECT pg_promote()` on it, waits for it to leave recovery.
3. Flips its role to Primary in the registry and broadcasts `FailoverCompleted` over SSE —
   this is how the Query Router "hears about" the new primary (it keeps a live cache fed by
   the same SSE stream, no polling needed).
4. Every other surviving replica was still streaming from the now-dead primary and would
   otherwise sit "Active" while silently going stale forever — Watchdog forces them through
   the same mandatory re-sync path described below, pointed at the new primary.

If no healthy replica is available, Watchdog publishes an `Alert` event instead of
promoting anyone, and keeps retrying.

## Mandatory re-sync of returning nodes

A node that comes back reachable after being `Inactive`/`Failed` is **never** marked
`Active` directly — it always passes through `Resyncing` first:

- Replica Manager containers don't use a named volume for `PGDATA`, so "re-sync" = recreate
  the container from scratch (`docker rm` + recreate), which re-triggers `pg_basebackup -R`
  against the *current* primary on first boot. This sidesteps a footgun: Postgres runs as
  the container's PID 1, so an in-place `pg_ctl stop` via `docker exec` would kill the whole
  container instead of letting it restart cleanly.
- Watchdog polls `pg_stat_replication` on the current primary until the returning node's lag
  drops below `ResyncCaughtUpLagBytesThreshold` (or `ResyncTimeoutMs` elapses, in which case
  the node is marked `Failed` and stays out of rotation).
- On success the node is marked `Active` **with role = Replica**, even if it used to be the
  primary. It is only ever handed the Primary role again through a fresh, explicit
  failover — never automatically — which is what prevents split-brain.
- One exception: if a primary goes down and comes back **without** any failover having
  happened (e.g. no replica was ever available to promote), it never actually lost the
  Primary role, so there's nothing to reconcile against — Watchdog just asks the Replica
  Manager to restart that one container and resumes it as primary directly.

## Demo schema

Three demo tables are created on the primary's first boot and stream to every replica like
any other data, to make replication easy to see and test from the SQL console:

```sql
CREATE TABLE datasets (
    id         SERIAL PRIMARY KEY,
    title      TEXT NOT NULL,
    owner      TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE publications (
    id         SERIAL PRIMARY KEY,
    title      TEXT NOT NULL,
    dataset_id INTEGER REFERENCES datasets (id) ON DELETE CASCADE,
    doi        TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE resources (
    id         SERIAL PRIMARY KEY,
    name       TEXT NOT NULL,
    type       TEXT NOT NULL,
    status     TEXT NOT NULL DEFAULT 'active',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

## API reference

Both services expose Swagger UI at `/swagger` for interactive exploration. Main endpoints:

**MRS.Replication.Api** (`:5080`)

| Endpoint | Purpose |
|---|---|
| `POST /api/setup` | Provision the primary + N replicas and register them with Watchdog. |
| `GET /api/replicas` / `POST /api/replicas` / `DELETE /api/replicas/{id}` | Manage desired replica count. |
| `POST /api/nodes/{id}/start` | (Re)start a node container. |
| `POST /api/replicas/{id}/resync` | Force a re-sync of a specific node. |
| `PUT /api/config/mode` | Switch replication mode (sync/async) at runtime. |
| `GET /api/nodes` | Live node list with status and queue depth. |
| `POST /api/query` | Run SQL through the Query Router (writes → primary, reads → round-robin). |
| `GET /api/health` | Liveness probe. |

**MRS.Replication.Watchdog** (`:5081`)

| Endpoint | Purpose |
|---|---|
| `POST /nodes` / `DELETE /nodes/{id}` | Register/deregister a node to monitor. |
| `GET /status` / `GET /status/{id}` | Current status of all nodes, or one node. |
| `GET /config` / `PUT /config` | Read/update `ReplicationConfig` thresholds. |
| `GET /events` | SSE stream of node/failover/resync events. |
| `GET /health` | Liveness probe. |

## Out of scope

- Keycloak integration on the frontend/API.
- Logical replication of a table subset (only full streaming replication is implemented).
- Watchdog's monitoring interface is intentionally generic (register a node, probe it,
  report status) but only a Postgres `INodeProbe` implementation exists so far — what
  exactly it should check for *other* SLICES-RI resources needs a separate follow-up.

## Q&A summary (from the original correspondence)

| Question | Answer in this implementation |
|---|---|
| Replica sync before a primary failure | Immediate push on every write; sync/async is configurable via `PUT /api/config/mode` without rebuilding containers |
| Watchdog and request redirection | Standalone service + a .NET Query Router API layer (`MRS.Replication.Api`) instead of shell scripts |
| A previously-down node returning | Mandatory re-sync (`Resyncing` state) before it's ever marked `Active` again; always rejoins as a Replica |

---

*Developed as part of an internship at SLICES Research Infrastructure.*
