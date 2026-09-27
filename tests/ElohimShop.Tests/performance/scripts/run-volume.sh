#!/usr/bin/env bash
set -euo pipefail
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
performance_dir=$(cd "$script_dir/.." && pwd)
mode=${1:-run}
case "$mode" in prepare|run|gui-prepare|gui-run) ;; *) echo 'Uso: bash scripts/run-volume.sh prepare|run|gui-prepare|gui-run' >&2; exit 2;; esac
reset_requested="${VOLUME_RESET:-${RESET_TENANT:-0}}"
case "${reset_requested,,}" in
 1|true|yes|y|on) reset_requested=1 ;;
 *) reset_requested=0 ;;
esac
jmeter_bin=${JMETER_BIN:-jmeter}
volume_file=${VOLUME_FILE:-$performance_dir/data/volume.csv}
should_setup=0
if [[ "$mode" == prepare ]]; then
  if [[ ! -f "$volume_file" ]] || grep -q '^ID_TENANT_PRUEBAS,' "$volume_file" || (( reset_requested )); then
    should_setup=1
  fi
fi
if (( should_setup )); then
 bash "$script_dir/setup-volume.sh"
fi
if [[ -n "${USERS_FILE:-}" ]]; then
 users_file="$USERS_FILE"
elif [[ -n "${VOLUME_USERS_FILE:-}" ]]; then
 users_file="$VOLUME_USERS_FILE"
elif [[ -f "$performance_dir/data/volume-users.csv" ]]; then
 users_file=$performance_dir/data/volume-users.csv
else
 users_file=$performance_dir/data/users.csv
fi
[[ -f "$volume_file" ]] || { echo 'Copia data/volume.example.csv como data/volume.csv y reemplaza los IDs de ejemplo.' >&2; exit 2; }
# Validate scenario fields only. Never print credentials from users.csv.
awk -F, 'NR==1 {sub(/\r$/, ""); if ($0!="tenant_id,product_count,iterations,branch_a,branch_b") exit 1}
 NR==2 {sub(/\r$/, ""); if (NF!=5 || $1=="" || $4=="" || $5=="" || $4==$5 || $0 ~ /ID_TENANT_PRUEBAS|ID_SUCURSAL_[AB]/ || $2 !~ /^[0-9]+$/ || $3 !~ /^[0-9]+$/ || $2<1 || $3<1) exit 1}
 END {if (NR!=2) exit 1}' "$volume_file" || { echo 'volume.csv requiere una fila con IDs reales del tenant y dos sucursales distintas, y cantidades positivas. Los textos ID_TENANT_PRUEBAS/ID_SUCURSAL_A/B son ejemplos.' >&2; exit 2; }
case "$mode" in *prepare)
 [[ -f "$users_file" ]] || { echo 'Falta data/users.csv con una cuenta autorizada para crear productos.' >&2; exit 2; }
 chmod 600 "$users_file"
 plan="$performance_dir/volume/catalog-prepare.jmx";;
 *) plan="$performance_dir/volume/catalog-volume.jmx";; esac
common=(-Jhost="${HOST:-localhost}" -Jport="${PORT:-5000}" -Jprotocol="${PROTOCOL:-http}"
 -Jvolume_file="$volume_file" -Jusers_file="$users_file"
 -Jjmeter.laf=javax.swing.plaf.metal.MetalLookAndFeel
 -Jjmeter.save.saveservice.output_format=csv -Jjmeter.save.saveservice.print_field_names=true
 -Jjmeter.save.saveservice.response_data=false -Jjmeter.save.saveservice.response_data.on_error=false)
if [[ "$mode" == gui-* ]]; then exec "$jmeter_bin" -t "$plan" "${common[@]}"; fi
run_id=$(date -u +%Y%m%dT%H%M%S%NZ)
result_dir="$performance_dir/results/volume/$mode-$run_id"
report_dir="$performance_dir/reports/volume/$mode-$run_id"
mkdir -p "$result_dir" "$(dirname "$report_dir")"
if [[ "$mode" == run ]]; then
 "$jmeter_bin" -n -t "$plan" "${common[@]}" -Jiterations="${WARMUP_ITERATIONS:-10}" \
   -l "$result_dir/warmup.jtl" -j "$result_dir/warmup.log"
 # Match the default JMeter CSV success column without exposing response bodies.
 awk -F, 'NR==1 {for(i=1;i<=NF;i++) if($i=="success") s=i; next} {n++; if(!s || $s!="true") bad=1} END {exit (!n || bad)}' "$result_dir/warmup.jtl" || {
   "$jmeter_bin" -g "$result_dir/warmup.jtl" -o "$report_dir"
   echo "Falló el calentamiento; medición cancelada. Diagnóstico: $report_dir/index.html" >&2; exit 1;
 }
fi
status=0
"$jmeter_bin" -n -t "$plan" "${common[@]}" -l "$result_dir/results.jtl" -j "$result_dir/jmeter.log" \
 -e -o "$report_dir" || status=$?
printf 'Reporte HTML: %s/index.html\nResultados: %s/results.jtl\n' "$report_dir" "$result_dir"
# JMeter can exit 0 even if assertions failed. Count requests and inspect success.
expected=$(awk -F, -v mode="$mode" 'NR==2 {print mode=="prepare" ? $2+4 : $3*2}' "$volume_file")
awk -F, -v expected="$expected" 'NR==1 {for(i=1;i<=NF;i++) if($i=="success") s=i; next} {n++; if(!s || $s!="true") bad=1} END {exit (!n || bad || n!=expected)}' "$result_dir/results.jtl" || status=1
if (( status != 0 )); then echo 'Ejecución fallida o incompleta; revisar errores en el HTML.' >&2;
else echo 'Solicitudes y aserciones completas. Revisar p95 por endpoint y recursos antes de aprobar el nivel.'; fi
exit "$status"
