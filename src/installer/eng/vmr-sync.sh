#!/bin/bash

### This script is used for synchronizing the dotnet/dotnet VMR locally. This means pulling new
### code from various repositories into the 'dotnet/dotnet' repository.
###
### The script is used during CI to ingest new code based on dotnet/installer but it can also help
### for reproducing potential failures during installer's PRs, namely to fix the Source-Build.
### Another usecase is to try manually synchronizing a given commit of some repo into the VMR and
### trying to Source-Build the VMR. This can help when fixing the Source-Build but using a commit
### from a not-yet merged branch (or fork) to test the fix will help.
###
### The tooling that synchronizes the VMR will need to clone the various repositories into a temporary
### folder. These clones can be re-used in future synchronizations so it is advised you dedicate a
### folder to this to speed up your re-runs.
###
### USAGE:
###   Synchronize current installer and all dependencies into a local VMR:
###     ./vmr-sync.sh --vmr "$HOME/repos/dotnet" --tmp "$HOME/repos/tmp"
###
###   Synchronize the VMR to a specific commit of dotnet/runtime using custom fork:
###     ./vmr-sync.sh \
###        --repository runtime:e7e71da303af8dc97df99b098f21f526398c3943 \
###        --remote runtime:https://github.com/yourfork/runtime          \
###        --tmp "$HOME/repos/tmp"
###
### Options:
###   -t, --tmp, --tmp-dir PATH
###       Required. Path to the temporary folder where repositories will be cloned
###
###   -b, --branch, --vmr-branch BRANCH_NAME
###       Optional. Branch of the 'dotnet/dotnet' repo to synchronize. The VMR will be checked out to this branch
###
###   --debug
###       Optional. Turns on the most verbose logging for the VMR tooling
###
###   --readme-template
###       Optional. Template for VMRs README.md used for regenerating the file to list the newest versions of
###       components.
###       Defaults to src/VirtualMonoRepo/README.template.md
###
###   --recursive
###       Optional. Recursively synchronize all the source build dependencies (declared in Version.Details.xml)
###       This is used when performing the full synchronization during installer's CI and the final VMR sync.
###       Defaults to false unless no repository is supplied in which case a recursive sync of installer is performed.
###
###   --remote name:URI
###       Optional. Additional remote to use during the synchronization
###       This can be used to synchronize to a commit from a fork of the repository
###       Example: 'runtime:https://github.com/yourfork/runtime'
###
###   -r, --repository name:GIT_REF
###       Optional. Repository + git ref separated by colon to synchronize to.
###       This can be a specific commit, branch, tag.
###       If not supplied, the revision of the parent installer repository of this script will be used (recursively).
###       Example: 'runtime:my-branch-name'
###
###   --tpn-template
###       Optional. Template for the header of VMRs THIRD-PARTY-NOTICES file.
###       Defaults to src/VirtualMonoRepo/THIRD-PARTY-NOTICES.template.txt
###
###   -v, --vmr, --vmr-dir PATH
###       Optional. Path to the dotnet/dotnet repository. When null, gets cloned to the temporary folder

source="${BASH_SOURCE[0]}"

# resolve $source until the file is no longer a symlink
while [[ -h "$source" ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"
  # if $source was a relative symlink, we need to resolve it relative to the path where the
  # symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done
scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"

function print_help () {
    sed -n '/^### /,/^$/p' "$source" | cut -b 5-
}

COLOR_RED=$(tput setaf 1 2>/dev/null || true)
COLOR_CYAN=$(tput setaf 6 2>/dev/null || true)
COLOR_CLEAR=$(tput sgr0 2>/dev/null || true)
COLOR_RESET=uniquesearchablestring
FAILURE_PREFIX='> '

function fail () {
  echo "${COLOR_RED}$FAILURE_PREFIX${1//${COLOR_RESET}/${COLOR_RED}}${COLOR_CLEAR}" >&2
}

function highlight () {
  echo "${COLOR_CYAN}$FAILURE_PREFIX${1//${COLOR_RESET}/${COLOR_CYAN}}${COLOR_CLEAR}"
}

installer_dir=$(realpath "$scriptroot/../")

tmp_dir=''
vmr_dir=''
vmr_branch=''
repository=''
additional_remotes=''
recursive=false
verbosity=verbose
readme_template="$installer_dir/src/VirtualMonoRepo/README.template.md"
tpn_template="$installer_dir/src/VirtualMonoRepo/THIRD-PARTY-NOTICES.template.txt"

# If installer is a repo, we're in an installer and not in the dotnet/dotnet repo
if [[ -d "$installer_dir/.git" ]]; then
  additional_remotes="installer:$installer_dir"
fi

while [[ $# -gt 0 ]]; do
  opt="$(echo "$1" | tr "[:upper:]" "[:lower:]")"
  case "$opt" in
    -t|--tmp|--tmp-dir)
      tmp_dir=$2
      shift
      ;;
    -v|--vmr|--vmr-dir)
      vmr_dir=$2
      shift
      ;;
    -b|--branch|--vmr-branch)
      vmr_branch=$2
      shift
      ;;
    -r|--repository)
      repository=$2
      shift
      ;;
    --recursive)
      recursive=true
      ;;
    --remote)
      additional_remotes="$additional_remotes,$2"
      shift
      ;;
    --readme-template)
      readme_template=$2
      shift
      ;;
    --tpn-template)
      tpn_template=$2
      shift
      ;;
    -d|--debug)
      verbosity=debug
      ;;
    -h|--help)
      print_help
      exit 0
      ;;
    *)
      fail "Invalid argument: $1"
      print_help
      exit 1
      ;;
  esac

  shift
