FROM postgres:16-alpine

COPY init/ /docker-entrypoint-initdb.d/
COPY mrs-entrypoint.sh /usr/local/bin/mrs-entrypoint.sh

RUN chmod +x /usr/local/bin/mrs-entrypoint.sh /docker-entrypoint-initdb.d/02-pg-hba.sh

ENTRYPOINT ["mrs-entrypoint.sh"]
