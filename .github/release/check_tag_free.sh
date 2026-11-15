#!/usr/bin/env bash
# Fail-closed check that a tag name is unused: no git tag ref AND no release (published or draft) with that tag_name.
# Needs: gh (authenticated via GH_TOKEN), jq. Usage: check_tag_free.sh OWNER/REPO TAG
# exit 0 = verified free; exit 1 = exists; exit 2 = could not determine (treated as failure by the workflow)
set -u
repo=$1; tag=$2
out=$(gh api "repos/$repo/git/ref/tags/$tag" 2>&1); rc=$?
if [ $rc -eq 0 ]; then echo "tag ref $tag exists"; exit 1; fi
case "$out" in *"HTTP 404"*) ;; *) echo "cannot determine tag ref state: $out"; exit 2;; esac
rel=$(gh api --paginate "repos/$repo/releases?per_page=100" 2>&1); rc=$?
if [ $rc -ne 0 ]; then echo "cannot list releases: $rel"; exit 2; fi
names=$(printf '%s' "$rel" | jq -r '.[].tag_name' 2>&1) || { echo "cannot parse releases: $names"; exit 2; }
if printf '%s\n' "$names" | grep -Fxq -- "$tag"; then echo "a release (maybe draft) with tag $tag exists"; exit 1; fi
echo "tag $tag is free"; exit 0
