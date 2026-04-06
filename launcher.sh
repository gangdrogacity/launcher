#!/usr/bin/env bash

set -uo pipefail

APP_NAME="GangDrogaCity Launcher (Bash)"
APP_VERSION="2.2.4.3"
USER_AGENT="GangDrogaCity-Launcher/1.0"

LAUNCHER_REPO_OWNER="gangdrogacity"
LAUNCHER_REPO_NAME="launcher"
MODPACK_REPO_OWNER="jamnaga"
MODPACK_REPO_NAME="wtf-modpack"
DEFAULT_BRANCH="main"

MINECRAFT_DIR="${HOME}/.gangdrogacity"
GAME_DIR="${MINECRAFT_DIR}/game"
DOWNLOAD_DIR="${MINECRAFT_DIR}/downloads"
SETTINGS_FILE="${MINECRAFT_DIR}/settings.conf"
MANIFEST_PATH="${DOWNLOAD_DIR}/manifest.json"
HASH_CACHE_PATH="${MINECRAFT_DIR}/hash_cache.json"
PACKAGE_VERSIONS_PATH="${DOWNLOAD_DIR}/package_versions.json"
COMMIT_FILE="${DOWNLOAD_DIR}/latest_modpack_commit.txt"
FABRIC_MARKER="${GAME_DIR}/fabricInstalled"
VERSION_FILE="${GAME_DIR}/version.txt"
LAST_REPORT_FILE="${GAME_DIR}/last_report_url.txt"

NETWORK_TIMEOUT=10
DOWNLOAD_CONCURRENCY=6
RETRY_COUNT=3
MC_RAM_MB=4096

OS_NAME="linux"
OS_RULE_NAME="linux"
PATH_SEP=":"
case "$(uname -s)" in
  Darwin)
    OS_NAME="mac"
    OS_RULE_NAME="osx"
    ;;
  Linux)
    OS_NAME="linux"
    OS_RULE_NAME="linux"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    OS_NAME="windows"
    OS_RULE_NAME="windows"
    PATH_SEP=";"
    ;;
esac

CPU_ARCH="x64"
case "$(uname -m)" in
  arm64|aarch64)
    CPU_ARCH="aarch64"
    ;;
  x86_64|amd64)
    CPU_ARCH="x64"
    ;;
esac

# settings runtime
CFG_USERNAME=""
CFG_CURRENT_BRANCH="${DEFAULT_BRANCH}"
CFG_FABRIC_LOADER_VERSION=""
CFG_MC_VERSION=""
CFG_FABRIC_VERSION_ID=""

REPO_BASEPATH=""
DATA_URL=""
LATEST_RELEASE_VERSION="${APP_VERSION}"
MC_PID=""

TMP_DIR=""

safe_mkdir() {
  mkdir -p "$1"
}

log() {
  printf "[%s] %s\n" "$(date +"%H:%M:%S")" "$*"
}

warn() {
  printf "[%s] WARNING: %s\n" "$(date +"%H:%M:%S")" "$*" >&2
}

err() {
  printf "[%s] ERROR: %s\n" "$(date +"%H:%M:%S")" "$*" >&2
}

lower_str() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

encode_url_path() {
  local raw="$1"
  jq -nr --arg v "${raw}" '$v | split("/") | map(@uri) | join("/")'
}

cleanup_tmp() {
  if [[ -n "${TMP_DIR}" && -d "${TMP_DIR}" ]]; then
    rm -rf "${TMP_DIR}" || true
  fi
}

ensure_tmp_dir() {
  local candidate=""

  # Primo tentativo: dentro la directory launcher utente.
  if candidate="$(mktemp -d "${MINECRAFT_DIR}/tmp.XXXXXX" 2>/dev/null)"; then
    TMP_DIR="${candidate}"
    return 0
  fi

  # Fallback: directory download.
  if candidate="$(mktemp -d "${DOWNLOAD_DIR}/tmp.XXXXXX" 2>/dev/null)"; then
    TMP_DIR="${candidate}"
    return 0
  fi

  # Ultimo fallback: /tmp di sistema.
  if candidate="$(mktemp -d "/tmp/gdc-launcher.XXXXXX" 2>/dev/null)"; then
    TMP_DIR="${candidate}"
    return 0
  fi

  err "Impossibile creare directory temporanea."
  return 1
}

on_exit() {
  cleanup_tmp
}
trap on_exit EXIT

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    err "Comando richiesto non trovato: $1"
    return 1
  }
}

init_fs() {
  safe_mkdir "${MINECRAFT_DIR}"
  safe_mkdir "${GAME_DIR}"
  safe_mkdir "${DOWNLOAD_DIR}"
  safe_mkdir "${GAME_DIR}/logs"
  [[ -f "${HASH_CACHE_PATH}" ]] || echo '{}' > "${HASH_CACHE_PATH}"
  [[ -f "${PACKAGE_VERSIONS_PATH}" ]] || echo '{}' > "${PACKAGE_VERSIONS_PATH}"
}

save_settings() {
  cat > "${SETTINGS_FILE}" <<EOF
username=${CFG_USERNAME}
version=${APP_VERSION}
fabricLoaderVersion=${CFG_FABRIC_LOADER_VERSION}
mcVersion=${CFG_MC_VERSION}
fabricVersionId=${CFG_FABRIC_VERSION_ID}
currentBranch=${CFG_CURRENT_BRANCH}
EOF
}

load_settings() {
  if [[ -f "${SETTINGS_FILE}" ]]; then
    while IFS='=' read -r k v; do
      case "${k}" in
        username) CFG_USERNAME="${v}" ;;
        fabricLoaderVersion) CFG_FABRIC_LOADER_VERSION="${v}" ;;
        mcVersion) CFG_MC_VERSION="${v}" ;;
        fabricVersionId) CFG_FABRIC_VERSION_ID="${v}" ;;
        currentBranch) CFG_CURRENT_BRANCH="${v}" ;;
      esac
    done < "${SETTINGS_FILE}"
  fi
  [[ -n "${CFG_CURRENT_BRANCH}" ]] || CFG_CURRENT_BRANCH="${DEFAULT_BRANCH}"
}

set_repo_urls() {
  REPO_BASEPATH="https://raw.githubusercontent.com/${MODPACK_REPO_OWNER}/${MODPACK_REPO_NAME}/refs/heads/${CFG_CURRENT_BRANCH}/"
  DATA_URL="https://github.com/${MODPACK_REPO_OWNER}/${MODPACK_REPO_NAME}/archive/refs/heads/${CFG_CURRENT_BRANCH}.zip"
}

api_get() {
  local url="$1"
  curl -fsSL --connect-timeout "${NETWORK_TIMEOUT}" --max-time 30 \
    -H "User-Agent: ${USER_AGENT}" \
    -H "Cache-Control: no-cache" \
    "$url"
}

download_file() {
  local url="$1"
  local dest="$2"
  local tmp="${dest}.tmp"
  local attempt

  safe_mkdir "$(dirname "${dest}")"

  for attempt in $(seq 1 "${RETRY_COUNT}"); do
    if curl -fsSL --connect-timeout "${NETWORK_TIMEOUT}" --max-time 0 \
      -H "User-Agent: ${USER_AGENT}" \
      -H "Cache-Control: no-cache" \
      "$url" -o "${tmp}"; then
      mv -f "${tmp}" "${dest}"
      return 0
    fi
    rm -f "${tmp}" || true
    if [[ "${attempt}" -lt "${RETRY_COUNT}" ]]; then
      warn "Download fallito (${attempt}/${RETRY_COUNT}): ${url}. Retry..."
      sleep "${attempt}"
    fi
  done
  return 1
}

format_mb() {
  awk -v b="$1" 'BEGIN { printf "%.1f", (b / 1048576) }'
}

format_duration() {
  local sec="$1"
  (( sec < 0 )) && sec=0
  local h=$((sec / 3600))
  local m=$(((sec % 3600) / 60))
  local s=$((sec % 60))
  if (( h > 0 )); then
    printf "%02d:%02d:%02d" "$h" "$m" "$s"
  else
    printf "%02d:%02d" "$m" "$s"
  fi
}

