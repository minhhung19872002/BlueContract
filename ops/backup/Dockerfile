# Backup sidecar (AUDIT-1 §13.3): daily pg_dump + copy of the file bucket into /backups, same layout as scripts/backup.*.
FROM alpine:3.20
RUN apk add --no-cache bash tzdata postgresql16-client aws-cli coreutils
ENV TZ=Asia/Ho_Chi_Minh
COPY backup-once.sh restore-once.sh backup-loop.sh /usr/local/bin/
RUN sed -i 's/\r$//' /usr/local/bin/*.sh && chmod +x /usr/local/bin/*.sh
VOLUME /backups
CMD ["/usr/local/bin/backup-loop.sh"]
