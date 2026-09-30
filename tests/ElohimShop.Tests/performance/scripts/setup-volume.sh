#!/usr/bin/env bash
# Bootstrap a dedicated test tenant through public application APIs. No DB edits.
set -euo pipefail
umask 077
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
performance_dir=$(cd "$script_dir/.." && pwd)
volume_file=${VOLUME_FILE:-$performance_dir/data/volume.csv}
users_file=${VOLUME_USERS_FILE:-$performance_dir/data/volume-users.csv}
state_file=${VOLUME_SETUP_STATE:-$performance_dir/data/volume-setup.json}
base_url="${PROTOCOL:-http}://${HOST:-localhost}:${PORT:-5000}"
reset_requested="${VOLUME_RESET:-${RESET_TENANT:-0}}"
case "${reset_requested,,}" in
 1|true|yes|y|on) reset_requested=1 ;;
 *) reset_requested=0 ;;
esac
for tool in curl jq; do command -v "$tool" >/dev/null || { echo "Se requiere $tool para crear el tenant de pruebas." >&2; exit 2; }; done
if (( reset_requested )); then
  rm -f "$state_file" "$volume_file" "$users_file"
fi
count=${PRODUCT_COUNT:-1000}
iterations=${ITERATIONS:-200}
if [[ -f "$volume_file" ]]; then
 IFS=, read -r current count_csv iterations_csv rest < <(sed -n '2p' "$volume_file")
 if [[ -n "$current" && "$current" != ID_TENANT_PRUEBAS ]]; then
  echo 'volume.csv ya contiene un tenant; no se crea ni reemplaza automáticamente.'; exit 0
 fi
 count=${PRODUCT_COUNT:-${count_csv:-1000}}; iterations=${ITERATIONS:-${iterations_csv:-200}}
fi
[[ "$count" =~ ^[1-9][0-9]*$ && "$iterations" =~ ^[1-9][0-9]*$ ]] || { echo 'Volumen e iteraciones deben ser enteros positivos.' >&2; exit 2; }
mkdir -p "$(dirname "$state_file")" "$(dirname "$volume_file")" "$(dirname "$users_file")"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
request() {
 local method=$1 path=$2 expected=$3
 shift 3
 local status
 status=$(curl --silent --show-error --connect-timeout 5 --max-time 30 \
  -X "$method" "$base_url$path" -H 'Content-Type: application/json' \
  "$@" -o "$work/response.json" -w '%{http_code}')
 [[ "$status" == "$expected" ]] || { echo "Preparación del tenant: $method $path respondió HTTP $status (esperado $expected). Estado guardado; no se reintentan escrituras." >&2; exit 1; }
}
if [[ ! -f "$state_file" ]]; then
 [[ ! -e "$users_file" ]] || { echo 'Ya existe volume-users.csv sin estado de preparación. No se sobrescribirá.' >&2; exit 2; }
 suffix=$(cat /proc/sys/kernel/random/uuid)
 jq -n --arg base "$base_url" --arg email "volumen-$suffix@example.test" \
  --arg password "Vol-$(cat /proc/sys/kernel/random/uuid)-A9!" --arg name "Volumen $suffix" \
  '{base_url:$base,email:$email,password:$password,name:$name,registered:false}' > "$state_file"
fi
[[ $(jq -r .base_url "$state_file") == "$base_url" ]] || { echo 'El estado pertenece a otra URL; no se reutiliza.' >&2; exit 2; }
if [[ $(jq -r .registered "$state_file") != true ]]; then
 # If a prior POST succeeded but its response was lost, login recovers the same account.
 jq '{correo:.email,contrasena:.password}' "$state_file" > "$work/login.json"
 status=$(curl --silent --show-error --connect-timeout 5 --max-time 30 -X POST "$base_url/api/v1/auth/login" \
   -H 'Content-Type: application/json' --data-binary "@$work/login.json" -o "$work/response.json" -w '%{http_code}')
 if [[ "$status" == 401 ]]; then
  jq '{correo:.email,contrasena:.password,nombre:.name,tipoUsuario:"administrador",direccion:"Entorno de pruebas VOL-01"}' "$state_file" > "$work/register.json"
  request POST /api/v1/auth/register 201 --data-binary "@$work/register.json"
 elif [[ "$status" != 200 ]]; then
  echo "Login de bootstrap respondió HTTP $status; se detiene antes de registrar." >&2; exit 1
 fi
 jq -e '.token | type == "string" and length > 0' "$work/response.json" >/dev/null
 jq '.registered=true' "$state_file" > "$work/state.json"; mv "$work/state.json" "$state_file"
else
 jq '{correo:.email,contrasena:.password}' "$state_file" > "$work/login.json"
 request POST /api/v1/auth/login 200 --data-binary "@$work/login.json"
fi
# Keep token in a mode-600 header file, not in process arguments or logs.
jq -er '"Authorization: Bearer " + .token' "$work/response.json" > "$work/headers"
request GET /api/v1/tiendas 200 -H "@$work/headers"
name=$(jq -r .name "$state_file")
match=$(jq --arg name "Tienda de $name" '[.[] | select(.nombre==$name)]' "$work/response.json")
[[ $(jq length <<< "$match") == 1 ]] || { echo 'No se encontró una única tienda de esta preparación; no se elige una tienda ajena.' >&2; exit 1; }
tenant=$(jq -er '.[0].id' <<< "$match")
slug=$(jq -er '.[0].slug' <<< "$match")
printf 'X-Tenant-ID: %s\n' "$tenant" >> "$work/headers"
request GET /api/v1/sucursales 200 -H "@$work/headers"
branch_a=$(jq -er '[.[] | select(.nombre=="Sucursal Principal")] | if length==1 then .[0].id else error("Sucursal principal ambigua") end' "$work/response.json")
branch_b=$(jq -r '[.[] | select(.nombre=="Sucursal Volumen B")] | if length==0 then "" elif length==1 then .[0].id else error("Sucursal B ambigua") end' "$work/response.json")
if [[ -z "$branch_b" ]]; then
 printf '%s' '{"nombre":"Sucursal Volumen B","direccion":"Entorno de pruebas VOL-01","telefono":null}' > "$work/branch.json"
 request POST /api/v1/sucursales 201 -H "@$work/headers" --data-binary "@$work/branch.json"
 branch_b=$(jq -er .id "$work/response.json")
fi
{
 echo 'correo,contrasena,tenant_slug'
 jq -r --arg slug "$slug" '[.email,.password,$slug] | @csv' "$state_file"
} > "$work/users.csv"
{
 echo 'tenant_id,product_count,iterations,branch_a,branch_b'
 printf '%s,%s,%s,%s,%s\n' "$tenant" "$count" "$iterations" "$branch_a" "$branch_b"
} > "$work/volume.csv"
mv "$work/users.csv" "$users_file"
mv "$work/volume.csv" "$volume_file"
printf 'Tenant de pruebas listo: %s\nConfiguración: %s\nCredenciales separadas: %s\n' "$slug" "$volume_file" "$users_file"