download_status_monitor() {
  local total_count="$1"
  local total_bytes="$2"
  local progress_file="$3"
  local stop_file="$4"
  local start_ts
  start_ts="$(date +%s)"
  local bar_w=40

  while [[ ! -f "${stop_file}" ]]; do
    local done_count done_bytes now elapsed pct pct_int speed eta filled empty bar_filled bar_empty
    done_count="$(wc -l < "${progress_file}" 2>/dev/null | tr -d ' ')"
    done_bytes="$(awk '{s+=$1} END {print s+0}' "${progress_file}" 2>/dev/null)"
    now="$(date +%s)"
    elapsed=$((now - start_ts))
    (( elapsed <= 0 )) && elapsed=1

    pct="$(awk -v d="${done_bytes}" -v t="${total_bytes}" 'BEGIN { if (t<=0) print "0.0"; else printf "%.1f", (d*100.0)/t }')"
    speed=$((done_bytes / elapsed))
    if (( speed > 0 )); then
      eta=$(((total_bytes - done_bytes) / speed))
    else
      eta=0
    fi

    pct_int="$(awk -v p="${pct}" 'BEGIN { printf "%d", p }')"
    (( pct_int < 0 )) && pct_int=0
    (( pct_int > 100 )) && pct_int=100
    filled=$((pct_int * bar_w / 100))
    empty=$((bar_w - filled))
    bar_filled="$(printf '%*s' "${filled}" '' | tr ' ' '#')"
    bar_empty="$(printf '%*s' "${empty}" '' | tr ' ' '.')"

    printf "\rScaricamento: [%s%s] %3s%% | file %s/%s | %s/%s MB | %s MB/s | ETA %s" \
      "${bar_filled}" "${bar_empty}" "${pct_int}" \
      "${done_count}" "${total_count}" \
      "$(format_mb "${done_bytes}")" "$(format_mb "${total_bytes}")" \
      "$(format_mb "${speed}")" "$(format_duration "${eta}")"
    sleep 1
  done

  # Stampa finale pulita
  local final_count final_bytes final_pct
  final_count="$(wc -l < "${progress_file}" 2>/dev/null | tr -d ' ')"
  final_bytes="$(awk '{s+=$1} END {print s+0}' "${progress_file}" 2>/dev/null)"
  final_pct="$(awk -v d="${final_bytes}" -v t="${total_bytes}" 'BEGIN { if (t<=0) print "100.0"; else printf "%.1f", (d*100.0)/t }')"
  local final_int final_filled final_empty final_bar_filled final_bar_empty
  final_int="$(awk -v p="${final_pct}" 'BEGIN { printf "%d", p }')"
  (( final_int < 0 )) && final_int=0
  (( final_int > 100 )) && final_int=100
  final_filled=$((final_int * bar_w / 100))
  final_empty=$((bar_w - final_filled))
  final_bar_filled="$(printf '%*s' "${final_filled}" '' | tr ' ' '#')"
  final_bar_empty="$(printf '%*s' "${final_empty}" '' | tr ' ' '.')"

  printf "\rScaricamento: [%s%s] %3s%% | file %s/%s | %s/%s MB | completato\n" \
    "${final_bar_filled}" "${final_bar_empty}" "${final_int}" "${final_count}" "${total_count}" \
    "$(format_mb "${final_bytes}")" "$(format_mb "${total_bytes}")"
}

download_modpack_file_worker() {
  local rel="$1"
  local size="$2"
  local once="$3"
  local progress_file="$4"
  local fail_file="$5"

  local fp="${GAME_DIR}/${rel}"
  [[ "${once}" == "true" ]] && fp="${fp}.once"

  safe_mkdir "$(dirname "${fp}")"
  local rel_encoded
  rel_encoded="$(encode_url_path "${rel}")"
  if ! download_file "${REPO_BASEPATH}${rel_encoded}" "${fp}"; then
    printf '%s\n' "${rel}" >> "${fail_file}"
    return 1
  fi

  if [[ "${once}" == "true" ]]; then
    cp -f "${fp}" "${fp%.once}" || true
  fi

  printf '%s\n' "${size}" >> "${progress_file}"
  return 0
}

refresh_hash_cache_from_todo() {
  local list_todo="$1"
  while IFS=$'\t' read -r rel size sha once; do
    [[ -z "${rel}" ]] && continue
    local fp="${GAME_DIR}/${rel}"
    [[ "${once}" == "true" ]] && fp="${fp}.once"
    if [[ -f "${fp}" ]]; then
      hash_cache_set "${fp}_${size}" "${sha}"
    fi
  done < "${list_todo}"
}

download_modpack_files_parallel() {
  local list_todo="$1"
  local todo_count="$2"
  local total_bytes="$3"

  local work_dir
  if ! work_dir="$(mktemp -d "${MINECRAFT_DIR}/dl.XXXXXX" 2>/dev/null)"; then
    if ! work_dir="$(mktemp -d "${DOWNLOAD_DIR}/dl.XXXXXX" 2>/dev/null)"; then
      work_dir="$(mktemp -d "/tmp/gdc-dl.XXXXXX")" || return 1
    fi
  fi

  local progress_file="${work_dir}/download_progress_sizes.txt"
  local fail_file="${work_dir}/download_failed_files.txt"
  local stop_file="${work_dir}/download_monitor.stop"
  : > "${progress_file}"
  : > "${fail_file}"
  rm -f "${stop_file}" || true

  download_status_monitor "${todo_count}" "${total_bytes}" "${progress_file}" "${stop_file}" &
  local monitor_pid="$!"
  local worker_pids=()

  live_workers_count() {
    local c=0
    local pid
    for pid in "${worker_pids[@]:-}"; do
      if kill -0 "${pid}" >/dev/null 2>&1; then
        c=$((c + 1))
      fi
    done
    echo "${c}"
  }

  while IFS=$'\t' read -r rel size _sha once; do
    [[ -z "${rel}" ]] && continue

    while [[ "$(live_workers_count)" -ge "${DOWNLOAD_CONCURRENCY}" ]]; do
      sleep 0.1
    done

    download_modpack_file_worker "${rel}" "${size}" "${once}" "${progress_file}" "${fail_file}" &
    worker_pids+=("$!")
  done < "${list_todo}"

  local pid
  for pid in "${worker_pids[@]:-}"; do
    wait "${pid}" || true
  done

  touch "${stop_file}"
  wait "${monitor_pid}" 2>/dev/null || true

  if [[ -s "${fail_file}" ]]; then
    err "Download fallito per alcuni file. Primi elementi:"
    head -n 5 "${fail_file}" | while IFS= read -r bad; do
      err " - ${bad}"
    done
    rm -rf "${work_dir}" || true
    return 1
  fi

  rm -rf "${work_dir}" || true
  return 0
}

file_size() {
  local p="$1"
  if [[ "${OS_NAME}" == "mac" ]]; then
    stat -f%z "$p"
  else
    stat -c%s "$p"
  fi
}

sha256_file() {
  local p="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$p" | awk '{print tolower($1)}'
  else
    shasum -a 256 "$p" | awk '{print tolower($1)}'
  fi
}

sha1_file() {
  local p="$1"
  if command -v sha1sum >/dev/null 2>&1; then
    sha1sum "$p" | awk '{print tolower($1)}'
  else
    shasum -a 1 "$p" | awk '{print tolower($1)}'
  fi
}

semver_gt() {
  local a="$1"
  local b="$2"
  [[ "$(printf '%s\n%s\n' "$a" "$b" | sort -V | tail -n1)" == "$a" && "$a" != "$b" ]]
}

check_internet() {
  if command -v ping >/dev/null 2>&1; then
    ping -c 1 -W 2 8.8.8.8 >/dev/null 2>&1 && return 0
    ping -c 1 -W 2 1.1.1.1 >/dev/null 2>&1 && return 0
  fi
  curl -fsSI --connect-timeout 3 --max-time 5 "https://www.google.com/generate_204" >/dev/null 2>&1
}

wait_for_internet() {
  log "Verifica connessione internet..."
  until check_internet; do
    err "Non sei connesso a internet. Attendo la rete..."
    sleep 2
  done
  log "Connessione internet rilevata."
}

