#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
performance_dir=$(cd "$script_dir/.." && pwd)
jmeter_bin="${JMETER_BIN:-jmeter}"
base_url="${BASE_URL:-http://localhost:5000}"
step_duration="${STEP_DURATION:-120}"
step_ramp_up="${STEP_RAMP_UP:-10}"
step_gap="${STEP_GAP:-5}"
baseline_seconds="${BASELINE_SECONDS:-180}"
recovery_seconds="${RECOVERY_SECONDS:-180}"
monitor_interval="${MONITOR_INTERVAL:-5}"
warmup_duration="${WARMUP_DURATION:-15}"
group_duration=$((step_ramp_up + step_duration))
step_span=$((group_duration + step_gap))
test_duration=$((group_duration * 8 + step_gap * 7))
run_id=$(date -u +%Y%m%dT%H%M%SZ)
result_dir="$performance_dir/results/stress/$run_id"
report_dir="$performance_dir/reports/stress/$run_id"

command -v "$jmeter_bin" >/dev/null || { printf 'No se encontró JMeter: %s\n' "$jmeter_bin" >&2; exit 2; }
curl --fail --silent --show-error "$base_url/api/v1/productos" -H "X-Tenant-Slug: ${TENANT_SLUG:-dmhub}" >/dev/null
url_parts=$(python3 -c 'import sys,urllib.parse as u; p=u.urlsplit(sys.argv[1]); print(p.scheme, p.hostname, p.port or (443 if p.scheme=="https" else 80))' "$base_url")
read -r protocol host port <<< "$url_parts"
mkdir -p "$result_dir" "$(dirname "$report_dir")"

common_args=(
  -Jprotocol="$protocol" -Jhost="$host" -Jport="$port"
  -Jtenant_slug="${TENANT_SLUG:-dmhub}" -Jtenant_id="${TENANT_ID:-}"
  -Jconnect_timeout="${CONNECT_TIMEOUT:-5000}" -Jresponse_timeout="${RESPONSE_TIMEOUT:-10000}"
  -Jjmeter.save.saveservice.output_format=csv -Jjmeter.save.saveservice.print_field_names=true
  -Jjmeter.save.saveservice.response_code=true -Jjmeter.save.saveservice.response_message=true
  -Jjmeter.save.saveservice.thread_name=true -Jjmeter.save.saveservice.latency=true
  -Jjmeter.save.saveservice.connect_time=true -Jjmeter.save.saveservice.successful=true
)

printf 'Calentamiento de productos: %s s\n' "$warmup_duration"
"$jmeter_bin" -n -t "$performance_dir/stress/products-warmup.jmx" \
  "${common_args[@]}" -Jwarmup_duration="$warmup_duration" \
  -l "$result_dir/warmup.jtl" -j "$result_dir/warmup-jmeter.log"

printf 'Baseline: %s s\n' "$baseline_seconds"
"$script_dir/monitor-resources.sh" "$baseline_seconds" "$monitor_interval" "$result_dir/baseline" baseline

printf 'NF-02: 8 escalones (%s s ramp-up + %s s estables + %s s separación; %s s total)\n' "$step_ramp_up" "$step_duration" "$step_gap" "$test_duration"
"$script_dir/monitor-resources.sh" "$test_duration" "$monitor_interval" "$result_dir/stress" stress &
monitor_pid=$!
set +e
"$jmeter_bin" -n -t "$performance_dir/stress/products-stress.jmx" \
  "${common_args[@]}" -Jgroup_duration="$group_duration" -Jstep_ramp_up="$step_ramp_up" \
  -Jstep_02_delay="$step_span" -Jstep_03_delay="$((step_span * 2))" \
  -Jstep_04_delay="$((step_span * 3))" -Jstep_05_delay="$((step_span * 4))" \
  -Jstep_06_delay="$((step_span * 5))" -Jstep_07_delay="$((step_span * 6))" \
  -Jstep_08_delay="$((step_span * 7))" \
  -l "$result_dir/results.jtl" -j "$result_dir/jmeter.log" -e -o "$report_dir"
jmeter_status=$?
set -e
wait "$monitor_pid"

printf 'Recuperación: %s s\n' "$recovery_seconds"
"$script_dir/monitor-resources.sh" "$recovery_seconds" "$monitor_interval" "$result_dir/recovery" recovery
python3 "$script_dir/analyze-jtl.py" "$result_dir/results.jtl" --mode stress --output "$result_dir/summary.csv"
python3 "$script_dir/analyze-resources.py" "$result_dir" --active-prefix stress --output "$result_dir/resource-summary.csv"
printf 'Resultados: %s\nReporte: %s/index.html\n' "$result_dir" "$report_dir"
exit "$jmeter_status"