done

# Validation

if [[ ! -d "$installer_dir" ]]; then
  fail "Directory '$installer_dir' does not exist. Please specify the path to the dotnet/installer repo"
  exit 1
fi

if [[ -z "$tmp_dir" ]]; then
  fail "Missing --tmp-dir argument. Please specify the path to the temporary folder where the repositories will be cloned"
  exit 1
fi

if [[ ! -f "$readme_template" ]]; then
  fail "File '$readme_template' does not exist. Please specify a valid path to the README template"
  exit 1
fi

if [[ ! -f "$tpn_template" ]]; then
  fail "File '$tpn_template' does not exist. Please specify a valid path to the THIRD-PARTY-NOTICES template"
  exit 1
fi

# Sanitize the input

# Default when no repository is provided
if [[ -z "$repository" ]]; then
  repository="installer:$(git -C "$installer_dir" rev-parse HEAD)"
  recursive=true
fi

if [[ -z "$vmr_dir" ]]; then
  vmr_dir="$tmp_dir/dotnet"
fi

if [[ ! -d "$tmp_dir" ]]; then
  mkdir -p "$tmp_dir"
fi

if [[ "$verbosity" == "debug" ]]; then
  set -x
fi

# Prepare the VMR

if [[ ! -d "$vmr_dir" ]]; then
  highlight "Cloning 'dotnet/dotnet' into $vmr_dir.."
  git clone https://github.com/dotnet/dotnet "$vmr_dir"

  if [[ -n "$vmr_branch" ]]; then
    git -C "$vmr_dir" switch -c "$vmr_branch"
  fi
else
  if ! git -C "$vmr_dir" diff --quiet; then
    fail "There are changes in the working tree of $vmr_dir. Please commit or stash your changes"
    exit 1
  fi

  if [[ -n "$vmr_branch" ]]; then
    highlight "Preparing $vmr_dir"
    git -C "$vmr_dir" checkout "$vmr_branch"
    git -C "$vmr_dir" pull
  fi
fi

set -e

# Prepare darc

highlight 'Installing .NET, preparing the tooling..'
source "$scriptroot/common/tools.sh"
InitializeDotNetCli true
dotnet=$(realpath "$scriptroot/../.dotnet/dotnet")
"$dotnet" tool restore

highlight "Starting the synchronization of '$repository'.."
set +e

recursive_arg=''
if [[ "$recursive" == "true" ]]; then
  recursive_arg="--recursive"
fi

if [[ -n "$additional_remotes" ]]; then
  additional_remotes="--additional-remotes $additional_remotes"
fi

# Synchronize the VMR

"$dotnet" darc vmr update                    \
  --vmr "$vmr_dir"                           \
  --tmp "$tmp_dir"                           \
  --$verbosity                               \
  $recursive_arg                             \
  --readme-template "$readme_template"       \
  --tpn-template "$tpn_template"             \
  $additional_remotes                        \
  "$repository"

if [[ $? == 0 ]]; then
  highlight "Synchronization succeeded"
else
  fail "Synchronization of dotnet/dotnet to '$repository' failed!"
  fail "'$vmr_dir' is left in its last state (re-run of this script will reset it)."
  fail "Please inspect the logs which contain path to the failing patch file (use --debug to get all the details)."
  fail "Once you make changes to the conflicting VMR patch, commit it locally and re-run this script."
  exit 1
fi
