from __future__ import annotations

import os
import shutil
import subprocess
import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk


APP_NAME = "Capri"
INSTALL_ROOT = Path(os.environ.get("LOCALAPPDATA", Path.home())) / "Programs" / APP_NAME


def bundled_app() -> Path:
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return base / "app" / "Capri"


def powershell(script: str, *, hidden: bool = True) -> None:
    flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if hidden else 0
    subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        check=True,
        creationflags=flags,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


def ps_quote(value: str | Path) -> str:
    return "'" + str(value).replace("'", "''") + "'"


def create_shortcut(link: Path, target: Path, working_dir: Path) -> None:
    script = (
        "$shell=New-Object -ComObject WScript.Shell;"
        f"$link=$shell.CreateShortcut({ps_quote(link)});"
        f"$link.TargetPath={ps_quote(target)};"
        f"$link.WorkingDirectory={ps_quote(working_dir)};"
        f"$link.IconLocation={ps_quote(str(target) + ',0')};$link.Save()"
    )
    powershell(script)


def register_uninstall(install_dir: Path) -> None:
    import winreg

    key_path = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Capri"
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path) as key:
        winreg.SetValueEx(key, "DisplayName", 0, winreg.REG_SZ, "Capri")
        winreg.SetValueEx(key, "DisplayVersion", 0, winreg.REG_SZ, "0.6.0-groq")
        winreg.SetValueEx(key, "Publisher", 0, winreg.REG_SZ, "Capri")
        winreg.SetValueEx(key, "InstallLocation", 0, winreg.REG_SZ, str(install_dir))
        winreg.SetValueEx(key, "DisplayIcon", 0, winreg.REG_SZ, str(install_dir / "Capri.exe"))
        uninstall = f'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{install_dir / "Uninstall-Capri.ps1"}"'
        winreg.SetValueEx(key, "UninstallString", 0, winreg.REG_SZ, uninstall)


def write_uninstaller(install_dir: Path) -> None:
    script = r'''$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
$installRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $MyInvocation.MyCommand.Path)).Path
$marker = Join-Path $installRoot 'capri-install.marker'
if (-not (Test-Path -LiteralPath $marker) -or -not (Test-Path -LiteralPath (Join-Path $installRoot 'Capri.exe'))) { exit 2 }
$answer = [System.Windows.Forms.MessageBox]::Show('Remover Capri, os dados locais e os arquivos instalados?','Capri','YesNo','Question')
if ($answer -ne 'Yes') { exit }
Get-Process Capri -ErrorAction SilentlyContinue | Stop-Process -Force
$desktop = [Environment]::GetFolderPath('Desktop')
$programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Capri'
Remove-Item -LiteralPath (Join-Path $desktop 'Capri.lnk') -Force
Remove-Item -LiteralPath $programs -Recurse -Force
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Capri' -Recurse -Force
$quotedRoot = $installRoot.Replace("'", "''")
$cleanup = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$quotedRoot' -Recurse -Force"
Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @('-NoProfile','-Command',$cleanup)
'''
    (install_dir / "Uninstall-Capri.ps1").write_text(script, encoding="utf-8")