check_launcher_update() {
  log "Controllo aggiornamenti launcher..."
  local rel_json
  if ! rel_json="$(api_get "https://api.github.com/repos/${LAUNCHER_REPO_OWNER}/${LAUNCHER_REPO_NAME}/releases")"; then
    warn "Impossibile controllare aggiornamenti launcher."
    return 0
  fi

  local tag
  tag="$(jq -r '.[0].tag_name // empty' <<<"${rel_json}")"
  if [[ -z "${tag}" || "${tag}" == "null" ]]; then
    return 0
  fi
  LATEST_RELEASE_VERSION="${tag#v}"

  if semver_gt "${LATEST_RELEASE_VERSION}" "${APP_VERSION}"; then
    warn "Nuova versione launcher disponibile: ${LATEST_RELEASE_VERSION} (attuale: ${APP_VERSION})."
    warn "Aggiornamento auto-binario VB non applicabile in Bash. Aggiorna il repository e rilancia lo script."
  else
    log "Launcher aggiornato (${APP_VERSION})."
  fi
}

get_remote_modpack_commit() {
  api_get "https://api.github.com/repos/${MODPACK_REPO_OWNER}/${MODPACK_REPO_NAME}/commits/${CFG_CURRENT_BRANCH}" | jq -r '.sha // empty'
}

get_saved_modpack_commit() {
  [[ -f "${COMMIT_FILE}" ]] && cat "${COMMIT_FILE}" || true
}

save_modpack_commit() {
  echo -n "$1" > "${COMMIT_FILE}"
}

hash_cache_get() {
  local key="$1"
  safe_mkdir "$(dirname "${HASH_CACHE_PATH}")"
  [[ -f "${HASH_CACHE_PATH}" ]] || echo '{}' > "${HASH_CACHE_PATH}"
  jq -r --arg k "$key" '.[$k] // empty' "${HASH_CACHE_PATH}" 2>/dev/null || echo ""
}

hash_cache_set() {
  local key="$1"
  local val="$2"
  safe_mkdir "$(dirname "${HASH_CACHE_PATH}")"
  [[ -f "${HASH_CACHE_PATH}" ]] || echo '{}' > "${HASH_CACHE_PATH}"

  local tmp="${HASH_CACHE_PATH}.tmp.$$.$RANDOM"
  if jq --arg k "$key" --arg v "$val" '.[$k] = $v' "${HASH_CACHE_PATH}" > "${tmp}" 2>/dev/null; then
    mv -f "${tmp}" "${HASH_CACHE_PATH}" 2>/dev/null || true
  else
    rm -f "${tmp}" || true
    echo '{}' > "${HASH_CACHE_PATH}"
  fi
}

verify_file_with_cache() {
  local path="$1"
  local expected_size="$2"
  local expected_hash="$3"

  [[ -f "${path}" ]] || return 1

  local actual_size
  actual_size="$(file_size "${path}")"
  [[ "${actual_size}" == "${expected_size}" ]] || return 1

  local key="${path}_${expected_size}"
  local cached
  cached="$(hash_cache_get "${key}")"
  local cached_lc expected_lc
  cached_lc="$(lower_str "${cached}")"
  expected_lc="$(lower_str "${expected_hash}")"
  if [[ -n "${cached}" && "${cached_lc}" == "${expected_lc}" ]]; then
    return 0
  fi

  local actual_hash
  actual_hash="$(sha256_file "${path}")"
  hash_cache_set "${key}" "${actual_hash}"
  [[ "$(lower_str "${actual_hash}")" == "${expected_lc}" ]]
}

should_skip_manifest_file() {
  local p="$1"
  [[ "$(lower_str "${p}")" == *"[server]"* ]]
}

remove_all_non_manifest_files() {
  local exclude_minecraft="${1:-true}"
  [[ -f "${MANIFEST_PATH}" ]] || {
    err "Manifest assente per pulizia."
    return 1
  }

  log "Pulizia file obsoleti..."
  local valid_list
  if ! valid_list="$(mktemp "${MINECRAFT_DIR}/valid_files.XXXXXX" 2>/dev/null)"; then
    if ! valid_list="$(mktemp "${DOWNLOAD_DIR}/valid_files.XXXXXX" 2>/dev/null)"; then
      valid_list="$(mktemp "/tmp/gdc-valid-files.XXXXXX")" || return 1
    fi
  fi
  : > "${valid_list}"

  jq -r '.files[]?.path' "${MANIFEST_PATH}" | while IFS= read -r rel; do
    [[ -z "${rel}" ]] && continue
    printf '%s\n' "${GAME_DIR}/${rel}" >> "${valid_list}"
  done

  jq -r '.files[]? | select(.once == true) | .path' "${MANIFEST_PATH}" | while IFS= read -r rel; do
    [[ -z "${rel}" ]] && continue
    printf '%s\n' "${GAME_DIR}/${rel}.once" >> "${valid_list}"
  done

  jq -r '.packages[]? | .extractTo as $et | .filesToExtract[]? | ($et + "/" + .)' "${MANIFEST_PATH}" | while IFS= read -r rel; do
    rel="${rel#/}"
    printf '%s\n' "${GAME_DIR}/${rel}" >> "${valid_list}"
  done

  if [[ -f "${DOWNLOAD_DIR}/.gitignore" ]]; then
    while IFS= read -r line; do
      line="${line%%#*}"
      line="${line## }"
      [[ -z "${line}" ]] && continue
      if [[ -d "${GAME_DIR}/${line}" ]]; then
        find "${GAME_DIR}/${line}" -type f >> "${valid_list}" || true
      elif [[ -f "${GAME_DIR}/${line}" ]]; then
        printf '%s\n' "${GAME_DIR}/${line}" >> "${valid_list}"
      fi
    done < "${DOWNLOAD_DIR}/.gitignore"
  fi

  if [[ "${exclude_minecraft}" == "true" ]]; then
    local p
    for p in \
      "${GAME_DIR}/assets" \
      "${GAME_DIR}/versions" \
      "${GAME_DIR}/libraries" \
      "${GAME_DIR}/config" \
      "${GAME_DIR}/mods/mcef-libraries"; do
      [[ -d "${p}" ]] && find "${p}" -type f >> "${valid_list}" || true
    done
    [[ -f "${VERSION_FILE}" ]] && printf '%s\n' "${VERSION_FILE}" >> "${valid_list}"
    [[ -f "${FABRIC_MARKER}" ]] && printf '%s\n' "${FABRIC_MARKER}" >> "${valid_list}"
  fi

  sort -u "${valid_list}" -o "${valid_list}"

  while IFS= read -r f; do
    grep -Fxq "$f" "${valid_list}" || {
      rm -f "$f" || true
      log "Rimosso: ${f#${GAME_DIR}/}"
    }
  done < <(find "${GAME_DIR}" -type f)

  find "${GAME_DIR}" -type d -empty -mindepth 1 -delete || true
  rm -f "${valid_list}" || true
}

extract_7z() {
  local archive="$1"
  local out_dir="$2"
  local archive_for_extract="${archive}"
  safe_mkdir "${out_dir}"

  # Fallback per archivi multi-volume (.7z.001, .7z.002, ...):
  # bsdtar non gestisce sempre i volumi, quindi ricompone un file .7z temporaneo.
  if [[ "${archive}" =~ \.7z\.[0-9]{3}$ ]]; then
    local first_part="${archive%.[0-9][0-9][0-9]}.001"
    if [[ -f "${first_part}" ]]; then
      local merged
      merged="$(mktemp "/tmp/gdc-merged-archive.XXXXXX.7z")" || return 1
      local part
      while IFS= read -r part; do
        cat "${part}" >> "${merged}" || {
          rm -f "${merged}" || true
          return 1
        }
      done < <(ls "${archive%.[0-9][0-9][0-9]}".[0-9][0-9][0-9] 2>/dev/null | sort -V)
      archive_for_extract="${merged}"
    fi
  fi

  if command -v 7zz >/dev/null 2>&1; then
    7zz x -y "${archive_for_extract}" "-o${out_dir}" >/dev/null
    [[ "${archive_for_extract}" != "${archive}" ]] && rm -f "${archive_for_extract}" || true
    return
  fi
  if command -v 7z >/dev/null 2>&1; then
    7z x -y "${archive_for_extract}" "-o${out_dir}" >/dev/null
    [[ "${archive_for_extract}" != "${archive}" ]] && rm -f "${archive_for_extract}" || true
    return
  fi
  if command -v 7zr >/dev/null 2>&1; then
    7zr x -y "${archive_for_extract}" "-o${out_dir}" >/dev/null
    [[ "${archive_for_extract}" != "${archive}" ]] && rm -f "${archive_for_extract}" || true
    return
  fi
  if command -v bsdtar >/dev/null 2>&1; then
    bsdtar -xf "${archive_for_extract}" -C "${out_dir}"
    [[ "${archive_for_extract}" != "${archive}" ]] && rm -f "${archive_for_extract}" || true
    return
  fi

  [[ "${archive_for_extract}" != "${archive}" ]] && rm -f "${archive_for_extract}" || true

  err "Nessun estrattore 7z disponibile (7zz/7z/7zr/bsdtar)."
  return 1
}

