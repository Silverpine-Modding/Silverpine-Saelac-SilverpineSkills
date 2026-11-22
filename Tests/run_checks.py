"""Run after building Checks.csproj in Release. Uses a fresh, isolated Silverpine batch player."""
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT.parents[1]
sys.path.insert(0, str(PROJECT.parent / "CompanionParty/Tests"))
from run_smoke import copy_tree


def run():
    installed = "--installed" in sys.argv[1:]
    requested = [arg for arg in sys.argv[1:] if arg != "--installed"]
    isolation = PROJECT.parent / "QuitToMainMenu/Tests/Isolation"
    subprocess.run(["dotnet", "build", str(isolation / "Isolation.csproj"), "-c", "Release",
                    "--nologo", "-m:1", "-p:NuGetAudit=false"], check=True)
    sources = {}
    for name in ("CompanionParty", "AdventurersGuild", "CustomCraftingRecipes", "CustomGrowables"):
        build = PROJECT.parent / name / "bin/Release"
        if name in ("CompanionParty", "AdventurersGuild"):
            build /= "netstandard2.1"
        sources[name] = (ROOT / "BepInEx/plugins" / name if installed else build) / (name + ".dll")
    with tempfile.TemporaryDirectory(prefix="SkillsQA-", dir=ROOT / "artifacts") as folder:
        game = Path(folder)
        for name in ("Silverpine.exe", "UnityPlayer.dll", "winhttp.dll", "doorstop_config.ini"):
            shutil.copy2(ROOT / name, game)
        for name in ("MonoBleedingEdge", "BepInEx/core", "Silverpine_Data/Managed",
                     "Silverpine_Data/Plugins", "Silverpine_Data/Resources"):
            copy_tree(ROOT / name, game / name)
        for path in (ROOT / "Silverpine_Data").iterdir():
            if path.is_file():
                target = game / "Silverpine_Data" / path.name
                try:
                    os.link(path, target)
                except OSError:
                    shutil.copy2(path, target)
        (game / "quit-to-menu-test.instance").touch()
        patchers = game / "BepInEx/patchers"
        patchers.mkdir()
        shutil.copy2(isolation / "bin/Release/netstandard2.1/QuitToMenuTestIsolation.dll", patchers)
        plugins = game / "BepInEx/plugins"
        copy_tree(ROOT / "BepInEx/plugins/ModdingTools", plugins / "ModdingTools")
        skills = ROOT / "BepInEx/plugins/SilverpineSkills" if installed else PROJECT / "bin/Release/netstandard2.1"
        shutil.copy2(skills / "SilverpineSkills.dll", plugins)
        shutil.copy2(PROJECT / "Tests/bin/Release/netstandard2.1/SilverpineSkills.Checks.dll", plugins)
        for mode in ("native", *sources, "all", "scene"):
            if requested and mode not in requested:
                continue
            enabled = [name for name in sources if mode in ("all", "scene", name)]
            for name, dll in sources.items():
                if name in enabled:
                    shutil.copy2(dll, plugins)
                else:
                    (plugins / dll.name).unlink(missing_ok=True)
            loader = "CustomItemLoader.dll"
            if "CustomGrowables" in enabled:
                shutil.copy2(ROOT / "BepInEx/plugins/CustomItemLoader" / loader, plugins)
            else:
                (plugins / loader).unlink(missing_ok=True)
            (game / "skills-test-only").write_text("\n".join(enabled))
            report = game / "skills-results"
            report.unlink(missing_ok=True)
            cmd = [str(game / "Silverpine.exe"), "-batchmode", "-nographics", "-logfile", str(game / "unity.log")]
            if mode == "scene":
                cmd.remove("-nographics")
                cmd.extend(["-skills-scene", "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0"])
            with subprocess.Popen(cmd, cwd=game, creationflags=subprocess.CREATE_NO_WINDOW) as process:
                deadline = time.monotonic() + 60
                try:
                    while not report.exists() and process.poll() is None and time.monotonic() < deadline:
                        time.sleep(.2)
                finally:
                    if process.poll() is None:
                        process.kill()
                    process.wait()
            lines = report.read_text(encoding="utf-8-sig").splitlines() if report.exists() else []
            log = game / "BepInEx/LogOutput.log"
            output = log.read_text(encoding="utf-8-sig", errors="replace") if log.exists() else ""
            if not lines or lines[-1] != "PASS":
                unity = game / "unity.log"
                detail = unity.read_text(encoding="utf-8-sig", errors="replace") if unity.exists() else ""
                raise RuntimeError(mode + "\n" + "\n".join(lines) + "\n" + output[-3000:] + "\n" + detail[-3000:])
            print(f"{mode}: {len(lines)-1} checks passed", flush=True)


if __name__ == "__main__":
    run()
