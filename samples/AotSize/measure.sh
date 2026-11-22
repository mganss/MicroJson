#!/usr/bin/env bash
#
# Publishes the sample apps with Native AOT, checks that they work, and reports their sizes.
# Fails if publishing produces warnings (see Directory.Build.props), if an app prints the wrong output,
# or if a MicroJson app adds more to the size of the baseline app than its limit below.
#
# Usage: measure.sh [runtime identifier, default linux-x64]
# The size table is appended to $GITHUB_STEP_SUMMARY when running in GitHub Actions.

set -euo pipefail

rid="${1:-linux-x64}"

# maximum size in KB that each MicroJson app may add to the baseline app (measured on win-x64 with .NET 10:
# MicroJsonTyped 22 KB, MicroJsonDom 150 KB, MicroJsonDomDouble 71.5 KB; the difference between the two DOM apps
# is the cost of boxing decimal numbers)
declare -A limits=([MicroJsonTyped]=40 [MicroJsonDom]=192 [MicroJsonDomDouble]=96)

dir="$(cd "$(dirname "$0")" && pwd)"
out="$dir/out/$rid"
ext=""
[[ "$rid" == win-* ]] && ext=".exe"

input='{"Name":"Ann","Age":42,"Tags":["a"]}'
expected='{"Name":"Ann","Age":43,"Tags":["a"]}'
apps=(Baseline MicroJsonDom MicroJsonDomDouble MicroJsonTyped SystemTextJson)
declare -A sizes

for app in "${apps[@]}"; do
    echo "Publishing $app for $rid"
    dotnet publish "$dir/$app" -c Release -r "$rid" -o "$out/$app" -nologo -v q

    exe="$out/$app/$app$ext"
    actual="$("$exe" "$input" | tr -d '\r')"

    if [[ "$actual" != "$expected" ]]; then
        echo "::error::$app printed '$actual', expected '$expected'"
        exit 1
    fi

    sizes[$app]=$(wc -c < "$exe" | tr -d ' ')
done

baseline=${sizes[Baseline]}
failed=0

table="| App | Size (bytes) | Added to baseline |"$'\n'"|---|---:|---:|"
for app in "${apps[@]}"; do
    size=${sizes[$app]}
    overhead=$((size - baseline))
    table+=$'\n'"| $app | $size | $(awk "BEGIN { printf \"%.1f KB\", $overhead / 1024 }") |"

    limit=${limits[$app]:-}
    if [[ -n "$limit" && $overhead -gt $((limit * 1024)) ]]; then
        echo "::error::$app adds $overhead bytes to the baseline, more than its limit of $limit KB"
        failed=1
    fi
done

echo
echo "Native AOT size ($rid)"
echo "$table"

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    {
        echo "### Native AOT size ($rid)"
        echo
        echo "$table"
    } >> "$GITHUB_STEP_SUMMARY"
fi

exit $failed