process_manifest_packages() {
  [[ -f "${MANIFEST_PATH}" ]] || return 0
  local has_packages
  has_packages="$(jq '.packages | length // 0' "${MANIFEST_PATH}")"
  [[ "${has_packages}" -gt 0 ]] || return 0

  log "Verifica pacchetti opzionali/extra: ${has_packages}"
  [[ -f "${PACKAGE_VERSIONS_PATH}" ]] || echo '{}' > "${PACKAGE_VERSIONS_PATH}"

  local pkg
  while IFS= read -r pkg; do
    local package_name package_action package_version required overwrite extract_to
    package_name="$(jq -r '.name // "Package"' <<<"${pkg}")"
    package_action="$(jq -r '.action // ""' <<<"${pkg}")"
    package_version="$(jq -r '.version // ""' <<<"${pkg}")"
    required="$(jq -r '.required // false' <<<"${pkg}")"
    overwrite="$(jq -r '.overwrite // false' <<<"${pkg}")"
    extract_to="$(jq -r '.extractTo // ""' <<<"${pkg}")"

    if [[ "${package_action}" != "extract" ]]; then
      log "Package non gestito (${package_action}): ${package_name}"
      continue
    fi

    local installed_version
    installed_version="$(jq -r --arg k "${package_name}" '.[$k] // ""' "${PACKAGE_VERSIONS_PATH}")"
    local effective_overwrite="${overwrite}"
    if [[ -n "${package_version}" && "${package_version}" != "${installed_version}" ]]; then
      effective_overwrite="true"
      if [[ -z "${installed_version}" ]]; then
        log "Package ${package_name}: installazione versione ${package_version}"
      else
        log "Package ${package_name}: aggiornamento ${installed_version} -> ${package_version}"
      fi
    fi

    local target_dir="${GAME_DIR}/${extract_to}"
    target_dir="${target_dir%/}"
    [[ -n "${target_dir}" ]] || target_dir="${GAME_DIR}"

    local archive_start=""

    # Gestione parts (download preciso nel medesimo ordine logico del VB)
    local part_count
    part_count="$(jq '.parts | length // 0' <<<"${pkg}")"
    if [[ "${part_count}" -gt 0 ]]; then
      local idx=0
      while [[ "${idx}" -lt "${part_count}" ]]; do
        local part_name
        part_name="$(jq -r --argjson i "${idx}" '.parts[$i]' <<<"${pkg}")"
        local rel_part
        rel_part="$(jq -r --arg pn "${part_name}" '.files[]? | select(.path | endswith($pn)) | .path' <<<"${pkg}" | head -n1)"
        if [[ -z "${rel_part}" ]]; then
          if [[ "${required}" == "true" ]]; then
            err "Parte non mappata in files: ${part_name} (${package_name})"
            return 1
          fi
          warn "Parte non mappata in files: ${part_name} (${package_name})"
          idx=$((idx + 1))
          continue
        fi

        local expected_size expected_hash local_part
        expected_size="$(jq -r --arg rp "${rel_part}" '.files[]? | select(.path == $rp) | .size // 0' <<<"${pkg}" | head -n1)"
        expected_hash="$(jq -r --arg rp "${rel_part}" '.files[]? | select(.path == $rp) | .sha256 // ""' <<<"${pkg}" | head -n1)"
        local_part="${GAME_DIR}/${rel_part}"

        if ! verify_file_with_cache "${local_part}" "${expected_size}" "${expected_hash}"; then
          log "Package ${package_name}: download parte $((idx + 1))/${part_count}"
          local rel_part_encoded
          rel_part_encoded="$(encode_url_path "${rel_part}")"
          download_file "${REPO_BASEPATH}${rel_part_encoded}" "${local_part}" || {
            if [[ "${required}" == "true" ]]; then
              return 1
            fi
            warn "Download parte package fallito: ${rel_part}"
          }
          hash_cache_set "${local_part}_${expected_size}" "${expected_hash}"
        fi

        [[ -n "${archive_start}" ]] || archive_start="${local_part}"
        idx=$((idx + 1))
      done
    fi

    if [[ -z "${archive_start}" ]]; then
      archive_start="$(jq -r '.files[]?.path' <<<"${pkg}" | while IFS= read -r p; do
        [[ -f "${GAME_DIR}/${p}" ]] && { echo "${GAME_DIR}/${p}"; break; }
      done)"
    fi

    if [[ -z "${archive_start}" || ! -f "${archive_start}" ]]; then
      if [[ "${required}" == "true" ]]; then
        err "File archive non trovato per package: ${package_name}"
        return 1
      fi
      warn "File archive non trovato per package: ${package_name}"
      continue
    fi

    # Se tutti i filesToExtract ci sono gia' e overwrite disattivo, skip.
    local files_extract_count
    files_extract_count="$(jq '.filesToExtract | length // 0' <<<"${pkg}")"
    if [[ "${effective_overwrite}" != "true" && "${files_extract_count}" -gt 0 ]]; then
      local all_ready="true"
      local rel
      while IFS= read -r rel; do
        [[ -f "${target_dir}/${rel}" ]] || { all_ready="false"; break; }
      done < <(jq -r '.filesToExtract[]?' <<<"${pkg}")
      if [[ "${all_ready}" == "true" ]]; then
        log "Package gia' pronto: ${package_name}"
        if [[ -n "${package_version}" ]]; then
          local tmpv="${PACKAGE_VERSIONS_PATH}.tmp"
          jq --arg k "${package_name}" --arg v "${package_version}" '.[$k]=$v' "${PACKAGE_VERSIONS_PATH}" > "${tmpv}" && mv -f "${tmpv}" "${PACKAGE_VERSIONS_PATH}"
        fi
        continue
      fi
    fi

    log "Estrazione package: ${package_name}"
    local safe_name
    safe_name="$(echo "${package_name}" | tr -c '[:alnum:]_.-' '_')"
    local temp_extract="${DOWNLOAD_DIR}/packages_tmp/${safe_name}"
    rm -rf "${temp_extract}" || true
    safe_mkdir "${temp_extract}"

    extract_7z "${archive_start}" "${temp_extract}" || {
      if [[ "${required}" == "true" ]]; then
        return 1
      fi
      warn "Estrazione package fallita (non obbligatorio): ${package_name}"
      continue
    }

    safe_mkdir "${target_dir}"

    if [[ "${files_extract_count}" -gt 0 ]]; then
      local rel
      while IFS= read -r rel; do
        local src="${temp_extract}/${rel}"
        local dst="${target_dir}/${rel}"
        if [[ ! -f "${src}" ]]; then
          if [[ "${required}" == "true" ]]; then
            err "File estratto mancante: ${rel} (${package_name})"
            return 1
          fi
          warn "File package non trovato: ${rel}"
          continue
        fi
        safe_mkdir "$(dirname "${dst}")"
        if [[ -f "${dst}" && "${effective_overwrite}" != "true" ]]; then
          continue
        fi
        cp -f "${src}" "${dst}"
      done < <(jq -r '.filesToExtract[]?' <<<"${pkg}")
    else
      while IFS= read -r src; do
        local rel="${src#${temp_extract}/}"
        local dst="${target_dir}/${rel}"
        safe_mkdir "$(dirname "${dst}")"
        if [[ -f "${dst}" && "${effective_overwrite}" != "true" ]]; then
          continue
        fi
        cp -f "${src}" "${dst}"
      done < <(find "${temp_extract}" -type f)
    fi

    rm -rf "${temp_extract}" || true

    if [[ -n "${package_version}" ]]; then
      local tmpv="${PACKAGE_VERSIONS_PATH}.tmp"
      jq --arg k "${package_name}" --arg v "${package_version}" '.[$k]=$v' "${PACKAGE_VERSIONS_PATH}" > "${tmpv}" && mv -f "${tmpv}" "${PACKAGE_VERSIONS_PATH}"
    fi

    log "Package estratto: ${package_name}"
  done < <(jq -c '.packages[]?' "${MANIFEST_PATH}")
}

