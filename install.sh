#!/usr/bin/env bash
set -euo pipefail

# NetValve™ Installer & Setup
# Usage: curl -fsSL https://netvalve.hvh.gg/install | sudo bash
# Or with version: NETVALVE_VERSION=1.0.0 curl -fsSL https://netvalve.hvh.gg/install | sudo bash
# Or from a local archive in the current directory:
# NETVALVE_VERSION=1.0.0 sudo ./install.sh --local

VERSION="${NETVALVE_VERSION:-latest}"
REPO="${NETVALVE_REPO:-HvH-gg/NetValve}"
INSTALL_DIR="/usr/local/bin/netvalve"
USE_LOCAL_ARCHIVE=0
LOCAL_ARCHIVE_DIR="$(pwd -P)"

PACKAGE_MANAGER=""
MICROSOFT_DISTRO=""
MICROSOFT_VERSION=""
MICROSOFT_REPO_KIND=""
RELEASE_TAG=""
DOWNLOAD_URL=""
DISPLAY_VERSION="debug"
RELEASE_TAG_CANDIDATES=()
TRIED_DOWNLOAD_URLS=()

print_usage() {
  echo "Usage: install.sh [--local] [--help]"
  echo ""
  echo "Options:"
  echo "  --local   Install from ./netvalve-\${NETVALVE_VERSION}-linux-x64.tar.gz"
  echo "            in the current directory instead of downloading from GitHub."
  echo "  --help    Show this help message."
}

parse_args() {
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --local)
        USE_LOCAL_ARCHIVE=1
        ;;
      -h|--help)
        print_usage
        exit 0
        ;;
      *)
        echo "Unknown argument: $1" >&2
        print_usage >&2
        exit 1
        ;;
    esac

    shift
  done
}

normalize_version() {
  printf '%s' "${1#v}"
}

add_release_tag_candidate() {
  local tag="$1"
  local existing_tag

  if [ -z "$tag" ]; then
    return
  fi

  for existing_tag in "${RELEASE_TAG_CANDIDATES[@]}"; do
    if [ "$existing_tag" = "$tag" ]; then
      return
    fi
  done

  RELEASE_TAG_CANDIDATES+=("$tag")
}

resolve_latest_release() {
  local latest_url
  local resolved_tag

  latest_url=$(curl -fsSL -o /dev/null -w '%{url_effective}' "https://github.com/${REPO}/releases/latest")

  case "$latest_url" in
    */releases/tag/*) ;;
    *)
      return 1
      ;;
  esac

  resolved_tag="${latest_url##*/}"
  if [ -z "$resolved_tag" ] || [ "$resolved_tag" = "latest" ]; then
    return 1
  fi

  RELEASE_TAG="$resolved_tag"
  VERSION="$(normalize_version "$resolved_tag")"
}

prepare_release_tag_candidates() {
  local requested_version="$1"
  local normalized_version

  normalized_version="$(normalize_version "$requested_version")"
  VERSION="$normalized_version"
  RELEASE_TAG_CANDIDATES=()

  add_release_tag_candidate "$requested_version"
  add_release_tag_candidate "v$normalized_version"
  add_release_tag_candidate "$normalized_version"
}

download_release_archive() {
  local output_path="$1"
  local tag
  local url

  TRIED_DOWNLOAD_URLS=()

  for tag in "${RELEASE_TAG_CANDIDATES[@]}"; do
    url="https://github.com/${REPO}/releases/download/${tag}/netvalve-${VERSION}-linux-x64.tar.gz"
    TRIED_DOWNLOAD_URLS+=("$url")

    if curl -fsSL "$url" -o "$output_path"; then
      RELEASE_TAG="$tag"
      DOWNLOAD_URL="$url"
      return 0
    fi

    rm -f "$output_path"
  done

  return 1
}

copy_local_release_archive() {
  local output_path="$1"
  local archive_path="${LOCAL_ARCHIVE_DIR}/netvalve-${VERSION}-linux-x64.tar.gz"

  if [ ! -f "$archive_path" ]; then
    return 1
  fi

  cp "$archive_path" "$output_path"
  DOWNLOAD_URL="$archive_path"
}

set_display_version() {
  if [ -n "${VERSION:-}" ] && [ "$VERSION" != "latest" ]; then
    DISPLAY_VERSION="$VERSION"
  else
    DISPLAY_VERSION="debug"
  fi
}

is_unsigned_integer() {
  case "${1:-}" in
    ''|*[!0-9]*)
      return 1
      ;;
    *)
      return 0
      ;;
  esac
}

