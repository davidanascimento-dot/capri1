from pathlib import Path

ROOT = Path(SPECPATH).resolve()
APP_DIR = ROOT.parent / "dist-api-editor-v5" / "Capri"
DEFAULT_CONFIG = ROOT / "config.yaml"

a = Analysis(
    [str(ROOT / "CapriSetup.py")],
    pathex=[str(ROOT)],
    binaries=[],
    datas=[(str(APP_DIR), "app/Capri"), (str(DEFAULT_CONFIG), "app")],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
# Keep incompatible ICU DLLs collected from unrelated folders on PATH out of the setup runtime.
a.binaries = [entry for entry in a.binaries if entry[0].lower() not in {"icuuc.dll", "icudt78.dll"}]
pyz = PYZ(a.pure)
exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="Capri-Setup",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