check_disk_space() {
  local available_kb
  available_kb="$(df -Pk "${MINECRAFT_DIR}" | awk 'NR==2{print $4}')"
  local min_kb=$((1024 * 1024))
  if [[ "${available_kb}" -lt "${min_kb}" ]]; then
    err "Spazio disco insufficiente. Necessario almeno 1 GB libero."
    return 1
  fi
}

step1_sync() {
  local step1only="${1:-false}"
  local force_sync="${2:-false}"

  check_disk_space || return 1
  set_repo_urls

  rm -f "${DOWNLOAD_DIR}/modpack.zip" || true

  log "Recupero manifest GDC..."
  download_file "${REPO_BASEPATH}manifest.json" "${MANIFEST_PATH}" || {
    err "Download manifest fallito."
    return 1
  }

  local remote_commit local_commit
  remote_commit="$(get_remote_modpack_commit || true)"
  local_commit="$(get_saved_modpack_commit || true)"

  if [[ -n "${remote_commit}" && "${force_sync}" != "true" && "${remote_commit}" == "${local_commit}" ]]; then
    log "Sync saltato (commit invariato)."
    if [[ "${step1only}" != "true" ]]; then
      check_java_status || { err "Java runtime non valido."; return 1; }
      step2_install
    fi
    return 0
  fi

  log "Analisi file necessari..."
  local list_all="${TMP_DIR}/manifest_files_all.tsv"
  local list_todo="${TMP_DIR}/manifest_files_todo.tsv"
  : > "${list_all}"
  : > "${list_todo}"

  jq -r '.files[] | [.path, (.size|tostring), .sha256, ((.once // false)|tostring)] | @tsv' "${MANIFEST_PATH}" > "${list_all}"

  local total_count=0
  local todo_count=0
  local total_bytes=0

  while IFS=$'\t' read -r rel size sha once; do
    [[ -z "${rel}" ]] && continue
    if should_skip_manifest_file "${rel}"; then
      continue
    fi
    total_count=$((total_count + 1))
    total_bytes=$((total_bytes + size))

    local fp="${GAME_DIR}/${rel}"
    local target_fp="${fp}"

    if [[ "${once}" == "true" ]]; then
      target_fp="${fp}.once"
      if [[ -f "${target_fp}" && ! -f "${fp}" ]]; then
        safe_mkdir "$(dirname "${fp}")"
        cp -f "${target_fp}" "${fp}" || true
      fi
    fi

    if ! verify_file_with_cache "${target_fp}" "${size}" "${sha}"; then
      printf '%s\t%s\t%s\t%s\n' "${rel}" "${size}" "${sha}" "${once}" >> "${list_todo}"
      todo_count=$((todo_count + 1))
    fi
  done < "${list_all}"

  log "File da scaricare: ${todo_count}/${total_count}"

  if [[ "${todo_count}" -gt 0 ]]; then
    log "Download file modpack in corso (parallelo: ${DOWNLOAD_CONCURRENCY})..."
    download_modpack_files_parallel "${list_todo}" "${todo_count}" "${total_bytes}" || return 1
    # Aggiorna cache hash in modo sequenziale per evitare race condition da piu' processi.
    refresh_hash_cache_from_todo "${list_todo}"
  fi

  remove_all_non_manifest_files true || true
  process_manifest_packages || return 1

  [[ -n "${remote_commit}" ]] && save_modpack_commit "${remote_commit}"

  log "Download modpack completato."

  if [[ "${step1only}" != "true" ]]; then
    check_java_status || { err "Java runtime non valido."; return 1; }
    step2_install
  fi
}

