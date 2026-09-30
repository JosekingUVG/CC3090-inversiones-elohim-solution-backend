#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

duration_seconds="${1:?Uso: monitor-resources.sh DURACION INTERVALO PREFIJO FASE}"
interval_seconds="${2:-5}"
output_prefix="${3:?Debe indicar el prefijo de salida}"
phase="${4:-load}"

mkdir -p "$(dirname "$output_prefix")"
docker_csv="${output_prefix}-docker.csv"
host_csv="${output_prefix}-host.csv"
docker_errors="${output_prefix}-docker-errors.log"

printf '%s\n' 'timestamp_utc,phase,container,cpu_percent,memory_usage,memory_percent,net_io,block_io,pids' > "$docker_csv"
printf '%s\n' 'timestamp_utc,phase,cpu_percent,memory_used_mib,memory_total_mib,memory_percent,swap_used_mib,swap_total_mib' > "$host_csv"
: > "$docker_errors"

read_cpu() {
  awk '/^cpu / { idle=$5+$6; total=0; for(i=2;i<=NF;i++) total+=$i; print total, idle; exit }' /proc/stat
}

read -r previous_total previous_idle < <(read_cpu)
start_epoch=$(date +%s)

while (( $(date +%s) - start_epoch < duration_seconds )); do
  timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)

  docker stats --no-stream \
    --format "${timestamp},${phase},{{.Name}},{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}},{{.NetIO}},{{.BlockIO}},{{.PIDs}}" \
    >> "$docker_csv" 2>> "$docker_errors" || true

  read -r current_total current_idle < <(read_cpu)
  total_delta=$((current_total - previous_total))
  idle_delta=$((current_idle - previous_idle))
  if (( total_delta > 0 )); then
    cpu_percent=$(awk -v total="$total_delta" -v idle="$idle_delta" 'BEGIN { printf "%.2f", 100 * (total-idle) / total }')
  else
    cpu_percent="0.00"
  fi
  previous_total=$current_total
  previous_idle=$current_idle

  read -r mem_total_kib mem_available_kib swap_total_kib swap_free_kib < <(
    awk '
      /^MemTotal:/ {mt=$2}
      /^MemAvailable:/ {ma=$2}
      /^SwapTotal:/ {st=$2}
      /^SwapFree:/ {sf=$2}
      END {print mt, ma, st, sf}
    ' /proc/meminfo
  )
  mem_used_kib=$((mem_total_kib - mem_available_kib))
  swap_used_kib=$((swap_total_kib - swap_free_kib))
  mem_percent=$(awk -v used="$mem_used_kib" -v total="$mem_total_kib" 'BEGIN { printf "%.2f", 100 * used / total }')
  awk -v ts="$timestamp" -v ph="$phase" -v cpu="$cpu_percent" \
      -v mu="$mem_used_kib" -v mt="$mem_total_kib" -v mp="$mem_percent" \
      -v su="$swap_used_kib" -v st="$swap_total_kib" \
      'BEGIN { printf "%s,%s,%s,%.2f,%.2f,%s,%.2f,%.2f\n", ts,ph,cpu,mu/1024,mt/1024,mp,su/1024,st/1024 }' \
      >> "$host_csv"

  sleep "$interval_seconds"
done