prompt_value() {
  local prompt="$1"
  local __resultvar="$2"
  local value=""

  if [ ! -r /dev/tty ]; then
    echo "Interactive setup requires a TTY." >&2
    exit 1
  fi

  read -r -p "$prompt" value </dev/tty
  printf -v "$__resultvar" '%s' "$value"
}

detect_package_manager() {
  if command -v apt-get >/dev/null 2>&1; then
    PACKAGE_MANAGER="apt-get"
  elif command -v dnf >/dev/null 2>&1; then
    PACKAGE_MANAGER="dnf"
  elif command -v yum >/dev/null 2>&1; then
    PACKAGE_MANAGER="yum"
  elif command -v zypper >/dev/null 2>&1; then
    PACKAGE_MANAGER="zypper"
  else
    PACKAGE_MANAGER=""
    return 1
  fi
}

detect_microsoft_repo() {
  if [ ! -r /etc/os-release ]; then
    return 1
  fi

  . /etc/os-release

  MICROSOFT_DISTRO=""
  MICROSOFT_VERSION=""
  MICROSOFT_REPO_KIND=""

  case "${ID:-}" in
    ubuntu|debian)
      MICROSOFT_DISTRO="$ID"
      MICROSOFT_VERSION="${VERSION_ID:-}"
      MICROSOFT_REPO_KIND="deb"
      ;;
    almalinux|alma)
      MICROSOFT_DISTRO="alma"
      MICROSOFT_VERSION="${VERSION_ID%%.*}"
      MICROSOFT_REPO_KIND="rpm"
      ;;
    amzn|amazonlinux)
      MICROSOFT_DISTRO="amazonlinux"
      MICROSOFT_VERSION="${VERSION_ID:-}"
      MICROSOFT_REPO_KIND="rpm"
      ;;
    centos|fedora|rhel|rocky)
      MICROSOFT_DISTRO="$ID"
      MICROSOFT_VERSION="${VERSION_ID%%.*}"
      MICROSOFT_REPO_KIND="rpm"
      ;;
    opensuse-leap|opensuse)
      MICROSOFT_DISTRO="opensuse"
      MICROSOFT_VERSION="${VERSION_ID%%.*}"
      MICROSOFT_REPO_KIND="repo"
      ;;
    sles|sles_sap|sle_hpc)
      MICROSOFT_DISTRO="sles"
      MICROSOFT_VERSION="${VERSION_ID%%.*}"
      MICROSOFT_REPO_KIND="rpm"
      ;;
    *)
      return 1
      ;;
  esac

  if [ -z "$MICROSOFT_DISTRO" ] || [ -z "$MICROSOFT_VERSION" ] || [ -z "$MICROSOFT_REPO_KIND" ]; then
    return 1
  fi
}

print_runtime_install_instructions() {
  detect_package_manager || true

  if ! detect_microsoft_repo; then
    echo "  # Unsupported distro for automatic Microsoft repo instructions."
    echo "  # Check the official .NET install docs for your distro:"
    echo "  # https://learn.microsoft.com/dotnet/core/install/linux"
    return
  fi

  case "$MICROSOFT_REPO_KIND" in
    deb)
      echo "  wget https://packages.microsoft.com/config/${MICROSOFT_DISTRO}/${MICROSOFT_VERSION}/packages-microsoft-prod.deb -O packages-microsoft-prod.deb"
      echo "  sudo dpkg -i packages-microsoft-prod.deb"
      echo "  sudo apt-get update"
      echo "  sudo apt-get install -y aspnetcore-runtime-8.0"
      ;;
    rpm)
      echo "  sudo rpm -Uvh https://packages.microsoft.com/config/${MICROSOFT_DISTRO}/${MICROSOFT_VERSION}/packages-microsoft-prod.rpm"
      case "$PACKAGE_MANAGER" in
        dnf)
          echo "  sudo dnf install -y aspnetcore-runtime-8.0"
          ;;
        yum)
          echo "  sudo yum install -y aspnetcore-runtime-8.0"
          ;;
        zypper)
          echo "  sudo zypper install -y aspnetcore-runtime-8.0"
          ;;
        *)
          echo "  # Then install aspnetcore-runtime-8.0 with your rpm-compatible package manager"
          ;;
      esac
      ;;
    repo)
      echo "  sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc"
      echo "  wget https://packages.microsoft.com/config/${MICROSOFT_DISTRO}/${MICROSOFT_VERSION}/prod.repo -O microsoft-prod.repo"
      echo "  sudo mv microsoft-prod.repo /etc/zypp/repos.d/microsoft-prod.repo"
      echo "  sudo chown root:root /etc/zypp/repos.d/microsoft-prod.repo"
      echo "  sudo zypper refresh"
      echo "  sudo zypper install -y aspnetcore-runtime-8.0"
      ;;
  esac
}