java_major_version() {
  local java_bin="$1"
  local vline
  vline="$(${java_bin} -version 2>&1 | head -n1)"
  # Gestisce sia formato classico 1.8.x che moderno 17.x/21.x
  local version_str
  version_str="$(echo "${vline}" | sed -E 's/.*"([^"]+)".*/\1/')"
  if [[ "${version_str}" == 1.* ]]; then
    echo "${version_str}" | awk -F. '{print $2}'
  else
    echo "${version_str}" | awk -F. '{print $1}'
  fi
}

find_java_path() {
  local cand

  # Priorita' al runtime locale del launcher
  while IFS= read -r cand; do
    [[ -x "${cand}" ]] && { echo "${cand}"; return 0; }
  done < <(find "${GAME_DIR}/java" -type f -name java 2>/dev/null)

  if command -v java >/dev/null 2>&1; then
    cand="$(command -v java)"
    if [[ -x "${cand}" ]]; then
      echo "${cand}"
      return 0
    fi
  fi

  return 1
}

download_and_install_java() {
  local url="https://api.adoptium.net/v3/binary/latest/17/ga/${OS_NAME}/${CPU_ARCH}/jre/hotspot/normal/eclipse"
  local archive="${DOWNLOAD_DIR}/java-runtime.tar.gz"
  local out_dir="${GAME_DIR}/java"

  log "Java non trovato. Download runtime Java 17..."
  download_file "${url}" "${archive}" || return 1

  rm -rf "${out_dir}" || true
  safe_mkdir "${out_dir}"
  tar -xzf "${archive}" -C "${out_dir}" || return 1

  find_java_path >/dev/null 2>&1
}

check_java_status() {
  local java_bin
  if ! java_bin="$(find_java_path)"; then
    download_and_install_java || return 1
    java_bin="$(find_java_path)" || return 1
  fi

  local major
  major="$(java_major_version "${java_bin}")"
  if [[ -z "${major}" || "${major}" -lt 17 ]]; then
    warn "Java trovato ma versione non adeguata (${major}). Provo auto-install Java 17."
    download_and_install_java || return 1
    java_bin="$(find_java_path)" || return 1
  fi

  log "Java in uso: ${java_bin}"
  return 0
}

cleanup_forge_installation() {
  local forge_dir="${GAME_DIR}/versions/1.20.1-forge-47.3.33"
  [[ -d "${forge_dir}" ]] && rm -rf "${forge_dir}"
  rm -f "${GAME_DIR}/forgeInstalled" "${GAME_DIR}/forgeDownloaded" || true
  rm -f "${GAME_DIR}/forge-installer.jar" "${GAME_DIR}/install_forge.bat" || true
  log "Pulizia Forge completata."
}

is_forge_installed() {
  [[ -d "${GAME_DIR}/versions/1.20.1-forge-47.3.33" || -f "${GAME_DIR}/forgeInstalled" ]]
}

is_fabric_installed() {
  local version_id="fabric-loader-${CFG_FABRIC_LOADER_VERSION}-${CFG_MC_VERSION}"
  [[ -f "${GAME_DIR}/versions/${version_id}/${version_id}.json" ]]
}

maven_url_from_name() {
  local base="$1"
  local name="$2"
  IFS=':' read -r group artifact ver classifier <<<"${name}"
  [[ -n "${group}" && -n "${artifact}" && -n "${ver}" ]] || return 1
  local group_path
  group_path="$(printf '%s' "${group}" | tr '.' '/')"
  local suffix=""
  [[ -n "${classifier:-}" ]] && suffix="-${classifier}"
  echo "${base}/${group_path}/${artifact}/${ver}/${artifact}-${ver}${suffix}.jar"
}

library_local_path_from_name() {
  local name="$1"
  IFS=':' read -r group artifact ver classifier <<<"${name}"
  local group_path
  group_path="$(printf '%s' "${group}" | tr '.' '/')"
  local suffix=""
  [[ -n "${classifier:-}" ]] && suffix="-${classifier}"
  echo "${GAME_DIR}/libraries/${group_path}/${artifact}/${ver}/${artifact}-${ver}${suffix}.jar"
}

install_fabric() {
  local loader_ver="$1"
  local mc_ver="$2"
  local version_id="fabric-loader-${loader_ver}-${mc_ver}"
  local profile_url="https://meta.fabricmc.net/v2/versions/loader/${mc_ver}/${loader_ver}/profile/json"
  local version_dir="${GAME_DIR}/versions/${version_id}"
  local version_json="${version_dir}/${version_id}.json"

  log "Download profilo Fabric..."
  local profile
  profile="$(api_get "${profile_url}")" || return 1

  safe_mkdir "${version_dir}"
  printf '%s\n' "${profile}" > "${version_json}"

  local total
  total="$(jq '.libraries | length // 0' "${version_json}")"
  log "Download librerie Fabric (${total})..."

  local idx=0
  while IFS= read -r lib; do
    idx=$((idx + 1))
    local name url local_path
    name="$(jq -r '.name // empty' <<<"${lib}")"
    [[ -n "${name}" ]] || continue

    local_path="$(library_local_path_from_name "${name}")"
    if [[ -f "${local_path}" && "$(file_size "${local_path}")" -ge 100 ]]; then
      continue
    fi

    safe_mkdir "$(dirname "${local_path}")"
    local urls=()
    url="$(jq -r '.url // empty' <<<"${lib}")"
    if [[ -n "${url}" ]]; then
      urls+=("$(maven_url_from_name "${url%/}" "${name}")")
    fi
    urls+=("$(maven_url_from_name "https://maven.fabricmc.net" "${name}")")
    urls+=("$(maven_url_from_name "https://repo1.maven.org/maven2" "${name}")")

    local ok="false"
    local u
    for u in "${urls[@]}"; do
      [[ -n "${u}" ]] || continue
      if download_file "${u}" "${local_path}"; then
        ok="true"
        break
      fi
    done

    if [[ "${ok}" != "true" ]]; then
      err "Libreria Fabric mancante: ${name}"
      return 1
    fi

    if (( idx % 20 == 0 )); then
      log "Fabric libraries progress: ${idx}/${total}"
    fi
  done < <(jq -c '.libraries[]?' "${version_json}")

  return 0
}

step2_install() {
  if is_forge_installed; then
    log "Migrazione da Forge a Fabric in corso..."
    cleanup_forge_installation
  fi

  if [[ -f "${FABRIC_MARKER}" ]] && is_fabric_installed; then
    local mf_loader mf_mc
    mf_loader="$(jq -r '.fabricLoaderVersion // empty' "${MANIFEST_PATH}")"
    mf_mc="$(jq -r '.mcVersion // empty' "${MANIFEST_PATH}")"
    if [[ "${CFG_FABRIC_LOADER_VERSION}" == "${mf_loader}" && "${CFG_MC_VERSION}" == "${mf_mc}" ]]; then
      log "Fabric Loader gia' installato: ${CFG_FABRIC_LOADER_VERSION}"
      step3_install
      return
    fi
  fi

  CFG_FABRIC_LOADER_VERSION="$(jq -r '.fabricLoaderVersion // empty' "${MANIFEST_PATH}")"
  CFG_MC_VERSION="$(jq -r '.mcVersion // empty' "${MANIFEST_PATH}")"
  CFG_FABRIC_VERSION_ID="fabric-loader-${CFG_FABRIC_LOADER_VERSION}-${CFG_MC_VERSION}"
  save_settings

  rm -f "${FABRIC_MARKER}" || true

  log "Installazione Fabric Loader..."
  install_fabric "${CFG_FABRIC_LOADER_VERSION}" "${CFG_MC_VERSION}" || {
    err "Errore durante installazione Fabric."
    return 1
  }

  log "Fabric Loader installato."
  step3_install
}

current_version_json_for_mc() {
  local version="$1"
  local manifest_url="https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"
  local manifest_json
  manifest_json="$(api_get "${manifest_url}")" || return 1
  jq -r --arg v "${version}" '.versions[] | select(.id == $v) | .url' <<<"${manifest_json}" | head -n1
}

is_library_allowed() {
  local lib_json="$1"
  local rules_count
  rules_count="$(jq '.rules | length // 0' <<<"${lib_json}")"
  if [[ "${rules_count}" -eq 0 ]]; then
    return 0
  fi

  local allowed="false"
  local rule
  while IFS= read -r rule; do
    local action osname
    action="$(jq -r '.action // "allow"' <<<"${rule}")"
    osname="$(jq -r '.os.name // empty' <<<"${rule}")"

    if [[ -z "${osname}" ]]; then
      if [[ "${action}" == "allow" ]]; then
        allowed="true"
      elif [[ "${action}" == "disallow" ]]; then
        allowed="false"
      fi
      continue
    fi

    if [[ "${osname}" == "${OS_RULE_NAME}" ]]; then
      if [[ "${action}" == "allow" ]]; then
        allowed="true"
      elif [[ "${action}" == "disallow" ]]; then
        allowed="false"
      fi
    fi
  done < <(jq -c '.rules[]?' <<<"${lib_json}")

  [[ "${allowed}" == "true" ]]
}

download_library_from_object() {
  local lib_json="$1"
  is_library_allowed "${lib_json}" || return 0

  local artifact_url artifact_path name local_path
  artifact_url="$(jq -r '.downloads.artifact.url // empty' <<<"${lib_json}")"
  artifact_path="$(jq -r '.downloads.artifact.path // empty' <<<"${lib_json}")"
  name="$(jq -r '.name // empty' <<<"${lib_json}")"

  if [[ -n "${artifact_path}" ]]; then
    local_path="${GAME_DIR}/libraries/${artifact_path}"
  elif [[ -n "${name}" ]]; then
    local_path="$(library_local_path_from_name "${name}")"
  else
    return 0
  fi

  if [[ -f "${local_path}" && "$(file_size "${local_path}")" -ge 100 ]]; then
    return 0
  fi

  safe_mkdir "$(dirname "${local_path}")"

  if [[ -n "${artifact_url}" ]]; then
    download_file "${artifact_url}" "${local_path}" && return 0
  fi

  if [[ -n "${name}" ]]; then
    local u
    for u in \
      "$(maven_url_from_name "https://libraries.minecraft.net" "${name}")" \
      "$(maven_url_from_name "https://repo1.maven.org/maven2" "${name}")"; do
      [[ -n "${u}" ]] || continue
      if download_file "${u}" "${local_path}"; then
        return 0
      fi
    done
  fi

  return 1
}

download_minecraft_version() {
  local version="$1"
  local version_url version_json_path version_jar_path

  version_url="$(current_version_json_for_mc "${version}")"
  [[ -n "${version_url}" ]] || {
    err "Versione Minecraft non trovata: ${version}"
    return 1
  }

  local version_json
  version_json="$(api_get "${version_url}")" || return 1

  local version_dir="${GAME_DIR}/versions/${version}"
  version_json_path="${version_dir}/${version}.json"
  version_jar_path="${version_dir}/${version}.jar"
  safe_mkdir "${version_dir}"
  printf '%s\n' "${version_json}" > "${version_json_path}"

  local client_url client_sha1 client_size
  client_url="$(jq -r '.downloads.client.url // empty' <<<"${version_json}")"
  client_sha1="$(jq -r '.downloads.client.sha1 // empty' <<<"${version_json}")"
  client_size="$(jq -r '.downloads.client.size // 0' <<<"${version_json}")"

  local need_client="true"
  if [[ -f "${version_jar_path}" ]]; then
    if [[ "$(file_size "${version_jar_path}")" == "${client_size}" ]]; then
      if [[ "$(sha1_file "${version_jar_path}")" == "${client_sha1}" ]]; then
        need_client="false"
      fi
    fi
  fi
  if [[ "${need_client}" == "true" ]]; then
    log "Download client.jar ${version}..."
    download_file "${client_url}" "${version_jar_path}" || return 1
  fi

  local asset_index_id asset_index_url asset_index_path
  asset_index_id="$(jq -r '.assetIndex.id // empty' <<<"${version_json}")"
  asset_index_url="$(jq -r '.assetIndex.url // empty' <<<"${version_json}")"
  asset_index_path="${GAME_DIR}/assets/indexes/${asset_index_id}.json"

  safe_mkdir "${GAME_DIR}/assets/indexes"
  if [[ ! -f "${asset_index_path}" ]]; then
    log "Download asset index ${asset_index_id}..."
    download_file "${asset_index_url}" "${asset_index_path}" || return 1
  fi

  local missing_assets=0
  while IFS=$'\t' read -r hash _size; do
    [[ -z "${hash}" ]] && continue
    local obj_path="${GAME_DIR}/assets/objects/${hash:0:2}/${hash}"
    if [[ ! -f "${obj_path}" ]]; then
      missing_assets=$((missing_assets + 1))
      safe_mkdir "$(dirname "${obj_path}")"
      download_file "https://resources.download.minecraft.net/${hash:0:2}/${hash}" "${obj_path}" || return 1
      if (( missing_assets % 500 == 0 )); then
        log "Assets scaricati: ${missing_assets}"
      fi
    fi
  done < <(jq -r '.objects[] | [.hash, (.size|tostring)] | @tsv' "${asset_index_path}")

  local libs_total
  libs_total="$(jq '.libraries | length // 0' "${version_json_path}")"
  log "Verifica librerie vanilla (${libs_total})..."
  local idx=0
  while IFS= read -r lib; do
    idx=$((idx + 1))
    download_library_from_object "${lib}" || {
      warn "Libreria vanilla mancante (tentativo fallito)."
    }
    if (( idx % 100 == 0 )); then
      log "Librerie vanilla progress: ${idx}/${libs_total}"
    fi
  done < <(jq -c '.libraries[]?' "${version_json_path}")

  return 0
}

download_version_dependencies() {
  local version_id="$1"
  local version_json_path="${GAME_DIR}/versions/${version_id}/${version_id}.json"
  [[ -f "${version_json_path}" ]] || {
    err "Version JSON mancante per dipendenze: ${version_id}"
    return 1
  }

  local total
  total="$(jq '.libraries | length // 0' "${version_json_path}")"
  log "Verifica dipendenze ${version_id} (${total})..."

  local i=0
  while IFS= read -r lib; do
    i=$((i + 1))
    download_library_from_object "${lib}" || {
      warn "Libreria dipendenza non scaricata correttamente."
    }
  done < <(jq -c '.libraries[]?' "${version_json_path}")
}

is_jar_valid() {
  local jar="$1"
  unzip -tq "${jar}" >/dev/null 2>&1
}

verify_and_fix_corrupted_jars() {
  log "Verifica integrita' JAR..."
  local broken=0
  while IFS= read -r jar; do
    if ! is_jar_valid "${jar}"; then
      rm -f "${jar}" || true
      broken=$((broken + 1))
      warn "Rimosso jar corrotto: ${jar#${GAME_DIR}/}"
    fi
  done < <(find "${GAME_DIR}/mods" "${GAME_DIR}/libraries" -type f -name '*.jar' 2>/dev/null)

  log "JAR corrotti rimossi: ${broken}"
}

step3_install() {
  if [[ ! -f "${FABRIC_MARKER}" ]]; then
    log "Download Minecraft..."
    download_minecraft_version "${CFG_MC_VERSION}" || return 1

    check_java_status || return 1

    echo -n "${LATEST_RELEASE_VERSION}" > "${VERSION_FILE}"
    echo -n "True" > "${FABRIC_MARKER}"
    log "Installazione Fabric completata."
  fi

  step4_finalize
}

step4_finalize() {
  log "Finalizzazione..."
  verify_and_fix_corrupted_jars
  download_minecraft_version "${CFG_MC_VERSION}" || return 1
  download_version_dependencies "${CFG_FABRIC_VERSION_ID}" || return 1
  log "Launcher pronto."
}

build_classpath() {
  local version_json_path="$1"
  local parent_json_path="$2"

  local cp_file="${TMP_DIR}/classpath.txt"
  : > "${cp_file}"

  local add_libs_from_json
  add_libs_from_json() {
    local p="$1"
    [[ -f "${p}" ]] || return 0
    while IFS= read -r lib; do
      is_library_allowed "${lib}" || continue
      local artifact_path name lib_path
      artifact_path="$(jq -r '.downloads.artifact.path // empty' <<<"${lib}")"
      name="$(jq -r '.name // empty' <<<"${lib}")"
      if [[ -n "${artifact_path}" ]]; then
        lib_path="${GAME_DIR}/libraries/${artifact_path}"
      elif [[ -n "${name}" ]]; then
        lib_path="$(library_local_path_from_name "${name}")"
      else
        continue
      fi
      [[ -f "${lib_path}" ]] && echo "${lib_path}" >> "${cp_file}"
    done < <(jq -c '.libraries[]?' "${p}")
  }

  add_libs_from_json "${version_json_path}"
  add_libs_from_json "${parent_json_path}"

  local parent_ver
  parent_ver="$(jq -r '.inheritsFrom // empty' "${version_json_path}")"
  [[ -z "${parent_ver}" ]] && parent_ver="${CFG_MC_VERSION}"
  local client_jar="${GAME_DIR}/versions/${parent_ver}/${parent_ver}.jar"
  [[ -f "${client_jar}" ]] && echo "${client_jar}" >> "${cp_file}"

  awk '!seen[$0]++' "${cp_file}" | paste -sd "${PATH_SEP}" -
}

apply_reduced_graphics_options() {
  local options_file="${GAME_DIR}/options.txt"
  safe_mkdir "$(dirname "${options_file}")"
  touch "${options_file}"

  update_option_line() {
    local key="$1"
    local value="$2"
    if grep -qE "^${key}:" "${options_file}"; then
      sed -i.bak "s#^${key}:.*#${key}:${value}#" "${options_file}"
    else
      echo "${key}:${value}" >> "${options_file}"
    fi
  }

  update_option_line "graphicsMode" "0"
  update_option_line "renderDistance" "5"
  update_option_line "enableVsync" "false"
  update_option_line "entityShadows" "false"
  update_option_line "simulationDistance" "5"
  update_option_line "ao" "false"
  update_option_line "particles" "0"
  update_option_line "renderClouds" "false"
  update_option_line "maxFps" "60"
  update_option_line "mipmapLevels" "1"
}

launch_minecraft() {
  local reduced_mode="${1:-false}"

  [[ -n "${CFG_USERNAME}" && "${#CFG_USERNAME}" -ge 3 ]] || {
    err "Username non valido. Impostalo nelle impostazioni (min 3 caratteri)."
    return 1
  }

  step1_sync true false || return 1

  log "Verifica Minecraft vanilla..."
  download_minecraft_version "${CFG_MC_VERSION}" || return 1

  log "Verifica librerie Fabric..."
  download_version_dependencies "${CFG_FABRIC_VERSION_ID}" || return 1

  local java_bin
  java_bin="$(find_java_path)" || {
    err "Java non trovato."
    return 1
  }

  if [[ "${reduced_mode}" == "true" ]]; then
    log "Applicazione modalita' grafica ridotta..."
    apply_reduced_graphics_options
    # Rimuove mod [client] eccetto drippyloadingscreen
    while IFS= read -r mod; do
      local lower
      lower="$(lower_str "${mod}")"
      if [[ "${lower}" == *"[client]"* && "${lower}" != *"drippyloadingscreen"* ]]; then
        rm -f "${mod}" || true
      fi
    done < <(find "${GAME_DIR}/mods" -type f -name '*.jar' 2>/dev/null)
  fi

  local version_id="${CFG_FABRIC_VERSION_ID}"
  local version_dir="${GAME_DIR}/versions/${version_id}"
  local version_json_path="${version_dir}/${version_id}.json"
  [[ -f "${version_json_path}" ]] || {
    err "Versione Fabric mancante: ${version_id}"
    return 1
  }

  local parent_ver parent_json_path
  parent_ver="$(jq -r '.inheritsFrom // empty' "${version_json_path}")"
  [[ -z "${parent_ver}" ]] && parent_ver="${CFG_MC_VERSION}"
  parent_json_path="${GAME_DIR}/versions/${parent_ver}/${parent_ver}.json"

  local natives_path="${GAME_DIR}/natives/${CFG_MC_VERSION}"
  safe_mkdir "${natives_path}"

  local classpath
  classpath="$(build_classpath "${version_json_path}" "${parent_json_path}")"

  local main_class
  main_class="$(jq -r '.mainClass // "net.fabricmc.loader.impl.launch.knot.KnotClient"' "${version_json_path}")"

  local screen_w="1280"
  local screen_h="720"
  if command -v tput >/dev/null 2>&1; then
    local cols lines
    cols="$(tput cols 2>/dev/null || echo 160)"
    lines="$(tput lines 2>/dev/null || echo 45)"
    screen_w=$((cols * 8))
    screen_h=$((lines * 16))
  fi

  local args=()
  if [[ "${OS_NAME}" == "mac" ]]; then
    args+=("-XstartOnFirstThread")
  fi
  args+=("-Xmx${MC_RAM_MB}M" "-Xms${MC_RAM_MB}M")
  args+=("-XX:+UseG1GC" "-Dsun.rmi.dgc.server.gcInterval=2147483646" "-XX:+UnlockExperimentalVMOptions" "-XX:G1NewSizePercent=20" "-XX:G1ReservePercent=20" "-XX:MaxGCPauseMillis=50" "-XX:G1HeapRegionSize=32M")
  args+=("-Djava.library.path=${natives_path}")
  args+=("-cp" "${classpath}")
  args+=("${main_class}")

  args+=("--username" "${CFG_USERNAME}")
  args+=("--version" "${version_id}")
  args+=("--gameDir" "${GAME_DIR}")
  args+=("--assetsDir" "${GAME_DIR}/assets")
  args+=("--assetIndex" "5")
  args+=("--uuid" "00000000-0000-0000-0000-000000000000")
  args+=("--accessToken" "0")
  args+=("--userType" "legacy")
  args+=("--width" "${screen_w}")
  args+=("--height" "${screen_h}")

  printf '"%s" ' "${java_bin}" > "${GAME_DIR}/start.sh"
  printf '%q ' "${args[@]}" >> "${GAME_DIR}/start.sh"
  printf '\n' >> "${GAME_DIR}/start.sh"
  chmod +x "${GAME_DIR}/start.sh"

  log "Avvio Minecraft con Fabric..."
  (cd "${GAME_DIR}" && "${java_bin}" "${args[@]}" > "${GAME_DIR}/logs/latest.log" 2>&1) &
  MC_PID="$!"
  log "Minecraft avviato. PID: ${MC_PID}"

  sleep 2
  monitor_process_non_blocking
}

monitor_process_non_blocking() {
  if [[ -n "${MC_PID}" ]]; then
    if kill -0 "${MC_PID}" >/dev/null 2>&1; then
      log "Stato gioco: IN ESECUZIONE (usa opzione Chiudi Minecraft)."
    else
      local ec=0
      wait "${MC_PID}" || ec=$?
      log "Minecraft chiuso (ExitCode=${ec})"
      if [[ "${ec}" -ne 0 ]]; then
        warn "Rilevato crash. Puoi inviare il log con l'opzione dedicata."
      fi
      MC_PID=""
    fi
  fi
}

close_minecraft() {
  if [[ -z "${MC_PID}" ]]; then
    log "Minecraft non risulta in esecuzione."
    return 0
  fi
  if kill -0 "${MC_PID}" >/dev/null 2>&1; then
    log "Chiusura Minecraft..."
    kill "${MC_PID}" || true
    sleep 1
    kill -9 "${MC_PID}" >/dev/null 2>&1 || true
  fi
  MC_PID=""
}

upload_crash_report() {
  local log_file="${GAME_DIR}/logs/latest.log"
  [[ -f "${log_file}" ]] || {
    err "Log crash non trovato: ${log_file}"
    return 1
  }

  log "Invio crash report..."
  local resp
  resp="$(curl -fsS -X PUT "https://drop.stefanodeblasi.it/upload/" \
      -H "Accept: application/json" \
      -H "Linx-Randomize: yes" \
      --data-binary "@${log_file}")" || {
    err "Upload crash report fallito."
    return 1
  }

  local url
  url="$(jq -r '.url // empty' <<<"${resp}")"
  if [[ -n "${url}" ]]; then
    echo "${url}" > "${LAST_REPORT_FILE}"
    log "Crash report URL: ${url}"
  else
    err "Risposta upload senza URL valido."
    return 1
  fi
}

verify_installation() {
  log "Verifica installazione (force sync)..."
  step1_sync false true
}

mc_reinstall() {
  if [[ -n "${MC_PID}" ]] && kill -0 "${MC_PID}" >/dev/null 2>&1; then
    err "Chiudi prima Minecraft e riprova."
    return 1
  fi
  rm -f "${VERSION_FILE}" "${FABRIC_MARKER}" || true
  boot
}

reinstall_all() {
  if [[ -n "${MC_PID}" ]] && kill -0 "${MC_PID}" >/dev/null 2>&1; then
    err "Chiudi prima Minecraft e riprova."
    return 1
  fi

  log "Reinstallazione totale in corso..."
  rm -rf "${MINECRAFT_DIR}" || true
  init_fs
  save_settings
  boot
}

change_branch() {
  log "Recupero branch da GitHub..."
  local branches_json
  branches_json="$(api_get "https://api.github.com/repos/${MODPACK_REPO_OWNER}/${MODPACK_REPO_NAME}/branches")" || {
    err "Impossibile recuperare la lista branch."
    return 1
  }

  local branches=()
  while IFS= read -r b; do
    [[ -n "${b}" ]] && branches+=("${b}")
  done < <(jq -r '.[].name' <<<"${branches_json}" | sort)

  if [[ "${#branches[@]}" -eq 0 ]]; then
    err "Nessun branch disponibile."
    return 1
  fi

  echo "Seleziona branch:"
  local i=1
  for b in "${branches[@]}"; do
    printf "%2d) %s\n" "${i}" "${b}"
    i=$((i + 1))
  done

  local sel
  read -rp "Numero branch: " sel
  if ! [[ "${sel}" =~ ^[0-9]+$ ]] || [[ "${sel}" -lt 1 || "${sel}" -gt "${#branches[@]}" ]]; then
    warn "Selezione non valida."
    return 1
  fi

  CFG_CURRENT_BRANCH="${branches[$((sel - 1))]}"
  set_repo_urls
  save_settings
  log "Branch selezionato: ${CFG_CURRENT_BRANCH}"
  boot
}

settings_menu() {
  while true; do
    echo
    echo "=== Impostazioni ==="
    echo "1) Imposta username (attuale: ${CFG_USERNAME:-<vuoto>})"
    echo "2) Cambia branch (attuale: ${CFG_CURRENT_BRANCH})"
    echo "3) Verifica installazione"
    echo "4) Reinstalla Minecraft"
    echo "5) Reinstalla tutto"
    echo "6) Torna al menu principale"
    read -rp "Scelta: " opt

    case "${opt}" in
      1)
        read -rp "Nuovo username: " u
        if [[ -z "${u}" || "${#u}" -lt 3 ]]; then
          err "Il nome utente deve essere di almeno 3 caratteri."
        else
          CFG_USERNAME="${u}"
          save_settings
          log "Username aggiornato."
        fi
        ;;
      2)
        change_branch
        ;;
      3)
        verify_installation
        ;;
      4)
        mc_reinstall
        ;;
      5)
        read -rp "Confermi reinstallazione totale? [y/N]: " yn
        if [[ "${yn}" =~ ^[Yy]$ ]]; then
          reinstall_all
        fi
        ;;
      6)
        break
        ;;
      *)
        warn "Scelta non valida."
        ;;
    esac
  done
}

