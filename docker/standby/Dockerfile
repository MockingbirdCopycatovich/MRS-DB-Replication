FROM postgres:16

LABEL description="MRS Standby PostgreSQL node"

COPY postgresql.conf /etc/postgresql/postgresql.conf
COPY entrypoint.sh   /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]