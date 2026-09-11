#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_FILE="$ROOT_DIR/demos/ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
INSTALL_NAME="${INSTALL_NAME:-gear}"
INSTALL_BASE="${INSTALL_BASE:-$HOME/.local/share/gear-cli}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
ZSHRC_FILE="${ZSHRC_FILE:-$HOME/.zshrc}"
SKIP_ZSHRC="${SKIP_ZSHRC:-0}"
RID="${RID:-}"
INSTALL_HARNESS="${INSTALL_HARNESS:-1}"
HARNESS_CONFIG_DIR="$INSTALL_BASE/config/gear"

is_python_externally_managed() {
  python3 - <<'PY'
import os
import sys
import sysconfig
stdlib = sysconfig.get_path("stdlib") or ""
marker = os.path.join(stdlib, "EXTERNALLY-MANAGED")
sys.exit(0 if os.path.exists(marker) else 1)
PY
}

install_harness_in_venv() {
  local venv_dir="$INSTALL_BASE/harness-venv"
  local cli_link="$BIN_DIR/cli-anything-gear"
  echo "Installing harness into isolated venv: $venv_dir"
  python3 -m venv "$venv_dir"
  "$venv_dir/bin/python" -m pip install -U pip
  "$venv_dir/bin/python" -m pip install -e "$ROOT_DIR/agent-harness"
  ln -sfn "$venv_dir/bin/cli-anything-gear" "$cli_link"
}

sync_harness_llm_config() {
  local src_env="$ROOT_DIR/agent-harness/cli_anything/gear/.env"
  local src_yaml="$ROOT_DIR/agent-harness/cli_anything/gear/config/llm_config.yaml"
  local dst_env="$HARNESS_CONFIG_DIR/.env"
  local dst_yaml="$HARNESS_CONFIG_DIR/llm_config.yaml"

  mkdir -p "$HARNESS_CONFIG_DIR"
  if [[ -f "$src_env" ]]; then
    cp "$src_env" "$dst_env"
    chmod 600 "$dst_env" || true
  fi
  if [[ -f "$src_yaml" ]]; then
    cp "$src_yaml" "$dst_yaml"
    chmod 600 "$dst_yaml" || true
  fi
}

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"

  case "$os" in
    Darwin) os="osx" ;;
    Linux) os="linux" ;;
    *)
      echo "Unsupported OS: $os" >&2
      exit 1
      ;;
  esac

  case "$arch" in
    arm64|aarch64) arch="arm64" ;;
    x86_64|amd64) arch="x64" ;;
    *)
      echo "Unsupported architecture: $arch" >&2
      exit 1
      ;;
  esac

  echo "${os}-${arch}"
}

if [[ ! -f "$PROJECT_FILE" ]]; then
  echo "ERROR: project file not found: $PROJECT_FILE" >&2
  exit 1
fi

if [[ -z "$RID" ]]; then
  RID="$(detect_rid)"
fi

PUBLISH_DIR="$INSTALL_BASE/$RID"
TARGET_EXE="$PUBLISH_DIR/ZL.Gear.ConsoleApp"
LINK_PATH="$BIN_DIR/$INSTALL_NAME"

echo "[1/4] Publishing ZL.Gear.ConsoleApp ($RID) ..."
dotnet publish "$PROJECT_FILE" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o "$PUBLISH_DIR"

echo "[2/4] Creating command link: $LINK_PATH"
mkdir -p "$BIN_DIR"
ln -sfn "$TARGET_EXE" "$LINK_PATH"
chmod +x "$TARGET_EXE" "$LINK_PATH"

if [[ "$SKIP_ZSHRC" != "1" ]]; then
  echo "[3/4] Updating $ZSHRC_FILE"
  mkdir -p "$(dirname "$ZSHRC_FILE")"
  touch "$ZSHRC_FILE"

  awk '
    BEGIN { skip=0 }
    /^# >>> gear-cli >>>$/ { skip=1; next }
    /^# <<< gear-cli <<</ { skip=0; next }
    /^export ZL_GEAR_CLI_BIN=/ { next }
    /^export GEAR_LLM_ENV_FILE=/ { next }
    /^export GEAR_LLM_YAML_FILE=/ { next }
    /^export PATH="\$HOME\/\.local\/bin:\$PATH"$/ { next }
    skip==0 { print }
  ' "$ZSHRC_FILE" > "${ZSHRC_FILE}.tmp"
  mv "${ZSHRC_FILE}.tmp" "$ZSHRC_FILE"

  cat >> "$ZSHRC_FILE" <<'EOS'
