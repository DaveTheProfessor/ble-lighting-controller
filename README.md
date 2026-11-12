# BLE Lighting Controller

Windows software for iPixel-compatible Bluetooth LED signs and Hello Fairy lights.

## Features

- Windows Forms studio for text, emoji, colors, images, GIFs and saved designs.
- Shared Bluetooth queue: one sign connection at a time, with a four-second settling period.
- Per-sign progress, successful-send history and connection cleanup.
- Hello Fairy power, color, brightness and basic effect controls.
- Local America/New_York schedules with per-target delivery records and guarded retries.

## Get started

Read [SETUP.md](SETUP.md) for requirements, dependencies, compilation and isolated tests. This package contains source and static assets, not a prebuilt app or Python runtime.

StudioApp.cs is the main interface. ControllerApp.cs is the earlier simplified text controller. Python modules implement rendering, Bluetooth operations, history and scheduling.

Examples contain no real devices or schedules. Configure your own hardware locally and keep its identity and operating-state files out of Git.

## Verification and limitations

Both C# applications compile, and the four fake-device test scripts pass on the verified Windows environment. They exercise serial sending, saved-design selection, history and scheduler retry/deduplication without contacting Bluetooth hardware.

Fresh-machine GUI operation and compatibility with other hardware models have not been verified. Acknowledgements do not prove that displayed pixels match the intended image. A phone app holding the Bluetooth connection can prevent a PC connection.

The optional install-scheduler.ps1 helper can replace the Windows task named LED Sign Studio - Scheduled Updates and start it immediately. Do not run it during review or against an existing installation unless you intend to change that deployment.

## Private operating data

Device addresses, live configurations, schedules, history, databases, logs, generated display media, credentials and customer/staff photographs are excluded. The .gitignore file lists the main local-state paths. Dependency setup and builds do not start the app or register the scheduler.

## Rights and third-party components

No project-wide open-source license is granted; see [LICENSE](LICENSE). Third-party terms remain applicable, including Twemoji artwork under CC BY 4.0. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
