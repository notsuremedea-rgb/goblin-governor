# Contributing

Run build.ps1 and test.ps1 before proposing changes. Tests use fake GPU setters and separate watchdog processes; no GPU crash or administrator permission is needed. Test outputs are isolated from the app's state.

Preserve the fixed-lock call, reset ownership journal, watchdog, startup confirmation and one-attempt-per-sign-in rules. Monitoring must never reapply settings. Do not silently substitute unsupported controls. Keep text and selected states large and high contrast.

Report your laptop model, GPU, driver version, Windows version, exact mode/range, startup status, observed behaviour and steps to reproduce. Distinguish an accepted driver request from an observed clock and from long-term stability. Do not deliberately crash your GPU to produce a report.

Before sharing logs or screenshots, remove usernames, personal paths, GPU UUIDs, serial numbers and unrelated process information. Never upload the state folder, certificates, private keys or crash dumps containing personal data.