class SetupWindow:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("Capri Setup")
        self.root.geometry("510x290")
        self.root.resizable(False, False)
        self.path = tk.StringVar(value=str(INSTALL_ROOT))
        self.launch_after = tk.BooleanVar(value=True)

        frame = ttk.Frame(root, padding=22)
        frame.pack(fill="both", expand=True)
        ttk.Label(frame, text="Capri", font=("Segoe UI", 22, "bold")).pack(anchor="w")
        ttk.Label(
            frame,
            text="Instale a Capri neste computador. O instalador mantém os dados existentes ao atualizar.",
            wraplength=450,
        ).pack(anchor="w", pady=(5, 18))
        ttk.Label(frame, text="Pasta de instalação").pack(anchor="w")
        row = ttk.Frame(frame)
        row.pack(fill="x", pady=(4, 12))
        ttk.Entry(row, textvariable=self.path).pack(side="left", fill="x", expand=True)
        ttk.Button(row, text="Procurar…", command=self.choose_folder).pack(side="left", padx=(8, 0))
        ttk.Checkbutton(frame, text="Iniciar Capri depois da instalação", variable=self.launch_after).pack(anchor="w")
        self.status = ttk.Label(frame, text="Pronta para instalar")
        self.status.pack(anchor="w", pady=(14, 4))
        self.progress = ttk.Progressbar(frame, mode="indeterminate")
        self.progress.pack(fill="x")
        buttons = ttk.Frame(frame)
        buttons.pack(fill="x", side="bottom", pady=(16, 0))
        ttk.Button(buttons, text="Cancelar", command=self.root.destroy).pack(side="right")
        self.install_button = ttk.Button(buttons, text="Instalar", command=self.install)
        self.install_button.pack(side="right", padx=(0, 8))

    def choose_folder(self) -> None:
        selected = filedialog.askdirectory(initialdir=self.path.get(), title="Escolha a pasta da Capri")
        if selected:
            self.path.set(selected)

    def install(self) -> None:
        destination = Path(self.path.get()).expanduser()
        source = bundled_app()
        if not (source / "Capri.exe").is_file() or not (source / "_internal").is_dir():
            messagebox.showerror("Capri Setup", "Os arquivos de instalação não foram encontrados.")
            return
        if destination.exists() and (destination / "Capri.exe").exists():
            if not messagebox.askyesno("Atualizar Capri", "A Capri já está nessa pasta. Deseja atualizá-la e manter os dados?"):
                return
            running = subprocess.run(
                ["tasklist.exe", "/FI", "IMAGENAME eq Capri.exe", "/NH"],
                capture_output=True,
                text=True,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            ).stdout
            if "Capri.exe" in running:
                if not messagebox.askyesno("Fechar Capri", "A Capri está aberta. Feche-a para continuar a atualização?"):
                    return
                subprocess.run(
                    ["taskkill.exe", "/IM", "Capri.exe", "/F"],
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                )
        self.install_button.configure(state="disabled")
        self.status.configure(text="Instalando arquivos…")
        self.progress.start(12)
        threading.Thread(target=self.copy_and_finish, args=(source, destination), daemon=True).start()

    def copy_and_finish(self, source: Path, destination: Path) -> None:
        try:
            destination.mkdir(parents=True, exist_ok=True)
            config_path = destination / "config.yaml"
            saved_config = config_path.read_bytes() if config_path.is_file() else None
            shutil.copytree(source, destination, dirs_exist_ok=True)
            if saved_config is not None:
                config_path.write_bytes(saved_config)
            else:
                bundled_config = source.parent / "config.yaml"
                if bundled_config.is_file():
                    shutil.copy2(bundled_config, config_path)
            (destination / "data").mkdir(exist_ok=True)
            (destination / "capri-install.marker").write_text("Capri installer\n", encoding="utf-8")
            write_uninstaller(destination)
            desktop = Path(os.environ.get("USERPROFILE", Path.home())) / "Desktop"
            start_menu = Path(os.environ.get("APPDATA", Path.home())) / "Microsoft" / "Windows" / "Start Menu" / "Programs" / APP_NAME
            desktop.mkdir(parents=True, exist_ok=True)
            start_menu.mkdir(parents=True, exist_ok=True)
            create_shortcut(desktop / "Capri.lnk", destination / "Capri.exe", destination)
            create_shortcut(start_menu / "Capri.lnk", destination / "Capri.exe", destination)
            register_uninstall(destination)
            self.root.after(0, lambda: self.finished(destination))
        except Exception as exc:
            self.root.after(0, lambda error=str(exc): self.failed(error))

    def finished(self, destination: Path) -> None:
        self.progress.stop()
        self.status.configure(text="Instalação concluída")
        launch = self.launch_after.get()
        messagebox.showinfo("Capri Setup", "Capri foi instalada com sucesso.")
        self.root.destroy()
        if launch:
            os.startfile(destination / "Capri.exe")

    def failed(self, error: str) -> None:
        self.progress.stop()
        self.status.configure(text="Não foi possível concluir a instalação")
        self.install_button.configure(state="normal")
        messagebox.showerror("Capri Setup", error)


if __name__ == "__main__":
    window = tk.Tk()
    SetupWindow(window)
    window.mainloop()
