#!/usr/bin/env sh
#
# Fails if anything derived from Space Engineers 2 has been committed.
#
# Technic.md §7.1 asks for exactly this check. .gitignore already covers these patterns, but
# .gitignore is advisory — `git add -f`, a rule edited in good faith, or a tool that writes into a
# tracked directory all bypass it silently. This is the part that fails loudly.
#
# The one permitted exception is tests/fixtures/def/**, which is hand-written JSON in the real
# envelope shape with invented values, deliberately named so the ignore rules cannot swallow it.
#
# Runs on Linux, macOS and Git Bash. Exits non-zero with the offending paths listed.

set -eu

violations=$(
    git ls-files |
    while IFS= read -r file; do
        case "$file" in
            # Hand-written synthetic fixtures: the documented exception.
            tests/fixtures/def/*) continue ;;

            # The extracted config, under any name that would be mistaken for it.
            gamedata.json|*/gamedata.json) echo "$file" ;;

            # Keen's definition files and any binary VRage container.
            *.def|*.vrb) echo "$file" ;;
        esac
    done
)

if [ -n "$violations" ]; then
    echo "Game data has been committed. Remove these and see Technic.md §7.1:" >&2
    echo "$violations" | sed 's/^/  /' >&2
    exit 1
fi

echo "No Space Engineers 2 data is committed."