echo "NetValve™ Installer"
parse_args "$@"

echo "===================="
echo ""

# Check if running as root
if [[ $EUID -ne 0 ]]; then
   echo "This script must be run as root. Try with sudo."
   exit 1
fi

# Dependency check
MISSING_DEPS=()
if [ "$USE_LOCAL_ARCHIVE" -ne 1 ]; then
  command -v curl >/dev/null 2>&1 || MISSING_DEPS+=("curl")
fi
command -v tar >/dev/null 2>&1 || MISSING_DEPS+=("tar")
command -v tc >/dev/null 2>&1 || MISSING_DEPS+=("tc")

# Check for aspnetcore runtime
if [ ! -f "/usr/share/dotnet/dotnet" ]; then
  echo "aspnetcore-runtime-8.0 is required."
  echo "Install with:"
  print_runtime_install_instructions
  exit 1
fi

if [[ ${#MISSING_DEPS[@]} -gt 0 ]]; then
  echo "Install packages providing these commands:"
  echo "  ${MISSING_DEPS[*]}"
  if detect_package_manager; then
    echo "Detected package manager: ${PACKAGE_MANAGER}"
    case "$PACKAGE_MANAGER" in
      apt-get)
        echo "Example: sudo apt-get install -y curl tar iproute2"
        ;;
      zypper)
        echo "Example: sudo zypper install -y curl tar iproute2"
        ;;
      dnf)
        echo "Example: sudo dnf install -y curl tar iproute iproute-tc"
        ;;
      yum)
        echo "Example: sudo yum install -y curl tar iproute iproute-tc"
        ;;
    esac
  fi
  exit 1
fi

# Resolve requested release
if [ "$USE_LOCAL_ARCHIVE" -eq 1 ]; then
  if [ "$VERSION" = "latest" ]; then
    echo "Local archive mode requires NETVALVE_VERSION to be set."
    echo "Example:"
    echo "  NETVALVE_VERSION=1.0.0 sudo ./install.sh --local"
    exit 1
  fi

  prepare_release_tag_candidates "$VERSION"
elif [ "$VERSION" = "latest" ]; then
  echo "Resolving latest NetValve release from GitHub..."
  if ! resolve_latest_release; then
    echo "Failed to resolve the latest GitHub release for ${REPO}."
    echo "Check GitHub access from this server or install a specific version with:"
    echo "  NETVALVE_VERSION=1.0.0 curl -fsSL https://netvalve.hvh.gg/install | sudo bash"
    exit 1
  fi

  echo "Resolved latest release: ${RELEASE_TAG} (version ${VERSION})"
  RELEASE_TAG_CANDIDATES=("$RELEASE_TAG")
else
  prepare_release_tag_candidates "$VERSION"
fi

set_display_version

if [ -f "$INSTALL_DIR/NetValve.Server.dll" ]; then
  echo "Existing NetValve installation found at $INSTALL_DIR."
  echo "Updating to NetValve v${DISPLAY_VERSION}..."
else
  echo "Installing NetValve v${DISPLAY_VERSION}..."
fi

TMP_DIR=$(mktemp -d)
cd "$TMP_DIR"

if [ "$USE_LOCAL_ARCHIVE" -eq 1 ]; then
  if ! copy_local_release_archive netvalve.tgz; then
    echo "Failed to find local NetValve release ${VERSION}."
    echo "Expected file:"
    echo "  ${LOCAL_ARCHIVE_DIR}/netvalve-${VERSION}-linux-x64.tar.gz"
    rm -rf "$TMP_DIR"
    exit 1
  fi

  echo "Using local archive: $DOWNLOAD_URL"
else
  if ! download_release_archive netvalve.tgz; then
    echo "Failed to download NetValve release ${VERSION}."
    echo "Check that the release exists and GitHub is accessible from this server."
    echo ""
    echo "Tried download URLs:"
    for tried_url in "${TRIED_DOWNLOAD_URLS[@]}"; do
      echo "  $tried_url"
    done
    rm -rf "$TMP_DIR"
    exit 1
  fi

  echo "Downloading from: $DOWNLOAD_URL"
fi

echo "Extracting NetValve..."
mkdir -p "$INSTALL_DIR"
find "$INSTALL_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
tar -xzf netvalve.tgz -C "$INSTALL_DIR"

