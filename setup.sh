#!/usr/bin/env bash
set -euo pipefail

if [ $# -ne 1 ] || [ -z "$1" ]; then
    echo "Usage: ./setup.sh <ProjectName>"
    echo "  ProjectName must be a valid .NET project name (PascalCase, no spaces)."
    exit 1
fi

OLD="OxpeckerApi"
NEW="$1"

echo "Renaming project from '$OLD' to '$NEW'..."

# 1. Replace in file contents (source files, build files, docs — skip bin/ and obj/)
for file in $(grep -rl "$OLD" --include='*.fs' --include='*.fsproj' --include='*.slnx' --include='*.md' --include='*.json' . \
    | grep -v '/bin/' | grep -v '/obj/' | grep -v '/.git/'); do
    # Skip setup.sh itself
    if [[ "$file" == "./setup.sh" ]]; then continue; fi
    echo "  Updating $file"
    sed -i "s/$OLD/$NEW/g" "$file"
done

# 2. Rename .slnx
if [ -f "${OLD}.slnx" ]; then
    echo "  Renaming ${OLD}.slnx -> ${NEW}.slnx"
    mv "${OLD}.slnx" "${NEW}.slnx"
fi

# 3. Rename .fsproj
if [ -f "src/${OLD}/${OLD}.fsproj" ]; then
    echo "  Renaming src/${OLD}/${OLD}.fsproj -> src/${NEW}/${NEW}.fsproj"
    mv "src/${OLD}/${OLD}.fsproj" "src/${OLD}/${NEW}.fsproj"
fi

# 4. Rename project directory
if [ -d "src/${OLD}" ]; then
    echo "  Renaming src/${OLD} -> src/${NEW}"
    mv "src/${OLD}" "src/${NEW}"
fi

# 5. Clean build artifacts so the new project starts fresh
rm -rf bin/ obj/ src/"${NEW}"/bin/ src/"${NEW}"/obj/

# 6. Delete setup script
echo "  Removing setup.sh"
rm -- "$0"

echo "Done. Project renamed to '$NEW'."
echo "Run: dotnet build && dotnet run --project src/$NEW"