boot() {
  log "Avvio ${APP_NAME} v${APP_VERSION} | branch=${CFG_CURRENT_BRANCH}"
  wait_for_internet
  check_launcher_update
  step1_sync false false
}

main_menu() {
  while true; do
    monitor_process_non_blocking
    echo
    echo "=== ${APP_NAME} ==="
    if [[ -n "${MC_PID}" ]] && kill -0 "${MC_PID}" >/dev/null 2>&1; then
      echo "1) CHIUDI Minecraft"
    else
      echo "1) PLAY"
    fi
    echo "2) PLAY (grafica ridotta)"
    echo "3) Impostazioni"
    echo "4) Invia crash report"
    echo "5) Verifica installazione (force sync)"
    echo "6) Esci"
    read -rp "Scelta: " choice

    case "${choice}" in
      1)
        if [[ -n "${MC_PID}" ]] && kill -0 "${MC_PID}" >/dev/null 2>&1; then
          close_minecraft
        else
          launch_minecraft false
        fi
        ;;
      2)
        launch_minecraft true
        ;;
      3)
        settings_menu
        ;;
      4)
        upload_crash_report
        ;;
      5)
        verify_installation
        ;;
      6)
        close_minecraft
        break
        ;;
      *)
        warn "Scelta non valida."
        ;;
    esac
  done
}

preflight() {
  require_cmd curl || return 1
  require_cmd jq || return 1
  require_cmd unzip || return 1
  if ! command -v sha256sum >/dev/null 2>&1 && ! command -v shasum >/dev/null 2>&1; then
    err "Serve sha256sum o shasum."
    return 1
  fi
}

main() {
  init_fs
  ensure_tmp_dir || exit 1
  load_settings
  set_repo_urls

  preflight || exit 1

  boot || {
    err "Bootstrap fallito."
    exit 1
  }

  main_menu
}

main "$@"