# >>> gear-cli >>>
export PATH="$HOME/.local/bin:$PATH"
export ZL_GEAR_CLI_BIN="gear --json"
export GEAR_LLM_ENV_FILE="$HOME/.local/share/gear-cli/config/gear/.env"
export GEAR_LLM_YAML_FILE="$HOME/.local/share/gear-cli/config/gear/llm_config.yaml"
# <<< gear-cli <<<
EOS
else
  echo "[3/4] Skipped zshrc update (SKIP_ZSHRC=1)"
fi

echo "[4/4] Installing cli-anything-gear harness"
install_harness_ok=0
if [[ "$INSTALL_HARNESS" != "1" ]]; then
  echo "Skipped harness install (INSTALL_HARNESS=$INSTALL_HARNESS)"
  install_harness_ok=1
else
  if is_python_externally_managed; then
    echo "Detected externally managed Python (PEP 668). Skipping system pip install path."
    if command -v pipx >/dev/null 2>&1; then
      echo "Trying pipx editable install..."
      if pipx install --force --editable "$ROOT_DIR/agent-harness"; then
        install_harness_ok=1
      else
        echo "pipx install failed. Falling back to isolated venv..."
        if install_harness_in_venv; then
          install_harness_ok=1
        fi
      fi
    else
      if install_harness_in_venv; then
        install_harness_ok=1
      fi
    fi
  else
    if python3 -m pip install -e "$ROOT_DIR/agent-harness"; then
      install_harness_ok=1
    else
      echo "Primary install failed. Trying user-site install..."
      if python3 -m pip install --user -e "$ROOT_DIR/agent-harness"; then
        install_harness_ok=1
      elif command -v pipx >/dev/null 2>&1; then
        echo "User-site install failed. Trying pipx editable install..."
        if pipx install --force --editable "$ROOT_DIR/agent-harness"; then
          install_harness_ok=1
        elif install_harness_in_venv; then
          install_harness_ok=1
        fi
      elif install_harness_in_venv; then
        install_harness_ok=1
      fi
    fi
  fi
fi

if [[ "$install_harness_ok" != "1" ]]; then
  echo "ERROR: cli-anything-gear harness install failed after all fallback strategies." >&2
  echo "Try manual install:"
  echo "  pipx install --editable \"$ROOT_DIR/agent-harness\""
  echo "  or"
  echo "  python3 -m venv \"$INSTALL_BASE/harness-venv\" && \"$INSTALL_BASE/harness-venv/bin/python\" -m pip install -e \"$ROOT_DIR/agent-harness\""
  exit 1
fi

echo "[5/5] Syncing cli-anything-gear LLM config"
sync_harness_llm_config

if [[ ! -x "$LINK_PATH" ]]; then
  echo "ERROR: expected executable link not found: $LINK_PATH" >&2
  exit 1
fi

if [[ "$SKIP_ZSHRC" != "1" ]]; then
  local_block_count="$(grep -c '^# >>> gear-cli >>>$' "$ZSHRC_FILE" || true)"
  if [[ "$local_block_count" != "1" ]]; then
    echo "ERROR: expected exactly one gear-cli block in $ZSHRC_FILE, found $local_block_count" >&2
    exit 1
  fi
fi

echo
echo "Install complete."
echo "Command check:"
echo "  $LINK_PATH commands --json"
if command -v cli-anything-gear >/dev/null 2>&1; then
  echo "  cli-anything-gear --json commands"
else
  echo "  cli-anything-gear is NOT installed in current PATH (gear is ready)."
fi
if [[ "$SKIP_ZSHRC" != "1" ]]; then
  echo
  echo "Run to apply current shell:"
  echo "  source \"$ZSHRC_FILE\""
fi
