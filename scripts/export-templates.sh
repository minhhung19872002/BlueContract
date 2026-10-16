#!/usr/bin/env bash
# Ghi các mẫu biểu chuẩn (DOCX) và 2 biểu mẫu import Excel vào thư mục templates/ ở gốc repo.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet run --project "$root/backend/src/BlueContract.Api" -- --export-templates "$root/templates"
echo "Đã xuất mẫu biểu vào $root/templates"