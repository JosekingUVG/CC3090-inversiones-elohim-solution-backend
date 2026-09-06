#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
performance_dir=$(cd "$script_dir/.." && pwd)
jmeter_bin="${JMETER_BIN:-jmeter}"
base_url="${BASE_URL:-http://localhost:5000}"
users_file="${USERS_FILE:-$performance_dir/data/users.example.csv}"
users="${USERS:-50}"
ramp_up="${RAMP_UP:-60}"
duration="${DURATION:-300}"
baseline_seconds="${BASELINE_SECONDS:-180}"
recovery_seconds="${RECOVERY_SECONDS:-180}"
monitor_interval="${MONITOR_INTERVAL:-5}"
warmup_users="${WARMUP_USERS:-5}"
warmup_duration="${WARMUP_DURATION:-20}"
run_id=$(date -u +%Y%m%dT%H%M%SZ)
result_dir="$performance_dir/results/load/$run_id"
report_dir="$performance_dir/reports/load/$run_id"

command -v "$jmeter_bin" >/dev/null || { printf 'No se encontró JMeter: %s\n' "$jmeter_bin" >&2; exit 2; }
command -v curl >/dev/null || { printf 'Se requiere curl para verificar la API.\n' >&2; exit 2; }
curl --fail --silent --show-error "$base_url/api/v1/productos" -H "X-Tenant-Slug: ${TENANT_SLUG:-dmhub}" >/dev/null

url_parts=$(python3 -c 'import sys,urllib.parse as u; p=u.urlsplit(sys.argv[1]); print(p.scheme, p.hostname, p.port or (443 if p.scheme=="https" else 80))' "$base_url")
read -r protocol host port <<< "$url_parts"
mkdir -p "$result_dir" "$(dirname "$report_dir")"

common_args=(
  -Jprotocol="$protocol" -Jhost="$host" -Jport="$port"
  -Jusers_file="$users_file" -Jtenant_id="${TENANT_ID:-}"
  -Jconnect_timeout="${CONNECT_TIMEOUT:-5000}" -Jresponse_timeout="${RESPONSE_TIMEOUT:-10000}"
  -Jthink_min_ms="${THINK_MIN_MS:-1000}" -Jthink_range_ms="${THINK_RANGE_MS:-2000}"
  -Jjmeter.save.saveservice.output_format=csv -Jjmeter.save.saveservice.print_field_names=true
  -Jjmeter.save.saveservice.response_code=true -Jjmeter.save.saveservice.response_message=true
  -Jjmeter.save.saveservice.thread_name=true -Jjmeter.save.saveservice.latency=true
  -Jjmeter.save.saveservice.connect_time=true -Jjmeter.save.saveservice.successful=true
)
if [[ -n "${TENANT_SLUG:-}" ]]; then
  common_args+=(-Jtenant_slug="$TENANT_SLUG")
fi
if [[ -n "${JMETER_EMAIL:-}" ]]; then
  common_args+=(-Jemail="$JMETER_EMAIL")
fi
if [[ -n "${JMETER_PASSWORD:-}" ]]; then
  common_args+=(-Jpassword="$JMETER_PASSWORD")
fi

printf 'Calentamiento: %s usuarios durante %s s\n' "$warmup_users" "$warmup_duration"
"$jmeter_bin" -n -t "$performance_dir/load/auth-cart-load.jmx" \
  "${common_args[@]}" -Jusers="$warmup_users" -Jramp_up=2 -Jduration="$warmup_duration" \
  -l "$result_dir/warmup.jtl" -j "$result_dir/warmup-jmeter.log"

printf 'Baseline: %s s\n' "$baseline_seconds"
"$script_dir/monitor-resources.sh" "$baseline_seconds" "$monitor_interval" "$result_dir/baseline" baseline

printf 'NF-01: %s usuarios, ramp-up %s s, duración %s s\n' "$users" "$ramp_up" "$duration"
"$script_dir/monitor-resources.sh" "$duration" "$monitor_interval" "$result_dir/load" load &
monitor_pid=$!
set +e
"$jmeter_bin" -n -t "$performance_dir/load/auth-cart-load.jmx" \
  "${common_args[@]}" -Jusers="$users" -Jramp_up="$ramp_up" -Jduration="$duration" \
  -l "$result_dir/results.jtl" -j "$result_dir/jmeter.log" -e -o "$report_dir"
jmeter_status=$?
set -e
wait "$monitor_pid"

printf 'Recuperación: %s s\n' "$recovery_seconds"
"$script_dir/monitor-resources.sh" "$recovery_seconds" "$monitor_interval" "$result_dir/recovery" recovery

analysis_status=0
python3 "$script_dir/analyze-jtl.py" "$result_dir/results.jtl" --mode load --output "$result_dir/summary.csv" || analysis_status=$?
python3 "$script_dir/analyze-resources.py" "$result_dir" --active-prefix load --output "$result_dir/resource-summary.csv"
printf 'Resultados: %s\nReporte: %s/index.html\n' "$result_dir" "$report_dir"
(( jmeter_status == 0 && analysis_status == 0 ))
