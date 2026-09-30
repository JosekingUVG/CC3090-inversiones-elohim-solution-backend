#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
performance_dir=$(cd "$script_dir/.." && pwd)
image="${JMETER_DOCKER_IMAGE:-justb4/jmeter:5.5}"
mapped_args=()

for argument in "$@"; do
  mapped_args+=("${argument//$performance_dir/\/tests}")
done

exec docker run --rm --network host \
  --user "$(id -u):$(id -g)" \
  --volume "$performance_dir:/tests" \
  --workdir /tests \
  "$image" "${mapped_args[@]}"

