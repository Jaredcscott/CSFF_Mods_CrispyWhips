import sys
if sys.version_info < (3, 6):
    print("Python 3.6 or later is required.")
    sys.exit(1)

import json
import shutil
from pathlib import Path

# ---------------------------
# File Selection
# ---------------------------

def get_save_file():
    if len(sys.argv) >= 2:
        return Path(sys.argv[1])

    path = input("Enter path to save file: ").strip('" ')
    return Path(path)

# ---------------------------
# Find ONLY skills
# ---------------------------

def find_skills(obj, found=None):
    if found is None:
        found = []

    if isinstance(obj, dict):
        stat_id = obj.get("StatID")

        if isinstance(stat_id, str) and "(Skill_" in stat_id:
            found.append(obj)

        for v in obj.values():
            find_skills(v, found)

    elif isinstance(obj, list):
        for item in obj:
            find_skills(item, found)

    return found

# ---------------------------
# Clean display name
# ---------------------------

def clean_name(stat_id):
    return stat_id.split("(Skill_")[1].rstrip(")")

# ---------------------------
# Reset ONLY stale actions
# ---------------------------

def clear_stale(stat):
    if "StaleActions" in stat:
        stat["StaleActions"] = []

# ---------------------------
# Main
# ---------------------------

def main():
    save_file = get_save_file()

    if not save_file.exists():
        print("File not found.")
        input()
        return

    backup = save_file.with_suffix(".bak")
    shutil.copy(save_file, backup)
    print(f"\nBackup created: {backup}")

    with open(save_file, "r", encoding="utf-8") as f:
        data = json.load(f)

    skills = find_skills(data)

    if not skills:
        print("No skills found.")
        input()
        return

    print("\n=== Skills Found ===")
    for i, s in enumerate(skills):
        name = clean_name(s["StatID"])
        stale_count = len(s.get("StaleActions", []))
        print(f"{i+1}. {name} (StaleActions: {stale_count})")

    print("\nA. Clear ALL stale actions")
    print("Q. Quit")

    choice = input("\nSelect option: ").strip().lower()

    if choice == "q":
        return

    if choice == "a":
        for s in skills:
            clear_stale(s)
        print("\nAll stale actions cleared!")
    else:
        try:
            idx = int(choice) - 1
            clear_stale(skills[idx])
            print("\nSelected skill cleared!")
        except:
            print("Invalid choice.")
            input()
            return

    with open(save_file, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)

    print("\nDone!")
    input("Press Enter to close...")

main()