cd /
rm -rf "$TMP_DIR"

echo "NetValve v${DISPLAY_VERSION} installed to $INSTALL_DIR"

# Interactive setup
echo ""
echo "NetValve™ Interactive Setup"
echo "============================"

DEFAULT_DEV=$(ip route get 8.8.8.8 | grep -oP 'dev \K\S+' || true)
prompt_value "Network interface [${DEFAULT_DEV:-eth0}]: " DEV
DEV=${DEV:-${DEFAULT_DEV:-eth0}}

# Configure ingest servers
echo ""
echo "How many CS2 servers will send data to this controller?"
prompt_value "> " SERVER_COUNT
if ! is_unsigned_integer "$SERVER_COUNT" || [ "$SERVER_COUNT" -lt 1 ]; then
  SERVER_COUNT=1
fi

INGEST_SERVERS_JSON=""
i=1
while [ "$i" -le "$SERVER_COUNT" ]; do
  echo ""
  echo "Configuring server $i..."
  prompt_value "Server ID (e.g., server-$i): " SERVER_ID
  SERVER_ID=${SERVER_ID:-server-$i}
  prompt_value "Auth Token (leave empty for no auth): " TOKEN
  prompt_value "Server game port for TC shaping [optional]: " SERVER_PORT

  if [ -n "$INGEST_SERVERS_JSON" ]; then
    INGEST_SERVERS_JSON="$INGEST_SERVERS_JSON,"
  fi

  INGEST_SERVERS_JSON="$INGEST_SERVERS_JSON{\"ServerId\":\"$SERVER_ID\""
  if [ -n "$TOKEN" ]; then
    INGEST_SERVERS_JSON="$INGEST_SERVERS_JSON,\"Token\":\"$TOKEN\""
  fi
  if is_unsigned_integer "$SERVER_PORT"; then
    INGEST_SERVERS_JSON="$INGEST_SERVERS_JSON,\"Port\":$SERVER_PORT"
  fi
  INGEST_SERVERS_JSON="$INGEST_SERVERS_JSON}"
  i=$((i + 1))
done

# Create config
mkdir -p /etc/netvalve
cat > /etc/netvalve/config.json <<EOF
{
  "NetworkInterface": "$DEV",
  "IfbDevice": "ifb0",
  "UpdateIntervalSeconds": 10,
  "Ingest": {
    "BindAddress": "0.0.0.0",
    "Port": 8443,
    "Servers": [$INGEST_SERVERS_JSON]
  }
}
EOF

# Install systemd service
echo "Installing systemd service..."
cp "$INSTALL_DIR/netvalve-controller.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable netvalve-controller

echo ""
echo "=========================================="
echo "NetValve v${DISPLAY_VERSION} Setup Complete!"
echo "=========================================="
echo ""
HOST_IP=$(hostname -I | awk '{print $1}')
echo "1. Install the CS2 plugin on each server:"
echo "   Download: https://github.com/HvH-gg/NetValve/releases"
echo "   Extract NetValve.Plugin-X.Y.X.zip to addons/counterstrikesharp/"
echo "   Configure: addons/counterstrikesharp/configs/plugins/NetValve/NetValve.json"
echo ""
echo "   IMPORTANT: You might have to use the host's actual IP address instead of 127.0.0.1"
echo "   Host IP: $HOST_IP"
echo ""
echo "2. Plugin config example for server 'server-1':"
echo "   {"
echo "     \"ControllerUrl\": \"ws://$HOST_IP:8443/ws\","
echo "     \"AuthToken\": \"your-token\","
echo "     \"ServerId\": \"server-1\""
echo "   }"
echo ""
echo "3. After the plugin is loaded on a CS2 server, set runtime cvars in the server console or cfg:"
echo "   netvalve_targetping 50"
echo "   netvalve_pingmode strict"
echo ""
echo "4. Start the service: sudo systemctl start netvalve-controller"
echo "5. Check status: sudo systemctl status netvalve-controller"
echo "6. View logs: sudo journalctl -u netvalve-controller -f"
echo ""
echo "To add more servers later, edit /etc/netvalve/config.json and restart:"
echo "  sudo systemctl restart netvalve-controller"
echo ""
echo "To reinstall or update, run:"
echo "  curl -fsSL https://netvalve.hvh.gg/install | sudo bash"
echo "Or from a local archive in the current directory:"
echo "  NETVALVE_VERSION=${DISPLAY_VERSION} sudo ./install.sh --local"
