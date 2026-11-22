# Setup

## Requirements

Windows x64, Windows PowerShell, the .NET Framework 4.x C# compiler/Windows Forms, and internet access for dependency setup. Bluetooth hardware is needed only for later device use. Dependencies target Python 3.12.10 on Windows. No API key is needed.

## Install and build

Use a separate working directory, not an existing live lighting deployment:

    powershell.exe -NoProfile -File .\setup-review.ps1
    powershell.exe -NoProfile -File .\build.ps1

Setup creates runtime\python.exe and installs pinned packages. It refuses to overwrite an existing runtime directory. The build creates LED Sign Studio.exe and the earlier LED Sign Controller.exe; neither is launched automatically.

The app expects the embedded runtime and its package/font directory layout. Substituting a normal virtual environment requires adapting those paths.

## Isolated tests

These use temporary directories, synthetic identities, fake Bluetooth clients and injected schedule callbacks:

    .\runtime\python.exe -B .\tests\test_serial_sending.py
    .\runtime\python.exe -B .\tests\test_saved_selection.py
    .\runtime\python.exe -B .\tests\test_sent_history.py
    .\runtime\python.exe -B .\tests\test_sign_schedule.py
    .\runtime\python.exe -B .\tests\test_long_scroll.py
    .\runtime\python.exe -B .\tests\test_stop_sending.py

Do not run the scheduler installer as part of testing.

## Your hardware

After building, launch LED Sign Studio.exe or use Open Controller.cmd. Keep the app, Python modules, runtime and assets together. Use Scan only when you intend to discover nearby Bluetooth hardware.

The examples directory contains empty device/schedule lists and generic design/settings examples, not usable addresses for anyone else's hardware.

Background sending needs an awake, signed-in Windows computer within Bluetooth range. The optional install-scheduler.ps1 registers or replaces LED Sign Studio - Scheduled Updates, starts it immediately and changes which copy handles future updates. Use it only for an intentional deployment.

Keep device/configuration files, schedules, logs, history and generated media private. No real deployment was changed to prepare or test this release.
