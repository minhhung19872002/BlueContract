#!/usr/bin/env bash
# Runs backup-once.sh every day at BACKUP_TIME (HH:MM, Vietnam time; default 01:30). BACKUP_TIME empty = disabled.
set -uo pipefail
at="${BACKUP_TIME-01:30}"
if [ -z "$at" ]; then
  echo "Sao lưu tự động đang tắt (BACKUP_TIME rỗng)."
  exec sleep infinity
fi
echo "Sao lưu tự động lúc $at hằng ngày, giữ ${BACKUP_KEEP:-30} bản."
while true; do
  now=$(date +%s)
  next=$(date -d "today $at" +%s)
  [ "$next" -le "$now" ] && next=$(date -d "tomorrow $at" +%s)
  sleep $((next - now))
  /usr/local/bin/backup-once.sh || echo "[$(date -Iseconds)] SAO LƯU THẤT BẠI" >&2
done
