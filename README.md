# MRS DB Replication

> **Fault-tolerant PostgreSQL replication for the Metadata Registry System (MRS)**  
> Part of the SLICES Research Infrastructure — Data Management Infrastructure (DMI)

---

## Table of Contents

- [Background](#background)
- [Goals](#goals)
- [Repository Structure](#repository-structure)
- [Demo Database Schema](#demo-database-schema)
- [Quick Start](#quick-start)
- [Watchdog](#watchdog)
- [Failover Procedure](#failover-procedure)
- [References](#references)

---

## Background

MRS (Metadata Registry System) is a core component of the [SLICES Research Infrastructure](https://doc.slices-ri.eu), responsible for centralizing and managing metadata for datasets, publications, software tools, and internal resources. It follows the **FAIR principles** (Findable, Accessible, Interoperable, Reusable).

MRS is built on a **PostgreSQL** database connected to a backend that exposes all functionality to the MRS Portal front-end. As a critical piece of infrastructure, it requires **high availability** — a single point of failure in the database layer is unacceptable.

This project explores and implements **fault-tolerant database replication** for MRS, so that if the primary database instance fails, a standby replica can take over with minimal downtime.

---

## Goals

- Implement **PostgreSQL Streaming Replication** between a Primary node and at least two Standby nodes
- Provide a **demo database** with 3 representative tables mirroring the MRS domain
- Document a clear **manual failover procedure** for operators
- Containerise the entire setup using **Docker Compose** for reproducibility

---

## Repository Structure

```
MRS-DB-Replication/
│
├── README.md                        # This file
│
├── docker/
│   ├── docker-compose.yml           # Spins up primary + 2 standbys
│   ├── primary/
│   │   ├── Dockerfile
│   │   ├── postgresql.conf          # Primary config (wal_level, max_wal_senders, etc.)
│   │   └── pg_hba.conf              # Allows replication user from standby IPs
│   └── standby/
│       ├── Dockerfile
│       ├── postgresql.conf          # Standby config (hot_standby = on)
│       └── entrypoint.sh            # Runs pg_basebackup on first start
│
├── sql/
│   ├── 00_init_replication.sh       # Creates the replication user (auto-run on first boot)
│   ├── 01_init_schema.sql           # Creates 3 demo tables (auto-run on first boot)
│   └── 02_seed_data.sql             # Inserts sample rows (auto-run on first boot)
│
└── scripts/
    ├── check_replication.sh         # Shows lag, connected standbys, sync state
    └── watchdog.sh                  # Monitors the primary and auto-promotes the best
                                     # standby when the primary goes down

```

---

## Demo Database Schema

The demo schema represents a simplified MRS domain with three tables:

```sql
-- Registered datasets (e.g. measurement results, experiment outputs)
CREATE TABLE datasets (
    id          SERIAL PRIMARY KEY,
    title       VARCHAR(255) NOT NULL,
    doi         VARCHAR(100) UNIQUE,
    description TEXT,
    created_at  TIMESTAMP DEFAULT NOW()
);

-- Academic publications linked to SLICES experiments
CREATE TABLE publications (
    id          SERIAL PRIMARY KEY,
    title       VARCHAR(255) NOT NULL,
    authors     TEXT,
    journal     VARCHAR(255),
    year        INT,
    doi         VARCHAR(100) UNIQUE
);

-- Software tools and scripts used within SLICES
CREATE TABLE software_tools (
    id             SERIAL PRIMARY KEY,
    name           VARCHAR(255) NOT NULL,
    version        VARCHAR(50),
    language       VARCHAR(50),
    repository_url TEXT,
    registered_at  TIMESTAMP DEFAULT NOW()
);
```

---

## Quick Start

**Prerequisites:** Docker and Docker Compose installed.

```bash
# 1. Clone the repository
git clone https://github.com/MockingbirdCopycatovich/MRS-DB-Replication.git
cd MRS-DB-Replication

# 2. Build images and start the cluster (1 primary + 2 standbys)
docker compose -f docker/docker-compose.yml up --build -d
```

The `sql/` directory is mounted into `/docker-entrypoint-initdb.d` on the primary, so PostgreSQL automatically runs `00_init_replication.sh`, `01_init_schema.sql`, and `02_seed_data.sql` on first boot — no manual SQL step required.

```bash
# 3. Verify replication is working
bash scripts/check_replication.sh
```

You should see both standbys listed as connected with near-zero replication lag.

**Port mappings:**

| Container      | Host port | Role    |
|----------------|-----------|---------|
| `mrs-primary`  | `5432`    | Primary |
| `mrs-standby-1`| `5433`    | Replica |
| `mrs-standby-2`| `5434`    | Replica |

**Tear down and rebuild from scratch:**

```bash
# Stop and remove containers + volumes
docker compose -f docker/docker-compose.yml down -v

# Rebuild images without cache and start
docker compose -f docker/docker-compose.yml up --build -d
```

---

## Watchdog

`watchdog.sh` is a bash-based availability monitor that automatically detects primary failure and promotes the most up-to-date standby without any manual intervention.

### Running the watchdog

```bash
bash scripts/watchdog.sh
```

The watchdog runs in the foreground and logs all events with timestamps to stderr. Keep it running in a dedicated terminal or a `screen`/`tmux` session while the cluster is active.

### Configuration

The following variables at the top of `watchdog.sh` can be adjusted:

| Variable            | Default          | Description                                      |
|---------------------|------------------|--------------------------------------------------|
| `PRIMARY`           | `mrs-primary`    | Docker container name of the primary             |
| `STANDBYS`          | `mrs-standby-1`, `mrs-standby-2` | Ordered list of standby containers |
| `CHECK_INTERVAL`    | `5`              | Seconds between health checks                    |
| `FAILURE_THRESHOLD` | `3`              | Consecutive failures before failover is triggered|

### After a failover

The watchdog prints a checklist once promotion completes:

1. **Update `DB_HOST`** in the MRS backend config to point to the new primary container
2. **Do not restart** the old primary until it has been reconfigured as a standby
3. Run `bash scripts/check_replication.sh` to verify the new cluster state

> **Note:** The watchdog updates its own in-memory state after failover (new primary, remaining standbys) and continues monitoring — no restart required.

### Limitations

- State is in-memory only — if the watchdog process itself is restarted after a failover, `PRIMARY` reverts to `mrs-primary` in the script. Update the variable manually if that container is no longer the primary.
- The old primary is **not** automatically reconfigured as a standby. Manual reconfiguration (`pg_basebackup` + `standby.signal`) is required.

---

## Failover Procedure

### Automatic failover (watchdog)

Start the watchdog before or immediately after bringing up the cluster:

```bash
bash scripts/watchdog.sh
```

It will handle promotion automatically. See the [Watchdog](#watchdog) section above for full details.

### Manual failover

```bash
# 1. Confirm the primary is down
bash scripts/check_replication.sh

# 2. Promote standby-1 to primary
docker exec -it mrs-standby-1 pg_ctl promote -D /var/lib/postgresql/data

```

---

## References

- [MRS Documentation — SLICES-RI](https://doc.slices-ri.eu)
- [PostgreSQL: High Availability, Load Balancing, and Replication](https://www.postgresql.org/docs/current/high-availability.html)
- [Patroni — GitHub](https://github.com/patroni/patroni)
- [pg_basebackup documentation](https://www.postgresql.org/docs/current/app-pgbasebackup.html)

---

*This project is developed as part of an internship at SLICES Research Infrastructure.*