FROM postgres:16

LABEL description="MRS Primary PostgreSQL node"

# Copy custom configs
COPY postgresql.conf /etc/postgresql/postgresql.conf
COPY pg_hba.conf     /etc/postgresql/pg_hba.conf

# Script that creates the replication user on first boot
COPY init-replication.sh /docker-entrypoint-initdb.d/01-init-replication.sh
RUN chmod +x /docker-entrypoint-initdb.d/01-init-replication.sh

CMD ["postgres", "-c", "config_file=/etc/postgresql/postgresql.conf", \
                        "-c", "hba_file=/etc/postgresql/pg_hba.conf"]